# Architecture

SwiftList employs a multi-process separated architecture consisting of **"System Service for Indexing + Hook Process for Input Interception + Application for UI Presentation"**.

## Process Responsibilities

![Process Responsibilities](/architecture-en.svg)

### SwiftList.Service (Indexer Mode - SYSTEM Privilege)
* **Responsibilities**: High-performance background indexer daemon.
* Scans local NTFS drive MFT and monitors USN Journal.
* Keeps the `RuntimeIndex` resident in memory for lightning-fast matching.
* Hosts the Named Pipe server (`"SwiftListPipe"`).
* **System-Wide Shared**: DB and memory structures are system-wide unique.

### SwiftList.Service (Hook Mode - User Privilege)
* **Responsibilities**: System-wide input hook and window focus tracker (runs via `Service.exe --hook`).
* Registers and intercepts Windows global keyboard hotkeys to trigger quick activation.
* Monitors explorer windows and active terminals via Win32 hooks to track physical paths in real time.
* Hosts a session-local Named Pipe server to communicate hook inputs back to the App.
* **Session Bound**: Starts and executes independently inside each logged-in user's desktop session.

### SwiftList.App (User Privilege)
* Provides a lightweight WPF query window, handling user input and search result rendering.
* Sends `SearchRequestMessage` to the Service, and retrieves user-customized search results isolated by `AsyncLocal`.
* Loads user-specific plugins such as custom alias extensions.