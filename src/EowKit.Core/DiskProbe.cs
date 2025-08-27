namespace EowKit.Core;

public static class DiskProbe
{
    public static long GetFreeBytes(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "/";
        var di = new DriveInfo(root);
        return di.AvailableFreeSpace;
    }
}