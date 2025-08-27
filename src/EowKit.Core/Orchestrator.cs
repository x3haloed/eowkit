using EowKit.Kiwix;
using EowKit.Ollama;

namespace EowKit.Core;

public sealed class Orchestrator
{
    private readonly Config _cfg;
    private readonly KiwixClient _kiwix;
    private readonly OllamaClient _ollama;

    public Orchestrator(Config cfg, KiwixClient kiwix, OllamaClient ollama)
    {
        _cfg = cfg; _kiwix = kiwix; _ollama = ollama;
    }

    public async Task<string> AnswerAsync(string question)
    {
        // 1) lexical recall via Kiwix
        var hits = await _kiwix.SearchAsync(question, _cfg.Retrieval.K);
        if (hits.Count == 0)
            return "No support found in this offline snapshot.";

        // 2) fetch top N articles (respect MaxArticles)
        var articles = new List<(string title, string text)>();
        foreach (var h in hits.Take(_cfg.Retrieval.MaxArticles))
        {
            var html = await _kiwix.GetContentHtmlAsync(h.Path);
            var text = TextUtil.HtmlToText(html);
            if (!string.IsNullOrWhiteSpace(text))
                articles.Add((h.Title, text));
        }

        // 3) craft messages with system + retrieved context
        var system = _cfg.Prompt.System.Trim();
        var context = string.Join("\n\n", articles.Select(a => $"# {a.title}\n{a.text[..Math.Min(a.text.Length, 6000)]}"));
        var prompt = $"{system}\n\nRetrieved context (from local Wikipedia):\n{context}\n\nQuestion: {question}";

        var reply = await _ollama.ChatOnceAsync(_cfg.Model.Ollama, prompt, _cfg.Llm.ContextTokens, _cfg.Llm.Temperature);

        // 4) add human-friendly citations (titles only, local snapshot)
        var cites = "Sources: " + string.Join("; ", articles.Select(a => a.title));
        return reply + "\n\n" + cites;
    }
}