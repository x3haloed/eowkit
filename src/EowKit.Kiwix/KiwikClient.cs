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

        var psi = new ProcessStartInfo("kiwix-serve", $"--port={_port} \"{zimPath}\"")
        {
            UseShellExecute = false, CreateNoWindow = true
        };
        Process.Start(psi);

        // wait a moment for server to bind
        for (int i=0;i<15;i++)
        {
            if (await IsAliveAsync()) return;
            await Task.Delay(500);
        }
        throw new Exception("kiwix-serve did not start");
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