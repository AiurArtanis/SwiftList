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

    public GeneralSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        _selectedLogLevel = LogLevelOptions.FirstOrDefault(o => o.Value == NormalizeLogLevel(_userSettings.LogLevel))
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
                var newLogLevel = LogLevelOptions.FirstOrDefault(o => o.Value == NormalizeLogLevel(_userSettings.LogLevel));
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
                    Logger.MinimumLevel = ParseLogLevel(value.Value);
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
                _logLevelOptions = new[]
                {
                    new LogLevelOption("Error", TranslationManager.Instance["LogLevel_Error"]),
                    new LogLevelOption("Warn", TranslationManager.Instance["LogLevel_Warn"]),
                    new LogLevelOption("Info", TranslationManager.Instance["LogLevel_Info"]),
                    new LogLevelOption("Debug", TranslationManager.Instance["LogLevel_Debug"])
                };
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
                var options = new List<LanguageOption>();
                foreach (var culture in TranslationManager.Instance.GetAvailableCultures())
                {
                    options.Add(new LanguageOption(culture, LanguageOption.GetLanguageDisplayName(culture)));
                }
                _languageOptions = options;
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
                var options = new List<ThemeOption>();
                foreach (var t in ThemeManager.Instance.GetAvailableThemes())
                {
                    options.Add(new ThemeOption(t.Id, t.DisplayName));
                }
                _themeOptions = options;
            }
            return _themeOptions;
        }
    }

    public string PreferredTheme => _userSettings.Theme;

    public bool StartWithWindows
    {
        get => _userSettings.StartWithWindows;
        set
        {
            if (_userSettings.StartWithWindows != value)
            {
                _userSettings.StartWithWindows = value;
                _userSettings.Save();
                OnPropertyChanged();
            }
        }
    }

    public bool AutoElevateIfAdmin
    {
        get => _userSettings.AutoElevateIfAdmin;
        set
        {
            if (_userSettings.AutoElevateIfAdmin != value)
            {
                _userSettings.AutoElevateIfAdmin = value;
                _userSettings.Save();
                OnPropertyChanged();
            }
        }
    }

    public bool AutoCheckUpdates
    {
        get => _userSettings.AutoCheckUpdates;
        set
        {
            if (_userSettings.AutoCheckUpdates != value)
            {
                _userSettings.AutoCheckUpdates = value;
                _userSettings.Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAutoSilentUpdateEnabled));
            }
        }
    }

    public bool IsUserAdmin => UpdateService.Instance.IsUserAdmin();

    public bool IsAutoSilentUpdateEnabled => IsUserAdmin && AutoCheckUpdates;

    public bool AutoSilentUpdate
    {
        get => IsUserAdmin && _userSettings.AutoSilentUpdate;
        set
        {
            if (!IsUserAdmin) return;
            if (_userSettings.AutoSilentUpdate != value)
            {
                _userSettings.AutoSilentUpdate = value;
                _userSettings.Save();
                OnPropertyChanged();
            }
        }
    }

    public string LogLevel => NormalizeLogLevel(_userSettings.LogLevel);


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
        Logger.MinimumLevel = ParseLogLevel(LogLevel);
        _userSettings.Save();
    }


    public static LogLevel ParseLogLevel(string? value) => value switch
    {
        "Error" => Core.LogLevel.Error,
        "Warn" => Core.LogLevel.Warn,
        "Debug" => Core.LogLevel.Debug,
        _ => Core.LogLevel.Info
    };

    private static string NormalizeLogLevel(string? value) => value switch
    {
        "Error" => "Error",
        "Warn" => "Warn",
        "Debug" => "Debug",
        _ => "Info"
    };

    public double SearchBarWidth
    {
        get => _userSettings.SearchWindow.SearchBarWidth;
        set { if (_userSettings.SearchWindow.SearchBarWidth != value) { _userSettings.SearchWindow.SearchBarWidth = value; _userSettings.Save(); OnPropertyChanged(); } }
    }

    public double SearchBarHeight
    {
        get => _userSettings.SearchWindow.SearchBarHeight;
        set { if (_userSettings.SearchWindow.SearchBarHeight != value) { _userSettings.SearchWindow.SearchBarHeight = value; _userSettings.Save(); OnPropertyChanged(); } }
    }

    public double SearchWindowCornerRadius
    {
        get => _userSettings.SearchWindow.CornerRadius;
        set { if (_userSettings.SearchWindow.CornerRadius != value) { _userSettings.SearchWindow.CornerRadius = value; _userSettings.Save(); OnPropertyChanged(); } }
    }

    public ICommand ResetLayoutCommand => new RelayCommand(ResetLayout);

    private void ResetLayout()
    {
        _userSettings.SearchWindow.SearchBarWidth = 632;
        _userSettings.SearchWindow.SearchBarHeight = 70;
        _userSettings.SearchWindow.CornerRadius = 12;
        _userSettings.SearchWindow.Left = null;
        _userSettings.SearchWindow.Top = null;
        _userSettings.Save();

        OnPropertyChanged(nameof(SearchBarWidth));
        OnPropertyChanged(nameof(SearchBarHeight));
        OnPropertyChanged(nameof(SearchWindowCornerRadius));
    }
}
