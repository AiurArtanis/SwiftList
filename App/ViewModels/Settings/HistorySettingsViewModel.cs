using System.IO;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings;

public class HistorySettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private string _selectedTab = "Search";
    private ICommand? _selectTabCommand;

    public HistorySettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        SearchHistory = new HistoryListViewModel(
            SearchHistoryStore.GetEntries,
            MapSearchEntry,
            () => _userSettings.EnableHistory,
            v => _userSettings.EnableHistory = v);

        KeywordHistory = new HistoryListViewModel(
            KeywordHistoryStore.GetEntries,
            MapKeywordEntry,
            () => _userSettings.EnableKeywordHistory,
            v => _userSettings.EnableKeywordHistory = v);
    }

    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    public ICommand SelectTabCommand => _selectTabCommand ??= new RelayCommand<string>(tab => SelectedTab = tab);

    public HistoryListViewModel SearchHistory { get; }
    public HistoryListViewModel KeywordHistory { get; }

    private const string FileIconGlyph = "";
    private const string FolderIconGlyph = "";
    private const string KeywordIconGlyph = "";

    private static HistoryEntryViewModel MapSearchEntry(string path)
    {
        var isDir = Directory.Exists(path);
        return new HistoryEntryViewModel
        {
            RawValue = path,
            Primary = Path.GetFileName(path) is { Length: > 0 } name ? name : path,
            Secondary = path,
            IconGlyph = isDir ? FolderIconGlyph : FileIconGlyph
        };
    }

    private static HistoryEntryViewModel MapKeywordEntry(string keyword) => new()
    {
        RawValue = keyword,
        Primary = keyword,
        Secondary = string.Empty,
        IconGlyph = KeywordIconGlyph
    };

    public void Save()
    {
        SearchHistoryStore.SaveEntries(SearchHistory.GetEntriesToSave());
        KeywordHistoryStore.SaveEntries(KeywordHistory.GetEntriesToSave());
    }
}
