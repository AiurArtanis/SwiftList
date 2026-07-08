# Startup Panel

A tab strip shown above the result list in the quick window whenever the search box is empty,
giving quick access to recent files, favorites, and history without typing a query.

- **Enable the startup panel** — master switch; off means the panel never activates at all,
  regardless of the per-tab settings below.

Two sub-tabs: **Recent Files** and **Plugin Tabs**.

## Recent Files

- **Enable panel** — checkbox; shows the tab when the search box is empty.
- **Directories** — folders to watch, one per line (same add/edit/remove-row and bulk-text editing
  as [Exclusion Rules](./index-drives#exclusion-rules)). Can include local drives, mapped network drives,
  and WSL paths (`\\wsl$\...` or `\\wsl.localhost\...`) — matched against whichever of those you've
  already configured and indexed under [Index](./index-drives).
- **Maximum number of files to show** — range 1–100, default 10.
- **Time range (minutes)** — only files modified within this many minutes of now are eligible, on
  top of the count cap above. Range 1–43200 (30 days), default 60.

## Plugin Tabs

Plugin-provided tabs (e.g. History, Favorites) each show a **×** button in the live panel to hide
them for now. This is a panel-local "hide it" choice — separate from disabling the plugin component
itself in [Plugins](./plugins), which stops it from being used at all. A tab closed this way is
listed here, grouped by the plugin that provides it, unchecked; check it to bring it back.

Only tabs whose plugin component is currently enabled show up in this list — one disabled entirely
under [Plugins](./plugins) never becomes a tab candidate in the first place.
