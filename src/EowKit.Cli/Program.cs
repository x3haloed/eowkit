using EowKit.Core;
using EowKit.Kiwix;
using EowKit.Ollama;
using Spectre.Console;

var cfgPath = args.Length > 1 ? args[1] : "configs/eowkit.toml";
var command = args.Length > 0 ? args[0] : "run";

switch (command)
{
    case "probe":
    {
        var probe = await HardwareProbe.ProbeAsync();
        AnsiConsole.MarkupLine($"[bold]RAM:[/] {probe.TotalRamBytes/1_000_000_000.0:F1} GB  " +
                               $"[bold]Disk Free:[/] {DiskProbe.GetFreeBytes(".")/1_000_000_000.0:F1} GB  " +
                               $"[bold]CPU AVX2:[/] {(probe.HasAvx2 ? "yes" : "no")}  [bold]Cores:[/] {probe.LogicalCores}  " +
                               $"[bold]GPU:[/] CUDA={(probe.HasCuda?"yes":"no")}, OpenCL={(probe.HasOpenCl?"yes":"no")}, Metal={(probe.HasMetal?"yes":"no")}" );
        break;
    }

    case "install":
    {
        var catalog = Catalog.Load("configs/catalog.toml");
        await Installer.RunAsync(catalog, cfgPath);
        if (AnsiConsole.Confirm("Enable the optional [green]reranker[/] and download required files now?"))
        {
            await Installer.InstallRerankerAsync(cfgPath);
        }
        break;
    }

    case "install-reranker":
    {
        await Installer.InstallRerankerAsync(cfgPath);
        break;
    }

    case "sum":
    {
        if (args.Length < 2) { Console.WriteLine("sum <path>"); return; }
        var sha = await EowKit.Core.Checksum.Sha256FileAsync(args[1]);
        Console.WriteLine(sha);
        break;
    }

    case "fetch-sum":
    {
        if (args.Length < 2) { Console.WriteLine("fetch-sum <url>"); return; }
        var sha = await EowKit.Core.Checksum.TryFetchSha256ForUrlAsync(args[1]);
        Console.WriteLine(sha ?? "");
        if (sha is null) Environment.ExitCode = 2;
        break;
    }

    case "run":
    default:
    {
        var cfg = Config.Load(cfgPath);
        var kiwix = new KiwixClient(cfg.Wiki.Bind, cfg.Wiki.KiwixPort);
        await kiwix.EnsureServeAsync(cfg.Wiki.Zim);

        var ollama = new OllamaClient(cfg.Llm.OllamaUrl, string.IsNullOrWhiteSpace(cfg.Paths.ModelsDir) ? null : cfg.Paths.ModelsDir);
        await ollama.EnsureServeAsync();
        await ollama.EnsureModelAsync(cfg.Model.Ollama, progress: line => AnsiConsole.MarkupLine($"[blue]{Markup.Escape(line)}[/]"));

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