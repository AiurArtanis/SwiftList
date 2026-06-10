using SwiftList.PluginSdk;

namespace SwiftList.App.Services;

public sealed record PluginActionRegistration(uint RuntimeActionId, IActionPlugin Plugin, ISearchResultAction Action);
