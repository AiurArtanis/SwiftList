using SwiftList.Core;

namespace SwiftList.App.Services;

// Routes a "swiftlist://" URI to the matching in-app action. Reached from two places (see App.xaml.cs):
// this process's own launch args, when the OS invoked SwiftList directly via the link; or forwarded
// through AppPipeService from a second-instance launch, when SwiftList was already running. Every route
// touches a Window, so dispatching onto the UI thread is mandatory here rather than left to callers.
// Unrecognized/malformed input takes no action beyond logging -- the protocol is registered system-wide
// (see UrlProtocolManager), so any process can invoke it with anything, and a bad or unknown link should
// never surprise the user with unexpected behavior.
public static class UriRouter
{
    public static bool IsSwiftListUri(string? candidate) =>
        Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme.Equals("swiftlist", StringComparison.OrdinalIgnoreCase);

    public static void Route(string uriString)
    {
        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("swiftlist", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Log($"[UriRouter] Ignoring malformed/non-swiftlist URI: {uriString}", LogLevel.Warn);
            return;
        }

        var route = uri.Host;
        var arg = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            switch (route.ToLowerInvariant())
            {
                case "":
                    (System.Windows.Application.Current.MainWindow as QuickSearchWindow)?.ShowWindow();
                    break;

                case "search":
                    (System.Windows.Application.Current.MainWindow as QuickSearchWindow)?.ShowWindow(string.IsNullOrEmpty(arg) ? null : arg);
                    break;

                case "fullsearch":
                    FileExecutor.OpenFileOrFolder("__SHOW_MORE__", arg);
                    break;

                case "settings":
                    AppWindowManager.ShowSettingsWindow(string.IsNullOrEmpty(arg) ? null : arg);
                    break;

                default:
                    Logger.Log($"[UriRouter] Unknown route: {uriString}", LogLevel.Warn);
                    break;
            }
        }));
    }
}
