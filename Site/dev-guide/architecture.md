# Architecture

![SwiftList architecture](/architecture.svg)

## Process split

SwiftList runs as three separate processes, deliberately isolated by privilege level and lifetime:

- **`SwiftList.Service`** — a Windows service running as `LocalSystem`. It owns all file indexing:
  reading the NTFS USN Journal and MFT for local drives, scanning and caching network shares, and
  answering search queries over a named pipe. Running this at SYSTEM level means it can read the
  raw volume metadata every user account is allowed to see, without granting the interactive App
  process elevated privileges it doesn't need.
- **`SwiftList.App`** — the per-user, session-level WPF application: the search windows, the
  Settings window, hotkey handling, and the Actions/QuickLook UI. It talks to the Service over a
  named pipe (`SearchService`/`UsnServicePipeServer` in `Core.Services`) and never touches the disk
  index directly.
- **`SwiftList.Service --hook`** — a small separate process hosting the low-level global keyboard
  hook, so a hook crash or a misbehaving foreground app can't take the main App process down with
  it.

## Shared Core

`Core` is a class library referenced by both the Service and the App. It holds:

- The search engine (`Core/SearchIndex/Fzf/*`) — a fuzzy-matching implementation modeled on the
  `fzf` command-line tool's algorithm, plus a query parser (`SearchQueryParser`) for drive-letter
  targeting and path-mode search.
- The runtime index (`Core/IndexV2/*`) — a memory-mapped, columnar snapshot format built from
  USN/MFT reads, with an in-memory delta overlay for changes since the last snapshot.
- IPC contracts (`SearchRequestMessage`, `SearchResponseBinarySerializer`, ...) shared verbatim by
  both processes, so the App and Service always agree on the wire format.
- `Logger` — writes to per-process log files (`service.log`, `app.log`, `hook.log`), all readable
  (but not all writable) from the App's Settings → Service Status log viewer.

## Where plugins fit

Plugins are `.dll` assemblies referencing `PluginSdk` and are loaded by the App process (see
[Getting Started](./getting-started) and [Packaging & Deployment](./packaging)). Two plugins ship
with SwiftList itself as first-class examples — `SwiftList.Plugins.CoreExtensions` (built-in file
actions and the shell context-menu integration) and `SwiftList.Plugins.PinyinAlias` (pinyin
aliasing for Chinese filenames) — see [Example Plugins](./examples) for a walkthrough of both.

Plugins never talk to the Service directly; they interact with the App through the interfaces
documented in the Plugin SDK Reference, and with the disk index (when they need custom indexed
directories) through `DirectoryIndexerService`, which proxies to the Service on their behalf.
