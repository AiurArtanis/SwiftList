using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Search;
using SwiftList.Core;

using SwiftList.App.Services.Plugin;
namespace SwiftList.App.ViewModels.Settings.General;

// Lets the user reorder the quick window's search-result "types" -- each enabled
// ISearchableItemProvider (Applications, Settings, File Filters, any third-party plugin) plus one
// synthetic "Files" entry for raw file-index results -- as a hard tier above match-quality weight
// (see SearchResultMapper.RankedCandidate.TypeRank), and optionally give each type a single-character
// trigger that exclusively filters to just that type (see BuildQuickResults' triggeredTypeId).
// History/Favorites stay hardcoded top-priority and are deliberately NOT part of this list. Edits
// stage in Items and only commit to _userSettings.ResultTypeOrder/ResultTypeTriggers when Save() runs
// (called from GeneralSettingsViewModel.Apply()).
public class ResultTypeOrderViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public ResultTypeOrderViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        var order = userSettings.ResultTypeOrder;
        var triggers = userSettings.ResultTypeTriggers;
        var candidates = new List<ResultTypeOrderItem>
        {
            new()
            {
                Id = SearchResultTypePriority.FilesTypeId,
                DisplayName = TranslationManager.Instance["General_ResultTypeFiles"],
                TriggerChar = triggers.GetValueOrDefault(SearchResultTypePriority.FilesTypeId, string.Empty)
            }
        };

        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            var id = SearchResultTypePriority.GetProviderTypeId(provider);
            candidates.Add(new ResultTypeOrderItem
            {
                Id = id,
                DisplayName = provider.Name,
                TriggerChar = triggers.GetValueOrDefault(id, string.Empty)
            });
        }

        foreach (var item in candidates.OrderBy(c => SearchResultTypePriority.Rank(c.Id, order)))
        {
            Items.Add(item);
        }

        MoveUpCommand = new RelayCommand<ResultTypeOrderItem>(MoveUp);
        MoveDownCommand = new RelayCommand<ResultTypeOrderItem>(MoveDown);
    }

    public ObservableCollection<ResultTypeOrderItem> Items { get; } = new();

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    private void MoveUp(ResultTypeOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx > 0) Items.Move(idx, idx - 1);
    }

    private void MoveDown(ResultTypeOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1) Items.Move(idx, idx + 1);
    }

    public void Save()
    {
        _userSettings.ResultTypeOrder = Items.Select(x => x.Id).ToList();
        _userSettings.ResultTypeTriggers = Items
            .Where(x => !string.IsNullOrEmpty(x.TriggerChar))
            .ToDictionary(x => x.Id, x => x.TriggerChar);
    }
}

public class ResultTypeOrderItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // Empty = no trigger configured. When this is the first character typed in the quick window,
    // only this type's results show (see SearchResultMapper.BuildQuickResults' triggeredTypeId).
    public string TriggerChar { get; set; } = string.Empty;
}
