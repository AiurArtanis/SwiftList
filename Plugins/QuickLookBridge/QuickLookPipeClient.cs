using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.QuickLookBridge;

// Talks directly to QuickLook's (github.com/QL-Win/QuickLook) named pipe -- the exact same one its own
// CLI second-instance forwarding uses -- instead of spawning a process per preview. Pipe name and message
// IDs below are copied verbatim from QuickLook/PipeServerManager.cs; they're QuickLook's private
// implementation detail, not a published API, so a future QuickLook release could change them without
// notice and silently break this.
internal static class QuickLookPipeClient
{
    private const int ConnectTimeoutMs = 1000;

    // QuickLook.App.PipeMessages.Invoke -- shows/updates the preview for `path` without touching
    // pin/topmost state (unlike Toggle, it doesn't hide an already-visible window for the same path).
    private const string InvokeMessage = "QuickLook.App.PipeMessages.Invoke";

    // QuickLook.App.PipeMessages.Close -- hides QuickLook's viewer window via its own code path. Used by
    // QuickLookEmbedHost on teardown, after detaching the window we'd re-parented, so QuickLook's own
    // window-state bookkeeping (_viewerWindow.IsVisible etc.) stays in sync instead of just being abandoned
    // wherever our forced re-parent/de-parent left it.
    private const string CloseMessage = "QuickLook.App.PipeMessages.Close";

    // Anything QuickLook's own PipeMessages switch doesn't recognize falls into its `default: return
    // false` branch -- a real message ID with zero visible side effect, used purely to test reachability.
    private const string PingMessage = "SwiftList.Plugins.QuickLookBridge.Ping";

    private static readonly string PipeName =
        "QuickLook.App.Pipe." + (WindowsIdentity.GetCurrent().User?.Value ?? string.Empty);

    // No caching: every call does a fresh probe.
    public static bool IsAvailable() => TrySend(PingMessage, string.Empty);

    public static bool TryInvokePreview(string path) => TrySend(InvokeMessage, path);

    public static bool TryClosePreview() => TrySend(CloseMessage, string.Empty);

    private static bool TrySend(string pipeMessage, string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(ConnectTimeoutMs);

            // QuickLook's server does an unconditional reader.ReadLine() with no null guard, so a client
            // that connects without ever writing a line crashes its read loop (NullReferenceException on
            // the null result) and takes down the pipe for the rest of that QuickLook session -- always
            // write a real line, even for the no-op ping.
            using var writer = new StreamWriter(client);
            writer.WriteLine($"{pipeMessage}|{path}|");
            writer.Flush();
            Logger.Log($"[QuickLookBridge] pipe send ok: {pipeMessage} '{path}' (pipe='{PipeName}')", LogLevel.Info);
            return true;
        }
        catch (Exception ex)
        {
            // Temporary diagnostic (Info, not Warn/Debug) while chasing an inconsistent-availability
            // report -- narrow the log level back down once that's understood.
            Logger.Log($"[QuickLookBridge] pipe send FAILED: {pipeMessage} '{path}' (pipe='{PipeName}') -> {ex.GetType().Name}: {ex.Message}", LogLevel.Info);
            return false;
        }
    }
}
