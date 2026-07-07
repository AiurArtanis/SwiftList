using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings;

public class SearchBarLayoutSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public SearchBarLayoutSettingsViewModel(UserSettings userSettings) => _userSettings = userSettings;

    public double SearchBarWidth
    {
        get => _userSettings.SearchWindow.SearchBarWidth;
        set
        {
            if (value < 300.0 || value > 1200.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Width must be between 300 and 1200.");
            }
            if (_userSettings.SearchWindow.SearchBarWidth != value)
            {
                _userSettings.SearchWindow.SearchBarWidth = value;
                _userSettings.Save();
                OnPropertyChanged();
            }
        }
    }

    public double SearchBarHeight
    {
        get => _userSettings.SearchWindow.SearchBarHeight;
        set
        {
            if (value < 45.0 || value > 120.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Height must be between 45 and 120.");
            }
            if (_userSettings.SearchWindow.SearchBarHeight != value)
            {
                _userSettings.SearchWindow.SearchBarHeight = value;
                _userSettings.Save();
                OnPropertyChanged();
            }
        }
    }

    public double CornerRadius
    {
        get => _userSettings.SearchWindow.CornerRadius;
        set
        {
            if (value < 0.0 || value > 50.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Corner radius must be between 0 and 50.");
            }
            if (_userSettings.SearchWindow.CornerRadius != value)
            {
                _userSettings.SearchWindow.CornerRadius = value;
                _userSettings.Save();
                OnPropertyChanged();
            }
        }
    }

    public double ResultIconSize
    {
        get => _userSettings.SearchWindow.ResultIconSize;
        set
        {
            if (value < UiMetrics.MinQuickResultIconSize || value > UiMetrics.MaxQuickResultIconSize)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Icon size must be between {UiMetrics.MinQuickResultIconSize} and {UiMetrics.MaxQuickResultIconSize}.");
            }
            if (_userSettings.SearchWindow.ResultIconSize != value)
            {
                _userSettings.SearchWindow.ResultIconSize = value;
                _userSettings.Save();
                UiMetrics.QuickResultIconSize = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand ResetCommand => new RelayCommand(Reset);

    private void Reset()
    {
        _userSettings.SearchWindow.SearchBarWidth = 632;
        _userSettings.SearchWindow.SearchBarHeight = 70;
        _userSettings.SearchWindow.CornerRadius = 12;
        _userSettings.SearchWindow.ResultIconSize = 42;
        _userSettings.SearchWindow.Left = null;
        _userSettings.SearchWindow.Top = null;
        _userSettings.Save();
        UiMetrics.ApplyScaleFromSettings();

        OnPropertyChanged(nameof(SearchBarWidth));
        OnPropertyChanged(nameof(SearchBarHeight));
        OnPropertyChanged(nameof(CornerRadius));
        OnPropertyChanged(nameof(ResultIconSize));
    }
}
