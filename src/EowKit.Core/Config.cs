using Tomlyn;
using Tomlyn.Model;

namespace EowKit.Core;

public sealed class Config
{
    public ModelSection Model { get; init; } = new();
    public MobileSection Mobile { get; init; } = new();
    public WikiSection Wiki { get; init; } = new();
    public LlmSection Llm { get; init; } = new();
    public RetrievalSection Retrieval { get; init; } = new();
    public PromptSection Prompt { get; init; } = new();

    public static Config Load(string path)
    {
        var text = File.ReadAllText(path);
        var doc = Tomlyn.Toml.Parse(text).ToModel();
        var model = (TomlTable)doc["model"];

        var mobile = (TomlTable)doc["mobile"];

        var wiki = (TomlTable)doc["wiki"];

        var llm = (TomlTable)doc["llm"];

        var ret = (TomlTable)doc["retrieval"];

        var prompt = (TomlTable)doc["prompt"];

        var cfg = new Config
        {
            Model = new ModelSection { Ollama = (string?)model["ollama"] ?? "" },
            Mobile = new MobileSection { MlcModel = (string?)mobile["mlc_model"] ?? "" },
            Wiki = new WikiSection
            {
                Zim = (string?)wiki["zim"] ?? "",
                KiwixPort = Convert.ToInt32(wiki["kiwix_port"]),
                Bind = (string?)wiki["bind"] ?? "127.0.0.1"
            },
            Llm = new LlmSection
            {
                OllamaUrl = (string?)llm["ollama_url"] ?? "http://127.0.0.1:11434",
                ContextTokens = Convert.ToInt32(llm["context_tokens"]),
                Temperature = Convert.ToDouble(llm["temperature"])
            },
            Retrieval = new RetrievalSection
            {
                K = Convert.ToInt32(ret["k"]),
                MaxArticles = Convert.ToInt32(ret["max_articles"]),
                Rerank = Convert.ToBoolean(ret["rerank"])
            },
            Prompt = new PromptSection { System = (string?)prompt["system"] ?? "" }
        };

        return cfg;
    }

    public sealed record ModelSection { public string Ollama { get; init; } = ""; }
    public sealed record MobileSection { public string MlcModel { get; init; } = ""; }
    public sealed record WikiSection { public string Zim { get; init; } = ""; public int KiwixPort { get; init; } = 8080; public string Bind { get; init; } = "127.0.0.1"; }
    public sealed record LlmSection { public string OllamaUrl { get; init; } = "http://127.0.0.1:11434"; public int ContextTokens { get; init; } = 4096; public double Temperature { get; init; } = 0.2; }
    public sealed record RetrievalSection { public int K { get; init; } = 40; public int MaxArticles { get; init; } = 5; public bool Rerank { get; init; } = false; }
    public sealed record PromptSection { public string System { get; init; } = ""; }
}