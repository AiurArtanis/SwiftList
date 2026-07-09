using SwiftList.App.Services;
using SwiftList.Core.Indexer.NetworkDrive;

namespace SwiftList.App.ViewModels.Settings;

// Per-category summary text (NetworkIndexSummary/WslIndexSummary/FolderIndexSummary), split out of
// NetworkDriveSettingsViewModel.cs to keep that file under the line limit. Each category computes its own
// enabled count, item total, and busy state independently -- never a combined total across all three,
// which used to read as nonsense on whichever tab wasn't the one actually busy/enabled.
public partial class NetworkDriveSettingsViewModel
{
    private void UpdateSummaries(IReadOnlyList<NetworkIndexStatus>? indexStatuses, bool driveBusy, bool wslBusy, bool folderBusy)
    {
        NetworkIndexSummary = BuildSummary(
            NetworkDrives.Count == 0, "Network_DrivesEmpty",
            NetworkDrives.Count(d => d.AppliedEnabled),
            SumItems(indexStatuses, NetworkDrives.Select(d => d.Drive)),
            driveBusy);

        // WslDrives.Count == 0 never actually happens while this summary is visible (the WSL tab only
        // renders when IsWslPanelVisible, i.e. at least one distro exists) -- guarded anyway rather than
        // assume the caller always agrees.
        WslIndexSummary = BuildSummary(
            WslDrives.Count == 0, "Network_DrivesEmpty",
            WslDrives.Count(w => w.AppliedEnabled),
            SumItems(indexStatuses, WslDrives.Select(w => $@"\\wsl$\{w.DistroName}")),
            wslBusy);

        FolderIndexSummary = BuildSummary(
            IsFolderIndexesEmpty, "Folder_IndexEmpty",
            FolderIndexes.Count(f => f.AppliedEnabled),
            SumItems(indexStatuses, FolderIndexes.Select(f => f.Path)),
            folderBusy);
    }

    private static string BuildSummary(bool isEmpty, string emptyKey, int enabledCount, int totalItems, bool busy)
    {
        if (isEmpty)
            return TranslationManager.Instance[emptyKey];

        var state = busy ? TranslationManager.Instance["Network_StatusIndexing"] : TranslationManager.Instance["Status_Ready"];
        return string.Format(TranslationManager.Instance["Network_SummaryTemplate"], state, enabledCount, totalItems);
    }

    private static int SumItems(IReadOnlyList<NetworkIndexStatus>? indexStatuses, IEnumerable<string> keys)
    {
        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        return (indexStatuses ?? Array.Empty<NetworkIndexStatus>()).Where(s => keySet.Contains(s.Drive)).Sum(s => s.Items);
    }
}
