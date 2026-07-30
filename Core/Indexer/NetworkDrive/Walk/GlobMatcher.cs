using System.Text.RegularExpressions;

namespace SwiftList.Core.Indexer.NetworkDrive.Walk;

internal static class GlobMatcher
{
    public static NetworkGlobPattern Compile(string pattern) => new(pattern);
}

internal sealed class NetworkGlobPattern
{
    // A literal shorter than this isn't selective enough to be worth testing before the regex.
    private const int MinimumUsefulLiteral = 3;

    private readonly string _rawPattern;
    private readonly Regex? _regex;
    private readonly string? _requiredLiteral;

    public NetworkGlobPattern(string pattern)
    {
        _rawPattern = pattern ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_rawPattern))
        {
            try
            {
                _regex = GlobToRegex.Compile(_rawPattern.Trim());
                _requiredLiteral = LongestRequiredLiteral(_rawPattern.Trim());
            }
            catch (Exception ex)
            {
                Logger.Log($"[NetworkGlobPattern] Failed to compile glob '{_rawPattern}' to regex: {ex.Message}", LogLevel.Warn);
            }
        }
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(_rawPattern);

    public bool IsMatch(string text)
    {
        if (IsEmpty)
            return string.IsNullOrEmpty(text);

        // Anything this pattern can match has to contain that literal, so a text that doesn't is a
        // no-match without running the regex at all. Contains is a vectorised scan against a compiled
        // regex's automaton, and the answer is overwhelmingly "no" -- the exclusion rules are asked
        // about every result on the drive and reject almost none of them.
        if (_requiredLiteral != null && !text.Contains(_requiredLiteral, StringComparison.OrdinalIgnoreCase))
            return false;

        if (_regex != null)
        {
            try
            {
                return _regex.IsMatch(text);
            }
            catch (RegexMatchTimeoutException)
            {
                Logger.Log($"[NetworkGlobPattern] Timeout matching '{text}' against regex for glob '{_rawPattern}'", LogLevel.Warn);
            }
        }

        return false;
    }

    /// <summary>
    /// Longest run of plain literal characters the pattern requires verbatim, or null if it has none
    /// worth testing.
    /// </summary>
    /// <remarks>
    /// Only runs that every match must contain count, so this skips anything inside {a,b} or [abc] --
    /// those are alternatives, and no one of them is required. Wildcards, separators and escapes end a
    /// run rather than extending it. Case is ignored on the way back out, matching how the compiled
    /// regex is built.
    /// </remarks>
    private static string? LongestRequiredLiteral(string glob)
    {
        var best = string.Empty;
        var start = -1;
        var brackets = 0;
        var inBraces = false;

        void EndRun(int end)
        {
            if (start >= 0 && end - start > best.Length)
                best = glob[start..end];
            start = -1;
        }

        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];

            if (inBraces)
            {
                if (c == '}') inBraces = false;
                continue;
            }
            if (brackets > 0)
            {
                if (c == '[') brackets++;
                else if (c == ']') brackets--;
                continue;
            }

            switch (c)
            {
                case '{':
                    EndRun(i);
                    inBraces = true;
                    break;
                case '[':
                    EndRun(i);
                    brackets = 1;
                    break;
                case '*':
                case '?':
                case '/':
                case '\\':
                case ',':
                case '}':
                case ']':
                    EndRun(i);
                    break;
                default:
                    if (start < 0) start = i;
                    break;
            }
        }
        EndRun(glob.Length);

        return best.Length >= MinimumUsefulLiteral ? best : null;
    }
}
