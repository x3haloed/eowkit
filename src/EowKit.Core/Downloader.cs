using System.Net.Http.Headers;
using System.Security.Cryptography;
using Spectre.Console;

namespace EowKit.Core;

public static class ResumableFetcher
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromHours(6)
    };

    public static async Task DownloadAsync(string url, string destPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        long existing = 0;
        if (File.Exists(destPath))
        {
            var info = new FileInfo(destPath);
            existing = info.Length;
        }

        using var head = new HttpRequestMessage(HttpMethod.Head, url);
        using var headResp = await Http.SendAsync(head);
        headResp.EnsureSuccessStatusCode();
        var total = headResp.Content.Headers.ContentLength ?? 0;

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0 && existing < total)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var fs = new FileStream(destPath, FileMode.Append, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);

        var buffer = new byte[1 << 20];
        long written = existing;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read));
            written += read;
            if (sw.ElapsedMilliseconds > 1000)
            {
                sw.Restart();
                var pct = total > 0 ? (written * 100.0 / total) : 0;
                AnsiConsole.MarkupLine($"Downloaded {written/1_000_000_000.0:F1} / {total/1_000_000_000.0:F1} GB ({pct:F1}%)");
            }
        }
    }
}

public static class Sha256Verifier
{
    public static async Task<bool> VerifyAsync(string path, string expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex)) return true;
        if (!File.Exists(path)) return false;

        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return hex == expectedHex.ToLowerInvariant();
    }
}

public static class Downloader
{
    // Downloads to targetDir with caching by filename; verifies SHA256 if provided. Returns absolute path.
    public static async Task<string> DownloadWithCacheAsync(string url, string targetDir, string? sha256)
    {
        Directory.CreateDirectory(targetDir);
        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        var dest = Path.GetFullPath(Path.Combine(targetDir, fileName));

        if (!File.Exists(dest))
        {
            await ResumableFetcher.DownloadAsync(url, dest);
        }

        if (!string.IsNullOrWhiteSpace(sha256))
        {
            var ok = await Sha256Verifier.VerifyAsync(dest, sha256!);
            if (!ok)
            {
                File.Delete(dest);
                await ResumableFetcher.DownloadAsync(url, dest);
                ok = await Sha256Verifier.VerifyAsync(dest, sha256!);
                if (!ok) throw new Exception($"SHA256 mismatch for {fileName}");
            }
        }

        return dest;
    }
}


