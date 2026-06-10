using SwiftList.PluginSdk;

namespace SwiftList.App.ViewModels.Search;

public class DynamicSidebarGroupViewModel : ViewModelBase
{
    private readonly SearchViewModel _mainVm;
    private DynamicSidebarItemViewModel? _selectedItem;

    public DynamicSidebarGroupViewModel(SidebarFilterGroup group, SearchViewModel mainVm)
    {
        _mainVm = mainVm;
        Header = group.Header;
        Items = group.Items.Select(item => new DynamicSidebarItemViewModel(item, this)).ToList();
        if (Items.Count > 0)
        {
            _selectedItem = Items[0];
        }
    }

    public string Header { get; }
    public List<DynamicSidebarItemViewModel> Items { get; }

    private bool _isFirst;
    public bool IsFirst
    {
        get => _isFirst;
        set => SetProperty(ref _isFirst, value);
    }

    public DynamicSidebarItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem != value)
            {
                _selectedItem = value;
                OnPropertyChanged();
                _mainVm.OnDynamicFilterChanged();
            }
        }
    }
}

public class DynamicSidebarItemViewModel
{
    private readonly SidebarFilterItem _item;
    public DynamicSidebarGroupViewModel Group { get; }

    public DynamicSidebarItemViewModel(SidebarFilterItem item, DynamicSidebarGroupViewModel group)
    {
        _item = item;
        Group = group;
    }

    public string Id => _item.Id;
    public string DisplayName => _item.DisplayName;
    public string IconString => !string.IsNullOrEmpty(_item.IconKey) ? _item.IconKey : "◆";
    public string? IconData => _item.IconData;
    public bool HasIconData => !string.IsNullOrEmpty(_item.IconData);
    public Func<ISearchResult, bool>? FilterPredicate => _item.FilterPredicate;
}
