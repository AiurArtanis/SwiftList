using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.App.Services;

public sealed record PluginActionRegistration(uint RuntimeActionId, IPlugin Plugin, ISearchResultAction Action);
