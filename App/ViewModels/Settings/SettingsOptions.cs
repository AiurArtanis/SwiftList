namespace SwiftList.App.ViewModels.Settings;

public sealed record LogLevelOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record ThemeOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record LanguageOption(string Value, string Label)
{
    public override string ToString() => Label;

    public static string GetLanguageDisplayName(string cultureCode)
    {
        try
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(cultureCode);
            var nativeName = culture.NativeName;
            if (!string.IsNullOrEmpty(nativeName))
            {
                return char.ToUpper(nativeName[0]) + nativeName.Substring(1);
            }
        }
        catch { }

        return cultureCode;
    }
}

public sealed record HotkeyOptionItem(object Value, string Label)
{
    public override string ToString() => Label;
}
