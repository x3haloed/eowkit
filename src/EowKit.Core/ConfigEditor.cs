namespace EowKit.Core;

public static class ConfigEditor
{
    // Set a key in a TOML section. Creates the section if missing.
    public static void SetInSection(string cfgPath, string section, string key, string valueWithQuotesWhenNeeded)
    {
        var lines = File.ReadAllLines(cfgPath).ToList();
        var secHeader = $"[{section}]";
        var iSec = lines.FindIndex(l => l.Trim() == secHeader);

        if (iSec < 0)
        {
            lines.Add("");
            lines.Add(secHeader);
            lines.Add($"{key} = {valueWithQuotesWhenNeeded}");
            File.WriteAllLines(cfgPath, lines);
            return;
        }

        var iEnd = lines.FindIndex(iSec + 1, l => l.TrimStart().StartsWith("[") && l.TrimEnd().EndsWith("]"));
        if (iEnd < 0) iEnd = lines.Count;

        for (int i = iSec + 1; i < iEnd; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith($"{key} "))
            {
                lines[i] = $"{key} = {valueWithQuotesWhenNeeded}";
                File.WriteAllLines(cfgPath, lines);
                return;
            }
        }

        lines.Insert(iEnd, $"{key} = {valueWithQuotesWhenNeeded}");
        File.WriteAllLines(cfgPath, lines);
    }
}


