namespace SwiftList.Core;

/// <summary>Backs the Startup Panel settings page and the tab strip shown above the
/// quick window's result list when the search box is empty. Each tab type gets its own Enabled flag
/// here so the settings checkbox and the tab's in-panel close (x) button stay in sync (both just flip
/// the same field) -- Recent Files and Last Directory each add their own Enabled flag + config block
/// here rather than a generic list, since there's nothing generic between them yet.</summary>
public class StartupPanelSettings
{
    // Master switch for the whole panel: off means nothing here activates when the search box is empty,
    // regardless of RecentFilesEnabled/LastDirectoryEnabled/ClosedTabIds below.
    public bool Enabled { get; set; } = true;

    public bool RecentFilesEnabled { get; set; } = true;

    // Defaults to the three folders a user is most likely to want watched out of the box. There's no
    // Environment.SpecialFolder.Downloads (it predates that folder existing) -- UserProfile + "Downloads"
    // is the same fallback ShellPathHelper.ResolveSpecialFolder already uses elsewhere in the codebase.
    public List<string> RecentFilesDirectories { get; set; } = new()
    {
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
    };

    public int RecentFilesCount { get; set; } = 10;

    // Only entries modified within this many minutes of "now" are eligible, on top of the count cap
    // above -- so an idle watched folder doesn't keep surfacing month-old files just because nothing
    // newer exists.
    public int RecentFilesMaxAgeMinutes { get; set; } = 60;

    public bool LastDirectoryEnabled { get; set; } = true;

    // Plugin-provided tabs (History, Favorites, ...) closed via the panel's own x button. Deliberately
    // separate from UserSettings.DisabledPluginComponents: that list governs whether a plugin component
    // is loaded/used at all (a disabled provider never even becomes a tab candidate), whereas this one
    // only tracks which already-enabled tab the user chose to hide from the panel. Keyed by the same
    // "{dll}::StartupPanelTabProvider::{id}" string PluginManagementViewModel uses for that other list,
    // purely for uniqueness -- the two lists are never read or written together.
    public List<string> ClosedTabIds { get; set; } = new();

    // User-chosen display order for the panel's tab strip, most-preferred first -- covers both the
    // built-in tabs (synthetic ids "__builtin::RecentFiles"/"__builtin::LastDirectory", see
    // ITabSource.Id) and plugin tabs (the same ComponentId ClosedTabIds above uses). A tab whose id
    // isn't listed here yet falls back to its original built-in-then-plugin-discovery-order position,
    // appended after every listed tab -- see StartupPanelController.BuildCandidateSources.
    public List<string> TabOrder { get; set; } = new();
}
