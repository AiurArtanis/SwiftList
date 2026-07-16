# Hotkeys

All global and in-app hotkeys below can be rebound from **Settings → Hotkeys**; defaults are shown
here. See [Settings → Hotkeys page](./settings/hotkeys-page) for the settings UI itself.

## Global hotkeys

| Action | Default | Notes |
|---|---|---|
| Toggle quick window | Double-tap `Ctrl` | Can also be set to a full combo (e.g. `Alt+Space`) instead of a double-tap. |
| Quick switch | `Ctrl+G` | Switches between the inline (embedded-in-Explorer) search bar and the main window. |
| Select next item | `Ctrl+N` | Also works as the literal Down arrow. |
| Select previous item | `Ctrl+P` | Also works as the literal Up arrow. |
| Jump to result 1–9 | `Ctrl` + digit | The modifier is configurable; the digit is always 1–9. |
| Open actions menu | `Ctrl+O` | Also works as the literal Right arrow on a selected result. |
| Complete from selection | `Ctrl+Tab` | In the quick window, fills the search box with the selected result's name/path. |
| QuickLook preview | `Alt+P` | Toggles the preview pane for the selected result. |
| Previous keyword history | `Alt+Up` | Cycles backward through your recently typed queries. |
| Next keyword history | `Alt+Down` | Cycles forward through your recently typed queries. |
| Delete keyword history entry | `Shift+Delete` | |
| Open full window | *(none)* | Opens the full window from the Quick Window, carrying over the current query — the same action as the Quick Window's own expand button. Not bound by default; set one from **Settings → Hotkeys**. |
| Next Startup Panel tab | `Ctrl+Right` | Wraps from the last tab back to the first. Only active while the [Startup Panel](./settings/startup-panel) is showing — otherwise the key does its normal job (e.g. moving the caret while typing a query). |
| Previous Startup Panel tab | `Ctrl+Left` | Wraps from the first tab back to the last. Same active-only-while-showing rule as above. |

## Quick navigation (mouse)

Enabled by default, toggled per-trigger in settings:

- **Double-click** empty space on the desktop or inside an Explorer window to trigger quick navigation.
- **Middle-click** empty space on the desktop or inside an Explorer window — or the file list of a
  supported third-party file manager (Directory Opus, Total Commander, ...), or a native Open/Save/
  Browse-for-folder dialog — to trigger quick navigation. Those other windows only respond to
  middle-click: double-clicking there already means "open this," so double-click isn't repurposed.

Either trigger pops a cascading menu of your Favorites, History, and configured quick-access folders
(see [Settings → Favorites](./settings/favorites) and [Settings → History](./settings/history)) —
plugins can contribute their own entries too, such as Total Commander's own Directory Hotlist if
you've set one up in `wincmd.ini`. Clicking a folder navigates the target window there; clicking a
file opens it there. Inside a file dialog specifically, clicking a file instead jumps the dialog to
that file's folder — it deliberately never auto-confirms Open/Save on your behalf.

## Hardcoded keys (not configurable)

These always behave the same way regardless of your hotkey settings:

| Key | Context | Behavior |
|---|---|---|
| `Escape` | Anywhere | Clears the search box if it has text; otherwise closes the window (or exits the actions menu). |
| `Enter` | Result list | Opens the selected result. |
| `Ctrl+Enter` | Result list | Locates the result in Explorer instead of opening it. |
| `Ctrl+Shift+Enter` | Result list | Opens the result elevated (Run as administrator). |
| `Left` / `Right` arrow | Actions menu | Go back a menu level / enter a submenu. |
| `Backspace` | Actions menu | Exits the actions menu when the search box is already empty. |

## Plugin action hotkeys

Plugins can register their own actions with a default hotkey (e.g. copy path, run as admin). These
show up under **Settings → Hotkeys → Plugin Actions**, grouped by the plugin that registered them,
and can be rebound the same way as built-in hotkeys.

## Process blacklist

If SwiftList's global hotkeys interfere with another application (a game capturing raw keyboard
input, for example), add that process to the **Process Blacklist** — see
[Settings → Hotkeys page](./settings/hotkeys-page#process-blacklist). While a blacklisted process is
in the foreground, SwiftList's global hotkeys, keystroke interception, and the quick navigation
mouse triggers above are all let through untouched.

Any foreground app that's genuinely full-screen gets this same treatment automatically — no
blacklist entry needed. Either way, an active file dialog is always exempt, so quick navigation
still works there.
