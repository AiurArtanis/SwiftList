namespace SwiftList.Core;

public enum LogLevel
{
    Error = 0,
    Warn = 1,
    Info = 2,
    Debug = 3
}

public static class Logger
{
    /// <summary>
    /// System-wide shared data directory: %ProgramData%\SwiftList
    /// Used by the service for logs, index cache, etc.
    /// </summary>
    public static readonly string SharedDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SwiftList");

    /// <summary>
    /// Per-user data directory: %LocalAppData%\SwiftList
    /// Used by the UI for per-user logs.
    /// </summary>
    public static readonly string UserDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SwiftList");

    private static string _logDir = string.Empty;
    private static string _logPath = string.Empty;
    private static LogLevel _minimumLevel = LogLevel.Info;
    private static readonly object LogLock = new();

    /// <summary>
    /// Gets the directory where the current log file is stored.
    /// </summary>
    public static string LogDir => _logDir;

    public static LogLevel MinimumLevel
    {
        get => _minimumLevel;
        set => _minimumLevel = value;
    }

    /// <summary>
    /// Initialize the logger.
    /// </summary>
    /// <param name="logFileName">Log file name, e.g. "swiftlist_service_log.txt"</param>
    /// <param name="baseDirectory">
    /// Base directory for the log file. Pass <see cref="SharedDataDir"/> for
    /// system-wide (service) logs, or <see cref="UserDataDir"/> for per-user (UI) logs.
    /// If null, defaults to <see cref="UserDataDir"/>.
    /// </param>
    /// <param name="overwrite">Whether to overwrite the log file on init.</param>
    public static void Initialize(string logFileName, string? baseDirectory = null, bool overwrite = true)
    {
        lock (LogLock)
        {
            try
            {
                _logDir = Path.Combine(baseDirectory ?? UserDataDir, "logs");
                Directory.CreateDirectory(_logDir);
                _logPath = Path.Combine(_logDir, logFileName);

                if (overwrite)
                {
                    File.WriteAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Log initialized ({logFileName})\n");
                }
            }
            catch
            {
                // Fallback: try writing next to the executable
                _logDir = AppDomain.CurrentDomain.BaseDirectory;
                _logPath = Path.Combine(_logDir, logFileName);
            }
        }
    }

    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        if (level > _minimumLevel)
            return;

        lock (LogLock)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}\n");
            }
            catch
            {
                // Ignore
            }
        }
    }
}
