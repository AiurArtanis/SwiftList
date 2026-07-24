using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;

using SwiftList.App.Services.Plugin;
namespace SwiftList.App.ViewModels.Settings.General;

// Lets the user reorder the full SearchWindow's results grid columns (built-in Name/Path/DateModified
// plus any third-party IResultColumnProvider's own columns) -- purely which columns show in which
// left-to-right position, NOT which column the rows are sorted by (that's runtime-only, see
// SearchResultSortMemory). Edits stage in Items and only commit to _userSettings.ColumnOrder when
// Save() runs (called from GeneralSettingsViewModel.Apply()).
public class ColumnOrderViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public ColumnOrderViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        var order = userSettings.ColumnOrder;

        var candidates = new List<ColumnOrderItem>
        {
            new() { Id = "Name", DisplayName = TranslationManager.Instance["Search_HeaderName"] },
            new() { Id = "Path", DisplayName = TranslationManager.Instance["Search_HeaderPath"] },
            new() { Id = "DateModified", DisplayName = TranslationManager.Instance["Search_HeaderDateModified"] },
        };

        foreach (var provider in PluginManager.Instance.ResultColumnProviders)
            foreach (var col in provider.GetColumns())
                candidates.Add(new ColumnOrderItem { Id = col.ColumnId, DisplayName = col.HeaderText });

        foreach (var item in candidates.OrderBy(c => Rank(c.Id, order)))
            Items.Add(item);

        MoveUpCommand = new RelayCommand<ColumnOrderItem>(MoveUp);
        MoveDownCommand = new RelayCommand<ColumnOrderItem>(MoveDown);
    }

    public ObservableCollection<ColumnOrderItem> Items { get; } = new();

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    // Position in the user's saved order (most-preferred first); an id that isn't listed yet falls back
    // to int.MaxValue, which -- since the caller's sort is stable -- lands it after every listed column
    // while preserving its original relative order against any OTHER unlisted column, same convention
    // SearchResultTypePriority.Rank/PluginManager.QuickNavigationProviders already use.
    private static int Rank(string columnId, List<string> order)
    {
        var idx = order.IndexOf(columnId);
        return idx >= 0 ? idx : int.MaxValue;
    }

    private void MoveUp(ColumnOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx > 0) Items.Move(idx, idx - 1);
    }

    private void MoveDown(ColumnOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1) Items.Move(idx, idx + 1);
    }

    public void Save()
    {
        _userSettings.ColumnOrder = Items.Select(x => x.Id).ToList();
    }
}

public class ColumnOrderItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
