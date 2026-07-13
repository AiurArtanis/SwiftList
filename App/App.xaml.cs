using System.Diagnostics;
using System.Windows;
using SwiftList.Core;
using SwiftList.Core.Services;
using SwiftList.App.ViewModels.Settings;
using SwiftList.App.Services;
using Application = System.Windows.Application;
using MessageBox = SwiftList.App.Views.Controls.CustomMessageBox;
namespace SwiftList.App;

public partial class App : Application
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private Mutex? _appMutex;
    public static Core.Hook.HookIpcClient? HookClient { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // SwiftList never set an explicit AppUserModelID, so Windows infers one on its own (commonly
        // derived from the exe's own path) -- the taskbar's default/resting icon for windows from a
        // path Windows treats as an "installed app" (Program Files + Start Menu registration) came
        // from that inferred identity rather than the live window icon ThemedWindowIconHelper sets,
        // even though title bar/Alt-Tab (which read the live window directly) were already correct.
        // Owning the identity explicitly is also just standard practice for a real desktop app
        // (correct taskbar grouping/pinning/jump-list/notification behavior).
        try
        {
            // Derived from the assembly name (App.csproj's <AssemblyName>) rather than a hardcoded
            // literal, so the two can't drift apart if the assembly is ever renamed. A null Name here
            // would mean the executing assembly has no name at all, which can't happen in practice;
            // the surrounding try/catch is the fallback if it somehow did.
            var appId = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!;
            SetCurrentProcessExplicitAppUserModelID(appId);
        }
        catch { /* best-effort; taskbar grouping falls back to Windows' own inference */ }

        // Only this thread (the Dispatcher), not Process.PriorityClass -- keeps input/rendering responsive
        // under CPU contention without making the whole process compete unfairly against everything else.
        Thread.CurrentThread.Priority = ThreadPriority.Highest;

        // Initialize logger first so we can log elevation decisions and issues

        Logger.Initialize("app.log", overwrite: true);
        var settings = UserSettings.Load();
        Logger.MinimumLevel = SettingsOptionGenerator.ParseLogLevel(settings.LogLevel);
        StartupManager.SetEnabled(settings.StartWithWindows);
        Logger.Log("=========================================");
        Logger.Log($"Application starting with arguments: {string.Join(" ", e.Args)}");
        Logger.Log($"[App] Running as Administrator: {ElevationManager.IsRunningAsAdmin()}");

        // Single instance check per user session

        // We append the username to guarantee multi-user isolation on the same machine

        var mutexName = $@"Local\SwiftList_App_{Environment.UserName}";
        _appMutex = new Mutex(true, mutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                var current = Process.GetCurrentProcess();
                foreach (var proc in Process.GetProcessesByName(current.ProcessName))
                {
                    if (proc.Id != current.Id)
                    {
                        AllowSetForegroundWindow(proc.Id);
                    }
                }
            }

            catch { }

            // Send activation command to the already running process and then exit immediately

            await AppPipeService.SendActivateSignalAsync();
            Shutdown();
            return;
        }

        var serviceExe = ServiceInstallManager.GetServiceExePath();
        HookClient = new Core.Hook.HookIpcClient(serviceExe, settings.AutoElevateIfAdmin);

        HookClient.OnMouseDoubleClick += (x, y) =>
        {
            if (!UserSettings.Load().Hotkeys.QuickNavTriggerOnDoubleClick) return;
            if (Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.IsPointInsideWindow(x, y)) return;
            var trk = InlineSearchManager.Instance.ExplorerTracker;
            var proc = GetProcessNameOfWindow(trk.ActiveHwnd);
            var cls = GetClassNameOfWindow(trk.ActiveHwnd);
            if (QuickNavigationTriggerGate.CanShow(trk.ActiveHwnd, proc, cls, trk.IsDesktop, x, y, PluginSdk.Abstractions.Plugins.MouseTriggerType.DoubleClick))
                Dispatcher.BeginInvoke(() => QuickNavigationMenu.Show(x, y));
        };

        HookClient.OnMouseMiddleClick += (x, y) =>
        {
            if (!UserSettings.Load().Hotkeys.QuickNavTriggerOnMiddleClick) return;
            if (Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.IsPointInsideWindow(x, y)) return;
            var trk = InlineSearchManager.Instance.ExplorerTracker;
            var proc = GetProcessNameOfWindow(trk.ActiveHwnd);
            var cls = GetClassNameOfWindow(trk.ActiveHwnd);
            if (QuickNavigationTriggerGate.CanShow(trk.ActiveHwnd, proc, cls, trk.IsDesktop, x, y, PluginSdk.Abstractions.Plugins.MouseTriggerType.MiddleClick)
                || FileDialogQuickNavGate.CanShow(trk.ActiveHwnd, proc, cls, x, y))
                Dispatcher.BeginInvoke(() => QuickNavigationMenu.Show(x, y));
        };

        PluginSdk.Models.ListControlIpcBridge.GetListItemsFunc = hwnd => HookClient != null ? Core.Hook.ListIpcCoordinator.GetListItems(hwnd, HookClient.SendMessage) : Array.Empty<string>();
        PluginSdk.Models.ListControlIpcBridge.GetSelectedIndicesFunc = (hwnd, className) => HookClient != null ? Core.Hook.ListIpcCoordinator.GetSelectedIndices(hwnd, className, HookClient.SendMessage) : Array.Empty<int>();

        PluginSdk.Models.ListControlIpcBridge.SelectItemAction = (hwnd, className, index, clearOthers, selectState) =>
            HookClient?.SendMessage(new IpcMessage
            {
                Id = IpcMessageId.SelectItem,
                Hwnd = hwnd.ToInt64(),
                StringVal1 = className,
                IntVal = index,
                BoolVal = clearOthers,
                IsDesktop = selectState
            });

        PluginSdk.Models.ListControlIpcBridge.ClearSelectionAction = (hwnd, className) =>
            HookClient?.SendMessage(new IpcMessage
            {
                Id = IpcMessageId.ClearSelection,
                Hwnd = hwnd.ToInt64(),
                StringVal1 = className
            });

        HookClient.OnActivated += () => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (InlineSearchManager.Instance.IsInlineSearchActive)
            {
                InlineSearchManager.Instance.FocusSearchBox();
            }
            else
            {
                var quickSearchWindow = Current.MainWindow as QuickSearchWindow;
                quickSearchWindow?.ToggleVisibility();
            }
        }));
        HookClient.Start();

        // Set up global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (s, args) => LogException("AppDomain UnhandledException", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (s, args) => { LogException("DispatcherUnhandledException", args.Exception); args.Handled = true; };
        TaskScheduler.UnobservedTaskException += (s, args) => { LogException("TaskScheduler UnobservedTaskException", args.Exception); args.SetObserved(); };

        // Force load all plugins (actions and alias providers) on startup

        _ = PluginManager.Instance;
        ViewModels.Search.SearchableItemMapper.Preload();

        // Startup — loading every plugin, preloading providers/icons, JIT and WPF init — inflates the
        // working set with transient allocations that the GC reclaims but doesn't return to the OS.
        // Once things settle, trim the working set once so the app idles lean instead of sitting at a
        // few hundred MB. (Search windows already trim on close for the ongoing case.)
        _ = Task.Delay(10000).ContinueWith(_ => Win32Api.TrimWorkingSet());

        // Now that all plugins are loaded, initialize translations.

        // This must happen after PluginManager to avoid a Lazy<T> circular initialization crash.

        try
        {
            // Register TranslationService delegate for decoupled plugins

            PluginSdk.Services.TranslationService.LookupFunc = key => TranslationManager.Instance[key];

            // Register IconService delegate for decoupled plugins
            PluginSdk.Services.IconService.GetIconFunc = (path, isDir) => ShellIconHelper.GetIconForPath(path, isDir);
            PluginSdk.Services.IconService.GetThumbnailFunc = (path, size) => ShellImageListInterop.TryGetPreviewThumbnail(path, size);

            // Register FileMetadataService delegate for decoupled plugins
            PluginSdk.Services.FileMetadataService.BatchLookupFunc = FileMetadataBridge.GetMetadataBatchAsync;

            // Register Logger delegate for decoupled plugins

            PluginSdk.Logger.LogAction = (msg, lvl) => Logger.Log(msg, (LogLevel)(int)lvl);
            TranslationManager.Instance.ReloadTranslations();
            Logger.Log("[App] TranslationManager initialized.");

            // Initialize ThemeManager with the saved theme setting
            var startupThemeId = settings.ThemeFollowSystem
                ? ThemeManager.Instance.ResolveLightDarkThemeId(SystemThemeWatcher.IsSystemLight, settings)
                : settings.Theme;
            ThemeManager.Instance.Initialize(startupThemeId);
            ThemeManager.Instance.InitializeSystemFollow();
            Logger.Log($"[App] ThemeManager initialized with theme: {startupThemeId}");
        }

        catch (Exception ex)
        {
            Logger.Log($"[App] Failed to initialize TranslationManager or ThemeManager: {ex.Message}", LogLevel.Error);
        }

        // Start the activation named pipe server to listen to subsequent launches

        _ = AppPipeService.StartPipeServerAsync();
        AppStartupServiceBootstrapper.EnsureServiceStarted();
        Logger.Log("Starting normal WPF GUI client mode.");
        base.OnStartup(e);

        // After QuickSearchWindow is created (via StartupUri), start InlineSearchManager
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Current.MainWindow is QuickSearchWindow quickSearchWindow)
            {
                InlineSearchManager.Instance.Start();
                Logger.Log("[App] InlineSearchManager started.");
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);

        // Background update check on startup

        _ = Task.Run(async () =>
        {
            try
            {
                // Delay slightly to ensure app is fully initialized and main window is up

                await Task.Delay(3000);
                var settings = UserSettings.Load();
                if (!settings.AutoCheckUpdates)
                {
                    return;
                }

                var release = await UpdateService.Instance.CheckForUpdatesAsync();
                if (release != null)
                {
                    var currentVersion = typeof(App).Assembly.GetName().Version;
                    var cleanTag = release.TagName.TrimStart('v', 'V');
                    if (Version.TryParse(cleanTag, out var latestVersion) && latestVersion > currentVersion)
                    {
                        // If auto silent update is enabled and user is admin, prompt user and execute silent update

                        if (settings.AutoSilentUpdate && UpdateService.Instance.IsUserAdmin())
                        {
                            var zipAsset = Array.Find(release.Assets, a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                            if (zipAsset != null)
                            {
                                _ = Dispatcher.BeginInvoke(new Action(async () =>
                                {
                                    var promptFormat = TranslationManager.Instance["About_SilentUpdatePrompt"];
                                    var prompt = string.Format(promptFormat, release.TagName);
                                    var title = TranslationManager.Instance["About_CheckUpdate"];
                                    MessageBox.Show(prompt, title, MessageBoxButton.OK, MessageBoxImage.Information);
                                    var success = await UpdateService.Instance.StartSilentUpdateAsync(zipAsset.BrowserDownloadUrl);
                                    if (success)
                                    {
                                        TrayCleanExitHelper.CleanExit();
                                    }

                                }));
                                return;
                            }
                        }

                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            var promptFormat = TranslationManager.Instance["About_NewVersionAvailablePrompt"];
                            var prompt = string.Format(promptFormat, release.TagName);
                            var title = TranslationManager.Instance["About_CheckUpdate"];
                            MessageBox.Show(prompt, title, MessageBoxButton.OK, MessageBoxImage.Information);
                            ShowSettingsWindow("About");
                        }));
                    }
                }
            }

            catch (Exception ex)
            {
                Logger.Log($"[App] Background startup update check failed: {ex.Message}", LogLevel.Warn);
            }

        });
    }

    public static void HideInlineSearch() => InlineSearchManager.Instance.CloseInlineSearch();

    private static string GetProcessNameOfWindow(IntPtr hwnd)
    {
        try { Core.Hook.ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var pid); return pid != 0 ? Process.GetProcessById((int)pid).ProcessName : "Unknown"; }
        catch { return "Unknown"; }
    }

    private static string GetClassNameOfWindow(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        return hwnd != IntPtr.Zero && Core.Hook.ExplorerNativeHooks.GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "Unknown";
    }

    private static void LogException(string source, Exception? ex)
    {
        var details = ex != null ? ex.ToString() : "Null exception object";
        Logger.Log($"CRITICAL CRASH ({source}):\n{details}", LogLevel.Error);

        // Show message box to alert user

        MessageBox.Show(string.Format(TranslationManager.Instance["Crash_Message"], source, ex?.Message, Logger.LogDir), TranslationManager.Instance["Crash_Title"], MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static void ShowSettingsWindow(string? targetSection = null) => AppWindowManager.ShowSettingsWindow(targetSection);

    public static void ShowSearchWindow() => AppWindowManager.ShowSearchWindow();

    public static void CloseAllManagedWindows() => AppWindowManager.CloseAllManagedWindows();

    protected override void OnExit(ExitEventArgs e)
    {
        HookClient?.Stop(); HookClient?.Dispose(); HookClient = null;
        AppPipeService.StopServer(); InlineSearchManager.Instance.Dispose(); CloseAllManagedWindows();
        if (_appMutex != null) { try { _appMutex.ReleaseMutex(); } catch { } _appMutex.Dispose(); }
        base.OnExit(e);
    }
}
