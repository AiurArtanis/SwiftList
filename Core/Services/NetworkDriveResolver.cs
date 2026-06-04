using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SwiftList.Core
{
    public class ResolvedNetworkDrive
    {
        public string Letter { get; set; } = string.Empty;
        public string UncPath { get; set; } = string.Empty;
        public bool IsReady { get; set; }
    }

    public static class NetworkDriveResolver
    {
        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetGetConnection(string lpLocalName, StringBuilder lpRemoteName, ref int lpnLength);

        /// <summary>
        /// Resolves a drive letter to its UNC path via WNetGetConnection (respects active session boundaries).
        /// Returns null if the drive is not a known mapped network drive.
        /// </summary>
        public static string? ResolveToUnc(string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter)) return null;
            
            // Get drive letter followed by colon, e.g. "Y:"
            string localName = driveLetter.Trim().Split(':')[0] + ":";

            var sb = new StringBuilder(512);
            int len = sb.Capacity;
            int error = WNetGetConnection(localName, sb, ref len);

            if (error == 0) // NO_ERROR
            {
                return sb.ToString();
            }
            
            return null;
        }

        /// <summary>
        /// Returns all network drives known to the current user session.
        /// </summary>
        public static List<ResolvedNetworkDrive> GetNetworkDrives()
        {
            var results = new List<ResolvedNetworkDrive>();

            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType == DriveType.Network)
                    {
                        string letter = d.Name.Split(':')[0].ToUpperInvariant();
                        string uncPath = ResolveToUnc(letter) ?? string.Empty;

                        results.Add(new ResolvedNetworkDrive
                        {
                            Letter = letter,
                            UncPath = uncPath,
                            IsReady = d.IsReady
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[NetworkDriveResolver] Failed to get network drives: {ex.Message}", SwiftList.Core.LogLevel.Error);
            }

            return results.OrderBy(d => d.Letter).ToList();
        }

        /// <summary>
        /// If the path is a mapped network drive path (e.g. "Y:\path"), resolves it to
        /// its UNC path (e.g. "\\server\share\path"). Otherwise returns the original path.
        /// </summary>
        public static string ResolveToPhysicalPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            string normalized = path.Trim().Replace('/', '\\');

            if (normalized.Length >= 2 && normalized[1] == ':' && char.IsLetter(normalized[0]))
            {
                string letter = normalized.Substring(0, 1).ToUpperInvariant();
                string? unc = ResolveToUnc(letter);
                if (!string.IsNullOrEmpty(unc))
                {
                    string relative = normalized.Substring(2).TrimStart('\\');
                    return Path.Combine(unc, relative);
                }
            }

            return path;
        }

        /// <summary>
        /// If the path is a UNC path (e.g. "\\server\share\path") that matches a mapped
        /// network drive, resolves it to the drive-letter path (e.g. "Y:\path").
        /// Otherwise returns the original path.
        /// </summary>
        public static string ResolveToLogicalPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            string normalized = path.Trim().Replace('/', '\\');
            if (!normalized.StartsWith(@"\\")) return path;

            try
            {
                foreach (var drive in GetNetworkDrives())
                {
                    string uncRoot = string.IsNullOrEmpty(drive.UncPath)
                        ? (ResolveToUnc(drive.Letter) ?? string.Empty)
                        : drive.UncPath;

                    if (string.IsNullOrEmpty(uncRoot)) continue;

                    uncRoot = uncRoot.Replace('/', '\\').TrimEnd('\\');
                    if (normalized.Equals(uncRoot, StringComparison.OrdinalIgnoreCase))
                        return drive.Letter + @":\";

                    if (normalized.StartsWith(uncRoot + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = normalized.Substring(uncRoot.Length).TrimStart('\\');
                        return Path.Combine(drive.Letter + @":\", relative);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[NetworkDriveResolver] ResolveToLogicalPath failed: {ex.Message}", SwiftList.Core.LogLevel.Error);
            }

            return path;
        }
    }
}
