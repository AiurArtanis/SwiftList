using System.Windows.Input;
using SwiftList.App.Helpers;

namespace SwiftList.App.ViewModels.Search;

// One entry in the startup panel's tab strip (shown above the quick window's results when the search
// box is empty and at least one tab has content). SelectCommand switches the panel's Results to this
// tab's items; CloseCommand disables the underlying source and asks the controller to drop this tab.
public class StartupPanelTabViewModel : ViewModelBase
{
    public string Label { get; }
    public ICommand CloseCommand { get; }
    public ICommand SelectCommand { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public StartupPanelTabViewModel(string label, Action onClose, Action onSelect)
    {
        Label = label;
        CloseCommand = new RelayCommand(onClose);
        SelectCommand = new RelayCommand(onSelect);
    }
}
