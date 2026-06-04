using System;
using System.Collections.Generic;
using System.IO;

namespace SwiftList.Core.SearchIndex.Fzf
{
    internal sealed class FzfPattern
    {
        private FzfPattern(string? targetDrive, FzfTermSet[] termSets)
        {
            TargetDrive = targetDrive;
            TermSets = termSets;
        }

        public string? TargetDrive { get; }
        public FzfTermSet[] TermSets { get; }
        public bool IsEmpty => TermSets.Length == 0;

        public static FzfPattern Parse(string query)
        {
            string? targetDrive = null;
            var terms = new List<string>();
            foreach (string rawTerm in query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (rawTerm.Length >= 2 && char.IsLetter(rawTerm[0]) && rawTerm[1] == Path.VolumeSeparatorChar)
                {
                    targetDrive = rawTerm[0].ToString();
                    continue;
                }

                terms.Add(rawTerm);
            }

            return new FzfPattern(targetDrive, ParseTermSets(string.Join(' ', terms)));
        }

        public static FzfPattern ParseText(string query)
        {
            return new FzfPattern(null, ParseTermSets(query));
        }

        public bool TryMatch(string text, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab = null)
        {
            int totalScore = 0;
            int minBegin = int.MaxValue;
            int minEnd = int.MaxValue;
            int maxEnd = 0;
            bool validOffsetFound = false;

            foreach (var set in TermSets)
            {
                bool matched = false;
                FzfMatchResult best = default;
                foreach (var term in set.Terms)
                {
                    var current = FzfAlgorithm.Match(term.Kind, text, term.Text, term.CaseSensitive, scheme, slab);
                    if (current.IsMatch)
                    {
                        if (term.Inverse)
                        {
                            matched = false;
                            best = default;
                            break;
                        }

                        matched = true;
                        best = current;
                        break;
                    }

                    if (term.Inverse)
                    {
                        matched = true;
                        best = new FzfMatchResult(0, 0, 0);
                    }
                }

                if (!matched)
                {
                    result = default;
                    return false;
                }

                totalScore += best.Score;
                if (best.Start < best.End)
                {
                    minBegin = Math.Min(minBegin, best.Start);
                    minEnd = Math.Min(minEnd, best.End);
                    maxEnd = Math.Max(maxEnd, best.End);
                    validOffsetFound = true;
                }
            }

            result = new FzfPatternResult(totalScore, minBegin, minEnd, maxEnd, validOffsetFound);
            return true;
        }

        public bool TryGetSimpleFuzzyTerm(out FzfTerm term)
        {
            term = default;
            if (TermSets.Length != 1)
                return false;

            var terms = TermSets[0].Terms;
            if (terms.Length != 1)
                return false;

            term = terms[0];
            return !term.Inverse && term.Kind == FzfTermKind.Fuzzy && term.Text.Length > 0;
        }

        public ulong GetQueryMask(out bool canFilter)
        {
            ulong mask = 0;
            canFilter = true;
            foreach (var set in TermSets)
            {
                if (set.Terms.Length > 1)
                {
                    canFilter = false;
                    return 0;
                }
                foreach (var term in set.Terms)
                {
                    if (term.Inverse)
                        continue;
                    mask |= FzfAlgorithm.GetCharMask(term.Text);
                }
            }
            return mask;
        }

        private static FzfTermSet[] ParseTermSets(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<FzfTermSet>();

            query = query.Replace("\\ ", "\t");
            var sets = new List<FzfTermSet>();
            var current = new List<FzfTerm>();
            bool switchSet = false;
            bool afterBar = false;

            foreach (string rawToken in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string token = rawToken.Replace('\t', ' ');
                if (current.Count > 0 && !afterBar && token == "|")
                {
                    switchSet = false;
                    afterBar = true;
                    continue;
                }

                afterBar = false;
                FzfTermKind kind = FzfTermKind.Fuzzy;
                bool inverse = false;
                if (token.StartsWith("!", StringComparison.Ordinal))
                {
                    inverse = true;
                    kind = FzfTermKind.Exact;
                    token = token.Substring(1);
                }

                if (token != "$" && token.EndsWith("$", StringComparison.Ordinal))
                {
                    kind = FzfTermKind.Suffix;
                    token = token.Substring(0, token.Length - 1);
                }

                if (token.Length > 2 && token.StartsWith("'", StringComparison.Ordinal) && token.EndsWith("'", StringComparison.Ordinal))
                {
                    kind = FzfTermKind.ExactBoundary;
                    token = token.Substring(1, token.Length - 2);
                }
                else if (token.StartsWith("'", StringComparison.Ordinal))
                {
                    kind = inverse ? FzfTermKind.Fuzzy : FzfTermKind.Exact;
                    token = token.Substring(1);
                }
                else if (token.StartsWith("^", StringComparison.Ordinal))
                {
                    kind = kind == FzfTermKind.Suffix ? FzfTermKind.Equal : FzfTermKind.Prefix;
                    token = token.Substring(1);
                }

                if (token.Length == 0)
                    continue;

                if (switchSet)
                {
                    sets.Add(new FzfTermSet(current.ToArray()));
                    current.Clear();
                }

                string lower = token.ToLowerInvariant();
                bool caseSensitive = token != lower;
                current.Add(new FzfTerm(kind, inverse, caseSensitive ? token : lower, caseSensitive));
                switchSet = true;
            }

            if (current.Count > 0)
                sets.Add(new FzfTermSet(current.ToArray()));

            return sets.ToArray();
        }

    }

    internal readonly record struct FzfTermSet(FzfTerm[] Terms);
    internal readonly record struct FzfTerm(FzfTermKind Kind, bool Inverse, string Text, bool CaseSensitive);
    internal readonly record struct FzfPatternResult(int Score, int MinBegin, int MinEnd, int MaxEnd, bool ValidOffsetFound);
}
