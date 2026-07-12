# 宿主服务

`PluginSdk.Services` 里的静态服务，把宿主应用的功能暴露给插件——每一个都是包装了宿主启动时接好
的委托的薄静态类，所以不管底层实际运行的是什么，插件的调用方式都一样。

| 服务 | 用途 |
|---|---|
| `FuzzyMatchService` | `IsMatch(pattern, text)` —— `text`(或它的某个别名)是否匹配 fzf 语法的 `pattern`,用的是和宿主自身搜索完全一致的匹配逻辑;`GetHighlightMask(text, query)` —— 对应这一对 (text, query) 的逐字符高亮掩码,用的是宿主自己那套字面量/模糊/别名多级兜底算法(含中文拼音),这样插件自己结果的高亮就能和其他结果保持一致,而不是只能处理简单的字面量子串匹配。 |
| `TranslationService` | `Get(key)` / `Format(key, args)` 在运行时按当前语言查询;`LoadEmbeddedTranslations(assembly, cultureKey, typeName)` 加载插件自己内嵌的 JSON 语言包;`GetSupportedCultures(assembly)`。 |
| `IconService` | `GetIcon(path, isDir)` 和 `GetThumbnail(path, size)` —— 带缓存的 Shell 图标/缩略图提取，插件不需要自己调 Windows 图标 API。 |
| `FavoritesService` | `GetFavorites()` —— 只读访问用户的[收藏夹](../../user-guide/settings/favorites)列表(`FavoriteItem`:Name、Path)。 |
| `HistoryService` | `GetHistoryPaths()` —— 来自[历史记录](../../user-guide/settings/history)的最近访问路径。 |
| `FileMetadataService` | `GetMetadataAsync(paths)` —— 批量查询 Size/Created/Modified/Accessed(`FileMetadata` record struct)，适合插件添加一个需要对大量结果同时取这些数据的[结果列](./ui-extensions#iresultcolumnprovider)。 |
| `DirectoryIndexerService` | `RegisterDirectory(pluginId, path, recursive, filterPattern)` / `UnregisterDirectories(pluginId)` / `SearchDirectoriesAsync(pluginId, query, token)` / `NotifyDirectoryChanged(pluginId)` —— 让插件注册自己的目录进行后台索引和 USN 监听，而不用自己重新实现这套机制。 |
| `PluginSettingsService` | `GetSetting<T>(pluginId, key, defaultValue)` —— 从宿主的配置存储里只读访问插件自己持久化的设置。 |
| `Logger` | `Log(message, level = LogLevel.Info)` —— 写入 App 的日志文件，和宿主自己的日志行一样，显示在**设置 → 运行状态 → App** 里。 |

`LogLevel` 是 `Error` / `Warn` / `Info` / `Debug`，与
[运行状态日志查看器](../../user-guide/settings/service-status)里的等级过滤器一致。
