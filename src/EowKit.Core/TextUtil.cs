using System.Text.RegularExpressions;

namespace EowKit.Core;

public static class TextUtil
{
    static readonly Regex HtmlTag = new("<.*?>", RegexOptions.Singleline | RegexOptions.Compiled);
    public static string HtmlToText(string html)
    {
        // cheap & cheerful for a terminal; good enough for summaries
        var text = HtmlTag.Replace(html, " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}