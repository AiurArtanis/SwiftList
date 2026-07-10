# Settings Reference

A search box sits in the Settings window's title bar. It matches fuzzily (the same fzf-style
matching the main search window uses, with pinyin alias support), not just plain substrings, across
every section — including the per-plugin entries under Plugins, Hotkeys' Plugin Actions tab, and
Startup Panel's Plugin Tabs sub-tab. Each result shows a breadcrumb (e.g. "Index > Network Drives")
so same-named settings under different tabs stay distinguishable. Selecting a result (click, or
Up/Down to highlight and Enter) switches to the right section and tab, scrolls the exact control
into view, and briefly flashes a highlight border around it.

The Settings window has nine sections in its left sidebar:

| Section | Covers |
|---|---|
| [Service Status](./service-status) | Background service install, and the App/Hook/Service log viewer. |
| [Index](./index-drives) | Local drives, network drives, WSL distributions (once detected), folder indexes, and exclusion rules. |
| [General](./general) | Startup behavior, updates, theme, language, search bar layout, and preview window size. |
| [Hotkeys](./hotkeys-page) | Global hotkeys, per-plugin action hotkeys, and the process blacklist. |
| [Plugins](./plugins) | Installed plugins and per-component enable/disable toggles. |
| [Favorites](./favorites) | Custom-named shortcuts to folders, files, and URLs. |
| [History](./history) | Search history and quick-window keyword history. |
| [Startup Panel](./startup-panel) | The empty-search-box tab strip: Recent Files, Last Directory, and reopening closed plugin tabs. |
| [About](./about) | Version info and update checking. |

Each page below documents every option on that section, in order, with its default value and any
valid range.
