using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SwiftList.Core.Indexer.NetworkDrive
{
    internal sealed class WalkFilter
    {
        private readonly string _root;
        private readonly string[] _excludedRoots;
        private readonly NetworkGlobPattern[] _ignoredGlobs;
        private readonly Regex[] _ignoredRegexes;
        private readonly bool _includeHiddenItems;
        private readonly bool _includeSystemItems;
        private readonly int _maxDepth;
        private readonly bool _useIgnoreFiles;

        public int WorkerCount { get; }

        private WalkFilter(
            string root,
            string[] excludedRoots,
            NetworkGlobPattern[] ignoredGlobs,
            Regex[] ignoredRegexes,
            bool includeHiddenItems,
            bool includeSystemItems,
            int maxDepth,
            int workerCount,
            bool useIgnoreFiles)
        {
            _root = root;
            _excludedRoots = excludedRoots;
            _ignoredGlobs = ignoredGlobs;
            _ignoredRegexes = ignoredRegexes;
            _includeHiddenItems = includeHiddenItems;
            _includeSystemItems = includeSystemItems;
            _maxDepth = Math.Max(0, maxDepth);
            _useIgnoreFiles = useIgnoreFiles;
            WorkerCount = Math.Max(0, workerCount);
        }

        public static WalkFilter Create(string root, WalkOptions options)
        {
            return new WalkFilter(
                root,
                BuildExcludedRoots(root, options.ExcludedPaths),
                BuildIgnoredGlobs(options.IgnoredPathGlobs),
                BuildIgnoredRegexes(options.IgnoredPathRegexes),
                options.IncludeHiddenItems,
                options.IncludeSystemItems,
                options.MaxDepth,
                options.WorkerCount,
                options.UseIgnoreFiles);
        }

        public NetworkIgnoreRuleSet LoadIgnoreRules(string physicalDir, string logicalDir, NetworkIgnoreRuleSet inherited)
        {
            if (!_useIgnoreFiles)
                return inherited;

            NetworkIgnoreRuleSet current = inherited;
            current = LoadIgnoreFile(Path.Combine(physicalDir, ".ignore"), logicalDir, current);
            current = LoadIgnoreFile(Path.Combine(physicalDir, ".fdignore"), logicalDir, current);
            current = LoadIgnoreFile(Path.Combine(physicalDir, ".gitignore"), logicalDir, current);
            return current;
        }

        private bool IsIgnoredByGlobalFilters(string fullPath, string name, bool isDirectory)
        {
            if (_ignoredGlobs.Length == 0 && _ignoredRegexes.Length == 0)
                return false;

            string relativePath = GetRelativePath(fullPath, isDirectory);
            string fullPathNormalized = fullPath.Replace('\\', '/');

            foreach (var glob in _ignoredGlobs)
            {
                if (glob.IsMatch(relativePath) || glob.IsMatch(fullPathNormalized) || glob.IsMatch(fullPath))
                    return true;
            }

            foreach (var regex in _ignoredRegexes)
            {
                if (regex.IsMatch(name) || regex.IsMatch(relativePath) || regex.IsMatch(fullPathNormalized) || regex.IsMatch(fullPath))
                    return true;
            }

            return false;
        }

        public bool ShouldIndex(string fullPath, string name, bool isDirectory, FileAttributes attributes, NetworkIgnoreRuleSet ignoreRules)
        {
            if (!_includeHiddenItems && (attributes & FileAttributes.Hidden) != 0)
                return false;

            if (!_includeSystemItems && (attributes & FileAttributes.System) != 0)
                return false;

            if (IsExcluded(fullPath))
                return false;

            if (ignoreRules.IsIgnored(fullPath, name, isDirectory))
                return false;

            if (IsIgnoredByGlobalFilters(fullPath, name, isDirectory))
                return false;

            return true;
        }

        public bool ShouldDescend(string fullPath, FileAttributes attributes, int depth, NetworkIgnoreRuleSet ignoreRules)
        {
            if (_maxDepth > 0 && depth > _maxDepth)
                return false;

            if (!_includeHiddenItems && (attributes & FileAttributes.Hidden) != 0)
                return false;

            if (!_includeSystemItems && (attributes & FileAttributes.System) != 0)
                return false;

            if (IsExcluded(fullPath))
                return false;

            string name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
            if (ignoreRules.IsIgnored(fullPath, name, isDirectory: true))
                return false;

            if (IsIgnoredByGlobalFilters(fullPath, name, isDirectory: true))
                return false;

            return true;
        }

        private NetworkIgnoreRuleSet LoadIgnoreFile(string physicalPath, string logicalDir, NetworkIgnoreRuleSet inherited)
        {
            if (!File.Exists(physicalPath))
                return inherited;

            try
            {
                var rules = inherited;
                string basePath = PathHelpers.NormalizePath(logicalDir, true);
                foreach (string rawLine in File.ReadLines(physicalPath))
                {
                    var rule = NetworkIgnoreRule.Parse(basePath, rawLine);
                    if (rule != null)
                        rules = rules.Add(rule.Value);
                }

                return rules;
            }
            catch
            {
                return inherited;
            }
        }

        private bool IsExcluded(string fullPath)
        {
            if (_excludedRoots.Length == 0)
                return false;

            string normalized = PathHelpers.NormalizePath(fullPath, isDirectory: true);
            foreach (string excluded in _excludedRoots)
            {
                if (normalized.StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private string GetRelativePath(string fullPath, bool isDirectory)
        {
            string normalized = PathHelpers.NormalizePath(fullPath, isDirectory);
            if (normalized.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
                return normalized.Substring(_root.Length).TrimEnd(Path.DirectorySeparatorChar);

            return normalized;
        }

        private static string[] BuildExcludedRoots(string root, IReadOnlyList<string> excludedPaths)
        {
            if (excludedPaths.Count == 0)
                return Array.Empty<string>();

            var roots = new List<string>();
            foreach (string path in excludedPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                string normalized = PathHelpers.NormalizePath(Environment.ExpandEnvironmentVariables(path), true);
                if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    roots.Add(normalized);
            }

            return roots.ToArray();
        }

        private static NetworkGlobPattern[] BuildIgnoredGlobs(IReadOnlyList<string> ignoredGlobs)
        {
            if (ignoredGlobs.Count == 0)
                return Array.Empty<NetworkGlobPattern>();

            return ignoredGlobs
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Select(pattern => GlobMatcher.Compile(pattern.Trim()))
                .Where(pattern => !pattern.IsEmpty)
                .ToArray();
        }

        private static Regex[] BuildIgnoredRegexes(IReadOnlyList<string> ignoredRegexes)
        {
            if (ignoredRegexes.Count == 0)
                return Array.Empty<Regex>();

            var compiled = new List<Regex>();
            foreach (string pattern in ignoredRegexes)
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
                    Logger.Log($"[WalkFilter] Invalid exclude regex '{pattern}': {ex.Message}", LogLevel.Warn);
                }
            }

            return compiled.ToArray();
        }
    }
}
