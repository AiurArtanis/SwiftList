using System.Collections.Generic;

namespace SwiftList.Core.Indexer.NetworkDrive
{
    internal sealed record WalkOptions(
        IReadOnlyList<string> ExcludedPaths,
        IReadOnlyList<string> IgnoredPathGlobs,
        IReadOnlyList<string> IgnoredPathRegexes,
        bool IncludeHiddenItems,
        bool IncludeSystemItems,
        int MaxDepth,
        int WorkerCount,
        bool UseIgnoreFiles);

    internal readonly record struct NetworkDriveWalkStats(
        int Skipped,
        int Errors,
        int EnumerateErrors,
        int AttributeErrors,
        int ReparseSkipped,
        int SlowDirectories);
}
