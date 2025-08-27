using System.Diagnostics;
using System.Net.Http.Json;

namespace EowKit.Kiwix;

public sealed class KiwixClient
{
    private readonly HttpClient _http = new();
    private readonly string _host;
    private readonly int _port;

    public KiwixClient(string host, int port)
    {
        _host = host; _port = port;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task EnsureServeAsync(string zimPath)
    {
        if (await IsAliveAsync()) return;

        var servePath = ResolveKiwixServePath();
        if (servePath is null)
        {
            var msg = OperatingSystem.IsMacOS() ?
                "kiwix-serve not found. It should have been downloaded automatically; re-run install." :
                (OperatingSystem.IsWindows() ?
                    "kiwix-serve not found. It should have been downloaded automatically; re-run install." :
                    "kiwix-serve not found. It should have been downloaded automatically; re-run install.");
            throw new Exception(msg);
        }

        var psi = new ProcessStartInfo(servePath, $"--port={_port} \"{zimPath}\"")
        {
            UseShellExecute = false, CreateNoWindow = true
        };
        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to start kiwix-serve at '{servePath}': {ex.Message}");
        }

        // wait a moment for server to bind
        for (int i=0;i<15;i++)
        {
            if (await IsAliveAsync()) return;
            await Task.Delay(500);
        }
        throw new Exception("kiwix-serve did not start");
    }

    static string? ResolveKiwixServePath()
    {
        // 1) Environment override
        var env = Environment.GetEnvironmentVariable("KIWIX_SERVE");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        // 2) Local directory next to executable
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, OperatingSystem.IsWindows() ? "kiwix-serve.exe" : "kiwix-serve");
        if (File.Exists(candidate)) return candidate;

        // 3) Repo-configured tools dir (paths.kiwix_tools_dir)
        var cfgPath = Path.Combine(AppContext.BaseDirectory, "configs", "eowkit.toml");
        try
        {
            if (File.Exists(cfgPath))
            {
                var line = File.ReadAllLines(cfgPath).FirstOrDefault(l => l.TrimStart().StartsWith("kiwix_tools_dir"));
                if (line is not null)
                {
                    var eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        var val = line[(eq+1)..].Trim().Trim('"');
                        var p = Path.Combine(val, OperatingSystem.IsWindows() ? "kiwix-serve.exe" : "kiwix-serve");
                        if (File.Exists(p)) return p;
                    }
                }
            }
        }
        catch { }

        // 4) Common locations
        var candidates = new List<string>();
        if (OperatingSystem.IsMacOS())
        {
            candidates.Add("/opt/homebrew/bin/kiwix-serve");
            candidates.Add("/usr/local/bin/kiwix-serve");
        }
        else if (OperatingSystem.IsWindows())
        {
            candidates.Add("kiwix-serve.exe"); // rely on PATH
        }
        else
        {
            candidates.Add("/usr/bin/kiwix-serve");
            candidates.Add("/usr/local/bin/kiwix-serve");
        }

        foreach (var p in candidates)
        {
            try
            {
                if (p.Contains("/")) { if (File.Exists(p)) return p; }
                else
                {
                    // 5) which/where
                    var tool = OperatingSystem.IsWindows() ? "where" : "which";
                    var psi = new ProcessStartInfo(tool, OperatingSystem.IsWindows() ? "kiwix-serve.exe" : "kiwix-serve") { RedirectStandardOutput = true, UseShellExecute = false };
                    var pr = Process.Start(psi);
                    var outp = pr!.StandardOutput.ReadToEnd().Trim();
                    pr.WaitForExit(2000);
                    if (!string.IsNullOrWhiteSpace(outp))
                    {
                        var first = outp.Split('\n', '\r').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim();
                        if (!string.IsNullOrWhiteSpace(first) && File.Exists(first)) return first;
                    }
                }
            }
            catch { /* ignore */ }
        }

        return null;
    }

    async Task<bool> IsAliveAsync()
    {
        try
        {
            var url = $"http://{_host}:{_port}/search?pattern=test&content=html";
            using var r = await _http.GetAsync(url);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public sealed record SearchHit(string Title, string Path);

    public async Task<List<SearchHit>> SearchAsync(string pattern, int k)
    {
        // Kiwix returns HTML/XML; we'll parse a simple HTML list by regex (fast + dirty)
        var url = $"http://{_host}:{_port}/search?pattern={Uri.EscapeDataString(pattern)}&content=html";
        var html = await _http.GetStringAsync(url);
        var hits = new List<SearchHit>();

        // very simple extraction: <a href="/A/B/C">Title</a>
        var aIdx = 0;
        while ((aIdx = html.IndexOf("<a href=\"", aIdx, StringComparison.Ordinal)) >= 0 && hits.Count < k)
        {
            var start = aIdx + "<a href=\"".Length;
            var end = html.IndexOf("\"", start, StringComparison.Ordinal);
            if (end < 0) break;
            var href = html[start..end];

            var tStart = html.IndexOf(">", end, StringComparison.Ordinal) + 1;
            var tEnd = html.IndexOf("</a>", tStart, StringComparison.Ordinal);
            if (tStart <= 0 || tEnd < 0) break;
            var title = html[tStart..tEnd].Trim();

            if (href.StartsWith("/"))
                hits.Add(new SearchHit(title, href));

            aIdx = tEnd + 4;
        }
        return hits;
    }

    public async Task<string> GetContentHtmlAsync(string pathOrTitle)
    {
        // prefer /content/ path; if caller passed /wiki/..., just forward it
        var path = pathOrTitle.StartsWith("/") ? pathOrTitle : $"/wiki/{Uri.EscapeDataString(pathOrTitle)}";
        var url = $"http://{_host}:{_port}{path}";
        return await _http.GetStringAsync(url);
    }
}