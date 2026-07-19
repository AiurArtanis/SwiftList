using System.Diagnostics;
using System.Windows;
using SwiftList.Core;
using SwiftList.Core.Services;
using SwiftList.App.ViewModels.Settings;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Search;
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

        HookClient = new Core.Hook.HookIpcClient();

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
        _ = Task.Delay(10000).ContinueWith(_ => Win32Api.TrimWorkingSet());

        try
        {
            PluginSdk.Services.TranslationService.LookupFunc = key => TranslationManager.Instance[key];
            PluginSdk.Services.TranslationService.CurrentCultureFunc = () => TranslationManager.Instance.CurrentCulture;
            PluginSdk.Services.SearchRefreshService.RefreshMatchingFunc = queryMatches =>
                // Callers may invoke this from a background thread (e.g. after an async fetch
                // completes), so marshal onto the UI thread here rather than requiring every caller
                // to remember to do so themselves.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (Window window in Windows)
                    {
                        if (window.DataContext is QuickSearchViewModel quickVm)
                        {
                            var currentQuery = quickVm.SearchQuery;
                            if (queryMatches(currentQuery))
                                quickVm.Search.PerformSearch(currentQuery);
                        }
                        else if (window.DataContext is SearchViewModel searchVm)
                        {
                            var currentQuery = searchVm.AdvancedQuery;
                            if (queryMatches(currentQuery))
                                searchVm.PerformSearch(currentQuery);
                        }
                    }
                }));
            PluginSdk.Services.IconService.GetIconFunc = (path, isDir) => ShellIconHelper.GetIconForPath(path, isDir);
            PluginSdk.Services.IconService.GetIconCacheOnlyFunc = (path, isDir) =>
            {
                var icon = ShellIconHelper.GetIconFromCacheOnly(path, isDir, out var needsLoad);
                return (icon, needsLoad);
            };
            PluginSdk.Services.IconService.GetThumbnailFunc = (path, size) => ShellImageListInterop.TryGetPreviewThumbnail(path, size);
            PluginSdk.Services.FileMetadataService.BatchLookupFunc = FileMetadataBridge.GetMetadataBatchAsync;
            PluginSdk.Logger.LogAction = (msg, lvl) => Logger.Log(msg, (LogLevel)(int)lvl);
            TranslationManager.Instance.ReloadTranslations();
            Logger.Log("[App] TranslationManager initialized.");

            // Preload app searchable items now that translations are fully loaded and settled
            SearchableItemMapper.Preload();

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
        _ = AppSearchPipeService.StartPipeServerAsync(); // exposes the full window's search to external clients (see AppSearchPipeService)
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
        MessageBox.Show(string.Format(TranslationManager.Instance["Crash_Message"], source, ex?.Message, Logger.LogDir), TranslationManager.Instance["Crash_Title"], MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static void ShowSettingsWindow(string? targetSection = null) => AppWindowManager.ShowSettingsWindow(targetSection);
    public static void ShowSearchWindow() => AppWindowManager.ShowSearchWindow();
    public static void CloseAllManagedWindows() => AppWindowManager.CloseAllManagedWindows();

    protected override void OnExit(ExitEventArgs e)
    {
        HookClient?.Stop(); HookClient?.Dispose(); HookClient = null;
        AppPipeService.StopServer(); AppSearchPipeService.StopServer(); InlineSearchManager.Instance.Dispose(); CloseAllManagedWindows();
        if (_appMutex != null) { try { _appMutex.ReleaseMutex(); } catch { } _appMutex.Dispose(); }
        base.OnExit(e);
    }
}
