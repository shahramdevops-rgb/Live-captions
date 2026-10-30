using System;
using System.Collections.Generic;
using System.Text;

namespace LiveCaptionsGemini.Captions;

/// <summary>
/// Finds the caption text that appeared after the anchor (the full caption text at the last send/skip).
/// Live Captions is a rolling window: old lines scroll away and recent words keep being corrected
/// (including punctuation and capitals), so we locate a tail of the anchor in the current text
/// instead of comparing prefixes. Matching ignores case and punctuation.
/// Losing text that scrolled away, or a one-word duplicate at the boundary, is acceptable;
/// losing text in the middle is not, so ambiguous matches prefer sending too much.
/// </summary>
internal static class AnchorText
{
    private static readonly int[] TailLengths = { 80, 48, 28, 16 };
    private const int MaxDroppedWords = 3;
    // Shorter tails match by accident too easily (e.g. "the").
    private const int MinTailChars = 8;

    private static readonly char[] LeadingJunk = { '.', ',', ';', ':', '!', '?', '-', '…', '#', '+' };

    /// <param name="current">Normalized caption text now.</param>
    /// <param name="anchor">Normalized caption text at the last mark; null = everything is new.</param>
    public static string Extract(string current, string? anchor)
    {
        if (string.IsNullOrEmpty(current)) return "";
        if (string.IsNullOrEmpty(anchor)) return current;

        var cur = Skeleton.Of(current);
        var a = Skeleton.Of(anchor).Text;
        if (a.Length == 0) return current;
        if (cur.Text == a) return ""; // at most punctuation/capitals changed

        var anchorLength = a.Length;
        for (var dropped = 0; dropped <= MaxDroppedWords; dropped++)
        {
            var previousLength = -1;
            foreach (var length in TailLengths)
            {
                var tail = a.Length <= length ? a : a[^length..];
                if (tail.Length == previousLength) continue; // anchor shorter than several tail sizes
                previousLength = tail.Length;

                // A very short anchor is still usable as a whole, but not a short leftover after dropping words.
                if (tail.Length < MinTailChars && (dropped > 0 || tail.Length != anchorLength)) continue;

                var end = FindBest(cur.Text, tail, a[..^tail.Length]);
                if (end >= 0) return Clean(current[cur.OriginalEnd(end)..]);
            }

            // The last word may have been corrected since the mark: drop it and retry.
            var space = a.LastIndexOf(' ');
            if (space <= 0) break;
            a = a[..space];
        }

        // The anchor scrolled out entirely.
        return current;
    }

    /// <summary>
    /// End (exclusive, skeleton index) of the occurrence of <paramref name="tail"/> whose preceding text
    /// matches <paramref name="before"/> best; ties go to the earlier occurrence. -1 if none.
    /// Only occurrences ending on a word boundary count ("ba" must not match inside "bank").
    /// </summary>
    private static int FindBest(string text, string tail, string before)
    {
        int best = -1, bestScore = -1;
        for (var i = text.IndexOf(tail, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(tail, i + 1, StringComparison.Ordinal))
        {
            var end = i + tail.Length;
            if (end < text.Length && text[end] != ' ') continue;

            var score = CommonSuffixLength(before, text.AsSpan(0, i));
            if (score > bestScore)
            {
                best = end;
                bestScore = score;
            }
        }
        return best;
    }

    private static int CommonSuffixLength(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        var n = 0;
        while (n < x.Length && n < y.Length && x[^(n + 1)] == y[^(n + 1)]) n++;
        return n;
    }

    // "dog" → "dog. Then" leaves ". Then"; strip the punctuation that belongs to the previous sentence.
    private static string Clean(string text) => text.Trim().TrimStart(LeadingJunk).TrimStart();

    /// <summary>Lower-cased letters/digits with single spaces between words, plus a map back to the original.</summary>
    private readonly struct Skeleton
    {
        public string Text { get; }
        private readonly List<int> _map; // skeleton index → original index

        private Skeleton(string text, List<int> map)
        {
            Text = text;
            _map = map;
        }

        /// <summary>Original index just after the skeleton character at <paramref name="end"/> - 1.</summary>
        public int OriginalEnd(int end) => end == 0 ? 0 : _map[end - 1] + 1;

        public static Skeleton Of(string s)
        {
            var sb = new StringBuilder(s.Length);
            var map = new List<int>(s.Length);
            var pendingSpace = false;
            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];
                if (!char.IsLetterOrDigit(ch))
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }
                if (pendingSpace)
                {
                    sb.Append(' ');
                    map.Add(i);
                    pendingSpace = false;
                }
                sb.Append(char.ToLowerInvariant(ch));
                map.Add(i);
            }
            return new Skeleton(sb.ToString(), map);
        }
    }
}
