using System.Windows;

namespace SwiftList.PluginSdk;

public interface IThemeProvider
{
    string Name { get; }
    IEnumerable<ITheme> GetThemes();
}

public interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    ResourceDictionary GetResources();
}
