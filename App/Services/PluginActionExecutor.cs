using SwiftList.Core;

namespace SwiftList.App.Services;

public static class PluginActionExecutor
{
    public static bool TryExecute(AppSearchResult result, PluginSdk.Abstractions.IPluginSearchWindow view)
    {
        if (result.IsInstantResult)
        {
            try
            {
                if (result.InstantResultOnExecute != null)
                {
                    result.InstantResultOnExecute();
                }
                else if (result.InstantResultActionType == "Copy")
                {
                    System.Windows.Clipboard.SetText(result.InstantResultActionArgument);
                }
                else if (result.InstantResultActionType == "Execute")
                {
                    var arg = result.InstantResultActionArgument.Trim();
                    if (arg.StartsWith("kill:", StringComparison.OrdinalIgnoreCase))
                    {
                        var pidStr = arg.Substring(5).Trim();
                        if (uint.TryParse(pidStr, out var pid))
                        {
                            App.HookClient?.SendMessage(new IpcMessage
                            {
                                Id = IpcMessageId.KillProcess,
                                ProcessId = pid
                            });
                        }
                        return true;
                    }

                    var runAsAdmin = false;
                    if (arg.StartsWith("runas:", StringComparison.OrdinalIgnoreCase))
                    {
                        runAsAdmin = true;
                        arg = arg.Substring(6).Trim();
                    }

                    var fileName = arg;
                    var arguments = "";
                    if (arg.StartsWith("\""))
                    {
                        var endQuote = arg.IndexOf('\"', 1);
                        if (endQuote > 0)
                        {
                            fileName = arg.Substring(1, endQuote - 1);
                            arguments = arg.Substring(endQuote + 1).Trim();
                        }
                    }
                    else
                    {
                        var firstSpace = arg.IndexOf(' ');
                        if (firstSpace > 0)
                        {
                            fileName = arg.Substring(0, firstSpace);
                            arguments = arg.Substring(firstSpace + 1).Trim();
                        }
                    }

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = true
                    };
                    if (runAsAdmin)
                    {
                        psi.Verb = "runas";
                    }
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[PluginActionExecutor] Failed to execute instant result action: {ex.Message}", LogLevel.Error);
            }
            return true;
        }

        if (!result.IsPluginSearchAction || result.IsSearchSectionHeader) return false;

        var registration = PluginManager.Instance.AllActions.FirstOrDefault(x => x.RuntimeActionId == result.PluginActionId);
        if (registration == null)
        {
            Logger.Log($"[PluginActionExecutor] Plugin search action not found: {result.PluginActionId}", LogLevel.Warn);
            return false;
        }

        registration.Action.Execute(
            new PluginSearchResult(result.Name, result.PluginActionArgumentText, result.ContextDirectory), view);
        return true;
    }
}
