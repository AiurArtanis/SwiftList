# 宿主服務

`PluginSdk.Services` 裡的靜態服務，把宿主應用程式的功能公開給外掛——每一個都是包裝了宿主啟動時接
好的委派的薄靜態類別，所以不管底層實際執行的是什麼，外掛的呼叫方式都一樣。

| 服務 | 用途 |
|---|---|
| `FuzzyMatchService` | `IsMatch(pattern, text)` —— `text`(或它的某個別名)是否符合 fzf 語法的 `pattern`,用的是和宿主自身搜尋完全一致的比對邏輯;`GetHighlightMask(text, query)` —— 對應這一對 (text, query) 的逐字元高亮遮罩,用的是宿主自己那套字面量/模糊/別名多層保底演算法(含中文拼音),這樣外掛自己結果的高亮就能和其他結果保持一致,而不是只能處理簡單的字面量子字串比對。 |
| `TranslationService` | `Get(key)` / `Format(key, args)` 在執行階段按目前語言查詢;`LoadEmbeddedTranslations(assembly, cultureKey, typeName)` 載入外掛自己內嵌的 JSON 語言包;`GetSupportedCultures(assembly)`;`GetCurrentCulture()` —— 應用程式目前選定的介面語言(比如 `"zh-CN"`),這是一個獨立於作業系統語言的使用者設定。只有你確實需要拿到原始語言代碼本身時才用它(比如塞進 HTTP 的 `Accept-Language` 要求標頭,或者決定翻譯 API 的目標語言)——`CultureInfo.CurrentUICulture` 反映的是作業系統的語言,不是這個設定,一旦使用者的 Windows 語言和應用程式內語言不一致,兩者就會悄悄對不上。 |
| `IconService` | `GetIcon(path, isDir)` 和 `GetThumbnail(path, size)` —— 帶快取的 Shell 圖示/縮圖擷取，外掛不需要自己呼叫 Windows 圖示 API。 |
| `FavoritesService` | `GetFavorites()` —— 唯讀存取使用者的[我的最愛](../../user-guide/settings/favorites)清單(`FavoriteItem`:Name、Path)。 |
| `HistoryService` | `GetHistoryEntries()` —— 每一條已記錄的[歷史記錄](../../user-guide/settings/history)項目,按最近開啟優先排序,型別是 `HistoryEntry { Keyword, Path, Kind, Time }`(`Kind` 是 `HistoryEntryKind`:`File` / `Folder` / `Application`;`Keyword` 是開啟時輸入框裡的搜尋文字,沒打字直接從起始面板點開的話就是空字串;`Time` 是 Unix 秒)。同一個路徑最多只會出現一次,歸屬於最近一次帶它進來的那個關鍵字。 |
| `FileMetadataService` | `GetMetadataAsync(paths)` —— 批次查詢 Size/Created/Modified/Accessed([`FileMetadata`](./abstractions#filemetadata))，用於查詢**不屬於**你目前結果集的路徑——每個 `ISearchResult` 本身就透過自己的 `Metadata` 屬性免費攜帶這些資料(參見[共用抽象契約](./abstractions#isearchresult))，所以只有拿到的路徑不是來自結果物件(比如來自你自己的設定)時才需要用這個服務。 |
| `DirectoryIndexerService` | `RegisterDirectory(pluginId, path, recursive, filterPattern)` / `UnregisterDirectories(pluginId)` / `SearchDirectoriesAsync(pluginId, query, token)` / `NotifyDirectoryChanged(pluginId)` —— 讓外掛註冊自己的目錄進行背景索引和 USN 監看，而不用自己重新實作這套機制。 |
| `PluginSettingsService` | `GetSetting<T>(pluginId, key, defaultValue)` —— 從宿主的設定儲存區裡唯讀存取外掛自己持久化的設定。回退分三層:使用者存過就用持久化的值;沒存過就用你 `IConfigurable` schema 裡該欄位自己宣告的 `DefaultValue`;兩者都沒有才輪到你傳進來的 `defaultValue` 保底——這樣 schema 裡宣告的預設值就是唯一權威來源,呼叫方不需要在程式碼裡再手寫一份重複的預設值。如果你把某個設定快取了起來而不是每次都重新讀取,記得訂閱 `SettingChanged(pluginId, key)` 事件,在它為你的外掛觸發時清空快取——宿主是在設定頁儲存之後立刻觸發這個事件的,這是唯一可靠的失效時機(不管是按鍵觸發還是輪詢檢查,都要等到別的什麼東西湊巧觸發了才會看到變化,或者乾脆永遠看不到)。 |
| `SearchRefreshService` | `RefreshIfMatches(queryMatches)` —— 給資料是非同步到達的 `IInstantResultProvider` 用的(參見 [`IInstantResultProvider`](./core-search-actions#iinstantresultprovider)):等你的背景要求完成、結果也快取好之後，呼叫這個方法並傳入一個基於目前查詢文字的判斷函式，宿主會把所有符合這個判斷的、正在進行的搜尋重新跑一遍，這樣剛快取好的結果就能直接顯示出來，不需要使用者重新輸入。 |
| `Logger` | `Log(message, level = LogLevel.Info)` —— 寫入 App 的記錄檔，和宿主自己的記錄行一樣，顯示在**設定 → 執行狀態 → App** 裡。 |
| `PluginPromptService` | `Prompt(title, fields, initialValues?)` —— 彈出一個小的強制回應視窗，向使用者詢問給定[`PluginConfigField`](./abstractions#iconfigurable)欄位的值(用的正是 `IConfigurable` 的設定對話方塊那套欄位 schema/繪製邏輯)，按 `Key` 比對從 `initialValues` 預先填入，沒有就用各欄位自己的 `DefaultValue`。回傳按欄位 `Key` 索引的填寫結果，使用者取消則回傳 `null`——這些值不會讀取或寫入外掛真正持久化的設定，所以可以放心重複使用某個設定欄位的 schema 單純做一次性輸入(比如「新增前先給它取個名字」)，不會碰到背後真實的那個設定項目。 |

`LogLevel` 是 `Error` / `Warn` / `Info` / `Debug`，與
[執行狀態記錄檢視器](../../user-guide/settings/service-status)裡的層級篩選器一致。
