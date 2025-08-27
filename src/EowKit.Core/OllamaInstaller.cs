using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace EowKit.Core;

public static class OllamaInstaller
{
    public static async Task EnsureOllamaAsync(string installDir, string downloadsDir)
    {
        var binName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ollama.exe" : "ollama";
        var targetBin = Path.Combine(installDir, binName);
        if (File.Exists(targetBin)) return;

        Directory.CreateDirectory(downloadsDir);
        Directory.CreateDirectory(installDir);

        var (url, isZip) = ComposeLatestUrl();
        var archivePath = await Downloader.DownloadWithCacheAsync(url, downloadsDir, sha256: null);

        var extractDir = Path.Combine(downloadsDir, "ollama-extract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractDir);

        if (isZip)
        {
            ZipFile.ExtractToDirectory(archivePath, extractDir);
        }
        else
        {
            var psi = new ProcessStartInfo("tar", $"-xzf \"{archivePath}\" -C \"{extractDir}\"")
            { UseShellExecute = false, CreateNoWindow = true };
            var p = Process.Start(psi)!; p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception("Failed to extract ollama archive with tar");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Prefer CLI archive (ollama-darwin.tgz) which contains 'ollama'
            var cli = Directory.GetFiles(extractDir, "ollama", SearchOption.AllDirectories).FirstOrDefault();
            if (cli is null)
            {
                // If user got the app zip, just place it under installDir and rely on PATH later
                var app = Directory.GetFiles(extractDir, "Ollama.app", SearchOption.AllDirectories).FirstOrDefault();
                if (app is not null)
                {
                    var destApp = Path.Combine(installDir, "Ollama.app");
                    CopyTree(Path.GetDirectoryName(app)!, destApp);
                }
                else
                {
                    throw new Exception("Could not find ollama CLI or app in extracted archive");
                }
            }
            else
            {
                File.Copy(cli, targetBin, overwrite: true);
                TryMakeExecutable(targetBin);
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var cli = Directory.GetFiles(extractDir, "ollama", SearchOption.AllDirectories)
                               .FirstOrDefault(p => Path.GetFileName(p) == "ollama");
            if (cli is null) throw new Exception("ollama binary not found in archive");
            File.Copy(cli, targetBin, overwrite: true);
            TryMakeExecutable(targetBin);

            // Copy lib/ollama if present
            var libDir = Directory.GetDirectories(extractDir, "ollama", SearchOption.AllDirectories)
                                  .FirstOrDefault(d => Path.GetFileName(Path.GetDirectoryName(d)!) == "lib");
            if (libDir is not null)
            {
                CopyTree(Path.GetDirectoryName(libDir)!, Path.Combine(installDir, "lib"));
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var exe = Directory.GetFiles(extractDir, "ollama.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe is null) throw new Exception("ollama.exe not found in archive");
            File.Copy(exe, targetBin, overwrite: true);

            var libRoot = Directory.GetDirectories(extractDir, "ollama", SearchOption.AllDirectories)
                                   .FirstOrDefault(d => Path.GetFileName(Path.GetDirectoryName(d)!) == "lib");
            if (libRoot is not null)
            {
                CopyTree(Path.GetDirectoryName(libRoot)!, Path.Combine(installDir, "lib"));
            }
        }
        else
        {
            throw new Exception("Unsupported OS for ollama installer");
        }
    }

    static (string url, bool isZip) ComposeLatestUrl()
    {
        var baseUrl = "https://github.com/ollama/ollama/releases/latest/download";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return ($"{baseUrl}/ollama-darwin.tgz", false);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
            return ($"{baseUrl}/ollama-linux-{arch}.tgz", false);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ($"{baseUrl}/ollama-windows-amd64.zip", true);
        throw new Exception("Unsupported OS");
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

    static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, dir);
            Directory.CreateDirectory(Path.Combine(dst, rel));
        }
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var dest = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }
}


