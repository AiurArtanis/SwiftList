using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SwiftList.App.Services
{
    public class UpdateService
    {
        private static readonly Lazy<UpdateService> _instance = new Lazy<UpdateService>(() => new UpdateService());
        public static UpdateService Instance => _instance.Value;

        private readonly HttpClient _httpClient;
        private const string GITHUB_API_URL = "https://api.github.com/repos/SwiftList/SwiftList/releases/latest";

        private const int TokenLinkedToken = 19;
        private const uint TOKEN_QUERY = 0x0008;

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, ref IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private UpdateService()
        {
            _httpClient = new HttpClient();
            // User-Agent header is strictly required by GitHub API
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SwiftList", "1.0.0"));
        }

        /// <summary>
        /// Check if current process is running with administrative privileges or has a linked admin token.
        /// </summary>
        public bool IsUserAdmin()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    if (principal.IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        return true;
                    }
                }

                // If not running as admin, try to check if there is an elevated linked token (UAC)
                IntPtr hProcess = Process.GetCurrentProcess().Handle;
                if (OpenProcessToken(hProcess, TOKEN_QUERY, out IntPtr hToken))
                {
                    try
                    {
                        IntPtr hLinkedToken = IntPtr.Zero;
                        int returnLength = 0;
                        bool success = GetTokenInformation(hToken, TokenLinkedToken, ref hLinkedToken, IntPtr.Size, out returnLength);

                        if (success && hLinkedToken != IntPtr.Zero)
                        {
                            try
                            {
                                using (var linkedIdentity = new WindowsIdentity(hLinkedToken))
                                {
                                    var linkedPrincipal = new WindowsPrincipal(linkedIdentity);
                                    return linkedPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
                                }
                            }
                            finally
                            {
                                CloseHandle(hLinkedToken);
                            }
                        }
                    }
                    finally
                    {
                        CloseHandle(hToken);
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Retrieves the latest release info from GitHub.
        /// </summary>
        public async Task<GitHubReleaseInfo?> CheckForUpdatesAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(GITHUB_API_URL);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<GitHubReleaseInfo>(response, options);
            }
            catch (Exception ex)
            {
                SwiftList.Core.Logger.Log($"[UpdateService] Check update failed: {ex}", SwiftList.Core.LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// Downloads the portable zip, extracts it, and triggers the portable-updater.bat.
        /// </summary>
        public async Task<bool> StartSilentUpdateAsync(string zipUrl, Action<double>? progressCallback = null)
        {
            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(), "SwiftListUpdate");
                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, true);
                }
                Directory.CreateDirectory(tempPath);

                var tempZipFile = Path.Combine(tempPath, "latest.zip");

                // Download zip file with progress report
                using (var response = await _httpClient.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempZipFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        var totalRead = 0L;
                        int read;
                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;
                            if (totalBytes != -1 && progressCallback != null)
                            {
                                progressCallback((double)totalRead / totalBytes);
                            }
                        }
                    }
                }

                // Extract Zip
                var extractPath = Path.Combine(tempPath, "extracted");
                ZipFile.ExtractToDirectory(tempZipFile, extractPath);

                // Dynamically detect source path format (flat files vs wrapped in a SwiftList folder)
                var finalSourcePath = extractPath;
                var subDirs = Directory.GetDirectories(extractPath);
                if (subDirs.Length == 1 && Path.GetFileName(subDirs[0]).Equals("SwiftList", StringComparison.OrdinalIgnoreCase))
                {
                    finalSourcePath = subDirs[0];
                }

                // Find batch updater
                var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                var updaterBat = Path.Combine(currentDir, "portable-updater.bat");

                if (!File.Exists(updaterBat))
                {
                    throw new FileNotFoundException("Updater script (portable-updater.bat) not found in application directory.");
                }

                // Launch batch updater in background with Admin privileges (elevated if not already)
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{updaterBat}\" \"{finalSourcePath}\" \"{currentDir.TrimEnd('\\')}\"\"",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Verb = "runas" // Prompt for UAC elevation if not already running as admin
                };

                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                SwiftList.Core.Logger.Log($"[UpdateService] Auto update failed: {ex}", SwiftList.Core.LogLevel.Error);
                return false;
            }
        }
    }

    public class GitHubReleaseInfo
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
