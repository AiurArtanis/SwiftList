using SwiftList.PluginSdk.Abstractions;

namespace SwiftList.App.Services;

public class PluginSearchResult : ISearchResult
{
    public PluginSearchResult(string name, string argumentText, string contextDirectory)
    {
        Name = name;
        FullPath = argumentText;
        ContextDirectory = contextDirectory;
    }

    public string Name { get; }
    public string FullPath { get; }
    public string ContextDirectory { get; }
    public bool IsDir => false;
    public bool IsApplication => false;
    public DateTime DateModified => DateTime.MinValue;
}
