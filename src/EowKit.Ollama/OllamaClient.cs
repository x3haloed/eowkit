using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EowKit.Ollama;

public sealed class OllamaClient
{
    private readonly HttpClient _http = new();
    private readonly string _base;
    private readonly string? _modelsDir;

    public OllamaClient(string baseUrl, string? modelsDir = null)
    {
        _base = baseUrl.TrimEnd('/');
        _http.Timeout = TimeSpan.FromMinutes(5);
        _modelsDir = string.IsNullOrWhiteSpace(modelsDir) ? null : modelsDir;
    }

    public async Task EnsureServeAsync()
    {
        // Try a ping; if fail, start `ollama serve`
        if (await PingAsync()) return;

        var exe = ResolveOllamaPath() ?? "ollama";
        var psi = new ProcessStartInfo(exe, "serve")
        {
            UseShellExecute = false, CreateNoWindow = true
        };
        if (!string.IsNullOrWhiteSpace(_modelsDir))
        {
            Directory.CreateDirectory(_modelsDir);
            psi.Environment["OLLAMA_MODELS"] = _modelsDir;
        }
        Process.Start(psi);

        for (int i=0;i<30;i++)
        {
            if (await PingAsync()) return;
            await Task.Delay(500);
        }
        throw new Exception("ollama serve did not start");
    }

    static string? ResolveOllamaPath()
    {
        // Look next to app config paths. We expect installer wrote paths.ollama_dir
        try
        {
            var cfgPath = Path.Combine(AppContext.BaseDirectory, "configs", "eowkit.toml");
            if (File.Exists(cfgPath))
            {
                var lines = File.ReadAllLines(cfgPath);
                var explicitBin = lines.FirstOrDefault(x => x.TrimStart().StartsWith("ollama_bin"));
                if (explicitBin is not null)
                {
                    var eq = explicitBin.IndexOf('=');
                    if (eq > 0)
                    {
                        var root = explicitBin[(eq+1)..].Trim().Trim('"');
                        if (File.Exists(root)) return root;
                    }
                }
                var l = lines.FirstOrDefault(x => x.TrimStart().StartsWith("ollama_dir"));
                if (l is not null)
                {
                    var eq = l.IndexOf('=');
                    if (eq > 0)
                    {
                        var root = l[(eq+1)..].Trim().Trim('"');
                        var bin = Path.Combine(root, OperatingSystem.IsWindows() ? "ollama.exe" : "ollama");
                        if (File.Exists(bin)) return bin;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    async Task<bool> PingAsync()
    {
        try
        {
            using var r = await _http.GetAsync($"{_base}/api/tags");
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task EnsureModelAsync(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return;
        // naive check: list tags
        var tags = await _http.GetFromJsonAsync<JsonElement>($"{_base}/api/tags");
        var exists = tags.TryGetProperty("models", out var arr) &&
                     arr.EnumerateArray().Any(m => m.TryGetProperty("name", out var n) && n.GetString() == model);
        if (!exists)
        {
            var req = new { name = model };
            using var resp = await _http.PostAsJsonAsync($"{_base}/api/pull", req);
            resp.EnsureSuccessStatusCode();
        }
    }

    public async Task<string> ChatOnceAsync(string model, string prompt, int ctx, double temp, int? numThreads = null)
    {
        var req = new
        {
            model,
            options = new { num_ctx = ctx, temperature = temp, num_thread = numThreads },
            messages = new[]
            {
                new { role = "system", content = "You are a concise, citation-first encyclopedia assistant." },
                new { role = "user", content = prompt }
            },
            stream = false
        };

        using var resp = await _http.PostAsJsonAsync($"{_base}/api/chat", req);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var content = json.GetProperty("message").GetProperty("content").GetString() ?? "";
        return content.Trim();
    }
}