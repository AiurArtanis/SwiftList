using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings;

public class HistorySettingsViewModel : ViewModelBase
{
    private string _searchText = string.Empty;
    private readonly List<HistoryItemViewModel> _allItems = new();

    public HistorySettingsViewModel()
    {
        var entries = SearchHistoryStore.GetEntries();
        foreach (var entry in entries)
        {
            var isDir = Directory.Exists(entry);
            _allItems.Add(new HistoryItemViewModel
            {
                Path = entry,
                Name = Path.GetFileName(entry) ?? entry,
                IsDirectory = isDir
            });
        }

        FilteredItems = new ObservableCollection<HistoryItemViewModel>(_allItems);
        RemoveItemCommand = new RelayCommand<HistoryItemViewModel>(RemoveItem);
        ClearAllCommand = new RelayCommand(ClearAll);
    }

    public ObservableCollection<HistoryItemViewModel> FilteredItems { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand ClearAllCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    private void RemoveItem(HistoryItemViewModel? item)
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
            if (string.IsNullOrEmpty(SearchText) ||
                item.Path.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            {
                FilteredItems.Add(item);
            }
        }
    }

    public void Save() => SearchHistoryStore.SaveEntries(_allItems.Select(x => x.Path));
}

public class HistoryItemViewModel : ViewModelBase
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
}
