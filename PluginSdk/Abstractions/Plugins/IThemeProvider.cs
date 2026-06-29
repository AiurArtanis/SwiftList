namespace SwiftList.PluginSdk.Abstractions.Plugins;

public interface IThemeProvider
{
    string Name { get; }
    IEnumerable<ITheme> GetThemes();
}
