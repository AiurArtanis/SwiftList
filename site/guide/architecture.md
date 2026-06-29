# Architecture

SwiftList employs a multi-process separated architecture consisting of **"System Service for Indexing + Hook Process for Input Interception + Application for UI Presentation"**.

## Process Responsibilities

<svg viewBox="0 0 800 300" class="architecture-diagram" style="width: 100%; max-width: 800px; margin: 30px auto; display: block; font-family: system-ui, -apple-system, sans-serif;">
  <defs>
    <!-- Gradients -->
    <linearGradient id="serviceGrad" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#3b82f6" />
      <stop offset="100%" stop-color="#06b6d4" />
    </linearGradient>
    <linearGradient id="appGrad" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#8b5cf6" />
      <stop offset="100%" stop-color="#ec4899" />
    </linearGradient>
    <linearGradient id="hookGrad" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#f59e0b" />
      <stop offset="100%" stop-color="#ef4444" />
    </linearGradient>
    <linearGradient id="dbGrad" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#10b981" />
      <stop offset="100%" stop-color="#059669" />
    </linearGradient>
    
    <!-- Shadow -->
    <filter id="premium-shadow" x="-10%" y="-10%" width="130%" height="130%">
      <feDropShadow dx="0" dy="8" stdDeviation="12" flood-color="#0f172a" flood-opacity="0.08" />
      <feDropShadow dx="0" dy="2" stdDeviation="4" flood-color="#0f172a" flood-opacity="0.04" />
    </filter>
  </defs>

  <!-- Column 1: Service Indexer -->
  <g filter="url(#premium-shadow)">
    <rect x="30" y="30" width="200" height="90" rx="16" fill="var(--vp-c-bg-soft, #f6f6f7)" stroke="url(#serviceGrad)" stroke-width="2.5" />
    <rect x="45" y="45" width="8" height="8" rx="4" fill="#3b82f6" />
    <text x="130" y="62" font-weight="800" font-size="15" fill="var(--vp-c-text-1, #1e293b)" text-anchor="middle">Service (Indexer)</text>
    <text x="130" y="85" font-size="11" fill="var(--vp-c-text-2, #64748b)" text-anchor="middle" font-weight="500">bin: Service.exe --service</text>
    <text x="130" y="102" font-size="9" fill="#3b82f6" text-anchor="middle" font-weight="700" letter-spacing="1">SYSTEM DAEMON</text>
  </g>

  <!-- Column 2: App UI -->
  <g filter="url(#premium-shadow)">
    <rect x="300" y="30" width="200" height="90" rx="16" fill="var(--vp-c-bg-soft, #f6f6f7)" stroke="url(#appGrad)" stroke-width="2.5" />
    <rect x="315" y="45" width="8" height="8" rx="4" fill="#8b5cf6" />
    <text x="400" y="62" font-weight="800" font-size="15" fill="var(--vp-c-text-1, #1e293b)" text-anchor="middle">SwiftList.App</text>
    <text x="400" y="85" font-size="11" fill="var(--vp-c-text-2, #64748b)" text-anchor="middle" font-weight="500">Main Search UI (WPF)</text>
    <text x="400" y="102" font-size="9" fill="#8b5cf6" text-anchor="middle" font-weight="700" letter-spacing="1">USER DESKTOP LEVEL</text>
  </g>

  <!-- Column 3: Service Hook -->
  <g filter="url(#premium-shadow)">
    <rect x="570" y="30" width="200" height="90" rx="16" fill="var(--vp-c-bg-soft, #f6f6f7)" stroke="url(#hookGrad)" stroke-width="2.5" />
    <rect x="585" y="45" width="8" height="8" rx="4" fill="#f59e0b" />
    <text x="670" y="62" font-weight="800" font-size="15" fill="var(--vp-c-text-1, #1e293b)" text-anchor="middle">Service (Hook)</text>
    <text x="670" y="85" font-size="11" fill="var(--vp-c-text-2, #64748b)" text-anchor="middle" font-weight="500">bin: Service.exe --hook</text>
    <text x="670" y="102" font-size="9" fill="#f59e0b" text-anchor="middle" font-weight="700" letter-spacing="1">USER INPUT HOOK</text>
  </g>

  <!-- Column 1 Downward: NTFS -->
  <g filter="url(#premium-shadow)">
    <rect x="30" y="185" width="200" height="80" rx="14" fill="var(--vp-c-bg-soft, #f6f6f7)" stroke="url(#dbGrad)" stroke-width="2" />
    <text x="130" y="220" font-weight="700" font-size="13" fill="var(--vp-c-text-1, #1e293b)" text-anchor="middle">NTFS MFT &amp; USN</text>
    <text x="130" y="243" font-size="11" fill="var(--vp-c-text-2, #64748b)" text-anchor="middle">Low-Level Scan &amp; Monitor</text>
  </g>

  <!-- Column 2 Downward: Plugins -->
  <g filter="url(#premium-shadow)">
    <rect x="300" y="185" width="200" height="80" rx="14" fill="var(--vp-c-bg-soft, #f6f6f7)" stroke="url(#appGrad)" stroke-width="2" />
    <text x="400" y="220" font-weight="700" font-size="13" fill="var(--vp-c-text-1, #1e293b)" text-anchor="middle">User Plugins</text>
    <text x="400" y="243" font-size="11" fill="var(--vp-c-text-2, #64748b)" text-anchor="middle">PinyinAlias / Settings</text>
  </g>

  <!-- Column 3 Downward: System Hooks -->
  <g filter="url(#premium-shadow)">
    <rect x="570" y="185" width="200" height="80" rx="14" fill="var(--vp-c-bg-soft, #f6f6f7)" stroke="var(--vp-c-border, #e2e8f0)" stroke-width="2" />
    <text x="670" y="220" font-weight="700" font-size="13" fill="var(--vp-c-text-1, #1e293b)" text-anchor="middle">Win32 Hooks &amp; Trackers</text>
    <text x="670" y="243" font-size="11" fill="var(--vp-c-text-2, #64748b)" text-anchor="middle">Keyboard Hooks &amp; Tracking</text>
  </g>

  <!-- Search Pipe Connection (Indexer <-> App) -->
  <g>
    <path d="M 230 75 L 300 75" stroke="#3b82f6" stroke-dasharray="4,3" stroke-width="1.5" />
    <polygon points="230,75 240,71 240,79" fill="#3b82f6" />
    <polygon points="300,75 290,71 290,79" fill="#3b82f6" />
    
    <rect x="237" y="66" width="56" height="18" rx="5" fill="var(--vp-c-bg, #ffffff)" stroke="#3b82f6" stroke-width="1" />
    <text x="265" y="79" font-size="9" fill="#3b82f6" text-anchor="middle" font-weight="700">Search Pipe</text>
  </g>

  <!-- Hook IPC Connection (Hook <-> App) -->
  <g>
    <path d="M 500 75 L 570 75" stroke="#f59e0b" stroke-dasharray="4,3" stroke-width="1.5" />
    <polygon points="500,75 510,71 510,79" fill="#f59e0b" />
    <polygon points="570,75 560,71 560,79" fill="#f59e0b" />
    
    <rect x="515" y="66" width="40" height="18" rx="5" fill="var(--vp-c-bg, #ffffff)" stroke="#f59e0b" stroke-width="1" />
    <text x="535" y="79" font-size="9" fill="#f59e0b" text-anchor="middle" font-weight="700">IPC</text>
  </g>

  <!-- Column 1 Downward Line -->
  <g>
    <path d="M 130 120 L 130 185" stroke="#10b981" stroke-width="1.5" stroke-dasharray="3,3" />
    <polygon points="130,185 125,175 135,175" fill="#10b981" />
  </g>

  <!-- Column 2 Downward Line -->
  <g>
    <path d="M 400 120 L 400 185" stroke="#8b5cf6" stroke-width="1.5" stroke-dasharray="3,3" />
    <polygon points="400,185 395,175 405,175" fill="#8b5cf6" />
  </g>

  <!-- Column 3 Downward Line -->
  <g>
    <path d="M 670 120 L 670 185" stroke="#f59e0b" stroke-width="1.5" stroke-dasharray="3,3" />
    <polygon points="670,185 665,175 675,175" fill="#f59e0b" />
  </g>
</svg>

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
* Loads user-specific plugins such as Pinyin alias extensions.