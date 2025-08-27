using Spectre.Console;

namespace EowKit.Core;

public static class Installer
{
    // Downloads cache placed at ./downloads with SHA256 verification and resumable fetches
    public static async Task RunAsync(Catalog catalog, string cfgPath)
    {
        var cfg = Config.Load(cfgPath);
        var probe = await HardwareProbe.ProbeAsync();
        var freeDisk = DiskProbe.GetFreeBytes(".");

        AnsiConsole.MarkupLine($"[bold]Detected RAM[/]: {probe.TotalRamBytes/1_000_000_000.0:F1} GB, " +
                               $"[bold]Free Disk[/]: {freeDisk/1_000_000_000.0:F1} GB");

        // 1) Pick Wikipedia pack
        var wiki = AnsiConsole.Prompt(
            new SelectionPrompt<Catalog.WikiItem>()
                .Title("Choose a [green]Wikipedia snapshot[/]:")
                .PageSize(10)
                .AddChoices(catalog.Wikis)
                .UseConverter(w => $"{w.Name}  (~{w.ApproxBytes/1_000_000_000.0:F0} GB)")
        );
        if (wiki.ApproxBytes > freeDisk * 0.9) // 10% safety margin
            AnsiConsole.MarkupLine($"[red]WARNING[/]: Snapshot likely too large for available disk.");

        // 2) Pick model
        var model = AnsiConsole.Prompt(
            new SelectionPrompt<Catalog.ModelItem>()
                .Title("Choose an [green]LLM[/]:")
                .PageSize(10)
                .AddChoices(catalog.Models)
                .UseConverter(m => $"{m.Id} [{m.Runner}/{m.Precision}]  (~{m.ApproxBytes/1_000_000_000.0:F0} GB, min RAM {m.MinRamBytes/1_000_000_000.0:F0} GB)")
        );
        if (probe.TotalRamBytes < model.MinRamBytes)
            AnsiConsole.MarkupLine($"[red]WARNING[/]: Not enough RAM for {model.Id} (have {probe.TotalRamBytes/1_000_000_000.0:F1} GB, need {model.MinRamBytes/1_000_000_000.0:F1} GB).");

        if (model.ApproxBytes > freeDisk * 0.9)
            AnsiConsole.MarkupLine($"[red]WARNING[/]: Model may not fit on disk.");

        // 3) Downloads cache + resilient fetch
        var downloadsDir = Path.GetFullPath("downloads");
        Directory.CreateDirectory(downloadsDir);
        AnsiConsole.MarkupLine($"\n[bold]Downloads cache[/]: {downloadsDir}");

        var wikiTarget = Path.Combine(downloadsDir, wiki.Name);
        if (!File.Exists(wikiTarget) || !await Sha256Verifier.VerifyAsync(wikiTarget, wiki.Sha256))
        {
            AnsiConsole.MarkupLine("[grey]Fetching Wikipedia snapshot with resume + SHA256 verify...[/]");
            await ResumableFetcher.DownloadAsync(wiki.Url, wikiTarget);
            if (!string.IsNullOrWhiteSpace(wiki.Sha256))
            {
                var ok = await Sha256Verifier.VerifyAsync(wikiTarget, wiki.Sha256);
                if (!ok)
                    throw new Exception($"SHA256 mismatch for {wiki.Name}. Delete and retry.");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[green]ZIM present and verified.[/]");
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

        // 4) Patch config in-place
        ConfigPatcher(cfgPath, model.Id, wikiTarget);
        AnsiConsole.MarkupLine($"\n[green]Updated[/] {cfgPath} with model={model.Id} and wiki.zim={wiki.Name}");
    }

    static void ConfigPatcher(string cfgPath, string modelId, string wikiName)
    {
        var lines = File.ReadAllLines(cfgPath).ToList();
        int i;

        i = lines.FindIndex(l => l.TrimStart().StartsWith("ollama ="));
        if (i >= 0) lines[i] = $"ollama = \"{modelId}\"";

        i = lines.FindIndex(l => l.Contains("zim ="));
        if (i >= 0) lines[i] = $"zim = \"{wikiName}\"";

        File.WriteAllLines(cfgPath, lines);
    }
}