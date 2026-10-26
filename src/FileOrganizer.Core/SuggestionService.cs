namespace FileOrganizer.Core;

/// <summary>
/// Phase 2 offline destination suggestions. Pure C# — no file system, no network.
/// A destination is suggested from three offline signals only:
/// 1. Filename tokens matching the destination's name or path (e.g. "CV" in
///    "my-cv.pdf" matching "Jobs/CV").
/// 2. History: the same file extension was moved to that destination before.
/// 3. Favourites and recent use, which break ties toward destinations the user
///    already trusts.
/// Nothing is invented: a destination with no signal scores zero and is never
/// suggested. The wheel shows the top suggestion as a glowing petal; the rest
/// of the wheel keeps its normal favourites-first, recently-used order.
/// </summary>
public static class SuggestionService
{
    public sealed record HistorySample(string FileName, string DestinationId);

    public sealed record Suggestion(Destination Destination, int Score, string Reason);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "my", "new", "final", "copy", "doc", "file", "v1", "v2",
    };

    /// <summary>Returns at most <paramref name="maxSuggestions"/> suggestions, best first. Empty when nothing matches.</summary>
    public static IReadOnlyList<Suggestion> Suggest(
        IReadOnlyList<Destination> destinations,
        IReadOnlyList<string> filePaths,
        IReadOnlyList<HistorySample> history,
        int maxSuggestions = 3)
    {
        if (destinations.Count == 0 || filePaths.Count == 0 || maxSuggestions <= 0)
            return Array.Empty<Suggestion>();

        var fileNames = filePaths.Select(p => Path.GetFileName(p) ?? p).ToList();
        var extensions = fileNames
            .Select(f => Path.GetExtension(f).TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length > 0)
            .ToList();
        var fileTokens = fileNames.SelectMany(Tokenize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Extension -> destination ids, from history, most recent first is not
        // needed: every past move of that extension counts equally.
        var extensionHistory = history
            .Where(h => !string.IsNullOrWhiteSpace(h.FileName))
            .GroupBy(h => Path.GetExtension(h.FileName).TrimStart('.').ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Select(h => h.DestinationId).ToList());

        var results = new List<Suggestion>();
        foreach (var destination in destinations)
        {
            var score = 0;
            var reasons = new List<string>();

            var destTokens = Tokenize(destination.Name)
                .Concat(Tokenize(destination.Path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var tokenMatches = fileTokens.Intersect(destTokens, StringComparer.OrdinalIgnoreCase).ToList();
            if (tokenMatches.Count > 0)
            {
                score += 50 * tokenMatches.Count;
                reasons.Add($"Matches '{tokenMatches[0]}' in the file name");
            }

            var historyMatches = extensions
                .Where(ext => extensionHistory.TryGetValue(ext, out var ids) && ids.Contains(destination.Id))
                .ToList();
            if (historyMatches.Count > 0)
            {
                score += 30 * historyMatches.Count;
                reasons.Add($"You sent .{historyMatches[0]} files here before");
            }

            // Favourites and recency only break ties between destinations that
            // already have a real signal — a favourite alone is not a suggestion,
            // otherwise every file would "suggest" the favourite destination.
            if (score == 0) continue;

            if (destination.IsFavorite)
            {
                score += 10;
                reasons.Add("Favourite");
            }

            if (destination.LastUsedUtc is DateTime lastUsed)
            {
                var days = (DateTime.UtcNow - lastUsed).TotalDays;
                if (days <= 7) { score += 15; reasons.Add("Used recently"); }
                else if (days <= 30) { score += 5; reasons.Add("Used this month"); }
            }

            results.Add(new Suggestion(destination, score, reasons[0]));
        }

        return results
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.Destination.IsFavorite)
            .ThenByDescending(s => s.Destination.LastUsedUtc ?? DateTime.MinValue)
            .Take(maxSuggestions)
            .ToList();
    }

    /// <summary>Lowercase word tokens, with plural endings normalised ("pdfs" matches "pdf").</summary>
    internal static List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                AddToken(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0) AddToken(current.ToString());
        return tokens;

        void AddToken(string token)
        {
            if (token.Length < 2 || StopWords.Contains(token)) return;
            tokens.Add(token);
            // Plural normalisation: "jobs" also registers as "job", "pdfs" as "pdf".
            if (token.Length > 3 && token.EndsWith('s'))
                tokens.Add(token[..^1]);
        }
    }
}
