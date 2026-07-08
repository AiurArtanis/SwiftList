# Service Status

Manages the background indexing service, and gives you a live view of its logs.

## Service control

A status indicator shows **Running**, **Stopped**, or **Not Installed**, with a matching action
button:

- **Install & Start Service** (not installed)
- **Start Service** / **Stop Service** (installed)
- **Uninstall Service**

## Logs

Three tabs — **App**, **Hook**, **Service** — corresponding to the three processes SwiftList runs
(the elevated background indexer, the per-user App you interact with, and the keyboard-hook
process). Each tab shows that process's log lines, color-coded by level.

- **Level filter** dropdown — All / Error / Warn / Info / Debug.
- **Search box** — filters the visible lines by keyword, combined with the level filter.
- **Clear** button — empties the log for the currently selected tab. Clearing the Service tab's
  log is routed through the service itself (the App process doesn't have permission to write to
  it directly); the App and Hook logs are cleared directly since they're per-user files.

This is the first place to look when troubleshooting — see
[Troubleshooting](../troubleshooting#still-stuck).
