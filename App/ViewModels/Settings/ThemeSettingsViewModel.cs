using SwiftList.App.Services;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings;

/// <summary>Theme selection, including the optional "follow system light/dark" mode. Split out of
/// GeneralSettingsViewModel to keep that file under the project's line limit. Every change here
/// applies live (no Apply()/staging), matching how the manual theme pick has always worked.</summary>
public class ThemeSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private ThemeOption? _selectedTheme;
    private ThemeOption? _selectedLightTheme;
    private ThemeOption? _selectedDarkTheme;
    private bool _followSystem;
    private IReadOnlyList<ThemeOption>? _themeOptions;
    private IReadOnlyList<ThemeOption>? _lightThemeOptions;
    private IReadOnlyList<ThemeOption>? _darkThemeOptions;
    private IReadOnlyList<ThemeCardOption>? _themeCards;

    public ThemeSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        _selectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == _userSettings.Theme)
                         ?? ThemeOptions.FirstOrDefault();
        _selectedLightTheme = LightThemeOptions.FirstOrDefault(o => o.Value == _userSettings.LightThemeId)
                              ?? LightThemeOptions.FirstOrDefault();
        _selectedDarkTheme = DarkThemeOptions.FirstOrDefault(o => o.Value == _userSettings.DarkThemeId)
                             ?? DarkThemeOptions.FirstOrDefault();
        _followSystem = _userSettings.ThemeFollowSystem;

        // Dynamically refresh properties when the language changes -- ThemeOption.Label is a
        // TranslationService lookup (Theme_<Id>), so it genuinely changes text, unlike the option's
        // Id/Value. See GeneralSettingsViewModel's identical LogLevel/Language handling for why this
        // needs an explicit re-match-by-Value after the ItemsSource rebuild rather than relying on
        // record value-equality to "just work".
        TranslationManager.Instance.PropertyChanged += (s, e) =>
        {
            _themeOptions = null;
            _lightThemeOptions = null;
            _darkThemeOptions = null;
            // ThemeCardOption.DisplayName is also a TranslationService lookup, so the card grid needs
            // the same invalidate-and-rebuild treatment as the combobox option lists above.
            _themeCards = null;
            OnPropertyChanged(nameof(ThemeOptions));
            OnPropertyChanged(nameof(LightThemeOptions));
            OnPropertyChanged(nameof(DarkThemeOptions));
            OnPropertyChanged(nameof(ThemeCards));
            OnPropertyChanged(nameof(LightThemeCards));
            OnPropertyChanged(nameof(DarkThemeCards));

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                var newTheme = ThemeOptions.FirstOrDefault(o => o.Value == _userSettings.Theme);
                if (newTheme != null) SelectedTheme = newTheme;

                var newLightTheme = LightThemeOptions.FirstOrDefault(o => o.Value == _userSettings.LightThemeId);
                if (newLightTheme != null) SelectedLightTheme = newLightTheme;

                var newDarkTheme = DarkThemeOptions.FirstOrDefault(o => o.Value == _userSettings.DarkThemeId);
                if (newDarkTheme != null) SelectedDarkTheme = newDarkTheme;
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    public IReadOnlyList<ThemeOption> ThemeOptions => _themeOptions ??= SettingsOptionGenerator.GetThemeOptions();

    // Filtered to each half's own flavor -- a dark-flavored theme showing up as a candidate for the
    // "light" side (or vice versa) would defeat the point of "follow system" in the first place.
    public IReadOnlyList<ThemeOption> LightThemeOptions => _lightThemeOptions ??= SettingsOptionGenerator.GetThemeOptions(isDark: false);
    public IReadOnlyList<ThemeOption> DarkThemeOptions => _darkThemeOptions ??= SettingsOptionGenerator.GetThemeOptions(isDark: true);

    // Card-preview equivalents of the three option lists above, for the Appearance page's card grid.
    public IReadOnlyList<ThemeCardOption> ThemeCards => _themeCards ??= ThemeManager.Instance.GetAvailableThemes()
        .Select(t => new ThemeCardOption(t)).OrderBy(c => c.Id).ToList();
    public IReadOnlyList<ThemeCardOption> LightThemeCards => ThemeCards.Where(c => !c.IsDark).ToList();
    public IReadOnlyList<ThemeCardOption> DarkThemeCards => ThemeCards.Where(c => c.IsDark).ToList();

    public string PreferredTheme => _userSettings.Theme;

    // String-keyed mirrors of SelectedTheme/SelectedLightTheme/SelectedDarkTheme so the card grid's
    // ListBox can two-way bind via SelectedValue/SelectedValuePath (ThemeOption is an immutable record
    // with no settable Value, so binding straight into ".Value" isn't an option).
    public string? SelectedThemeId
    {
        get => SelectedTheme?.Value;
        set => SelectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == value) ?? SelectedTheme;
    }

    public string? SelectedLightThemeId
    {
        get => SelectedLightTheme?.Value;
        set => SelectedLightTheme = LightThemeOptions.FirstOrDefault(o => o.Value == value) ?? SelectedLightTheme;
    }

    public string? SelectedDarkThemeId
    {
        get => SelectedDarkTheme?.Value;
        set => SelectedDarkTheme = DarkThemeOptions.FirstOrDefault(o => o.Value == value) ?? SelectedDarkTheme;
    }

    // The manual theme picker only makes sense when "follow system" is off -- hidden (not just
    // greyed out) the rest of the time, since the light/dark pair takes over that role.
    public bool IsManualThemeEnabled => !_followSystem;

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
                OnPropertyChanged(nameof(SelectedThemeId));
            }
        }
    }

    // Which theme "follow system" applies for light/dark, plus the on/off switch itself. Turning it
    // on switches to the resolved light/dark pick; turning it off switches back to the manually
    // selected theme (SelectedTheme's own setter never got a chance to apply while follow-system was
    // overriding it) -- either way, skip the ApplyTheme call (and its fade animation) entirely when
    // the target is already the active theme.
    public bool FollowSystem
    {
        get => _followSystem;
        set
        {
            if (_followSystem != value)
            {
                _followSystem = value;
                _userSettings.ThemeFollowSystem = value;
                _userSettings.Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsManualThemeEnabled));

                var targetThemeId = value
                    ? ThemeManager.Instance.ResolveLightDarkThemeId(SystemThemeWatcher.IsSystemLight, _userSettings)
                    : _userSettings.Theme;
                if (!string.Equals(targetThemeId, ThemeManager.Instance.CurrentThemeId, StringComparison.OrdinalIgnoreCase))
                {
                    ThemeManager.Instance.ApplyTheme(targetThemeId, saveSettings: false);
                }
            }
        }
    }

    public ThemeOption? SelectedLightTheme
    {
        get => _selectedLightTheme;
        set
        {
            if (value == null) return;
            if (_selectedLightTheme != value)
            {
                // ThemeOption is a record, so a language switch alone (re-translated Label, same Id)
                // already trips this inequality -- gate the actual re-apply on the Id, not the record,
                // or every language change would needlessly re-apply and fade the active theme.
                var isThemeIdChanged = _userSettings.LightThemeId != value.Value;
                _selectedLightTheme = value;
                _userSettings.LightThemeId = value.Value;
                _userSettings.Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedLightThemeId));
                if (isThemeIdChanged && _followSystem && SystemThemeWatcher.IsSystemLight)
                {
                    ThemeManager.Instance.ApplyTheme(value.Value, saveSettings: false);
                }
            }
        }
    }

    public ThemeOption? SelectedDarkTheme
    {
        get => _selectedDarkTheme;
        set
        {
            if (value == null) return;
            if (_selectedDarkTheme != value)
            {
                var isThemeIdChanged = _userSettings.DarkThemeId != value.Value;
                _selectedDarkTheme = value;
                _userSettings.DarkThemeId = value.Value;
                _userSettings.Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedDarkThemeId));
                if (isThemeIdChanged && _followSystem && !SystemThemeWatcher.IsSystemLight)
                {
                    ThemeManager.Instance.ApplyTheme(value.Value, saveSettings: false);
                }
            }
        }
    }
}
