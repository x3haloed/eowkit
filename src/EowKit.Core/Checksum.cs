using System.Net.Http;
using System.Security.Cryptography;

namespace EowKit.Core;

public static class Checksum
{
    public static async Task<string> Sha256FileAsync(string path)
    {
        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<string?> TryFetchSha256ForUrlAsync(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var candidates = new[] { url + ".sha256", url + ".sha256sum" };
            foreach (var u in candidates)
            {
                using var resp = await http.GetAsync(u);
                if (!resp.IsSuccessStatusCode) continue;
                var text = (await resp.Content.ReadAsStringAsync()).Trim();
                // take first 64-hex sequence if present (ignore filenames in common formats)
                var hex = new string(text.ToLowerInvariant().Where(c => Uri.IsHexDigit(c)).Take(64).ToArray());
                if (hex.Length == 64) return hex;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}


