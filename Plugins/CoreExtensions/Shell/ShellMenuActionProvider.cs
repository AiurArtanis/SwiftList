using System.IO;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CoreExtensions.Shell;

/// <summary>
/// Dynamic action provider that loads the standard Windows Explorer context menu for files/folders.
/// </summary>
public class ShellMenuActionProvider : IDynamicActionProvider
{
    public string GroupName => TranslationService.Get("Plugin_ShellGroup");
    public int Priority => -1;

    private ShellMenuSession? _session;
    private string? _lastPath;

    private static int _warmedUp;

    public ShellMenuActionProvider()
    {
        // Warm up the STA worker and the folder shell extensions in the background at startup, so the
        // FIRST real menu open is already warm. Otherwise the first open cold-loads folder handlers and
        // shows incomplete items, only becoming complete on the second open. Once per process.
        if (Interlocked.Exchange(ref _warmedUp, 1) != 0)
            return;
        _ = Task.Run(() =>
        {
            try
            {
                var warmPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(warmPath) && Directory.Exists(warmPath))
                {
                    var session = ShellMenuSession.Create(warmPath);
                    session?.EnumerateItems();
                    session?.Dispose();
                }
            }
            catch
            {
                // Warm-up is best-effort.
            }
        });
    }

    public bool CanProvide(IReadOnlyList<ISearchResult> results)
    {
        // The native shell menu is single-item only for now (multi-file menu needs multi-PIDL);
        // hide it when more than one result is selected.
        if (results.Count != 1) return false;
        var result = results[0];
        if (result == null || string.IsNullOrEmpty(result.FullPath)) return false;
        return File.Exists(result.FullPath) || Directory.Exists(result.FullPath);
    }

    public IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu)
    {
        var result = results[0];
        // If root menu, create a new session
        if (hMenu == IntPtr.Zero)
        {
            _session?.Dispose();
            _session = ShellMenuSession.Create(result.FullPath);
            _lastPath = result.FullPath;
        }

        if (_session == null)
        {
            return Array.Empty<DynamicMenuItem>();
        }

        try
        {
            var items = _session.EnumerateItems(hMenu);
            var menuItems = new List<DynamicMenuItem>();
            foreach (var item in items)
            {
                menuItems.Add(new DynamicMenuItem
                {
                    Text = item.Text,
                    CommandId = item.CommandId,
                    IsSeparator = item.IsSeparator,
                    HasSubMenu = item.HasSubMenu,
                    SubMenuHandle = item.SubMenuHandle,
                    IsDisabled = item.IsDisabled,
                    HBitmapItem = item.HBitmapItem
                });
            }
            return menuItems;
        }
        catch
        {
            return Array.Empty<DynamicMenuItem>();
        }
    }

    public void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, IntPtr ownerHwnd)
    {
        var sessionToExecute = _session;
        _session = null; // Detach to allow parallel executions/cleanup

        if (sessionToExecute != null)
        {
            Task.Run(() =>
            {
                try
                {
                    sessionToExecute.InvokeCommand(commandId, ownerHwnd);
                }
                finally
                {
                    sessionToExecute.Dispose();
                }
            });
        }
    }

    public void ClearSession()
    {
        _session?.Dispose();
        _session = null;
        _lastPath = null;
    }
}
