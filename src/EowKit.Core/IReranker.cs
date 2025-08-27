namespace EowKit.Core;

public interface IReranker
{
    Task<List<(int index, float score)>> ScoreAsync(string query, List<string> docs);
}


