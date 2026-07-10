# Index

Four tabs: **Local Drives**, **Network Drives**, **Folders**, and **Exclusion Rules**.

## Local Drives

- Status card summarizing how many drives and items are indexed, plus a **Rebuild Index** button
  for a full re-scan.
- One row per local drive: an **enable/disable checkbox**, drive name, file system (NTFS/ReFS/...),
  current status, indexed item count, and a per-row **Rebuild**/**Remove** action.
- Local drives update continuously from the Windows USN Journal — a manual rebuild is rarely
  needed, but is there if something looks out of sync.

## Network Drives

- Same status card and **Rebuild Index** button as Local Drives.
- One row per mapped network drive: enable checkbox, path/name, status (Indexing / Ready / Cached
  / Failed / Pending / Connected), item count, and a **Refresh Mode** dropdown:
  - **Manual** — only refreshes on demand (via Rebuild Index).
  - **Every 15 minutes**
  - **Every hour**
  - **Daily**
- A **WSL Distributions** sub-section appears automatically if you have Windows Subsystem for
  Linux installed, listing each distribution with the same enable/status/refresh-mode controls as
  a network drive.

Network shares don't expose a change journal the way local NTFS volumes do, which is why they're
refreshed on a schedule instead of in real time.

## Folders

Index arbitrary individual folders instead of a whole drive or share — useful for indexing just
one subtree without pulling in everything else on that volume.

- An **Add Folder** button opens a folder picker; a **Rebuild Index** button re-scans every folder
  in the list.
- One row per added folder: enable checkbox, path, status, item count, and the same **Refresh
  Mode** dropdown (Manual / Every 15 minutes / Every hour / Daily) as network drives — folders are
  scanned on a schedule rather than tracked continuously, the same way network shares are.

## Exclusion Rules

Three sub-tabs, each with the same shape: a single-entry textbox + **Add** button, a list of
existing rules (each editable/deletable), and a bulk multi-line textbox with **Generate from
List** / **Apply to List** buttons for editing everything at once.

- **Path Exclusions** — full paths or environment variables (e.g. `D:\Cache`, `%ProgramData%`).
- **Glob Wildcards** — `*` (any characters in a filename), `?` (single character), `**` (recursive
  directories). Examples: `*.tmp`, `**/node_modules/**`, `bin/**`.
- **Regex Patterns** — arbitrary regular expressions matched against the path/filename (partial
  match). Examples: `^\.` (hidden files), `~$` (Office temp files), `\.git\`.

Exclusions apply to local, network, and folder indexing alike, and network drives/folders re-scan
automatically after you apply exclusion changes.
