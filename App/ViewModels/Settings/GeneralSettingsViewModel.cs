using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings;

public class GeneralSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private LogLevelOption? _selectedLogLevel;
    private ThemeOption? _selectedTheme;
    private IReadOnlyList<LogLevelOption>? _logLevelOptions;
    private IReadOnlyList<LanguageOption>? _languageOptions;
    private IReadOnlyList<ThemeOption>? _themeOptions;

    // Tab navigation for the System/Layout/Preview Window split of this page.
    private string _selectedTab = "System";
    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    private ICommand? _selectTabCommand;
    public ICommand SelectTabCommand => _selectTabCommand ??= new RelayCommand<string>(tab => SelectedTab = tab);

    public GeneralSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        PreviewWindow = new PreviewWindowSettingsViewModel(userSettings);

        _selectedLogLevel = LogLevelOptions.FirstOrDefault(o => o.Value == SettingsOptionGenerator.NormalizeLogLevel(_userSettings.LogLevel))
                            ?? LogLevelOptions[2]; // Default to Info

        _selectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == _userSettings.Theme)
                         ?? ThemeOptions.FirstOrDefault();

        // Dynamically refresh properties when the language changes
        TranslationManager.Instance.PropertyChanged += (s, e) =>
        {
            _logLevelOptions = null;
            _themeOptions = null;
            _languageOptions = null;

            OnPropertyChanged(nameof(LogLevelOptions));
            OnPropertyChanged(nameof(ThemeOptions));
            OnPropertyChanged(nameof(LanguageOptions));

            // Let WPF bind the new ItemsSource first, then restore selections
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                var newLogLevel = LogLevelOptions.FirstOrDefault(o => o.Value == SettingsOptionGenerator.NormalizeLogLevel(_userSettings.LogLevel));
                if (newLogLevel != null)
                {
                    SelectedLogLevel = newLogLevel;
                }

                var newTheme = ThemeOptions.FirstOrDefault(o => o.Value == _userSettings.Theme);
                if (newTheme != null)
                {
                    SelectedTheme = newTheme;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    public LogLevelOption? SelectedLogLevel
    {
        get => _selectedLogLevel;
        set
        {
            if (value == null) return;
            if (_selectedLogLevel != value)
            {
                var isLogLevelChanged = _userSettings.LogLevel != value.Value;
                _selectedLogLevel = value;
                _userSettings.LogLevel = value.Value;
                _userSettings.Save();
                if (isLogLevelChanged)
                {
                    Logger.MinimumLevel = SettingsOptionGenerator.ParseLogLevel(value.Value);
                    // Propagate to hook process so hook.log also respects the new level
                    App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(LogLevel));
            }
        }
    }

    public ThemeOption? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value == null) return;
            if (_selectedTheme != value)
            {
                var isThemeIdChanged = _userSettings.Theme != value.Value;
                _selectedTheme = value;
                _userSettings.Theme = value.Value;
                _userSettings.Save();
                if (isThemeIdChanged)
                {
                    ThemeManager.Instance.ApplyTheme(value.Value, saveSettings: false);
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreferredTheme));
            }
        }
    }

    public IReadOnlyList<LogLevelOption> LogLevelOptions
    {
        get
        {
            if (_logLevelOptions == null)
            {
                _logLevelOptions = SettingsOptionGenerator.GetLogLevelOptions();
            }
            return _logLevelOptions;
        }
    }

    public IReadOnlyList<LanguageOption> LanguageOptions
    {
        get
        {
            if (_languageOptions == null)
            {
                _languageOptions = SettingsOptionGenerator.GetLanguageOptions();
            }
            return _languageOptions;
        }
    }

    public IReadOnlyList<ThemeOption> ThemeOptions
    {
        get
        {
            if (_themeOptions == null)
            {
                _themeOptions = SettingsOptionGenerator.GetThemeOptions();
            }
            return _themeOptions;
        }
    }

    public string PreferredTheme => _userSettings.Theme;

    public bool StartWithWindows
    {
        get => _userSettings.StartWithWindows;
        set { if (_userSettings.StartWithWindows != value) { _userSettings.StartWithWindows = value; _userSettings.Save(); OnPropertyChanged(); } }
    }

    public bool AutoElevateIfAdmin
    {
        get => _userSettings.AutoElevateIfAdmin;
        set { if (_userSettings.AutoElevateIfAdmin != value) { _userSettings.AutoElevateIfAdmin = value; _userSettings.Save(); OnPropertyChanged(); } }
    }

    public bool AutoCheckUpdates
    {
        get => _userSettings.AutoCheckUpdates;
        set { if (_userSettings.AutoCheckUpdates != value) { _userSettings.AutoCheckUpdates = value; _userSettings.Save(); OnPropertyChanged(); OnPropertyChanged(nameof(IsAutoSilentUpdateEnabled)); } }
    }

    public bool IsUserAdmin => UpdateService.Instance.IsUserAdmin();

    public bool IsAutoSilentUpdateEnabled => IsUserAdmin && AutoCheckUpdates;

    public bool AutoSilentUpdate
    {
        get => IsUserAdmin && _userSettings.AutoSilentUpdate;
        set { if (!IsUserAdmin) return; if (_userSettings.AutoSilentUpdate != value) { _userSettings.AutoSilentUpdate = value; _userSettings.Save(); OnPropertyChanged(); } }
    }

    public string LogLevel => SettingsOptionGenerator.NormalizeLogLevel(_userSettings.LogLevel);

    public string PreferredLanguage
    {
        get => _userSettings.PreferredLanguage;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                OnPropertyChanged();
                return;
            }
            if (_userSettings.PreferredLanguage != value)
            {
                _userSettings.PreferredLanguage = value;
                _userSettings.Save();
                TranslationManager.Instance.CurrentCulture = value;
                OnPropertyChanged();
            }
        }
    }

    public void Apply()
    {
        StartupManager.SetEnabled(StartWithWindows);
        Logger.MinimumLevel = SettingsOptionGenerator.ParseLogLevel(LogLevel);
        _userSettings.Save();
    }

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

    public double SearchWindowCornerRadius
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

    public ICommand ResetLayoutCommand => new RelayCommand(ResetLayout);

    private void ResetLayout()
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
        OnPropertyChanged(nameof(SearchWindowCornerRadius));
        OnPropertyChanged(nameof(ResultIconSize));
    }

    public PreviewWindowSettingsViewModel PreviewWindow { get; }
}
