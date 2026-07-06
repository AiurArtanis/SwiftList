using System.Windows.Input;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Settings;

public enum LocalDriveRowAction
{
    None,
    Rebuild,
    Delete
}

public static class LocalDriveSettingsHelper
{
    public static string TranslateState(string state) => state switch
    {
        "ready" => TranslationManager.Instance["Local_StateReady"],
        "indexing" => TranslationManager.Instance["Local_StateIndexing"],
        "loading-cache" => TranslationManager.Instance["Local_StateLoadingCache"],
        "pending" => TranslationManager.Instance["Local_StatePending"],
        "disabled" => TranslationManager.Instance["Local_StateDisabled"],
        "unavailable" => TranslationManager.Instance["Local_DriveUnavailable"],
        "failed" => TranslationManager.Instance["Local_StateFailed"],
        "error" => TranslationManager.Instance["Local_StateError"],
        "idle" => TranslationManager.Instance["Local_StateIdle"],
        _ => state
    };
}

public class LocalDriveSettingsItem : ViewModelBase
{
    private bool _isEnabled;
    private string _kind = string.Empty;
    private string _strategy = string.Empty;
    private string _state = string.Empty;
    private string _itemCount = string.Empty;
    private bool _canRunRowAction;
    private bool _canEditEnabled;
    private LocalDriveRowAction _rowAction;

    public string Drive { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CachePath { get; set; } = string.Empty;
    public ICommand RowActionCommand { get; set; } = null!;

    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    public string Kind { get => _kind; set => SetProperty(ref _kind, value); }
    public string Strategy { get => _strategy; set => SetProperty(ref _strategy, value); }
    public string State { get => _state; set => SetProperty(ref _state, value); }
    public string ItemCount { get => _itemCount; set => SetProperty(ref _itemCount, value); }
    public bool CanEditEnabled { get => _canEditEnabled; set => SetProperty(ref _canEditEnabled, value); }
    public bool CanRunRowAction
    {
        get => _canRunRowAction;
        set
        {
            if (SetProperty(ref _canRunRowAction, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }
    public LocalDriveRowAction RowAction
    {
        get => _rowAction;
        set
        {
            if (!SetProperty(ref _rowAction, value))
                return;
            OnPropertyChanged(nameof(IsRowActionVisible));
            OnPropertyChanged(nameof(RowActionText));
        }
    }
    public bool IsRowActionVisible => RowAction != LocalDriveRowAction.None;
    public string RowActionText => RowAction switch
    {
        LocalDriveRowAction.Rebuild => TranslationManager.Instance["Local_RowRebuildBtn"],
        LocalDriveRowAction.Delete => TranslationManager.Instance["Local_RowDeleteBtn"],
        _ => string.Empty
    };

    public void NotifyLanguageChanged() => OnPropertyChanged(nameof(RowActionText));
}
