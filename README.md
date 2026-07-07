# ⚡ SwiftList

[简体中文](README_ZH.md) | English

SwiftList is an ultra-lightweight, high-performance, extensible global search and productivity
launcher for Windows, built on **.NET 10 (WPF)**. It's a modern, open-source alternative to
**Everything** and **Listary** — indexing local drives via the NTFS **USN Journal** and MFT for
near-instant, low-resource search.

📖 **[Full documentation, User Manual & Developer Manual](https://swiftlist.github.io/)** ([中文](https://swiftlist.github.io/zh-CN/))

## Highlights

- **Millisecond indexing** — reads the NTFS USN Journal/MFT directly instead of walking
  directories; a low-footprint background service keeps the index in sync in real time.
- **FZF-style fuzzy search** — multi-keyword fuzzy matching with prefix/suffix/exact/exclude
  operators, plus pinyin aliasing for Chinese filenames.
- **Three ways to search** — a quick popup window, a full main window, and an inline bar that
  docks directly into File Explorer or native file dialogs.
- **QuickLook preview**, a right-click-style actions menu, and hotkeys that are all rebindable.
- **Open plugin SDK** — extend search providers, aliases, context-menu actions, result columns,
  previews, and themes.
- **Process isolation** — a SYSTEM-level indexing service kept separate from the per-user app UI.

See the **[User Manual](https://swiftlist.github.io/user-guide/)** for search syntax, every
hotkey, and every settings option; the **[Developer Manual](https://swiftlist.github.io/dev-guide/)**
for architecture and the plugin SDK reference.

## Download

Grab the latest release from the [homepage](https://swiftlist.github.io/) or directly:

- [Installer (SwiftList-Setup.exe)](https://github.com/SwiftList/SwiftList/releases/latest/download/SwiftList-Setup.exe) — recommended, supports the background service.
- [Portable (SwiftList-Portable.zip)](https://github.com/SwiftList/SwiftList/releases/latest/download/SwiftList-Portable.zip) — no install, unzip and run.

## Building from Source

Requirements: Windows 10/11, .NET 10 SDK, Visual Studio 2022 or JetBrains Rider, and
[Inno Setup](https://jrsoftware.org/isinfo.php) if you want to build the installer.

- `build_and_run.bat` — rebuilds App/Core/Service/plugins and relaunches everything locally.
- `make.bat` — produces a Release build plus `dist/SwiftList-Setup.exe` and
  `dist/SwiftList-Portable.zip`.

See the **[Developer Manual](https://swiftlist.github.io/dev-guide/)** for the full architecture
and plugin SDK.

## 🎁 Support & Donation

If SwiftList has been useful to you, thank you for considering a donation!

- **USDT (TRC20)**: `TNDh3husX1trDW2ZPm4ZZYdoCoCRCZQXn5`

## License

MIT License.
