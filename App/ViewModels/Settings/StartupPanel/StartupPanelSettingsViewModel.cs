using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;

using SwiftList.App.Services.Plugin;
namespace SwiftList.App.ViewModels.Settings.StartupPanel;

/// <summary>
/// Backs the Startup Panel settings page: sub-tab navigation plus the "Recent Files"
/// (target directories + how many entries to show) and "Last Directory" (single enable checkbox)
/// sub-tabs. RecentFilesEnabled/LastDirectoryEnabled here are the same fields the live panel's own
/// tab-close (x) buttons flip directly in UserSettings -- this ViewModel just reflects whatever is on
/// disk when the Settings window opens.
/// </summary>
public class StartupPanelSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public StartupPanelSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        var panel = _userSettings.StartupPanel;

        _enabled = panel.Enabled;
        _recentFilesEnabled = panel.RecentFilesEnabled;
        _recentFilesCount = panel.RecentFilesCount;
        _recentFilesMaxAgeMinutes = panel.RecentFilesMaxAgeMinutes;
        _lastDirectoryEnabled = panel.LastDirectoryEnabled;
        foreach (var dir in panel.RecentFilesDirectories.Where(x => !string.IsNullOrWhiteSpace(x)))
            RecentFilesDirectories.Add(new ExclusionRuleItem(dir));
        RefreshBulkText();

        RefreshPluginTabs();
        TabOrder = new StartupPanelTabOrderViewModel(userSettings);

        AddDirectoryCommand = new RelayCommand(AddDirectory, CanAddDirectory);
        RemoveDirectoryCommand = new RelayCommand<ExclusionRuleItem>(RemoveDirectory);
        EditDirectoryCommand = new RelayCommand<ExclusionRuleItem>(EditDirectory);
        ApplyDirectoriesTextCommand = new RelayCommand(() => ApplyBulkText(DirectoriesText));
        SelectSubTabCommand = new RelayCommand<string>(tab => SelectedSubTab = tab);
    }

    // Master switch: off means the panel never activates at all, regardless of the per-tab settings
    // below (see StartupPanelController.TryActivateAsync).
    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    // Sub-tab navigation -- "RecentFiles", "LastDirectory", "PluginTabs", and "TabOrder" today, each
    // mirroring one of the live panel's own tab sources (TabOrder spans all three of the others).
    private string _selectedSubTab = "RecentFiles";
    public string SelectedSubTab
    {
        get => _selectedSubTab;
        set
        {
            if (SetProperty(ref _selectedSubTab, value) && value == "TabOrder")
            {
                // Picks up any RecentFiles/LastDirectory-enabled toggle or plugin-tab reopen/close made
                // in another sub-tab during this same Settings session -- the two enabled flags are
                // this ViewModel's own staged (not-yet-saved) values, while ClosedTabIds writes straight
                // to the live UserSettings object (see StartupPanelPluginTabViewModel.IsOpen) rather
                // than staging until Save() the way this page's other fields do.
                TabOrder.Refresh(RecentFilesEnabled, LastDirectoryEnabled);
            }
        }
    }
    public ICommand SelectSubTabCommand { get; }

    private bool _recentFilesEnabled;
    public bool RecentFilesEnabled
    {
        get => _recentFilesEnabled;
        set => SetProperty(ref _recentFilesEnabled, value);
    }

    public ObservableCollection<ExclusionRuleItem> RecentFilesDirectories { get; } = new();

    private bool _lastDirectoryEnabled;
    public bool LastDirectoryEnabled
    {
        get => _lastDirectoryEnabled;
        set => SetProperty(ref _lastDirectoryEnabled, value);
    }

    // Plugin-provided tabs (History/Favorites/...), grouped by owning plugin -- lets the user reopen one
    // that was closed via its x button in the live panel. Distinct from Plugin Management's enable/
    // disable: see StartupPanelPluginTabViewModel for why the two must not share storage.
    public ObservableCollection<StartupPanelPluginGroupViewModel> PluginTabGroups { get; } = new();
    public bool HasPluginTabs => PluginTabGroups.Count > 0;

    public StartupPanelTabOrderViewModel TabOrder { get; }

    // Called on construction, and again after Plugin Management applies an enable/disable change in the
    // same Settings window session (see SettingsViewModel.Apply, right after RefreshDisabledComponents) --
    // otherwise this list would only pick up that change the next time the whole window is reopened. Only
    // plugin-enabled providers show up here; one disabled via Plugin Management never becomes a tab
    // candidate at all (see StartupPanelController.BuildCandidateSources), so it has no business showing
    // up in this "reopen a closed tab" list either.
    public void RefreshPluginTabs()
    {
        PluginTabGroups.Clear();
        foreach (var group in BuildPluginTabGroups())
            PluginTabGroups.Add(group);
        OnPropertyChanged(nameof(HasPluginTabs));
    }

    // Internal, not private: also called by SettingsWindowSearchExtensions.BuildAllEntries to build the
    // same groups (in the same PluginManager.Instance.StartupPanelTabProviders order) for settings
    // search, both for the in-app search box when no live StartupPanelSettingsViewModel exists yet and
    // for the SDK-facing SettingsSearchService feed, which never has a live SettingsViewModel at all.
    internal static List<StartupPanelPluginGroupViewModel> BuildPluginTabGroups()
    {
        var manager = PluginManager.Instance;
        return manager.StartupPanelTabProviders
            .GroupBy(p => p.GetType().Assembly)
            .Select(g => new StartupPanelPluginGroupViewModel(
                PluginLoaderHelper.GetPluginDisplayName(g.Key, manager),
                g.Select(p => new StartupPanelPluginTabViewModel(p)).ToList()))
            .ToList();
    }

    private string _newDirectory = string.Empty;
    public string NewDirectory
    {
        get => _newDirectory;
        set
        {
            if (SetProperty(ref _newDirectory, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _directoriesText = string.Empty;
    public string DirectoriesText
    {
        get => _directoriesText;
        set => SetProperty(ref _directoriesText, value);
    }

    private int _recentFilesCount;
    public int RecentFilesCount
    {
        get => _recentFilesCount;
        set
        {
            if (value < 1 || value > 100)
                throw new ArgumentOutOfRangeException(nameof(value), "Count must be between 1 and 100.");
            SetProperty(ref _recentFilesCount, value);
        }
    }

    // Upper bound of 30 days (43200 minutes): generous enough for "what's new since last week/month"
    // while still keeping the field meaningfully scoped to "recent" rather than an unbounded history.
    private int _recentFilesMaxAgeMinutes;
    public int RecentFilesMaxAgeMinutes
    {
        get => _recentFilesMaxAgeMinutes;
        set
        {
            if (value < 1 || value > 43200)
                throw new ArgumentOutOfRangeException(nameof(value), "Time range must be between 1 and 43200 minutes.");
            SetProperty(ref _recentFilesMaxAgeMinutes, value);
        }
    }

    public ICommand AddDirectoryCommand { get; }
    public ICommand RemoveDirectoryCommand { get; }
    public ICommand EditDirectoryCommand { get; }
    public ICommand ApplyDirectoriesTextCommand { get; }

    public void Save()
    {
        ApplyBulkText(DirectoriesText);

        var panel = _userSettings.StartupPanel;
        panel.Enabled = Enabled;
        panel.RecentFilesEnabled = RecentFilesEnabled;
        panel.RecentFilesDirectories = NormalizeItems(RecentFilesDirectories);
        panel.RecentFilesCount = RecentFilesCount;
        panel.RecentFilesMaxAgeMinutes = RecentFilesMaxAgeMinutes;
        panel.LastDirectoryEnabled = LastDirectoryEnabled;

        TabOrder.Save();
    }

    private bool CanAddDirectory() => !string.IsNullOrWhiteSpace(NewDirectory);

    private void AddDirectory()
    {
        AddUnique(NewDirectory);
        NewDirectory = string.Empty;
        RefreshBulkText();
    }

    private void AddUnique(string value)
    {
        var normalized = value.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        if (RecentFilesDirectories.Any(x => x.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        RecentFilesDirectories.Add(new ExclusionRuleItem(normalized));
    }

    private void RemoveDirectory(ExclusionRuleItem item)
    {
        if (item == null)
            return;

        RecentFilesDirectories.Remove(item);
        RefreshBulkText();
    }

    private void EditDirectory(ExclusionRuleItem item)
    {
        if (item == null)
            return;

        NewDirectory = item.Value;
        RecentFilesDirectories.Remove(item);
        RefreshBulkText();
    }

    private void RefreshBulkText() => DirectoriesText = string.Join(Environment.NewLine, RecentFilesDirectories.Select(x => x.Value));

    private static List<string> NormalizeItems(ObservableCollection<ExclusionRuleItem> items) => items
        .Select(x => x.Value.Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static List<string> ParseLines(string text) => (text ?? string.Empty)
        .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
        .Select(x => x.Trim().Trim('"'))
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private void ApplyBulkText(string text)
    {
        RecentFilesDirectories.Clear();
        foreach (var value in ParseLines(text))
            RecentFilesDirectories.Add(new ExclusionRuleItem(value));
        RefreshBulkText();
    }
}
