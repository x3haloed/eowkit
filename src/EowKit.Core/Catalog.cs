using Tomlyn;
using Tomlyn.Model;

namespace EowKit.Core;

public sealed class Catalog
{
    public List<ModelItem> Models { get; init; } = new();
    public List<WikiItem> Wikis { get; init; } = new();
    public List<SourceItem> Sources { get; init; } = new();

    public static Catalog Load(string path)
    {
        var doc = Toml.Parse(File.ReadAllText(path)).ToModel();
        var cat = new Catalog();

        foreach (TomlTable m in (TomlTableArray)doc["models"])
        {
            cat.Models.Add(new ModelItem(
                (string)m["id"], (string)m["runner"], (string)m["precision"],
                Convert.ToInt64(m["approx_bytes"]), Convert.ToInt64(m["min_ram_bytes"])
            ));
        }
        foreach (TomlTable w in (TomlTableArray)doc["wikis"])
        {
            cat.Wikis.Add(new WikiItem(
                (string)w["name"], (string)w["url"], Convert.ToInt64(w["approx_bytes"])
            ));
        }
        if (doc.TryGetValue("sources", out var sourcesObj))
        {
            foreach (TomlTable s in (TomlTableArray)sourcesObj!)
            {
                cat.Sources.Add(new SourceItem((string)s["label"], (string)s["url"]));
            }
        }
        return cat;
    }

    public sealed record ModelItem(string Id, string Runner, string Precision, long ApproxBytes, long MinRamBytes);
    public sealed record WikiItem(string Name, string Url, long ApproxBytes);
    public sealed record SourceItem(string Label, string Url);
}