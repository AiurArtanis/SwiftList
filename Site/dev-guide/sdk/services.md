# Host Services

Static services in `PluginSdk.Services` that expose host-app functionality back to plugins — each
is a thin static class wrapping a delegate the host wires up at startup, so plugins call them the
same way regardless of what's running underneath.

| Service | Purpose |
|---|---|
| `FuzzyMatchService` | `IsMatch(pattern, text)` — whether `text` (or one of its aliases) matches an fzf-syntax `pattern`, using the exact same matching the host's own search uses; `GetHighlightMask(text, query)` — the per-character highlight mask for that pair, using the same literal/fuzzy/alias fallback tiers (including CJK pinyin) the host's own results highlight with, so a plugin's results highlight consistently instead of only handling a literal substring match. |
| `TranslationService` | `Get(key)` / `Format(key, args)` for runtime lookups against the active language; `LoadEmbeddedTranslations(assembly, cultureKey, typeName)` to load a plugin's own embedded JSON translations; `GetSupportedCultures(assembly)`. |
| `IconService` | `GetIcon(path, isDir)` and `GetThumbnail(path, size)` — cached shell icon/thumbnail extraction, so a plugin never has to shell out to the Windows icon APIs itself. |
| `FavoritesService` | `GetFavorites()` — read-only access to the user's [Favorites](../../user-guide/settings/favorites) list (`FavoriteItem`: Name, Path). |
| `HistoryService` | `GetHistoryPaths()` — recently-visited paths from [History](../../user-guide/settings/history). |
| `FileMetadataService` | `GetMetadataAsync(paths)` — batched Size/Created/Modified/Accessed lookup (`FileMetadata` record struct), for plugins adding a [result column](./ui-extensions#iresultcolumnprovider) that needs this data for many results at once. |
| `DirectoryIndexerService` | `RegisterDirectory(pluginId, path, recursive, filterPattern)` / `UnregisterDirectories(pluginId)` / `SearchDirectoriesAsync(pluginId, query, token)` / `NotifyDirectoryChanged(pluginId)` — lets a plugin register its own directories for background indexing and USN monitoring without reimplementing that machinery. |
| `PluginSettingsService` | `GetSetting<T>(pluginId, key, defaultValue)` — read-only access to a plugin's own persisted settings from the host's config store. |
| `Logger` | `Log(message, level = LogLevel.Info)` — writes to the App's log file, visible in **Settings → Service Status → App** exactly like the host's own log lines. |

`LogLevel` is `Error` / `Warn` / `Info` / `Debug`, matching the level filter in the
[Service Status log viewer](../../user-guide/settings/service-status).
