# 宿主服务

`PluginSdk.Services` 里的静态服务，把宿主应用的功能暴露给插件——每一个都是包装了宿主启动时接好
的委托的薄静态类，所以不管底层实际运行的是什么，插件的调用方式都一样。

| 服务 | 用途 |
|---|---|
| `FuzzyMatchService` | `IsMatch(pattern, text)` —— `text`(或它的某个别名)是否匹配 fzf 语法的 `pattern`,用的是和宿主自身搜索完全一致的匹配逻辑;`GetHighlightMask(text, query)` —— 对应这一对 (text, query) 的逐字符高亮掩码,用的是宿主自己那套字面量/模糊/别名多级兜底算法(含中文拼音),这样插件自己结果的高亮就能和其他结果保持一致,而不是只能处理简单的字面量子串匹配。 |
| `TranslationService` | `Get(key)` / `Format(key, args)` 在运行时按当前语言查询;`LoadEmbeddedTranslations(assembly, cultureKey, typeName)` 加载插件自己内嵌的 JSON 语言包;`GetSupportedCultures(assembly)`;`GetCurrentCulture()` —— 应用当前选定的界面语言(比如 `"zh-CN"`),这是一个独立于操作系统语言的用户设置。只有你确实需要拿到原始语言代码本身时才用它(比如塞进 HTTP 的 `Accept-Language` 请求头,或者决定翻译 API 的目标语言)——`CultureInfo.CurrentUICulture` 反映的是操作系统的语言,不是这个设置,一旦用户的 Windows 语言和应用内语言不一致,两者就会悄悄对不上。 |
| `IconService` | `GetIcon(path, isDir)` 和 `GetThumbnail(path, size)` —— 带缓存的 Shell 图标/缩略图提取，插件不需要自己调 Windows 图标 API。 |
| `FavoritesService` | `GetFavorites()` —— 只读访问用户的[收藏夹](../../user-guide/settings/favorites)列表(`FavoriteItem`:Name、Path)。 |
| `HistoryService` | `GetHistoryEntries()` —— 每一条已记录的[历史记录](../../user-guide/settings/history)条目,按最近打开优先排序,类型是 `HistoryEntry { Keyword, Path, Kind, Time }`(`Kind` 是 `HistoryEntryKind`:`File` / `Folder` / `Application`;`Keyword` 是打开时输入框里的搜索文字,没打字直接从初始面板点开的话就是空字符串;`Time` 是 Unix 秒)。同一个路径最多只会出现一次,归属于最近一次带它进来的那个关键字。 |
| `FileMetadataService` | `GetMetadataAsync(paths)` —— 批量查询 Size/Created/Modified/Accessed([`FileMetadata`](./abstractions#filemetadata))，用于查询**不属于**你当前结果集的路径——每个 `ISearchResult` 本身就通过自己的 `Metadata` 属性免费携带这些数据(参见[共享抽象契约](./abstractions#isearchresult))，所以只有拿到的路径不是来自结果对象(比如来自你自己的配置)时才需要用这个服务。 |
| `DirectoryIndexerService` | `RegisterDirectory(pluginId, path, recursive, filterPattern)` / `UnregisterDirectories(pluginId)` / `SearchDirectoriesAsync(pluginId, query, token)` / `NotifyDirectoryChanged(pluginId)` —— 让插件注册自己的目录进行后台索引和 USN 监听，而不用自己重新实现这套机制。 |
| `PluginSettingsService` | `GetSetting<T>(pluginId, key, defaultValue)` —— 从宿主的配置存储里只读访问插件自己持久化的设置。回退分三层:用户存过就用持久化的值;没存过就用你 `IConfigurable` schema 里该字段自己声明的 `DefaultValue`;两者都没有才轮到你传进来的 `defaultValue` 兜底——这样 schema 里声明的默认值就是唯一权威来源,调用方不需要在代码里再手写一份重复的默认值。如果你把某个设置缓存了起来而不是每次都重新读取,记得订阅 `SettingChanged(pluginId, key)` 事件,在它为你的插件触发时清空缓存——宿主是在设置页保存之后立刻触发这个事件的,这是唯一可靠的失效时机(不管是按键触发还是轮询检查,都要等到别的什么东西凑巧触发了才会看到变化,或者干脆永远看不到)。 |
| `SearchRefreshService` | `RefreshIfMatches(queryMatches)` —— 给数据是异步到达的 `IInstantResultProvider` 用的(参见 [`IInstantResultProvider`](./core-search-actions#iinstantresultprovider)):等你的后台请求完成、结果也缓存好之后，调用这个方法并传入一个基于当前查询文字的判断函数，宿主会把所有匹配这个判断的、正在进行的搜索重新跑一遍，这样刚缓存好的结果就能直接显示出来，不需要用户重新输入。 |
| `Logger` | `Log(message, level = LogLevel.Info)` —— 写入 App 的日志文件，和宿主自己的日志行一样，显示在**设置 → 运行状态 → App** 里。 |
| `PluginPromptService` | `Prompt(title, fields, initialValues?)` —— 弹出一个小的模态窗口，向用户询问给定[`PluginConfigField`](./abstractions#iconfigurable)字段的值(用的正是 `IConfigurable` 的配置对话框那套字段 schema/渲染逻辑)，按 `Key` 匹配从 `initialValues` 预填，没有就用各字段自己的 `DefaultValue`。返回按字段 `Key` 索引的填写结果，用户取消则返回 `null`——这些值不会读取或写入插件真正持久化的设置，所以可以放心复用某个配置字段的 schema 单纯做一次性输入(比如"添加前先给它起个名字")，不会碰到背后真实的那个设置项。 |

`LogLevel` 是 `Error` / `Warn` / `Info` / `Debug`，与
[运行状态日志查看器](../../user-guide/settings/service-status)里的等级过滤器一致。
