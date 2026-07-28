using System.Text.RegularExpressions;

using SwiftList.Core.Indexer.NetworkDrive.Walk;
namespace SwiftList.Core;

public sealed class ExclusionRuleSet
{
    private readonly string[] _excludedRoots;
    private readonly NetworkGlobPattern[] _ignoredGlobs;
    private readonly Regex[] _ignoredRegexes;
    private readonly string? _root;

    private ExclusionRuleSet(string[] excludedRoots, NetworkGlobPattern[] ignoredGlobs, Regex[] ignoredRegexes, string? root = null)
    {
        _excludedRoots = excludedRoots;
        _ignoredGlobs = ignoredGlobs;
        _ignoredRegexes = ignoredRegexes;
        _root = root;
    }

    public static ExclusionRuleSet Empty { get; } = new(Array.Empty<string>(), Array.Empty<NetworkGlobPattern>(), Array.Empty<Regex>());

    private static ExclusionRuleSet? _cachedRules;
    private static UserSettings? _cachedSettingsSource;
    private static readonly object _lock = new();

    public static void InvalidateCache()
    {
        lock (_lock)
        {
            _cachedRules = null;
            _cachedSettingsSource = null;
        }
    }

    public static ExclusionRuleSet From(UserSettings settings)
    {
        lock (_lock)
        {
            if (_cachedRules != null && ReferenceEquals(_cachedSettingsSource, settings))
            {
                return _cachedRules;
            }

            _cachedRules = new ExclusionRuleSet(
                BuildExcludedRoots(settings.ExcludedPaths),
                BuildIgnoredGlobs(settings.IgnoredPathGlobs),
                BuildIgnoredRegexes(settings.IgnoredPathRegexes));
            _cachedSettingsSource = settings;
            return _cachedRules;
        }
    }

    public static ExclusionRuleSet From(UserSettings settings, string root) => new(
        BuildExcludedRoots(settings.ExcludedPaths, NormalizePath(root, isDirectory: true)),
        BuildIgnoredGlobs(settings.IgnoredPathGlobs),
        BuildIgnoredRegexes(settings.IgnoredPathRegexes),
        NormalizePath(root, isDirectory: true));

    public bool IsExcluded(SearchResult result, string? exemptRoot = null) => IsExcludedPath(result.Path, result.IsDir, exemptRoot);

    public bool IsExcludedPath(string path, bool isDirectory, string? exemptRoot = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = NormalizePath(path, isDirectory);
        var normalizedExempt = !string.IsNullOrEmpty(exemptRoot) ? NormalizePath(exemptRoot, isDirectory: true) : null;

        // 1. Check excluded roots on the full normalized path
        foreach (var excludedRoot in _excludedRoots)
        {
            if (normalizedExempt != null &&
                (normalizedExempt.StartsWith(excludedRoot, StringComparison.OrdinalIgnoreCase) ||
                 (excludedRoot.StartsWith(normalizedExempt, StringComparison.OrdinalIgnoreCase) && string.Equals(normalized, excludedRoot, StringComparison.OrdinalIgnoreCase))))
                continue;

            if (normalized.StartsWith(excludedRoot, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (_ignoredGlobs.Length == 0 && _ignoredRegexes.Length == 0)
            return false;

        // 2. Check globs and regexes on the path itself and all of its parent directories
        var current = normalized;
        while (true)
        {
            var pathForGlob = current.TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(pathForGlob))
                break;

            var name = Path.GetFileName(pathForGlob);
            var relativePath = GetRelativePath(current);
            var slashPath = pathForGlob.Replace('\\', '/');

            foreach (var glob in _ignoredGlobs)
            {
                if (glob.IsMatch(pathForGlob) || glob.IsMatch(slashPath) || glob.IsMatch(relativePath))
                    return true;
            }

            foreach (var regex in _ignoredRegexes)
            {
                if (regex.IsMatch(name) || regex.IsMatch(pathForGlob) || regex.IsMatch(slashPath) || regex.IsMatch(relativePath))
                    return true;
            }

            // Get parent directory
            var parent = Path.GetDirectoryName(pathForGlob);
            if (string.IsNullOrEmpty(parent) || parent == current || parent == pathForGlob)
                break;

            current = parent + Path.DirectorySeparatorChar;
        }

        return false;
    }

    private static string[] BuildExcludedRoots(IReadOnlyList<string> paths)
        => BuildExcludedRoots(paths, root: null);

    private static string[] BuildExcludedRoots(IReadOnlyList<string> paths, string? root)
    {
        if (paths.Count == 0)
            return Array.Empty<string>();

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizePath(Environment.ExpandEnvironmentVariables(path), isDirectory: true))
            .Where(path => root == null || path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static NetworkGlobPattern[] BuildIgnoredGlobs(IReadOnlyList<string> globs)
    {
        if (globs.Count == 0)
            return Array.Empty<NetworkGlobPattern>();

        return globs
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => GlobMatcher.Compile(pattern.Trim()))
            .Where(pattern => !pattern.IsEmpty)
            .ToArray();
    }

    private static Regex[] BuildIgnoredRegexes(IReadOnlyList<string> regexes)
    {
        if (regexes.Count == 0)
            return Array.Empty<Regex>();

        var compiled = new List<Regex>();
        foreach (var pattern in regexes)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            try
            {
                compiled.Add(new Regex(
                    pattern.Trim(),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
                    TimeSpan.FromMilliseconds(50)));
            }
            catch (ArgumentException ex)
            {
                Logger.Log($"[ExclusionRuleSet] Invalid exclude regex '{pattern}': {ex.Message}", LogLevel.Warn);
            }
        }

        return compiled.ToArray();
    }

    private static string NormalizePath(string path, bool isDirectory)
    {
        var normalized = path.Trim().Trim('"')
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch
        {
        }

        return isDirectory
            ? normalized.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar
            : normalized;
    }

    private string GetRelativePath(string normalizedPath)
    {
        if (_root == null || !normalizedPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            return normalizedPath.TrimEnd(Path.DirectorySeparatorChar);

        return normalizedPath[_root.Length..].TrimEnd(Path.DirectorySeparatorChar);
    }
}
