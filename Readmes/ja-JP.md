<p align="center">
  <img src="../App/logo.png" alt="SwiftList logo" width="120">
</p>

# ⚡ SwiftList

[English](../README.md) | [简体中文](zh-CN.md) | [繁體中文（香港）](zh-HK.md) | [繁體中文（台灣）](zh-TW.md) | 日本語 | [한국어](ko-KR.md) | [Español](es-ES.md)

SwiftList は **.NET 10 (WPF)** で構築された、超軽量・高性能・拡張可能な Windows 向けグローバル検
索/効率化ランチャーです。**Everything** や **Listary** に代わるモダンなオープンソースの選択肢で、
NTFS の **USN Journal** と MFT を直接読み取ってローカルドライブをインデックス化し、瞬時かつ低リ
ソースな検索を実現します。

📖 **[ドキュメント全体・ユーザーマニュアル・開発者マニュアル](https://swiftlist.github.io/ja-JP/)**

## 主な特長

- **瞬時のインデックス作成** —— ディレクトリを走査する代わりに NTFS の USN Journal/MFT を直接読
  み取ります。軽量なバックグラウンドサービスがリアルタイムでインデックスを同期し続けます。
- **FZF スタイルのあいまい検索** —— 前方一致/後方一致/完全一致/除外演算子を備えた複数キーワードの
  あいまい検索に加え、中国語ファイル名向けのピンインエイリアスにも対応。
- **3 通りの検索方法** —— クイックポップアップウィンドウ、フルサイズのメインウィンドウ、そしてエ
  クスプローラーやネイティブのファイルダイアログに直接ドッキングするインライン検索バー。
- **QuickLook プレビュー**、右クリックメニュー風のアクションメニュー、すべて再設定可能なホット
  キー。
- **オープンなプラグイン SDK** —— 検索プロバイダー、エイリアス、コンテキストメニューアクション、
  結果列、プレビュー、テーマを拡張できます。
- **プロセスの分離** —— SYSTEM レベルのインデックスサービスは、ユーザー単位のアプリ UI とは別プロ
  セスとして動作します。

検索構文、すべてのホットキー、すべての設定項目については**[ユーザーマニュアル](https://swiftlist.github.io/ja-JP/user-guide/)**
を、アーキテクチャとプラグイン SDK リファレンスについては**[開発者マニュアル](https://swiftlist.github.io/ja-JP/dev-guide/)**
をご覧ください。

## ダウンロード

最新版は[ホームページ](https://swiftlist.github.io/ja-JP/)から、または直接以下から入手できます。

- [インストーラー (SwiftList-Setup.exe)](https://github.com/SwiftList/SwiftList/releases/latest/download/SwiftList-Setup.exe) —— 推奨。バックグラウンドサービスに対応。
- [ポータブル版 (SwiftList-Portable.zip)](https://github.com/SwiftList/SwiftList/releases/latest/download/SwiftList-Portable.zip) —— インストール不要、解凍してすぐ使えます。

## ソースからビルドする

必要環境: Windows 10/11、.NET 10 SDK、Visual Studio 2022 または JetBrains Rider。インストーラーを
ビルドする場合は [Inno Setup](https://jrsoftware.org/isinfo.php) も必要です。

- `build_and_run.bat` —— App/Core/Service/プラグインを再ビルドし、ローカルで再起動します。
- `make.bat` —— Release ビルドを生成し、`dist/SwiftList-Setup.exe` と
  `dist/SwiftList-Portable.zip` を出力します。

アーキテクチャとプラグイン SDK の詳細については**[開発者マニュアル](https://swiftlist.github.io/ja-JP/dev-guide/)**
をご覧ください。

## 🎁 サポート・寄付

SwiftList がお役に立てたなら、ぜひ寄付をご検討ください！

- **USDT (TRC20)**: `TNDh3husX1trDW2ZPm4ZZYdoCoCRCZQXn5`

## ライセンス

MIT License。
