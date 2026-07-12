using SwiftList.Plugins.BrowserData.Readers;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.BrowserData;

internal sealed class ProfileEntries
{
    public required BrowserProfileConfig Profile { get; init; }
    public List<BrowserEntry> Bookmarks { get; init; } = new();
    public List<BrowserEntry> History { get; init; } = new();
}

// Loads and caches every configured profile's bookmarks/history in memory. IInstantResultProvider.
// GetInstantResults runs synchronously on the UI thread per keystroke, so parsing JSON/querying SQLite
// can never happen inline there -- reloads run on a background thread, triggered by a config-signature
// change (mirrors FileFiltersSearchableItemProvider's own reload-on-config-change check) or a coarse
// staleness timer (history keeps growing while the user browses), and the snapshot swaps atomically
// once ready. A query in flight during a reload just keeps using the previous snapshot; there's no
// user-visible "loading" state, matching how other cached providers in this codebase behave.
internal static class BrowserDataCache
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);
    private static readonly object Lock = new();
    private static List<ProfileEntries> _snapshot = new();
    private static string _lastSignature = string.Empty;
    private static DateTime _lastLoadUtc = DateTime.MinValue;
    private static bool _loading;

    public static IReadOnlyList<ProfileEntries> GetSnapshot()
    {
        MaybeTriggerReload();
        lock (Lock)
        {
            return _snapshot;
        }
    }

    // Called once at plugin load time (see BrowserDataInstantProvider's IWarmupable) so the first real
    // "bm <query>" of the session doesn't land on a still-empty snapshot -- same reload path GetSnapshot
    // already uses, just triggered proactively instead of waiting for the first query.
    public static void Preload() => MaybeTriggerReload();

    private static void MaybeTriggerReload()
    {
        var configured = PluginSettingsService.GetSetting<List<BrowserProfileConfig>>("SwiftList.Plugins.BrowserData", "Profiles", null!);
        var signature = configured != null ? System.Text.Json.JsonSerializer.Serialize(configured) : string.Empty;

        var needsReload = signature != _lastSignature || DateTime.UtcNow - _lastLoadUtc > RefreshInterval;
        if (!needsReload)
            return;

        lock (Lock)
        {
            if (_loading)
                return;
            _loading = true;
        }

        _lastSignature = signature;
        _lastLoadUtc = DateTime.UtcNow;

        Task.Run(() =>
        {
            try
            {
                var loaded = LoadAll(configured ?? new List<BrowserProfileConfig>());
                lock (Lock)
                {
                    _snapshot = loaded;
                }
            }
            catch (Exception ex)
            {
                PluginSdk.Logger.Log($"[BrowserData] Reload failed: {ex.Message}", PluginSdk.LogLevel.Error);
            }
            finally
            {
                lock (Lock)
                {
                    _loading = false;
                }
            }
        });
    }

    private static List<ProfileEntries> LoadAll(List<BrowserProfileConfig> profiles)
    {
        var result = new List<ProfileEntries>();
        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Path) || !Directory.Exists(profile.Path))
                continue;

            try
            {
                var family = BrowserFamilyDetector.Detect(profile.Path);
                var entries = new ProfileEntries { Profile = profile };
                switch (family)
                {
                    case BrowserFamily.Chromium:
                        entries.Bookmarks.AddRange(ChromiumBookmarksReader.Read(profile.Path));
                        entries.History.AddRange(ChromiumHistoryReader.Read(profile.Path));
                        break;
                    case BrowserFamily.Firefox:
                        var (bookmarks, history) = FirefoxPlacesReader.Read(profile.Path);
                        entries.Bookmarks.AddRange(bookmarks);
                        entries.History.AddRange(history);
                        break;
                    default:
                        PluginSdk.Logger.Log($"[BrowserData] '{profile.Path}' doesn't look like a Chrome/Firefox profile folder (no Bookmarks/History/places.sqlite found), skipping.", PluginSdk.LogLevel.Warn);
                        continue;
                }
                result.Add(entries);
            }
            catch (Exception ex)
            {
                PluginSdk.Logger.Log($"[BrowserData] Failed to load profile '{profile.Path}': {ex.Message}", PluginSdk.LogLevel.Error);
            }
        }
        return result;
    }
}
