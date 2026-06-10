using System.IO;
using System.Windows;

namespace SwiftList.Tutorial;

public partial class App : Application
{
    private static Mutex? _mutex;

    private static readonly string TutorialDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SwiftListTutorial");

    private static readonly string[] TempPaths = new[]
    {
        Path.Combine(TutorialDir, "sl_logo.png"),
        Path.Combine(TutorialDir, "sl_demo.txt")
    };

    private static void CreateTutorialFiles()
    {
        try
        {
            Directory.CreateDirectory(TutorialDir);
            File.WriteAllText(TempPaths[0], "SwiftList Tutorial Dummy Logo PNG");
            File.WriteAllText(TempPaths[1], "SwiftList Tutorial Demo Text File.\nUse this to test Quick Copy and actions!");
        }
        catch { }
    }

    private static void CleanTutorialFiles()
    {
        foreach (var path in TempPaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }
        }
        try
        {
            if (Directory.Exists(TutorialDir))
            {
                Directory.Delete(TutorialDir, true);
            }
        }
        catch { }
        try
        {
            var copiedTmp = Path.Combine(Path.GetTempPath(), "swiftlist_copied.tmp");
            if (File.Exists(copiedTmp))
            {
                File.Delete(copiedTmp);
            }
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        const string mutexName = "Global\\SwiftListTutorialMutex";
        _mutex = new Mutex(true, mutexName, out var createdNew);

        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            Current.Shutdown();
            return;
        }

        CreateTutorialFiles();

        base.OnStartup(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Dispatcher Unhandled Exception: {e.Exception.Message}\n\nStack: {e.Exception.StackTrace}", "Tutorial Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            MessageBox.Show($"AppDomain Unhandled Exception: {ex.Message}\n\nStack: {ex.StackTrace}", "Tutorial Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CleanTutorialFiles();

        if (_mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch { }
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
