using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EowKit.Core;

public sealed class HardwareProbe
{
    public long TotalRamBytes { get; init; }

    public static async Task<HardwareProbe> ProbeAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new() { TotalRamBytes = GetWindowsTotalMem() };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new() { TotalRamBytes = await GetMacTotalMemAsync() };
        return new() { TotalRamBytes = GetLinuxTotalMem() };
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