# Actions Menu & Preview

## Actions menu

Every result — file, folder, or app — has a set of actions beyond "just open it": locate in
Explorer, copy path, run as administrator, cut/copy/paste the file itself, delete it (to the
Recycle Bin) or permanently delete it, and anything a plugin adds (for example, the full Windows
shell right-click menu, cascading submenus included).

Open it with the **Open actions menu** hotkey (`Ctrl+O` by default) or the literal Right arrow on a
selected result. Inside the menu:

- **Next/Previous item** — the arrow keys, or your own configured
  [next/previous-item hotkey](./hotkeys), move the highlight up and down the actions list. A
  custom binding (even something like a bare `Tab` key) is honored here exactly the same way it is
  in the main result list.
- **Right arrow / Enter** on an item with a submenu (e.g. a shell cascade menu like "Send to")
  drills into it; **Left arrow** or **Backspace** (with an empty search box) backs out one level.
- **Escape** exits the actions menu, or clears the search box first if you'd typed something to
  filter the action list.
- Type to filter the visible actions by name, the same way you'd filter search results.

## QuickLook preview

Press the **QuickLook** hotkey (`Alt+P` by default) on a selected result to open a preview pane
docked next to the search window — images, documents, and other previewable file types render
without leaving SwiftList. Press it again (or move to a result QuickLook can't preview) to close it.

Audio and video files — whatever formats WPF's built-in media playback supports without extra
codecs (MP4, WMV, AVI, MOV, MP3, WAV, WMA, and a few others) — play automatically as soon as the
preview opens, with a small themed transport bar (play/pause, seek, current/total time, mute)
instead of a static thumbnail. Moving to a different result or closing the preview stops playback
immediately. A file whose specific codec can't be decoded falls back to a static thumbnail instead.

The preview window's size is fixed and user-configurable — see
[Settings → General → Preview](./settings/general#preview) — and independent of how many results
are currently showing. Whatever size you set, SwiftList automatically keeps the preview window
fully on-screen: if it doesn't fit beside the search window on your monitor, it docks to whichever
side has room, and if the configured size is larger than your monitor's usable area, the window is
scaled down to fit rather than running off the edge.

If the file being previewed needs its own native handler to show a popup of its own — most
commonly Word or Excel asking for a password on an encrypted document — both the quick window and
the preview pane hide themselves for as long as that popup is open, since it would otherwise sit
unreachable behind them. This isn't SwiftList closing or crashing: resolve the popup (enter the
password, dismiss it, whatever it's asking) and both windows come back exactly as you left them,
search text and selection included.
