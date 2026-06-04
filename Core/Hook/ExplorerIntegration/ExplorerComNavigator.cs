using System;
using SwiftList.Core;

namespace SwiftList.Core.Hook
{
    internal static class ExplorerComNavigator
    {
        public static string? GetActiveExplorerPath(IntPtr targetHwnd, Action<string>? onError = null)
        {
            try
            {
                var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
                if (shellWindowsType == null) return null;

                dynamic shellWindows = Activator.CreateInstance(shellWindowsType)!;
                int count = shellWindows.Count;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic? window = shellWindows.Item(i);
                        if (window == null) continue;

                        IntPtr hwnd = (IntPtr)window.HWND;
                        if (hwnd == targetHwnd)
                        {
                            string path = window.Document.Folder.Self.Path;
                            if (!string.IsNullOrEmpty(path))
                            {
                                if (path.StartsWith("::") || path.Contains("::{") ||
                                    path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                                {
                                    return null;
                                }
                                return path;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                onError?.Invoke($"COM Error: {ex.Message}");
            }

            return null;
        }
    }
}
