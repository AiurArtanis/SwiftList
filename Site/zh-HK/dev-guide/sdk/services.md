# 宿主服務

`PluginSdk.Services` 裏的靜態服務，把宿主應用的功能暴露給插件——每一個都是包裝了宿主啓動時接好的委託的薄靜態類，所以不管底層實際運行的是什麼，插件的調用方式都一樣。

| 服務 | 用途 |
|---|---|
| `FuzzyMatchService` | `IsMatch(pattern, text)` —— `text`(或它的某個別名)是否匹配 fzf 語法的 `pattern`,用的是和宿主自身搜尋完全一致的匹配邏輯;`GetHighlightMask(text, query)` —— 對應這一對 (text, query) 的逐字元高亮掩碼,用的是宿主自己那套字面量/模糊/別名多級兜底算法(含中文拼音),這樣插件自己結果的高亮就能和其他結果保持一致,而不是只能處理簡單的字面量子串匹配。 |
| `TranslationService` | `Get(key)` / `Format(key, args)` 在運行時按當前語言查詢;`LoadEmbeddedTranslations(assembly, cultureKey, typeName)` 加載插件自己內嵌的 JSON 語言包;`GetSupportedCultures(assembly)`;`GetCurrentCulture()` —— 應用當前選定的介面語言(比如 `"zh-CN"`),這是一個獨立於操作系統語言的使用者設定。只有你確實需要拿到原始語言代碼本身時才用它(比如塞進 HTTP 的 `Accept-Language` 請求頭,或者決定翻譯 API 的目標語言)——`CultureInfo.CurrentUICulture` 反映的是操作系統的語言,不是這個設定,一旦使用者的 Windows 語言和應用內語言不一致,兩者就會悄悄對不上。 |
| `IconService` | `GetIcon(path, isDir)` 和 `GetThumbnail(path, size)` —— 帶快取的 Shell 圖示/縮略圖提取，插件不需要自己調 Windows 圖示 API。 |
| `FavoritesService` | `GetFavorites()` —— 只讀訪問使用者的[收藏夾](../../user-guide/settings/favorites)列表(`FavoriteItem`:Name、Path)。 |
| `HistoryService` | `GetHistoryEntries()` —— 每一條已記錄的[歷史記錄](../../user-guide/settings/history)條目,按最近打開優先排序,類型是 `HistoryEntry { Keyword, Path, Kind, Time }`(`Kind` 是 `HistoryEntryKind`:`File` / `Folder` / `Application`;`Keyword` 是打開時輸入框裏的搜尋文字,沒打字直接從初始面板點開的話就是空字串;`Time` 是 Unix 秒)。同一個路徑最多只會出現一次,歸屬於最近一次帶它進來的那個關鍵字。 |
| `FileMetadataService` | `GetMetadataAsync(paths)` —— 批量查詢 Size/Created/Modified/Accessed([`FileMetadata`](./abstractions#filemetadata))，用於查詢**不屬於**你當前結果集的路徑——每個 `ISearchResult` 本身就通過自己的 `Metadata` 屬性免費攜帶這些資料(參見[共享抽象契約](./abstractions#isearchresult))，所以只有拿到的路徑不是來自結果對象(比如來自你自己的配置)時才需要用這個服務。 |
| `DirectoryIndexerService` | `RegisterDirectory(pluginId, path, recursive, filterPattern)` / `UnregisterDirectories(pluginId)` / `SearchDirectoriesAsync(pluginId, query, token)` / `NotifyDirectoryChanged(pluginId)` —— 讓插件註冊自己的目錄進行後臺索引和 USN 監聽，而不用自己重新實現這套機制。 |
| `PluginSettingsService` | `GetSetting<T>(pluginId, key, defaultValue)` —— 從宿主的配置存儲裏只讀訪問插件自己持久化的設定。回退分三層:使用者存過就用持久化的值;沒存過就用你 `IConfigurable` schema 裏該欄位自己聲明的 `DefaultValue`;兩者都沒有才輪到你傳進來的 `defaultValue` 兜底——這樣 schema 裏聲明的預設值就是唯一權威來源,調用方不需要在代碼裏再手寫一份重複的預設值。如果你把某個設定快取了起來而不是每次都重新讀取,記得訂閱 `SettingChanged(pluginId, key)` 事件,在它為你的插件觸發時清空快取——宿主是在設定頁保存之後立刻觸發這個事件的,這是唯一可靠的失效時機(不管是按鍵觸發還是輪詢檢查,都要等到別的什麼東西湊巧觸發了才會看到變化,或者乾脆永遠看不到)。 |
| `SearchRefreshService` | `RefreshIfMatches(queryMatches)` —— 給資料是異步到達的 `IInstantResultProvider` 用的(參見 [`IInstantResultProvider`](./core-search-actions#iinstantresultprovider)):等你的後臺請求完成、結果也快取好之後，調用這個方法並傳入一個基於當前查詢文字的判斷函數，宿主會把所有匹配這個判斷的、正在進行的搜尋重新跑一遍，這樣剛快取好的結果就能直接顯示出來，不需要使用者重新輸入。 |
| `Logger` | `Log(message, level = LogLevel.Info)` —— 寫入 App 的日誌檔案，和宿主自己的日誌行一樣，顯示在**設定 → 運行狀態 → App** 裏。 |
| `PluginPromptService` | `Prompt(title, fields, initialValues?)` —— 彈出一個小的模態視窗，向使用者詢問給定[`PluginConfigField`](./abstractions#iconfigurable)欄位的值(用的正是 `IConfigurable` 的配置對話方塊那套欄位 schema/渲染邏輯)，按 `Key` 匹配從 `initialValues` 預填，沒有就用各欄位自己的 `DefaultValue`。返回按欄位 `Key` 索引的填寫結果，使用者取消則返回 `null`——這些值不會讀取或寫入插件真正持久化的設定，所以可以放心複用某個配置欄位的 schema 單純做一次性輸入(比如"添加前先給它起個名字")，不會碰到背後真實的那個設定項。 |

`LogLevel` 是 `Error` / `Warn` / `Info` / `Debug`，與[運行狀態日誌查看器](../../user-guide/settings/service-status)裏的等級過濾器一致。
