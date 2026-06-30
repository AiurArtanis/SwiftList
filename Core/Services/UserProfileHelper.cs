namespace SwiftList.Core.Services;

public static class UserProfileHelper
{
    public static List<string> GetAllUserProfilePaths()
    {
        var paths = new List<string>();
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            if (key != null)
            {
                foreach (var subkeyName in key.GetSubKeyNames())
                {
                    using var subkey = key.OpenSubKey(subkeyName);
                    if (subkey != null)
                    {
                        var path = subkey.GetValue("ProfileImagePath") as string;
                        if (!string.IsNullOrEmpty(path))
                        {
                            var fullPath = Environment.ExpandEnvironmentVariables(path);
                            if (Directory.Exists(fullPath))
                            {
                                paths.Add(Path.GetFullPath(fullPath));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[UserProfileHelper] Failed to read ProfileList from registry: {ex.Message}", LogLevel.Warn);
        }

        // Fallback to C:\Users if registry query fails
        if (paths.Count == 0)
        {
            var usersRoot = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Users");
            if (Directory.Exists(usersRoot))
            {
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(usersRoot))
                    {
                        paths.Add(Path.GetFullPath(dir));
                    }
                }
                catch { }
            }
        }

        var publicProfile = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Users", "Public");
        if (Directory.Exists(publicProfile))
        {
            var resolvedPublic = Path.GetFullPath(publicProfile);
            if (!paths.Contains(resolvedPublic, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(resolvedPublic);
            }
        }

        return paths;
    }

    public static string GetDesktopPath(string profilePath) => Path.Combine(profilePath, "Desktop");

    public static string GetStartMenuPath(string profilePath) => Path.Combine(profilePath, "AppData", "Roaming", "Microsoft", "Windows", "Start Menu");
}
