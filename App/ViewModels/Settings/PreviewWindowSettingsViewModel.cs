using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings;

public class PreviewWindowSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public PreviewWindowSettingsViewModel(UserSettings userSettings) => _userSettings = userSettings;

    public double Width
    {
        get => _userSettings.PreviewWindow.Width;
        set
        {
            if (value < UiMetrics.MinPreviewWindowWidth || value > UiMetrics.MaxPreviewWindowWidth)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Width must be between {UiMetrics.MinPreviewWindowWidth} and {UiMetrics.MaxPreviewWindowWidth}.");
            }
            if (_userSettings.PreviewWindow.Width != value)
            {
                _userSettings.PreviewWindow.Width = value;
                _userSettings.Save();
                UiMetrics.PreviewWindowWidth = value;
                OnPropertyChanged();
            }
        }
    }

    public double Height
    {
        get => _userSettings.PreviewWindow.Height;
        set
        {
            if (value < UiMetrics.MinPreviewWindowHeight || value > UiMetrics.MaxPreviewWindowHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Height must be between {UiMetrics.MinPreviewWindowHeight} and {UiMetrics.MaxPreviewWindowHeight}.");
            }
            if (_userSettings.PreviewWindow.Height != value)
            {
                _userSettings.PreviewWindow.Height = value;
                _userSettings.Save();
                UiMetrics.PreviewWindowHeight = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand ResetCommand => new RelayCommand(Reset);

    private void Reset()
    {
        _userSettings.PreviewWindow.Width = 400;
        _userSettings.PreviewWindow.Height = 529;
        _userSettings.Save();
        UiMetrics.ApplyScaleFromSettings();

        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
    }
}
