using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace EowKit.Core;

// Cross-encodes [CLS] query [SEP] doc [SEP], returns logits[0] as relevance score
public sealed class OnnxCrossEncoderReranker : IReranker, IDisposable
{
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tok;
    private readonly int _maxLen;

    public OnnxCrossEncoderReranker(string onnxPath, string vocabPath, int maxLen)
    {
        _session = new InferenceSession(onnxPath, SessionOptionsExtensions.MakeSessionOptionWithCudaProviderOrNull() ?? new SessionOptions());
        _tok = BertTokenizer.Create(vocabPath, new BertOptions{ LowerCaseBeforeTokenization = true });
        _maxLen = maxLen;
    }

    public async Task<List<(int index, float score)>> ScoreAsync(string query, List<string> docs)
    {
        var results = new List<(int idx, float score)>(docs.Count);
        const int BATCH = 8;

        for (int i = 0; i < docs.Count; i += BATCH)
        {
            var batch = docs.Skip(i).Take(BATCH).ToList();
            var (ids, attn, type) = EncodeBatch(query, batch, _maxLen);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", ids),
                NamedOnnxValue.CreateFromTensor("attention_mask", attn),
                NamedOnnxValue.CreateFromTensor("token_type_ids", type)
            };

            using var run = _session.Run(inputs);
            var logits = run[0].AsEnumerable<float>().ToArray();

            for (int j = 0; j < batch.Count; j++)
                results.Add((i + j, logits[j]));
        }
        await Task.CompletedTask;
        return results;
    }

    (DenseTensor<long> ids, DenseTensor<long> attn, DenseTensor<long> type)
    EncodeBatch(string query, List<string> docs, int maxLen)
    {
        var batchSize = docs.Count;
        var ids  = new DenseTensor<long>([batchSize, maxLen]);
        var attn = new DenseTensor<long>([batchSize, maxLen]);
        var type = new DenseTensor<long>([batchSize, maxLen]);

        for (int b = 0; b < batchSize; b++)
        {
            var encQ = _tok.EncodeToIds(query, addSpecialTokens: false);
            var encD = _tok.EncodeToIds(docs[b], addSpecialTokens: false);

            long cls = _tok.ClassificationTokenId;
            long sep = _tok.SeparatorTokenId;
            long pad = _tok.PaddingTokenId;

            int room = maxLen - 3;
            int qLen = Math.Min(encQ.Count, room / 3);
            int dLen = Math.Min(encD.Count, room - qLen);

            var seq = new List<long>(maxLen);
            var tokType = new List<long>(maxLen);

            seq.Add(cls); tokType.Add(0);
            seq.AddRange(encQ.Take(qLen).Select(i => (long)i)); tokType.AddRange(Enumerable.Repeat(0L, qLen));
            seq.Add(sep); tokType.Add(0);

            seq.AddRange(encD.Take(dLen).Select(i => (long)i)); tokType.AddRange(Enumerable.Repeat(1L, dLen));
            seq.Add(sep); tokType.Add(1);

            while (seq.Count < maxLen) { seq.Add(pad); tokType.Add(0); }
            var mask = seq.Select(x => x == pad ? 0L : 1L).ToArray();

            for (int t = 0; t < maxLen; t++)
            {
                ids[b, t]  = seq[t];
                attn[b, t] = mask[t];
                type[b, t] = tokType[t];
            }
        }
        return (ids, attn, type);
    }

    public void Dispose() => _session.Dispose();

    static class SessionOptionsExtensions
    {
        public static SessionOptions? MakeSessionOptionWithCudaProviderOrNull()
        {
            try
            {
                var so = new SessionOptions();
                return so;
            }
            catch { return null; }
        }
    }
}


