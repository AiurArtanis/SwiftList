using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using SwiftList.Core;
using SwiftList.Core.Services;
using SwiftList.App.ViewModels;
using SwiftList.App.ViewModels.Settings;
using SwiftList.App.Services;
using Application = System.Windows.Application;
using MessageBox = SwiftList.App.Views.Controls.CustomMessageBox;

namespace SwiftList.App
{
    public partial class App : Application
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        private System.Threading.Mutex? _appMutex;
        public static SwiftList.Core.Hook.HookIpcClient? HookClient { get; private set; }


        protected override void OnStartup(StartupEventArgs e)
        {
            // Initialize logger first so we can log elevation decisions and issues
            Logger.Initialize("app.log", overwrite: true);
            var settings = UserSettings.Load();
            Logger.MinimumLevel = ExperienceSettingsViewModel.ParseLogLevel(settings.LogLevel);
            Logger.Log("=========================================");
            Logger.Log($"Application starting with arguments: {string.Join(" ", e.Args)}");
            Logger.Log($"[App] Running as Administrator: {ElevationManager.IsRunningAsAdmin()}");


            // Single instance check per user session
            // We append the username to guarantee multi-user isolation on the same machine
            string mutexName = $@"Local\SwiftList_App_{Environment.UserName}";
            _appMutex = new System.Threading.Mutex(true, mutexName, out bool createdNew);

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
                AppPipeService.SendActivateSignal();
                Shutdown();
                return;
            }

            string serviceExe = ServiceInstallManager.GetServiceExePath();
            HookClient = new SwiftList.Core.Hook.HookIpcClient(serviceExe, settings.AutoElevateIfAdmin);
            HookClient.OnActivated += () =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var quickSearchWindow = Current.MainWindow as QuickSearchWindow;
                    quickSearchWindow?.ToggleVisibility();
                }));
            };
            HookClient.Start();

            // Set up global exception handlers
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                LogException("AppDomain UnhandledException", args.ExceptionObject as Exception);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                LogException("DispatcherUnhandledException", args.Exception);
                args.Handled = true; // Prevent crash if possible
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                LogException("TaskScheduler UnobservedTaskException", args.Exception);
                args.SetObserved();
            };

            // Force load all plugins (actions and alias providers) on startup
            _ = SwiftList.App.Services.PluginManager.Instance;

            // Now that all plugins are loaded, initialize translations.
            // This must happen after PluginManager to avoid a Lazy<T> circular initialization crash.
            try
            {
                // Register TranslationService delegate for decoupled plugins
                SwiftList.PluginSdk.TranslationService.LookupFunc = key => SwiftList.App.Services.TranslationManager.Instance[key];

                SwiftList.App.Services.TranslationManager.Instance.ReloadTranslations();
                Logger.Log("[App] TranslationManager initialized.");

                // Initialize ThemeManager with the saved theme setting
                ThemeManager.Instance.Initialize(settings.Theme);
                Logger.Log($"[App] ThemeManager initialized with theme: {settings.Theme}");
            }
            catch (Exception ex)
            {
                Logger.Log($"[App] Failed to initialize TranslationManager or ThemeManager: {ex.Message}");
            }

            // Start the activation named pipe server to listen to subsequent launches
            AppPipeService.StartPipeServer();

            Logger.Log("Starting normal WPF GUI client mode.");
            base.OnStartup(e);

            // After QuickSearchWindow is created (via StartupUri), start InlineSearchManager
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var quickSearchWindow = Current.MainWindow as QuickSearchWindow;
                if (quickSearchWindow != null)
                {
                    SwiftList.App.Services.InlineSearchManager.Instance.Start();
                    Logger.Log("[App] InlineSearchManager started.");
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public static void HideInlineSearch()
        {
            SwiftList.App.Services.InlineSearchManager.Instance.CloseInlineSearch();
        }

        private static void LogException(string source, Exception? ex)
        {
            string details = ex != null ? ex.ToString() : "Null exception object";
            Logger.Log($"CRITICAL CRASH ({source}):\n{details}");
            
            // Show message box to alert user
            MessageBox.Show(string.Format(SwiftList.App.Services.TranslationManager.Instance["Crash_Message"], source, ex?.Message, Logger.LogDir), SwiftList.App.Services.TranslationManager.Instance["Crash_Title"], MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public static void ShowSettingsWindow(string? targetSection = null)
            => AppWindowManager.ShowSettingsWindow(targetSection);

        public static void ShowSearchWindow()
            => AppWindowManager.ShowSearchWindow();

        public static void CloseAllManagedWindows()
            => AppWindowManager.CloseAllManagedWindows();

        protected override void OnExit(ExitEventArgs e)
        {
            HookClient?.Stop();
            HookClient?.Dispose();
            HookClient = null;

            AppPipeService.StopServer();

            SwiftList.App.Services.InlineSearchManager.Instance.Dispose();
            CloseAllManagedWindows();
            if (_appMutex != null)
            {
                try
                {
                    _appMutex.ReleaseMutex();
                }
                catch { }
                _appMutex.Dispose();
            }
            base.OnExit(e);
        }
    }
}
