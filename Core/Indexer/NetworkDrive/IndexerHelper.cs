using System;
using System.IO;

namespace SwiftList.Core.Indexer.NetworkDrive
{
    internal static class IndexerHelper
    {
        public static string? NormalizeFilter(string? directoryFilter)
        {
            if (string.IsNullOrWhiteSpace(directoryFilter))
                return null;

            string value = directoryFilter.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
            return value.EndsWith(Path.DirectorySeparatorChar) ? value : value + Path.DirectorySeparatorChar;
        }

        public static string NormalizeDrive(string drive)
        {
            if (string.IsNullOrWhiteSpace(drive))
                return string.Empty;

            char letter = char.ToUpperInvariant(drive.Trim()[0]);
            return char.IsLetter(letter) ? letter.ToString() : string.Empty;
        }

        public static string NormalizeRefreshMode(string? refreshMode)
        {
            return refreshMode switch
            {
                "15Minutes" => "15Minutes",
                "Hourly" => "Hourly",
                "Daily" => "Daily",
                _ => "Manual"
            };
        }

        public static TimeSpan? GetRefreshInterval(string refreshMode)
        {
            return refreshMode switch
            {
                "15Minutes" => TimeSpan.FromMinutes(15),
                "Hourly" => TimeSpan.FromHours(1),
                "Daily" => TimeSpan.FromDays(1),
                _ => null
            };
        }

        public static string GetCachePath(string drive)
        {
            return FileRecordStoreSerializer.GetBasePath(Path.Combine(Logger.UserDataDir, "indexes"), drive) + ".meta";
        }

        public static bool TryLoad(string drive, out NetworkIndex index)
        {
            index = new NetworkIndex(drive);
            var store = FileRecordStoreSerializer.Load(Path.Combine(Logger.UserDataDir, "indexes"), drive);
            if (store == null)
                return false;

            try
            {
                index = NetworkIndex.FromStore(store);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"[IndexerHelper] Failed to load network drive {drive}: {ex.Message}", SwiftList.Core.LogLevel.Error);
                return false;
            }
        }

        public static void Save(NetworkIndex index)
        {
            FileRecordStoreSerializer.Save(Path.Combine(Logger.UserDataDir, "indexes"), index.ToStore());
        }
    }
}
