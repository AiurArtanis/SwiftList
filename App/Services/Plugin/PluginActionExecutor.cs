using SwiftList.Core;

namespace SwiftList.App.Services.Plugin;

public static class PluginActionExecutor
{
    public static bool TryExecute(AppSearchResult result, PluginSdk.Abstractions.IPluginSearchWindow view, bool asAdmin = false)
    {
        // Apps (Start Menu shortcuts, packaged apps) launch through the same OnExecute delegate as an
        // instant result -- they just carry ResultKind "Application" instead so they get a real FullPath
        // and can be acted on (copy, locate in explorer, ...) like a normal file result.
        if (result.IsInstantResult || result.IsApplication)
        {
            // Dismiss the window before executing. An admin launch blocks on the UAC prompt, so
            // deferring the close (as the callers do on success) would leave the search window up
            // until the app actually starts. Closing up front makes it disappear immediately.
            view?.HideWindow();
            try
            {
                if (result.InstantResultOnExecute != null)
                {
                    if (asAdmin && !string.IsNullOrWhiteSpace(result.InstantResultActionArgument))
                        FileExecutor.OpenFileOrFolderAsAdmin(result.InstantResultActionArgument);
                    else
                        result.InstantResultOnExecute();
                }
                else if (result.InstantResultActionType == "Copy")
                {
                    System.Windows.Clipboard.SetText(result.InstantResultActionArgument);
                }
                else if (result.InstantResultActionType == "Execute")
                {
                    var arg = result.InstantResultActionArgument.Trim();
                    if (arg.StartsWith("cc_exec:", StringComparison.OrdinalIgnoreCase))
                    {
                        var json = arg.Substring(8).Trim();
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        var path = root.GetProperty("Path").GetString() ?? "";
                        var args = root.GetProperty("Arguments").GetString() ?? "";
                        var workingDir = root.GetProperty("WorkingDir").GetString() ?? "";
                        var runSilently = root.GetProperty("RunSilently").GetBoolean();
                        var targetRunAsAdmin = root.GetProperty("RunAsAdmin").GetBoolean();

                        var targetPsi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = args,
                            UseShellExecute = true
                        };
                        if (!string.IsNullOrWhiteSpace(workingDir))
                        {
                            targetPsi.WorkingDirectory = workingDir;
                        }
                        if (runSilently)
                        {
                            targetPsi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                            targetPsi.CreateNoWindow = true;
                        }
                        if (targetRunAsAdmin)
                        {
                            targetPsi.Verb = "runas";
                        }
                        System.Diagnostics.Process.Start(targetPsi);
                        return true;
                    }

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
            new[] { new PluginSearchResult(result.Name, result.PluginActionArgumentText, result.ContextDirectory) }, view);
        return true;
    }
}
