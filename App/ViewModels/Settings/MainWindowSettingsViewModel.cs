using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings;

// Settings for the full/main SearchWindow's default size -- distinct from SearchWindowSettings,
// which configures the quick window's search bar layout.
public class MainWindowSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public MainWindowSettingsViewModel(UserSettings userSettings) => _userSettings = userSettings;

    public double Width
    {
        get => _userSettings.MainWindow.Width;
        set
        {
            if (value < UiMetrics.MinMainWindowWidth || value > UiMetrics.MaxMainWindowWidth)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Width must be between {UiMetrics.MinMainWindowWidth} and {UiMetrics.MaxMainWindowWidth}.");
            }
            if (_userSettings.MainWindow.Width != value)
            {
                _userSettings.MainWindow.Width = value;
                _userSettings.Save();
                UiMetrics.MainWindowWidth = value;
                OnPropertyChanged();
            }
        }
    }

    public double Height
    {
        get => _userSettings.MainWindow.Height;
        set
        {
            if (value < UiMetrics.MinMainWindowHeight || value > UiMetrics.MaxMainWindowHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Height must be between {UiMetrics.MinMainWindowHeight} and {UiMetrics.MaxMainWindowHeight}.");
            }
            if (_userSettings.MainWindow.Height != value)
            {
                _userSettings.MainWindow.Height = value;
                _userSettings.Save();
                UiMetrics.MainWindowHeight = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand ResetCommand => new RelayCommand(Reset);

    private void Reset()
    {
        _userSettings.MainWindow.Width = UiMetrics.DefaultMainWindowWidth;
        _userSettings.MainWindow.Height = UiMetrics.DefaultMainWindowHeight;
        _userSettings.Save();
        UiMetrics.ApplyScaleFromSettings();

        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
    }
}
