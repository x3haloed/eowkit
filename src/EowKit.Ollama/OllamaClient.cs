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
        var jsonText = await _http.GetStringAsync($"{_base}/api/tags");
        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;
        bool exists = false;
        if (root.TryGetProperty("models", out var modelsArray))
        {
            foreach (var m in modelsArray.EnumerateArray())
            {
                if (m.TryGetProperty("name", out var n) && string.Equals(n.GetString(), model, StringComparison.Ordinal))
                {
                    exists = true; break;
                }
            }
        }
        if (!exists)
        {
            var payload = "{\"name\":\"" + EscapeJsonString(model) + "\"}";
            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync($"{_base}/api/pull", content);
            resp.EnsureSuccessStatusCode();
        }
    }

    public async Task<string> ChatOnceAsync(string model, string prompt, int ctx, double temp, int? numThreads = null)
    {
        var payload = BuildChatRequestPayload(model, prompt, ctx, temp, numThreads);
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{_base}/api/chat", content);
        resp.EnsureSuccessStatusCode();
        var respText = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respText);
        var root = doc.RootElement;
        var message = root.TryGetProperty("message", out var msgEl) ? msgEl : default;
        var text = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var c) ? (c.GetString() ?? string.Empty) : string.Empty;
        return text.Trim();
    }

    // Helpers (no reflection)
    private static string EscapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new System.Text.StringBuilder(s.Length + 16);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(ch)) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string BuildChatRequestPayload(string model, string prompt, int ctx, double temp, int? numThreads)
    {
        var sb = new System.Text.StringBuilder(512);
        sb.Append("{\"model\":\"").Append(EscapeJsonString(model)).Append("\",");
        sb.Append("\"options\":{");
        sb.Append("\"num_ctx\":").Append(ctx).Append(',');
        sb.Append("\"temperature\":").Append(temp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (numThreads.HasValue)
        {
            sb.Append(',').Append("\"num_thread\":").Append(numThreads.Value);
        }
        sb.Append("},");
        sb.Append("\"messages\":[");
        sb.Append("{\"role\":\"system\",\"content\":\"").Append(EscapeJsonString("You are a concise, citation-first encyclopedia assistant.")).Append("\"},");
        sb.Append("{\"role\":\"user\",\"content\":\"").Append(EscapeJsonString(prompt)).Append("\"}");
        sb.Append("],");
        sb.Append("\"stream\":false");
        sb.Append('}');
        return sb.ToString();
    }
}