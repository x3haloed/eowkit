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

        var addressArg = NeedsExplicitBind(_host) ? $" --address={_host}" : string.Empty;
        var psi = new ProcessStartInfo(servePath, $"--port={_port}{addressArg} \"{zimPath}\"")
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
        for (int i=0;i<60;i++)
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
                var lines = File.ReadAllLines(cfgPath);
                var explicitBin = lines.FirstOrDefault(l => l.TrimStart().StartsWith("kiwix_serve_bin"));
                if (explicitBin is not null)
                {
                    var eq = explicitBin.IndexOf('=');
                    if (eq > 0)
                    {
                        var val = explicitBin[(eq+1)..].Trim().Trim('"');
                        if (File.Exists(val)) return val;
                    }
                }
                var line = lines.FirstOrDefault(l => l.TrimStart().StartsWith("kiwix_tools_dir"));
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
            var connectHost = ResolveClientHost(_host);
            var rootUrl = $"http://{connectHost}:{_port}/";
            using (var r1 = await _http.GetAsync(rootUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                var code = (int)r1.StatusCode;
                if (code >= 200 && code < 400) return true;
            }

            var url = $"http://{connectHost}:{_port}/search?pattern=test&content=html";
            using (var r2 = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                if (r2.IsSuccessStatusCode) return true;
            }
        }
        catch { return false; }
        return false;
    }

    public sealed record SearchHit(string Title, string Path);

    public async Task<List<SearchHit>> SearchAsync(string pattern, int k)
    {
        // Kiwix returns HTML; some builds return 400 for certain param combos. Try fallbacks.
        var connectHost = ResolveClientHost(_host);
        var encoded = Uri.EscapeDataString(pattern);
        var candidateUrls = new[]
        {
            $"http://{connectHost}:{_port}/search?pattern={encoded}&content=html",
            $"http://{connectHost}:{_port}/search?pattern={encoded}"
        };

        string? html = null;
        foreach (var u in candidateUrls)
        {
            try
            {
                html = await _http.GetStringAsync(u);
                if (!string.IsNullOrWhiteSpace(html)) break;
            }
            catch
            {
                // try next form
            }
        }

        if (string.IsNullOrWhiteSpace(html)) return new List<SearchHit>();
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
        var connectHost = ResolveClientHost(_host);
        var url = $"http://{connectHost}:{_port}{path}";
        return await _http.GetStringAsync(url);
    }

    static bool NeedsExplicitBind(string host)
    {
        var h = host?.Trim();
        if (string.IsNullOrEmpty(h)) return false; // let kiwix-serve bind to all by default
        // If host is a real address (not wildcard), pass it
        if (h == "0.0.0.0" || h == "*" || h == "::" || h == "[::]") return false;
        return true;
    }

    static string ResolveClientHost(string host)
    {
        var h = host?.Trim();
        if (string.IsNullOrEmpty(h) || h == "0.0.0.0" || h == "*" || h == "::" || h == "[::]")
            return "127.0.0.1";
        return h;
    }
}