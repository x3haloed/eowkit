using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EowKit.Core;

public sealed class HardwareProbe
{
    public long TotalRamBytes { get; init; }
    public bool HasAvx2 { get; init; }
    public bool HasCuda { get; init; }
    public bool HasOpenCl { get; init; }
    public bool HasMetal { get; init; }
    public int LogicalCores { get; init; }

    public static async Task<HardwareProbe> ProbeAsync()
    {
        long ram;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ram = GetWindowsTotalMem();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            ram = await GetMacTotalMemAsync();
        else
            ram = GetLinuxTotalMem();

        return new()
        {
            TotalRamBytes = ram,
            HasAvx2 = System.Runtime.Intrinsics.X86.Avx2.IsSupported,
            HasCuda = DetectCuda(),
            HasOpenCl = DetectOpenCl(),
            HasMetal = DetectMetal(),
            LogicalCores = Environment.ProcessorCount
        };
    }

    static long GetLinuxTotalMem()
    {
        // /proc/meminfo MemTotal: kB
        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:"))
            {
                var kb = long.Parse(new string(line.Where(char.IsDigit).ToArray()));
                return kb * 1024;
            }
        }
        return 0;
    }

    static async Task<long> GetMacTotalMemAsync()
    {
        // Use sysctl -n hw.memsize to avoid P/Invoke complexity
        var psi = new ProcessStartInfo("sysctl", "-n hw.memsize") { RedirectStandardOutput = true };
        var p = Process.Start(psi)!;
        var s = await p.StandardOutput.ReadToEndAsync();
        p.WaitForExit();
        return long.TryParse(s.Trim(), out var bytes) ? bytes : 0;
    }

    static long GetWindowsTotalMem()
    {
        // GlobalMemoryStatusEx
        MEMORYSTATUSEX memStat = new() { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref memStat);
        return (long)memStat.ullTotalPhys;
    }

    static bool DetectCuda()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return NativeLibrary.TryLoad("nvcuda.dll", out _);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return NativeLibrary.TryLoad("libcuda.so.1", out _) || NativeLibrary.TryLoad("libcuda.so", out _);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return false;
        return false;
    }

    static bool DetectOpenCl()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return NativeLibrary.TryLoad("OpenCL.dll", out _);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return NativeLibrary.TryLoad("libOpenCL.so.1", out _) || NativeLibrary.TryLoad("libOpenCL.so", out _);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return NativeLibrary.TryLoad("/System/Library/Frameworks/OpenCL.framework/OpenCL", out _);
        return false;
    }

    static bool DetectMetal()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return false;
        return NativeLibrary.TryLoad("/System/Library/Frameworks/Metal.framework/Metal", out _);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}