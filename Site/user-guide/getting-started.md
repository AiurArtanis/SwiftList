# Getting Started

## Installing

Grab the latest release from the [download button](../) on the homepage — two flavors are
published for every release:

- **Installer** (`SwiftList-Setup.exe`) — recommended. It registers the background indexing
  service and can start SwiftList with Windows.
- **Portable** (`SwiftList-Portable.zip`) — unzip and run, no installation. You can still install
  the background service later from **Settings → Service Status**.

On first run, SwiftList installs and starts a Windows service (`SwiftList.Service`) that owns file
indexing. This split exists on purpose — see [Architecture](../dev-guide/architecture) if you're
curious why — but as a user, the only thing you need to know is: **Settings → Service Status**
tells you whether the service is running, and lets you install/start/stop/uninstall it if needed.

## The three windows

SwiftList doesn't have just one search window — it adapts to how you're using it:

- **Main window** — the full window you get from the taskbar/Start Menu shortcut, with the largest
  result list and an in-window Actions panel.
- **Quick window** — the compact, always-on-top popup you summon with the global toggle hotkey
  (double-tap `Ctrl` by default). Built for "hit hotkey → type → Enter" muscle memory.
- **Inline window** — embeds a SwiftList search bar directly into a supported native file dialog or
  File Explorer window, so you can search without leaving the dialog you're already in.

All three share the same search engine, hotkey system, and Actions menu — the difference is purely
where and how they appear.

## Basic search

Clearing the search box (or opening the quick window fresh) shows the
[Startup Panel](./settings/startup-panel) instead of an empty result list — a tab strip with quick
access to recent files, favorites, and history, no query needed.

Just start typing. Results update as you type, ranked by relevance (see
[Search Syntax](./search-syntax) for how matching and ranking work). Use the
[configurable next/previous-item hotkeys](./hotkeys) (arrow keys by default) to move the
selection, and Enter to open the highlighted result.

Next up: [Search Syntax](./search-syntax) to get the most out of the query box.
