using Spectre.Console;

namespace EowKit.Core;

public static class Installer
{
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

        // 3) Show recommended commands (no brew)
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Download commands (copy/paste):[/]");

        // Wikipedia fetch
        AnsiConsole.MarkupLine($"[grey]# Wikipedia[/]");
        AnsiConsole.MarkupLine($"curl -fLO \"{wiki.Url}\"");

        // Model fetch (Ollama vs MLC)
        if (model.Runner == "ollama")
        {
            AnsiConsole.MarkupLine($"[grey]# Ollama[/]");
            AnsiConsole.MarkupLine("curl -fsSL https://ollama.com/install.sh | sh");
            AnsiConsole.MarkupLine($"ollama pull {model.Id}");
        }
        else
        {
            AnsiConsole.MarkupLine($"[grey]# MLC (mobile artifact)[/]");
            AnsiConsole.MarkupLine($"# Visit: https://huggingface.co/{model.Id}");
        }

        // 4) Patch config in-place
        ConfigPatcher(cfgPath, model.Id, wiki.Name);
        AnsiConsole.MarkupLine($"\n[green]Updated[/] {cfgPath} with model={model.Id} and wiki.zim={wiki.Name}");
    }

    static void ConfigPatcher(string cfgPath, string modelId, string wikiName)
    {
        var lines = File.ReadAllLines(cfgPath).ToList();
        int i;

        i = lines.FindIndex(l => l.TrimStart().StartsWith("ollama ="));
        if (i >= 0) lines[i] = $"ollama = \"{modelId}\"";

        i = lines.FindIndex(l => l.Contains("zim ="));
        if (i >= 0) lines[i] = $"zim = \"/data/{wikiName}\"";

        File.WriteAllLines(cfgPath, lines);
    }
}