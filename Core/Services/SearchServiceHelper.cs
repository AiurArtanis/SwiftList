namespace SwiftList.Core;

internal static class SearchServiceHelper
{
    public static bool SearchNetworkDrives(
        string query,
        int maxResults,
        string? directoryFilter,
        ExclusionRuleSet exclusionRules,
        Action<SearchResult> onResult,
        CancellationToken token)
    {
        try
        {
            var parsed = SearchQueryParser.Parse(query);
            var queryExemptRoot = parsed.IsPathMode ? parsed.ExactPathLower : null;
            var found = 0;
            UserNetworkDriveSearch.SearchStreaming(query, maxResults, result =>
            {
                token.ThrowIfCancellationRequested();
                if (!exclusionRules.IsExcluded(result, directoryFilter) || !exclusionRules.IsExcluded(result, queryExemptRoot))
                {
                    Interlocked.Increment(ref found);
                    onResult(result);
                }
            }, token, directoryFilter);

            return found > 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.Log($"[SearchServiceHelper] Network drive search failed: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    public static bool CheckNeedsLiveSearch(string dir, ExclusionRuleSet exclusionRules)
    {
        try
        {
            var driveInfo = new DriveInfo(dir);
            if (driveInfo.DriveType == DriveType.Network)
            {
                var letter = dir.Substring(0, 1);
                var id = NetworkDriveResolver.GetNetworkId(letter);
                return string.IsNullOrWhiteSpace(id) || !UserSettings.Load().NetworkDrives.Any(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            }
            // Both NTFS and ReFS are indexed by the USN journal indexer.
            var fs = driveInfo.DriveFormat;
            var isIndexed = string.Equals(fs, "NTFS", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(fs, "ReFS", StringComparison.OrdinalIgnoreCase);
            return !isIndexed
                || exclusionRules.IsExcludedPath(dir, true)
                || exclusionRules.IsExcludedPath(Path.Combine(dir, "_live_search_dummy.txt"), false);
        }
        catch { return true; }
    }
}
