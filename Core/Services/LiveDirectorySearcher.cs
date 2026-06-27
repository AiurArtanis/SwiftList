using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core;

public static class LiveDirectorySearcher
{
    public static List<SearchResult> ScanDirectory(string directory, int maxProcessed, CancellationToken token)
    {
        var results = new List<SearchResult>();
        Logger.Log($"[LiveDirectorySearcher] ScanDirectory starting for '{directory}'. Exists: {Directory.Exists(directory)}", LogLevel.Debug);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return results;

        var queue = new Queue<string>();
        queue.Enqueue(directory);

        var drive = Path.GetPathRoot(directory) ?? string.Empty;
        var processedCount = 0;

        while (queue.Count > 0 && processedCount < maxProcessed)
        {
            token.ThrowIfCancellationRequested();
            var currentDir = queue.Dequeue();

            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(currentDir).GetFileSystemInfos();
            }
            catch
            {
                continue;
            }

            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attrs;
                try { attrs = entry.Attributes; } catch { continue; }
                processedCount++;
                if (processedCount >= maxProcessed)
                    break;

                var isDir = attrs.HasFlag(FileAttributes.Directory);
                results.Add(new SearchResult
                {
                    Name = entry.Name,
                    Path = entry.FullName,
                    IsDir = isDir,
                    Drive = drive
                });

                if (isDir)
                {
                    queue.Enqueue(entry.FullName);
                }
            }
        }
        Logger.Log($"[LiveDirectorySearcher] ScanDirectory finished for '{directory}'. Found: {results.Count}", LogLevel.Debug);
        return results;
    }

    public static bool MatchAndStream(
        List<SearchResult> entries,
        string query,
        Action<SearchResult, bool> onResult,
        CancellationToken token,
        bool onlyDirectChildren = false,
        string? parentPath = null)
    {
        if (entries == null || entries.Count == 0)
            return false;

        FzfPattern? pattern = null;
        FzfSlab? slab = null;
        if (!string.IsNullOrWhiteSpace(query))
        {
            pattern = FzfPattern.Parse(query);
            if (pattern.IsEmpty && pattern.TargetDrive == null)
                return false;
            slab = new FzfSlab();
        }

        var foundCount = 0;

        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();

            if (onlyDirectChildren && !string.IsNullOrEmpty(parentPath))
            {
                var entryParent = Path.GetDirectoryName(entry.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    ?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var normalizedParent = parentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.Equals(entryParent, normalizedParent, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var matched = false;
            if (pattern == null)
            {
                matched = true;
            }
            else
            {
                // 1. Try match the name itself using the core FZF engine
                if (pattern.TryMatch(entry.Name, out var matchResult, FzfScoringScheme.Default, slab))
                {
                    matched = true;
                }
                else
                {
                    // 2. Try match aliases generated for the name
                    var aliases = GenerateAliases(entry.Name);
                    if (aliases != null)
                    {
                        foreach (var alias in aliases)
                        {
                            if (pattern.TryMatch(alias, out var aliasMatch, FzfScoringScheme.Default, slab))
                            {
                                var span = aliasMatch.MaxEnd - aliasMatch.MinBegin;
                                var queryLen = pattern.GetTotalTermLength();
                                if (span > Math.Max(queryLen * 3, 20) || aliasMatch.Score < queryLen * 5)
                                    continue;

                                matched = true;
                                break;
                            }
                        }
                    }
                }
            }

            if (matched)
            {
                onResult(entry, false);
                foundCount++;
            }
        }

        return foundCount > 0;
    }

    private static string[]? GenerateAliases(string text)
    {
        if (string.IsNullOrEmpty(text) || !AliasProviderRegistry.HasNonAscii(text))
            return null;

        var list = new List<string>();
        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            try
            {
                var aliases = provider.GetAliases(text);
                if (aliases != null)
                {
                    list.AddRange(aliases);
                }
            }
            catch
            {
                // Ignore plugin errors
            }
        }
        return list.Count > 0 ? list.ToArray() : null;
    }

    public static (string DirectoryToScan, string FilterQuery) ResolvePathModeSearch(string exactPathLower)
    {
        if (string.IsNullOrEmpty(exactPathLower))
            return (string.Empty, string.Empty);

        if (Directory.Exists(exactPathLower))
        {
            return (exactPathLower, string.Empty);
        }

        var dir = Path.GetDirectoryName(exactPathLower);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(dir))
            {
                var filter = exactPathLower.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return (dir, filter);
            }
            dir = Path.GetDirectoryName(dir);
        }

        return (string.Empty, string.Empty);
    }
}
