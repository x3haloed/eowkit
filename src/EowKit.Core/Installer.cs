using Spectre.Console;

namespace EowKit.Core;

public static class Installer
{
    // Downloads cache placed at ./downloads with SHA256 verification and resumable fetches
    public static async Task RunAsync(Catalog catalog, string cfgPath)
    {
        var cfg = Config.Load(cfgPath);
        var probe = await HardwareProbe.ProbeAsync();

        // 0) Ask for directories (Enter to keep defaults)
        var defaultDownloads = string.IsNullOrWhiteSpace(cfg.Paths.DownloadsDir) ? "downloads" : cfg.Paths.DownloadsDir;
        var defaultZimDir    = string.IsNullOrWhiteSpace(cfg.Paths.ZimDir)       ? defaultDownloads : cfg.Paths.ZimDir;
        var defaultModelsDir = string.IsNullOrWhiteSpace(cfg.Paths.ModelsDir)    ? "models" : cfg.Paths.ModelsDir;

        var downloadsDirIn = AnsiConsole.Ask<string>("Downloads directory:", defaultDownloads);
        var zimDirIn       = AnsiConsole.Ask<string>("ZIM storage directory:", defaultZimDir);
        var modelsDirIn    = AnsiConsole.Ask<string>("Models directory (Ollama + reranker):", defaultModelsDir);

        // Persist directories in config and ensure they exist
        ConfigEditor.SetInSection(cfgPath, "paths", "downloads_dir", $"\"{downloadsDirIn.Replace("\\", "/")}\"");
        ConfigEditor.SetInSection(cfgPath, "paths", "zim_dir", $"\"{zimDirIn.Replace("\\", "/")}\"");
        ConfigEditor.SetInSection(cfgPath, "paths", "models_dir", $"\"{modelsDirIn.Replace("\\", "/")}\"");

        var downloadsDir = Path.GetFullPath(downloadsDirIn);
        var zimDir       = Path.GetFullPath(zimDirIn);
        var modelsDir    = Path.GetFullPath(modelsDirIn);
        Directory.CreateDirectory(downloadsDir);
        Directory.CreateDirectory(zimDir);
        Directory.CreateDirectory(modelsDir);

        // Volume notes
        var curRoot = Path.GetPathRoot(Path.GetFullPath("."));
        void NoteVolume(string label, string dir)
        {
            var root = Path.GetPathRoot(dir);
            if (!string.Equals(root, curRoot, StringComparison.OrdinalIgnoreCase))
                AnsiConsole.MarkupLine($"[yellow]Note[/]: {label} is on a different volume: {root}");
        }
        NoteVolume("Downloads", downloadsDir);
        NoteVolume("ZIM storage", zimDir);
        NoteVolume("Models", modelsDir);

        // Ensure local kiwix-serve in models/tools to make binary portable
        var toolsDir = Path.Combine(modelsDir, "tools");
        await KiwixToolsInstaller.EnsureKiwixServeAsync(toolsDir, downloadsDir);
        ConfigEditor.SetInSection(cfgPath, "paths", "kiwix_tools_dir", $"\"{toolsDir.Replace("\\", "/")}\"");

        // Ensure local ollama CLI so we can run without system install
        var ollamaDir = Path.Combine(modelsDir, "ollama");
        await OllamaInstaller.EnsureOllamaAsync(ollamaDir, downloadsDir);
        ConfigEditor.SetInSection(cfgPath, "paths", "ollama_dir", $"\"{ollamaDir.Replace("\\", "/")}\"");

        AnsiConsole.MarkupLine($"[bold]Detected RAM[/]: {probe.TotalRamBytes/1_000_000_000.0:F1} GB");

        // 1) Pick Wikipedia pack
        var wiki = AnsiConsole.Prompt(
            new SelectionPrompt<Catalog.WikiItem>()
                .Title("Choose a [green]Wikipedia snapshot[/]:")
                .PageSize(10)
                .AddChoices(catalog.Wikis)
                .UseConverter(w => Markup.Escape($"{w.Name}  (~{w.ApproxBytes/1_000_000_000.0:F0} GB)"))
        );
        var finalWikiPath = Path.Combine(zimDir, wiki.Name);
        var freeDiskWiki = DiskProbe.GetFreeBytes(finalWikiPath);
        if (wiki.ApproxBytes > freeDiskWiki * 0.9) // 10% safety margin
            AnsiConsole.MarkupLine($"[red]WARNING[/]: Snapshot likely too large for available disk at {Path.GetPathRoot(finalWikiPath)}.");

        // 2) Pick model
        var model = AnsiConsole.Prompt(
            new SelectionPrompt<Catalog.ModelItem>()
                .Title("Choose an [green]LLM[/]:")
                .PageSize(10)
                .AddChoices(catalog.Models)
                .UseConverter(m => Markup.Escape($"{m.Id} [{m.Runner}/{m.Precision}]  (~{m.ApproxBytes/1_000_000_000.0:F0} GB, min RAM {m.MinRamBytes/1_000_000_000.0:F0} GB)"))
        );
        // Suggest a default based on hardware
        var hw = probe;
        AnsiConsole.MarkupLine($"[grey]Hint[/]: {(hw.HasMetal ? "Metal GPU detected; consider fp16/q4_0 on Mac." : hw.HasCuda ? "CUDA GPU detected; larger models may work." : hw.HasAvx2 ? "AVX2 CPU detected; use q8_0 or fp16 small." : "No AVX2; prefer small quantized models.")}");
        if (probe.TotalRamBytes < model.MinRamBytes)
            AnsiConsole.MarkupLine($"[red]WARNING[/]: Not enough RAM for {model.Id} (have {probe.TotalRamBytes/1_000_000_000.0:F1} GB, need {model.MinRamBytes/1_000_000_000.0:F1} GB).");

        var freeDiskModels = DiskProbe.GetFreeBytes(modelsDir);
        if (model.ApproxBytes > freeDiskModels * 0.9)
            AnsiConsole.MarkupLine($"[red]WARNING[/]: Model may not fit on disk at {Path.GetPathRoot(modelsDir)}.");

        // 3) Downloads cache + resilient fetch + copy to target dir
        AnsiConsole.MarkupLine($"\n[bold]Downloads cache[/]: {downloadsDir}");
        var stageZim = Path.Combine(downloadsDir, wiki.Name);
        var wikiTarget = finalWikiPath;

        if (AnsiConsole.Confirm($"Download [bold]{wiki.Name}[/] now?"))
        {
            if (!File.Exists(stageZim) || !await Sha256Verifier.VerifyAsync(stageZim, wiki.Sha256))
            {
                AnsiConsole.MarkupLine("[grey]Fetching Wikipedia snapshot with resume + SHA256 verify...[/]");
                await ResumableFetcher.DownloadAsync(wiki.Url, stageZim);
                if (!string.IsNullOrWhiteSpace(wiki.Sha256))
                {
                    var ok = await Sha256Verifier.VerifyAsync(stageZim, wiki.Sha256);
                    if (!ok)
                        throw new Exception($"SHA256 mismatch for {wiki.Name}. Delete and retry.");
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[green]ZIM present in cache and verified.[/]");
            }

            if (!string.Equals(stageZim, wikiTarget, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(wikiTarget)!);
                File.Copy(stageZim, wikiTarget, overwrite: true);
                AnsiConsole.MarkupLine($"[green]✓[/] Copied ZIM to {wikiTarget}");
            }
            ConfigEditor.SetInSection(cfgPath, "wiki", "zim", $"\"{wikiTarget.Replace("\\", "/")}\"");
        }
        else
        {
            AnsiConsole.MarkupLine($"curl -fLO \"{wiki.Url}\"");
            ConfigEditor.SetInSection(cfgPath, "wiki", "zim", $"\"{wikiTarget.Replace("\\", "/")}\"");
        }

        // Model fetch (instructions only; Ollama handles pull on run)
        if (model.Runner == "ollama")
        {
            AnsiConsole.MarkupLine($"[grey]Model runner[/]: Ollama. The model will be pulled on first run if missing.");
        }
        else
        {
            AnsiConsole.MarkupLine($"[grey]MLC note[/]: Download mobile artifact from https://huggingface.co/{model.Id}");
        }

        // 4) Patch config in-place, and set a default thread count recommendation
        ConfigPatcher(cfgPath, model.Id, wikiTarget);
        var suggestedThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, Environment.ProcessorCount);
        if (hw.HasAvx2 && Environment.ProcessorCount >= 8) suggestedThreads = Math.Min(Environment.ProcessorCount - 2, 12);
        ConfigEditor.SetInSection(cfgPath, "llm_runtime", "num_threads", suggestedThreads.ToString());
        AnsiConsole.MarkupLine($"\n[green]Updated[/] {cfgPath} with model={model.Id} and wiki.zim={Path.GetFileName(wikiTarget)}");
    }

    static void ConfigPatcher(string cfgPath, string modelId, string wikiPath)
    {
        var lines = File.ReadAllLines(cfgPath).ToList();
        int i;

        i = lines.FindIndex(l => l.TrimStart().StartsWith("ollama ="));
        if (i >= 0) lines[i] = $"ollama = \"{modelId}\"";

        i = lines.FindIndex(l => l.Contains("zim ="));
        if (i >= 0) lines[i] = $"zim = \"{wikiPath.Replace("\\", "/")}\"";

        File.WriteAllLines(cfgPath, lines);
    }

    // NEW: dedicated reranker flow (ask → download → set TOML)
    public static async Task InstallRerankerAsync(string cfgPath)
    {
        var cfg = Config.Load(cfgPath);
        var cfgModelsDir = string.IsNullOrWhiteSpace(cfg.Paths.ModelsDir) ? "models" : cfg.Paths.ModelsDir;
        var downloadsDir = Path.GetFullPath(cfgModelsDir);
        Directory.CreateDirectory(downloadsDir);

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select reranker build:")
                .AddChoices("Standard (FP32, ~90–100 MB)", "Try INT8 Quantized (smaller, if available)")
        );

        var baseRepo = "https://huggingface.co/cross-encoder/ms-marco-MiniLM-L6-v2/resolve/main";
        var onnxCandidates = new List<string>();

        if (choice.StartsWith("Standard"))
        {
            onnxCandidates.Add($"{baseRepo}/onnx/model.onnx");
        }
        else
        {
            onnxCandidates.Add($"{baseRepo}/onnx/model_qint8.onnx");
            onnxCandidates.Add($"{baseRepo}/onnx/model.int8.onnx");
            onnxCandidates.Add($"{baseRepo}/onnx/model.quant.onnx");
            onnxCandidates.Add($"{baseRepo}/onnx/model.onnx");
        }

        string onnxPath = "";
        Exception? lastErr = null;
        foreach (var u in onnxCandidates)
        {
            try
            {
                onnxPath = await EowKit.Core.Downloader.DownloadWithCacheAsync(u, downloadsDir, sha256: null);
                break;
            }
            catch (Exception ex)
            {
                lastErr = ex;
            }
        }
        if (string.IsNullOrEmpty(onnxPath))
            throw new Exception("Failed to download reranker ONNX.", lastErr);

        var vocabUrl = $"{baseRepo}/vocab.txt";
        var vocabPath = await EowKit.Core.Downloader.DownloadWithCacheAsync(vocabUrl, downloadsDir, sha256: null);

        ConfigEditor.SetInSection(cfgPath, "reranker", "enabled", "true");
        ConfigEditor.SetInSection(cfgPath, "reranker", "onnx_model", $"\"{onnxPath.Replace("\\", "/")}\"");
        ConfigEditor.SetInSection(cfgPath, "reranker", "tokenizer_vocab", $"\"{vocabPath.Replace("\\", "/")}\"");
        if (!File.ReadAllText(cfgPath).Contains("max_seq_len"))
            ConfigEditor.SetInSection(cfgPath, "reranker", "max_seq_len", "256");

        AnsiConsole.MarkupLine($"[green]✓[/] Reranker enabled and configured in {cfgPath}");
    }
}