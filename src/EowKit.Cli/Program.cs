using EowKit.Core;
using EowKit.Kiwix;
using EowKit.Ollama;
using Spectre.Console;

var cfgPath   = args.Length > 1 ? args[1] : "configs/eowkit.toml";
var catalog   = Catalog.Load("configs/catalog.toml");
var command   = args.Length > 0 ? args[0] : "run";

switch (command)
{
    case "probe":
        var probe = await HardwareProbe.ProbeAsync();
        AnsiConsole.MarkupLine($"[bold]RAM:[/] {probe.TotalRamBytes/1_000_000_000.0:F1} GB  " +
                               $"[bold]Disk Free:[/] {DiskProbe.GetFreeBytes(".")/1_000_000_000.0:F1} GB");
        break;

    case "install":
        await Installer.RunAsync(catalog, cfgPath);
        break;

    case "run":
    default:
    {
        var cfg = Config.Load(cfgPath);
        // Start kiwix-serve (if not already running)
        var kiwix = new KiwixClient(cfg.Wiki.Bind, cfg.Wiki.KiwixPort);
        await kiwix.EnsureServeAsync(cfg.Wiki.Zim);
        // Ensure Ollama
        var ollama = new OllamaClient(cfg.Llm.OllamaUrl);
        await ollama.EnsureServeAsync();
        await ollama.EnsureModelAsync(cfg.Model.Ollama);

        // Wire the orchestrator (MCP-style tool loop)
        var orchestrator = new Orchestrator(cfg, kiwix, ollama);
        AnsiConsole.MarkupLine("[bold green]EOW Kit ready.[/] Type your question. Ctrl-C to exit.");
        while (true)
        {
            var q = AnsiConsole.Ask<string>("[bold]>[/] ");
            var answer = await orchestrator.AnswerAsync(q);
            AnsiConsole.WriteLine(answer);
        }
    }
}