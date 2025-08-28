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
    private Process? _serveProcess;

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
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["OLLAMA_DEBUG"] = "ERROR";
        if (!string.IsNullOrWhiteSpace(_modelsDir))
        {
            Directory.CreateDirectory(_modelsDir);
            psi.Environment["OLLAMA_MODELS"] = _modelsDir;
        }
        var p = Process.Start(psi)!;
        _serveProcess = p;
        // Discard output to avoid clutter and full buffers
        p.OutputDataReceived += (_, __) => { };
        p.ErrorDataReceived += (_, __) => { };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

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

    public async Task EnsureModelAsync(string model, Action<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(model)) return;
        if (await ModelExistsAsync(model)) return;

        // Stream pull progress (throttled)
        var pullPayload = "{\"name\":\"" + EscapeJsonString(model) + "\",\"stream\":true}";
        using (var pullContent = new StringContent(pullPayload, System.Text.Encoding.UTF8, "application/json"))
        using (var request = new HttpRequestMessage(HttpMethod.Post, $"{_base}/api/pull") { Content = pullContent })
        using (var resp = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            using var s = await resp.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(s);
            string? line;
            string lastDigest = string.Empty;
            string lastStatus = string.Empty;
            double lastPct = -1.0;
            var lastEmit = DateTime.MinValue;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    string status = root.TryGetProperty("status", out var st) ? (st.GetString() ?? "") : "";
                    string digest = root.TryGetProperty("digest", out var dg) ? (dg.GetString() ?? "") : "";
                    long completed = root.TryGetProperty("completed", out var cm) ? cm.GetInt64() : 0;
                    long total = root.TryGetProperty("total", out var tt) ? tt.GetInt64() : 0;
                    string err = root.TryGetProperty("error", out var er) ? (er.GetString() ?? "") : "";

                    if (!string.IsNullOrEmpty(err))
                    {
                        progress?.Invoke($"pull error: {err}");
                        continue;
                    }

                    bool shouldEmit = false;
                    if (total > 0)
                    {
                        var pct = completed * 100.0 / total;
                        if (Math.Abs(pct - lastPct) >= 1.0) { shouldEmit = true; lastPct = pct; }
                    }
                    if (!shouldEmit && (!string.Equals(status, lastStatus, StringComparison.Ordinal) || !string.Equals(digest, lastDigest, StringComparison.Ordinal)))
                    {
                        shouldEmit = true;
                    }
                    if (!shouldEmit && (DateTime.UtcNow - lastEmit).TotalSeconds >= 2)
                    {
                        shouldEmit = true;
                    }

                    if (shouldEmit)
                    {
                        lastEmit = DateTime.UtcNow;
                        lastStatus = status; lastDigest = digest;
                        if (total > 0)
                        {
                            progress?.Invoke($"pull {status} {digest} {completed}/{total} ({(completed*100.0/total):F1}%)");
                        }
                        else if (!string.IsNullOrEmpty(status) || !string.IsNullOrEmpty(digest))
                        {
                            progress?.Invoke($"pull {status} {digest}");
                        }
                    }
                }
                catch
                {
                    // rare non-JSON line; throttle
                    if ((DateTime.UtcNow - lastEmit).TotalSeconds >= 2)
                    {
                        lastEmit = DateTime.UtcNow;
                        progress?.Invoke(line);
                    }
                }
            }
        }

        // Poll until model appears (up to ~10 minutes)
        for (int i = 0; i < 600; i++)
        {
            await Task.Delay(1000);
            try
            {
                if (await ModelExistsAsync(model)) return;
            }
            catch { /* ignore and keep waiting */ }
        }
        throw new Exception($"Timeout waiting for Ollama to download model '{model}'.");
    }

    private async Task<bool> ModelExistsAsync(string model)
    {
        var payload = "{\"name\":\"" + EscapeJsonString(model) + "\"}";
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{_base}/api/show", content);
        if (resp.IsSuccessStatusCode) return true;
        if ((int)resp.StatusCode == 404) return false;
        return false;
    }

    public async Task<string> ChatOnceAsync(string model, string prompt, int ctx, double temp, int? numThreads = null)
    {
        // First try the chat endpoint
        var chatPayload = BuildChatRequestPayload(model, prompt, ctx, temp, numThreads);
        using (var chatContent = new StringContent(chatPayload, System.Text.Encoding.UTF8, "application/json"))
        using (var chatResp = await _http.PostAsync($"{_base}/api/chat", chatContent))
        {
            if (chatResp.IsSuccessStatusCode)
            {
                var respText = await chatResp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(respText);
                var root = doc.RootElement;
                var message = root.TryGetProperty("message", out var msgEl) ? msgEl : default;
                var text = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var c) ? (c.GetString() ?? string.Empty) : string.Empty;
                return text.Trim();
            }
            // If endpoint missing, fall back
            if ((int)chatResp.StatusCode != 404)
            {
                chatResp.EnsureSuccessStatusCode();
            }
        }

        // Fallback: use /api/generate with a single prompt and non-stream
        var sb = new System.Text.StringBuilder(512);
        sb.Append("{\"model\":\"").Append(EscapeJsonString(model)).Append("\",");
        sb.Append("\"prompt\":\"").Append(EscapeJsonString(prompt)).Append("\",");
        sb.Append("\"options\":{");
        sb.Append("\"num_ctx\":").Append(ctx).Append(',');
        sb.Append("\"temperature\":").Append(temp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (numThreads.HasValue)
        {
            sb.Append(',').Append("\"num_thread\":").Append(numThreads.Value);
        }
        sb.Append("},");
        sb.Append("\"stream\":false");
        sb.Append('}');
        var genPayload = sb.ToString();

        using var genContent = new StringContent(genPayload, System.Text.Encoding.UTF8, "application/json");
        using var genResp = await _http.PostAsync($"{_base}/api/generate", genContent);
        genResp.EnsureSuccessStatusCode();
        var genText = await genResp.Content.ReadAsStringAsync();
        using (var genDoc = JsonDocument.Parse(genText))
        {
            var root = genDoc.RootElement;
            var text = root.TryGetProperty("response", out var r) ? (r.GetString() ?? string.Empty) : string.Empty;
            return text.Trim();
        }
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