# 호스트 서비스

호스트 앱의 기능을 다시 플러그인에 노출하는, `PluginSdk.Services`에 있는 정적 서비스들입니다. 각각은
호스트가 시작 시 연결해 두는 델리게이트를 감싸는 얇은 정적 클래스이므로, 플러그인은 그 아래에서 실제로
무엇이 실행되든 항상 동일한 방식으로 호출합니다.

| 서비스 | 목적 |
|---|---|
| `FuzzyMatchService` | `IsMatch(pattern, text)` — `text`(또는 그 별칭 중 하나)가 fzf 문법의 `pattern`과 일치하는지, 호스트 자체 검색이 사용하는 것과 정확히 동일한 매칭 로직으로 판단합니다. `GetHighlightMask(text, query)` — 그 쌍에 대한 문자 단위 하이라이트 마스크로, 호스트 자체 결과가 하이라이트할 때 쓰는 것과 동일한 리터럴/퍼지/별칭 폴백 단계(CJK 병음 포함)를 사용합니다. 이 덕분에 플러그인의 결과도 단순 리터럴 부분 문자열 매칭만 처리하는 대신 일관되게 하이라이트됩니다. |
| `TranslationService` | 현재 활성화된 언어에 대해 런타임에 조회하는 `Get(key)` / `Format(key, args)`. 플러그인 자체의 내장 JSON 번역을 로드하는 `LoadEmbeddedTranslations(assembly, cultureKey, typeName)`. `GetSupportedCultures(assembly)`. `GetCurrentCulture()` — 앱에서 현재 선택된 UI 언어(예: `"zh-CN"`)로, OS 시스템 로캘과는 독립적인 사용자 설정입니다. 원시 문화권 코드 자체가 필요할 때(예: HTTP `Accept-Language` 헤더에 넣거나 번역 API의 대상 언어를 고를 때)만 이를 사용하세요 — `CultureInfo.CurrentUICulture`는 이 설정이 아니라 OS 로캘을 반영하며, 사용자의 Windows 언어와 앱 내 언어가 다를 때는 조용히 이 값과 어긋나게 됩니다. |
| `IconService` | `GetIcon(path, isDir)`와 `GetThumbnail(path, size)` — 캐시된 셸 아이콘/썸네일 추출로, 플러그인이 직접 Windows 아이콘 API를 호출할 필요가 없습니다. |
| `FavoritesService` | `GetFavorites()` — 사용자의 [즐겨찾기](../../user-guide/settings/favorites) 목록(`FavoriteItem`: Name, Path)에 대한 읽기 전용 접근입니다. |
| `HistoryService` | `GetHistoryEntries()` — 기록된 모든 [기록](../../user-guide/settings/history) 항목을 최근에 연 순서로, `HistoryEntry { Keyword, Path, Kind, Time }` 형태로 반환합니다(`Kind`는 `HistoryEntryKind`: `File` / `Folder` / `Application`. `Keyword`는 그 항목으로 이어진 검색 텍스트이며, 검색어 없이 시작 패널 탭에서 바로 연 경우에는 빈 문자열입니다. `Time`은 유닉스 초 단위입니다). 각 경로는 가장 최근에 그 경로로 이어진 키워드 아래로 한 번만 나타납니다. |
| `FileMetadataService` | `GetMetadataAsync(paths)` — 현재 결과 목록에 **이미 포함되어 있지 않은** 경로에 대한 일괄 Size/Created/Modified/Accessed 조회([`FileMetadata`](./abstractions#filemetadata)). 모든 `ISearchResult`는 이미 자체 `Metadata` 속성을 통해 이를 무료로 제공하므로([공유 추상화](./abstractions#isearchresult) 참고), 이 서비스는 다른 방식으로 얻은 경로(예: 자체 설정에서 가져온 경로)에 대해서만 사용하세요. |
| `DirectoryIndexerService` | `RegisterDirectory(pluginId, path, recursive, filterPattern)` / `UnregisterDirectories(pluginId)` / `SearchDirectoriesAsync(pluginId, query, token)` / `NotifyDirectoryChanged(pluginId)` — 플러그인이 그 메커니즘을 직접 재구현하지 않고도 자체 디렉터리를 백그라운드 인덱싱과 USN 모니터링에 등록할 수 있게 해줍니다. |
| `PluginSettingsService` | `GetSetting<T>(pluginId, key, defaultValue)` — 호스트의 설정 저장소에서 플러그인 자체의 영속화된 설정에 대한 읽기 전용 접근입니다. 세 단계를 순서대로 거칩니다. 사용자가 저장한 적이 있다면 그 영속화된 값, 아무것도 저장된 적이 없다면 해당 필드에 대한 `IConfigurable` 스키마 자체의 `DefaultValue`, 그마저도 없다면 마지막 수단으로 여러분이 전달한 `defaultValue` 인수입니다 — 이렇게 하면 스키마에 선언된 기본값이 단일한 정보원이 되어, 호출부에 하드코딩된 사본을 또 둘 필요가 없습니다. 값을 매번 다시 읽는 대신 캐시한다면, `SettingChanged(pluginId, key)` 이벤트를 구독하여 여러분의 플러그인에 대해 이 이벤트가 발생할 때 캐시를 폐기하세요 — 호스트는 설정 페이지에서 저장 직후에 이 이벤트를 발생시키며, 이는 무효화하기에 신뢰할 수 있는 유일한 시점입니다(매 키 입력마다, 또는 폴링 방식으로 확인하면 우연히 다음에 무언가가 트리거될 때까지, 혹은 영영 변경을 감지하지 못할 수 있습니다). |
| `SearchRefreshService` | `RefreshIfMatches(queryMatches)` — 데이터가 비동기로 도착하는 `IInstantResultProvider`용입니다([`IInstantResultProvider`](./core-search-actions#iinstantresultprovider) 참고). 백그라운드 조회가 끝나고 결과를 캐시한 뒤, 검색의 현재 쿼리 텍스트에 대한 서술자와 함께 이를 호출하면, 호스트가 그 서술자에 일치하는 모든 활성 검색을 다시 실행하여 사용자가 다시 입력할 필요 없이 이제 캐시된 결과가 실제로 나타나게 합니다. |
| `Logger` | `Log(message, level = LogLevel.Info)` — App의 로그 파일에 기록하며, **설정 → 서비스 상태 → App**에서 호스트 자체 로그 라인과 완전히 동일하게 보입니다. |
| `PluginPromptService` | `Prompt(title, fields, initialValues?)` — 주어진 [`PluginConfigField`](./abstractions#iconfigurable) 값을 묻는 작은 모달을 표시합니다(`IConfigurable`의 구성 대화상자가 사용하는 것과 동일한 필드 스키마/렌더링). `initialValues`(`Key`로 매칭)나 각 필드 자체의 `DefaultValue`로 미리 채워집니다. 입력된 값을 필드 `Key`로 키가 매겨진 형태로 반환하며, 사용자가 취소했다면 `null`을 반환합니다 — 이 값들은 플러그인의 실제 영속화된 설정에서 읽거나 쓰이는 일이 전혀 없으므로, 실제 설정을 건드리지 않으면서 설정 필드의 스키마를 순전히 일회성 입력(예: "추가하기 전에 이름을 지어주세요")에 재사용해도 안전합니다. |

`LogLevel`은 `Error` / `Warn` / `Info` / `Debug`로,
[서비스 상태 로그 뷰어](../../user-guide/settings/service-status)의 레벨 필터와 일치합니다.
