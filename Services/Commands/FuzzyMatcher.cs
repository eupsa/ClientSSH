using System.Globalization;
using System.Text;

namespace ClientSSH.Services.Commands;

/// <summary>
/// Palette search: every query character must appear in order in the text. Matches at word starts and
/// runs of consecutive characters score higher. Ignores case, accents and spaces in the query.
/// </summary>
public static class FuzzyMatcher
{
    private const int Match = 1, Consecutive = 5, WordStart = 8, KeywordPenalty = 2;

    /// <summary>Null when the text does not contain the query as a subsequence.</summary>
    public static int? Score(string query, string text)
    {
        var q = Fold(query).Replace(" ", "");
        if (q.Length == 0) return 0;
        var t = Fold(text);
        // Preferring word starts can skip letters a later query character needed; plain greedy never misses a match.
        var preferred = Walk(q, t, preferWordStarts: true);
        var greedy = Walk(q, t, preferWordStarts: false);
        return preferred is null ? greedy : greedy is null ? preferred : Math.Max(preferred.Value, greedy.Value);
    }

    /// <summary>Commands matching the query, best first; ties keep registration order. Empty query: all, unchanged.</summary>
    public static IEnumerable<PaletteCommand> Rank(string query, IEnumerable<PaletteCommand> commands)
    {
        if (string.IsNullOrWhiteSpace(query)) return commands;
        return commands
            .Select(c => (Command: c, Score: Best(query, c)))
            .Where(x => x.Score is not null)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Command);
    }

    private static int? Best(string query, PaletteCommand command)
    {
        var title = Score(query, command.Title);
        var keywords = Score(query, command.Keywords) - KeywordPenalty;
        return title is null ? keywords : keywords is null ? title : Math.Max(title.Value, keywords.Value);
    }

    private static int? Walk(string q, string t, bool preferWordStarts)
    {
        int score = 0, from = 0, previous = -1;
        foreach (var c in q)
        {
            var at = t.IndexOf(c, from);
            if (at < 0) return null;
            if (preferWordStarts && at != previous + 1 && !IsWordStart(t, at))
            {
                for (var i = at + 1; i < t.Length; i++)
                    if (t[i] == c && IsWordStart(t, i)) { at = i; break; }
            }

            score += Match;
            if (at == previous + 1 && previous >= 0) score += Consecutive;
            if (IsWordStart(t, at)) score += WordStart;
            score -= Math.Min(at - previous - 1, 3); // small gap penalty, capped so long titles are not buried
            previous = at;
            from = at + 1;
        }

        return score;
    }

    private static bool IsWordStart(string t, int i) => i == 0 || !char.IsLetterOrDigit(t[i - 1]);

    // One char out per char in, so positions line up with the original text.
    private static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
            var letter = decomposed.FirstOrDefault(d => CharUnicodeInfo.GetUnicodeCategory(d) != UnicodeCategory.NonSpacingMark, c);
            sb.Append(char.ToLowerInvariant(letter));
        }

        return sb.ToString();
    }
}
