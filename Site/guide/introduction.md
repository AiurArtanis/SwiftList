# Introduction to SwiftList

SwiftList is a high-performance, low-overhead, and highly extensible global search utility designed for Windows, aiming to be a beautiful, deeply customizable, open-source alternative to **Listary**. It quickly builds and maintains indexes for local files, while offering a plugin SDK for developers to extend its capabilities.

## Download & Installation

SwiftList provides official installer and portable zip releases (Latest Stable Version):

* 💾 **[Download Installer](https://github.com/SwiftList/SwiftList/releases/latest/download/SwiftList-Setup.exe)** — Full installer package (supports auto-start and background Windows Service indexing).
* 📦 **[Download Portable Version](https://github.com/SwiftList/SwiftList/releases/latest/download/SwiftList-Portable.zip)** — Standalone portable version.
* 🔍 Or visit **[GitHub Releases Page](https://github.com/SwiftList/SwiftList/releases)** to check all historical versions.

## Core Design Principles

1. **YAGNI (You Aren't Gonna Need It)**: Refuse bloated design; keep logic concise and execution fast.
2. **Process Separation & Isolation**:
   * **`SwiftList.Service` (System Service - `--service`)**: Runs in Session 0 with SYSTEM privileges, indexing NTFS MFT and USN logs, shared globally.
   * **`SwiftList.Service` (Hook Process - `--hook`)**: Runs in the user session, capturing global keyboard hotkeys and tracking explorer paths.
   * **`SwiftList.App` (User Application)**: Runs in the user session, hosting UI interactions, rendering search results, and loading user-scoped plugins.
3. **High-Speed FZF Matching**: Optimized search matching supporting fuzzy queries and customized aliases.

