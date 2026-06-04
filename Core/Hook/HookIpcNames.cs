using System;

namespace SwiftList.Core.Hook
{
    /// <summary>
    /// Centralized pipe naming for the hook IPC channel.
    /// Each (user, Windows session) pair gets its own pipe name, preventing
    /// conflicts between multiple logged-in users or Fast-User-Switching sessions.
    /// </summary>
    public static class HookIpcNames
    {
        /// <summary>
        /// The pipe name used to send notifications from the hook process to the App.
        /// Format: SwiftList_Hook_{Username}_{SessionId}
        /// </summary>
        public static string NotifyPipeName =>
            $"SwiftList_Hook_{SanitizeForPipeName(Environment.UserName)}_{GetCurrentSessionId()}";

        private static string SanitizeForPipeName(string value)
        {
            // Named pipe names cannot contain backslashes (domain\user); replace with underscore.
            return value.Replace('\\', '_').Replace('/', '_');
        }

        private static int GetCurrentSessionId()
        {
            try
            {
                return System.Diagnostics.Process.GetCurrentProcess().SessionId;
            }
            catch
            {
                return 0;
            }
        }
    }
}
