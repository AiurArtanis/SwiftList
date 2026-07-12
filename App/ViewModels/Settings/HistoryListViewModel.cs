using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings;

/// <summary>
/// Backs the reusable history list UI (search box, scrollable entries, remove/clear, enable toggle).
/// Shared by the "search history" and "keyword history" tabs -- each supplies its own storage and how
/// a raw stored string maps to a displayable entry.
/// </summary>
public class HistoryListViewModel : ViewModelBase
{
    private readonly Func<IReadOnlyList<string>> _loadEntries;
    private readonly Func<string, HistoryEntryViewModel> _mapEntry;
    private readonly Func<bool> _getEnabled;
    private readonly Action<bool> _setEnabled;
    private readonly List<HistoryEntryViewModel> _allItems = new();
    private string _searchText = string.Empty;

    public HistoryListViewModel(
        Func<IReadOnlyList<string>> loadEntries,
        Func<string, HistoryEntryViewModel> mapEntry,
        Func<bool> getEnabled,
        Action<bool> setEnabled)
    {
        _loadEntries = loadEntries;
        _mapEntry = mapEntry;
        _getEnabled = getEnabled;
        _setEnabled = setEnabled;

        foreach (var raw in _loadEntries())
            _allItems.Add(_mapEntry(raw));

        FilteredItems = new ObservableCollection<HistoryEntryViewModel>(_allItems);
        RemoveItemCommand = new RelayCommand<HistoryEntryViewModel>(RemoveItem);
        ClearAllCommand = new RelayCommand(ClearAll);
    }

    public bool IsHistoryEnabled
    {
        get => _getEnabled();
        set
        {
            if (_getEnabled() != value)
            {
                _setEnabled(value);
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<HistoryEntryViewModel> FilteredItems { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand ClearAllCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    private void RemoveItem(HistoryEntryViewModel? item)
    {
        if (item == null) return;
        _allItems.Remove(item);
        FilteredItems.Remove(item);
    }

    private void ClearAll()
    {
        _allItems.Clear();
        FilteredItems.Clear();
    }

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        foreach (var item in _allItems)
        {
            // FuzzyMatcher.ComputeBestMatch (same FzfPattern.Parse Core's file search uses) splits a
            // multi-word SearchText into independently-required terms -- a plain .Contains(SearchText)
            // treated the whole typed text (spaces included) as one literal string, so a query like
            // "foo bar" would never match an entry containing both words non-contiguously.
            if (string.IsNullOrEmpty(SearchText) ||
                FuzzyMatcher.ComputeBestMatch(SearchText, item.Primary, new[] { item.Secondary }).IsMatch)
            {
                FilteredItems.Add(item);
            }
        }
    }

    /// <summary>Returns the current entries (in their edited order) for the caller to persist.</summary>
    public IEnumerable<string> GetEntriesToSave() => _allItems.Select(x => x.RawValue);
}

/// <summary>One row in the history list -- a file/folder path (with a subtitle) or a bare keyword.</summary>
public class HistoryEntryViewModel : ViewModelBase
{
    public string RawValue { get; set; } = string.Empty;
    public string Primary { get; set; } = string.Empty;
    public string Secondary { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "";
}
