using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.App.Services.Plugin;

public sealed record PluginActionRegistration(uint RuntimeActionId, IPlugin Plugin, ISearchResultAction Action);
