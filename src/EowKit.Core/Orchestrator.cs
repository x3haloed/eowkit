using EowKit.Kiwix;
using EowKit.Ollama;

namespace EowKit.Core;

public sealed class Orchestrator
{
    private readonly Config _cfg;
    private readonly KiwixClient _kiwix;
    private readonly OllamaClient _ollama;
    private readonly IReranker? _reranker;

    public Orchestrator(Config cfg, KiwixClient kiwix, OllamaClient ollama)
    {
        _cfg = cfg; _kiwix = kiwix; _ollama = ollama;

        if (_cfg.Reranker.Enabled &&
            File.Exists(_cfg.Reranker.OnnxModel) &&
            File.Exists(_cfg.Reranker.TokenizerVocab))
        {
            _reranker = new OnnxCrossEncoderReranker(
                _cfg.Reranker.OnnxModel,
                _cfg.Reranker.TokenizerVocab,
                _cfg.Reranker.MaxSeqLen
            );
        }
    }

    public async Task<string> AnswerAsync(string question)
    {
        var hits = await _kiwix.SearchAsync(question, _cfg.Retrieval.K);
        if (hits.Count == 0) return "No support found in this offline snapshot.";

        List<(string title, string path, string text)> candidates;
        if (_reranker is not null)
        {
            var N = Math.Min(20, hits.Count);
            var prelim = new List<(string title, string path, string text)>(N);
            for (int i = 0; i < N; i++)
            {
                var html = await _kiwix.GetContentHtmlAsync(hits[i].Path);
                var text = TextUtil.HtmlToText(html);
                prelim.Add((hits[i].Title, hits[i].Path, text.Length > 2000 ? text[..2000] : text));
            }

            var scored = await _reranker.ScoreAsync(question, prelim.Select(p => p.text).ToList());
            var order = scored.OrderByDescending(s => s.score).Select(s => s.index).ToList();
            candidates = order.Select(ix => prelim[ix]).ToList();
        }
        else
        {
            candidates = hits.Select(h =>
            {
                var html = _kiwix.GetContentHtmlAsync(h.Path).GetAwaiter().GetResult();
                var text = TextUtil.HtmlToText(html);
                return (h.Title, h.Path, text);
            }).ToList();
        }

        var articles = candidates.Take(_cfg.Retrieval.MaxArticles)
                                .Select(a => (a.title, text: a.text.Length > 6000 ? a.text[..6000] : a.text))
                                .ToList();

        // 3) craft messages with system + retrieved context
        var system = _cfg.Prompt.System.Trim();
        var context = string.Join("\n\n", articles.Select(a => $"# {a.title}\n{a.text}"));
        var prompt = $"{system}\n\nRetrieved context (from local Wikipedia):\n{context}\n\nQuestion: {question}";

        var reply = await _ollama.ChatOnceAsync(_cfg.Model.Ollama, prompt, _cfg.Llm.ContextTokens, _cfg.Llm.Temperature);

        // 4) add human-friendly citations (titles only, local snapshot)
        var cites = "Sources: " + string.Join("; ", articles.Select(a => a.title));
        return reply + "\n\n" + cites;
    }
}