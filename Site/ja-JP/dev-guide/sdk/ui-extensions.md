# UI とプレビューの拡張

## 結果の表示

### `ISidebarFilterProvider`

結果サイドバーに分類用のフィルターグループを追加します(例:日付範囲やサイズの区分)。

```csharp
interface ISidebarFilterProvider
{
    int SortOrder { get; } // default 100; lower renders first
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}
```

`SidebarFilterGroup` は `Header`、`AllowMultiSelect` フラグ(デフォルト `false`。有効にすると、
そのグループで複数項目を同時に選択でき、OR で組み合わされます——重なり合う/累積する日付範囲のよう
に、一度に1つだけ選ぶ意味しか持たない項目については無効のままにしてください)、そして
`SidebarFilterItem` のリスト(Id、DisplayName、任意のアイコン、現在の結果リストに対する任意の非同
期 `FilterPredicate`)を持ちます。ホストはグループに選択がある時点でクリアボタンを表示するため、
プロバイダー側で独自の「すべて」/「いずれか」の疑似項目を用意する必要はありません。

### `IResultColumnProvider`

結果のグリッドビューに追加の列を挿入します(ファイルサイズ、更新日、カスタムメタデータなど)。

```csharp
interface IResultColumnProvider
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

`ResultColumnDefinition` は列 ID、ヘッダーテキスト、幅、そして任意の
`VisibilityPredicate`/`SortComparer` デリゲートを持ちます。

## スタートアップパネル

### `IStartupPanelTabProvider`

クイックウィンドウのスタートアップパネル——検索ボックスが空のときに結果リストの上に表示されるタ
ブストリップ([スタートアップパネル](../../user-guide/settings/startup-panel)を参照)——にタブを
提供します。CoreExtensions の履歴タブとお気に入りタブはどちらもこれを基盤にしています。詳しくは
[サンプルプラグイン](../examples#coreextensions-—-アクションとシェルのコンテキストメニュー)を参照してくだ
さい。

```csharp
interface IStartupPanelTabProvider : IPluginComponent
{
    IEnumerable<ISearchResult> GetItems();
}
```

`GetItems()` はパネルがアクティブになるたびに同期的に実行され、I/O なしで高速であることが期待され
ます——検索ボックスがクリアされるたびに呼び出され、キャッシュはされません。何も項目を返さないタブ
は、空の状態で表示されるのではなく、タブストリップから完全に除外されます。ユーザーは、設定 → プラ
グインでそのコンポーネントを丸ごと無効化するのとは別に、**×** ボタンでライブパネルからタブを個別
に非表示にできます——この2つは意図的に分けられています。ホストは、非表示状態を永続化するための安
定したキーとして、コンポーネントの具象クラスの型名(`GetType().Name`)を使用します。

## プレビューとサムネイル

### `IFilePreviewProvider`

特別に扱いたいファイルタイプについて、QuickLook プレビューペイン([アクションメニューとプレビュー
→ QuickLook プレビュー](../../user-guide/actions-and-preview#quicklook-プレビュー)を参照)にカスタム
の WPF `UIElement` を描画します。

```csharp
interface IFilePreviewProvider
{
    string Name { get; }
    int Priority { get; } // default 0; higher runs first
    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);
    bool RendersExternally { get; } // default false
}
```

`Priority` はあくまで*デフォルトの*順序です——ユーザーは 設定 → 一般 →
[プレビューとサムネイル](../../user-guide/settings/general#プレビューとサムネイル)から、あなたのプロ
バイダーとの相対順を含めて自由に並べ替えることができ、その設定が `Priority` の返す値に優先します。
自分のプロバイダーが宣言した優先度が、実際に実行される順序だと思い込まないでください。

プレビューの挙動を細かく調整する、任意の2つの補助インターフェースがあります。

- **`IPreviewSessionAware`** — プレビュープロバイダー自身がコストの高いプロセス外リソース(ホスト
  されたネイティブハンドラー、ファイルロックなど)を保持している場合、プレビュープロバイダー自体
  にこれを実装してください。`EndPreviewSession()` はプレビューセッション全体が終了したときに一度
  だけ呼び出され、個々のプレビュー切り替えのたびには呼び出されません。ただし1つ例外があります:
  `RendersExternally` が true のプロバイダーについては、ホストはそのプロバイダーから切り替わるた
  びに、セッション終了時だけでなく毎回これを呼び出します——詳しくは下記を参照してください。
- **`IReusablePreview`** — `CreatePreview` が返す `UIElement` が、ゼロから再構築するのではなく新
  しいファイルを指し直せる場合、その `UIElement` 側にこれを実装してください。`TrySetTarget(path,
  isDir)` は、変更をその場で処理できた場合は `true` を、代わりに新しいプレビューを構築するようホ
  ストに指示する場合は `false` を返します。

`RendersExternally` は、実際のプレビュー表示面が `CreatePreview` が返す `UIElement` ではなく、別
の外部管理されたウィンドウであるプロバイダー向けです——例えばファイルを丸ごと別のアプリケーション
に引き渡すような場合です。勝ち残ったプロバイダーがこれを設定している場合、ホストは `CreatePreview`
の内容を表示する代わりに自身のプレビューパネルを非表示にします(その内容は結局表示されないため、
単なるプレースホルダーで構いません)。**`IReceivesPreviewPanelBounds`** と組み合わせることで、ホス
ト自身のパネルが本来占めるはずだった正確な画面上の矩形(物理ピクセル)を取得でき、外部ウィンドウ
をそれ以外の場所ではなくその位置に配置できます。

```csharp
interface IReceivesPreviewPanelBounds
{
    void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);
}
```

同梱されている(実験的な)QuickLook Bridge プラグインが実際の例です。これは自身の名前付きパイプ
経由で外部の [QuickLook](https://github.com/QL-Win/QuickLook) アプリを検出し、接続できればそのウ
ィンドウをすべてのファイル/フォルダーに対してホストパネルの位置にドッキングします——ユーザー向け
の挙動については[アクションメニューとプレビュー → QuickLook 経由の外部プレビュー](../../user-guide/actions-and-preview#quicklook-による外部プレビュー-任意)
を参照してください。なお、これは SwiftList 自身に内蔵されているプレビューパネル(このコードベース
やドキュメントの中で非公式に「QuickLook」とも呼ばれています)とは別物である点に注意してください。

### `IThumbnailProvider`

マッチした結果に表示されるアイコン/サムネイルを上書きします。

```csharp
interface IThumbnailProvider : IPluginComponent
{
    int Priority { get; } // default 0; higher runs first
    bool CanProvideThumbnail(string path, bool isDir);
    ImageSource? GetThumbnail(string path, int size);
}
```

上記の `IFilePreviewProvider.Priority` と同じ注意点です:これもデフォルトの順序にすぎず、ユーザー
は 設定 → 一般 →
[プレビューとサムネイル](../../user-guide/settings/general#プレビューとサムネイル)(両方のプロバイダー
の並び順リストが同じタブにあります)から上書きできます。

## テーマとローカライズ

### `IThemeProvider` / `ITheme`

1つ以上のカスタム WPF リソースディクショナリを、選択可能なテーマとして登録します(**設定 → 一般
→ インターフェイスのテーマ**に表示されます)。

```csharp
interface IThemeProvider
{
    string Name { get; }
    IEnumerable<ITheme> GetThemes();
}

interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    double WindowOpacity { get; } // default 1.0
    ResourceDictionary GetResources();
}
```

### `ITranslationProvider`

指定されたカルチャに対する UI 文字列を提供します——プラグイン自身の UI のためであったり、
`PinyinAlias` のように単に自分の表示名だけのためであったりします。このインターフェースを、関連の
ない別のインターフェースと同じクラスに実装しているプラグインについては、[サンプルプラグイン](../examples)
を参照してください。

```csharp
interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedCultures { get; } // e.g. "zh-CN", "en-US"
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```

`TranslationService.LoadEmbeddedTranslations`([ホストサービス](./services)を参照)が、プラグイン
の DLL に埋め込まれた JSON ファイルでこれを支える標準的な方法です。
