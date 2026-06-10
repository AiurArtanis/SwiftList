using System.Text.RegularExpressions;

namespace SwiftList.Core.Indexer.NetworkDrive;

internal static class GlobMatcher
{
    public static NetworkGlobPattern Compile(string pattern) => new(pattern);
}

internal sealed class NetworkGlobPattern
{
    private readonly string _rawPattern;
    private readonly Regex? _regex;

    public NetworkGlobPattern(string pattern)
    {
        _rawPattern = pattern ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_rawPattern))
        {
            try
            {
                _regex = GlobToRegex.Compile(_rawPattern.Trim());
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
}
