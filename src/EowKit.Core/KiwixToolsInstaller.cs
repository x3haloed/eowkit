using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace EowKit.Core;

public static class KiwixToolsInstaller
{
    public static async Task EnsureKiwixServeAsync(string installDir, string downloadsDir)
    {
        var target = Path.Combine(installDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "kiwix-serve.exe" : "kiwix-serve");
        if (File.Exists(target)) return;

        Directory.CreateDirectory(downloadsDir);
        Directory.CreateDirectory(installDir);

        var (url, isZip, innerBinaryPath) = ComposeBestUrl();
        var archivePath = await Downloader.DownloadWithCacheAsync(url, downloadsDir, sha256: null);

        var extractDir = Path.Combine(downloadsDir, "kiwix-extract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractDir);

        if (isZip)
        {
            ZipFile.ExtractToDirectory(archivePath, extractDir);
        }
        else
        {
            // Use system tar for .tar.gz
            var psi = new ProcessStartInfo("tar", $"-xzf \"{archivePath}\" -C \"{extractDir}\"")
            { UseShellExecute = false, CreateNoWindow = true };
            var p = Process.Start(psi)!; p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception("Failed to extract kiwix-tools archive with tar");
        }

        // Find kiwix-serve inside extracted tree
        string? found = Directory.GetFiles(extractDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "kiwix-serve.exe" : "kiwix-serve", SearchOption.AllDirectories)
                                 .FirstOrDefault();

        if (found is null && !string.IsNullOrWhiteSpace(innerBinaryPath))
        {
            var candidate = Path.Combine(extractDir, innerBinaryPath);
            if (File.Exists(candidate)) found = candidate;
        }

        if (found is null)
            throw new Exception("kiwix-serve binary not found in extracted kiwix-tools archive");

        File.Copy(found, target, overwrite: true);
        TryMakeExecutable(target);
    }

    static (string url, bool isZip, string innerBinaryPath) ComposeBestUrl()
    {
        // Prefer latest known version; fall back to previous
        var versions = new[] { "3.7.0", "3.6.0" };

        foreach (var ver in versions)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x86_64";
                var url = $"https://download.kiwix.org/release/kiwix-tools/kiwix-tools_macos-{arch}-{ver}.tar.gz";
                return (url, false, "");
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var arch = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "x86_64",
                    Architecture.Arm64 => "aarch64",
                    Architecture.Arm => "armhf",
                    Architecture.X86 => "i586",
                    _ => "x86_64"
                };
                var url = $"https://download.kiwix.org/release/kiwix-tools/kiwix-tools_linux-{arch}-{ver}.tar.gz";
                return (url, false, "");
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Prefer 64-bit; fall back to i686
                var arch = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x86_64" : "i686";
                var url = $"https://download.kiwix.org/release/kiwix-tools/kiwix-tools_win-{arch}-{ver}.zip";
                return (url, true, "");
            }
        }

        throw new Exception("Unsupported OS for kiwix-tools download");
    }

    static void TryMakeExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var psi = new ProcessStartInfo("chmod", $"+x \"{path}\"") { UseShellExecute = false, CreateNoWindow = true };
            var p = Process.Start(psi)!; p.WaitForExit();
        }
        catch { }
    }
}


