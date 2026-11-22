namespace LinkDeck.Core.Search;

/// <summary>
/// Case-insensitive subsequence matcher. Higher scores are better; <c>null</c> means no match.
/// Rewards prefix matches, word-start matches and consecutive runs.
/// </summary>
public static class FuzzyMatcher
{
    public static int? Score(string query, string text)
    {
        if (string.IsNullOrEmpty(query))
        {
            return 0;
        }

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var score = 0;
        var ti = 0;
        var previousMatch = -2;
        var run = 0;

        foreach (var qc in query)
        {
            if (char.IsWhiteSpace(qc))
            {
                continue;
            }

            var found = -1;
            for (var i = ti; i < text.Length; i++)
            {
                if (char.ToUpperInvariant(text[i]) == char.ToUpperInvariant(qc))
                {
                    found = i;
                    break;
                }
            }

            if (found < 0)
            {
                return null;
            }

            var points = 1;
            if (found == 0)
            {
                points += 8;
            }
            else if (IsWordStart(text, found))
            {
                points += 6;
            }

            if (found == previousMatch + 1)
            {
                run++;
                points += 4 * run;
            }
            else
            {
                run = 0;
                points -= Math.Min(found - ti, 3);
            }

            score += points;
            previousMatch = found;
            ti = found + 1;
        }

        // Exact (case-insensitive) and prefix bonuses.
        if (string.Equals(query.Trim(), text, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }
        else if (text.StartsWith(query.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        return score;
    }

    private static bool IsWordStart(string text, int index)
    {
        var prev = text[index - 1];
        var cur = text[index];
        return !char.IsLetterOrDigit(prev) || (char.IsLower(prev) && char.IsUpper(cur));
    }
}
