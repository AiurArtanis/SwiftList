# 架构设计

SwiftList 采用 **“索引服务常驻后台 + 钩子进程拦截输入 + 应用进程呈现交互”** 的多进程分离架构。

## 进程职责划分

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
    <text x="130" y="62" font-weight="800" font-size="15" fill="var(--vp-c-text-1, #1e293b)" text-anchor="middle">Service (索引)</text>
    <text x="130" y="85" font-size="11" fill="var(--vp-c-text-2, #64748b)" text-anchor="middle" font-weight="500">bin: Service.exe --service</text>
    <text x="130" y="102" font-size="9" fill="#3b82f6" text-anchor="middle" font-weight="700" letter-spacing="1">SYSTEM 权限守护</text>
  </g>

  <!-- Column 2: App UI -->
  <g filter="url(#premium-shadow)">
    <rect x="300" y="30" width="200" height="90" rx="16" fill="var(--vp-c-bg-soft, #f6f6f7)" stroke="url(#appGrad)" stroke-width="2.5" />
    <rect x="315" y="45" width="8" height="8" rx="4" fill="#8b5cf6" />
    <text x="400" y="62" font-weight="800" font-size="15" fill="var(--vp-c-text-1, #1e293b)" text-anchor="middle">SwiftList.App</text>
    <text x="400" y="85" font-size="11" fill="var(--vp-c-text-2, #64748b)" text-anchor="middle" font-weight="500">主程序交互窗口 (WPF)</text>
    <text x="400" y="102" font-size="9" fill="#8b5cf6" text-anchor="middle" font-weight="700" letter-spacing="1">USER 桌面级交互</text>
  </g>

  <!-- Column 3: Service Hook -->
  <g filter="url(#premium-shadow)">
    <rect x="570" y="30" width="200" height="90" rx="16" fill="var(--vp-c-bg-soft, #f6f6f7)" stroke="url(#hookGrad)" stroke-width="2.5" />
    <rect x="585" y="45" width="8" height="8" rx="4" fill="#f59e0b" />
    <text x="670" y="62" font-weight="800" font-size="15" fill="var(--vp-c-text-1, #1e293b)" text-anchor="middle">Service (钩子)</text>
    <text x="670" y="85" font-size="11" fill="var(--vp-c-text-2, #64748b)" text-anchor="middle" font-weight="500">bin: Service.exe --hook</text>
    <text x="670" y="102" font-size="9" fill="#f59e0b" text-anchor="middle" font-weight="700" letter-spacing="1">USER 会话输入监听</text>
  </g>

  <!-- Column 1 Downward: NTFS -->
  <g filter="url(#premium-shadow)">
    <rect x="30" y="185" width="200" height="80" rx="14" fill="var(--vp-c-bg-soft, #f6f6f7)" stroke="url(#dbGrad)" stroke-width="2" />
    <text x="130" y="220" font-weight="700" font-size="13" fill="var(--vp-c-text-1, #1e293b)" text-anchor="middle">NTFS MFT &amp; USN</text>
    <text x="130" y="243" font-size="11" fill="var(--vp-c-text-2, #64748b)" text-anchor="middle">低延迟文件扫描与监听</text>
  </g>

  <!-- Column 2 Downward: Plugins -->
  <g filter="url(#premium-shadow)">
    <rect x="300" y="185" width="200" height="80" rx="14" fill="var(--vp-c-bg-soft, #f6f6f7)" stroke="url(#appGrad)" stroke-width="2" />
    <text x="400" y="220" font-weight="700" font-size="13" fill="var(--vp-c-text-1, #1e293b)" text-anchor="middle">用户本地插件</text>
    <text x="400" y="243" font-size="11" fill="var(--vp-c-text-2, #64748b)" text-anchor="middle">PinyinAlias / Settings</text>
  </g>

  <!-- Column 3 Downward: System Hooks -->
  <g filter="url(#premium-shadow)">
    <rect x="570" y="185" width="200" height="80" rx="14" fill="var(--vp-c-bg-soft, #f6f6f7)" stroke="var(--vp-c-border, #e2e8f0)" stroke-width="2" />
    <text x="670" y="220" font-weight="700" font-size="13" fill="var(--vp-c-text-1, #1e293b)" text-anchor="middle">Win32 Hooks &amp; Trackers</text>
    <text x="670" y="243" font-size="11" fill="var(--vp-c-text-2, #64748b)" text-anchor="middle">快捷键拦截与路径追踪</text>
  </g>

  <!-- Search Pipe Connection (Indexer <-> App) -->
  <g>
    <path d="M 230 75 L 300 75" stroke="#3b82f6" stroke-dasharray="4,3" stroke-width="1.5" />
    <polygon points="230,75 240,71 240,79" fill="#3b82f6" />
    <polygon points="300,75 290,71 290,79" fill="#3b82f6" />
    
    <rect x="237" y="66" width="56" height="18" rx="5" fill="var(--vp-c-bg, #ffffff)" stroke="#3b82f6" stroke-width="1" />
    <text x="265" y="79" font-size="9" fill="#3b82f6" text-anchor="middle" font-weight="700">检索通信</text>
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

### SwiftList.Service (索引模式 - SYSTEM 权限)
* **职责**：后台极速索引扫描器。
* 负责扫描本地磁盘 MFT 记录并实时监控 USN Journal。
* 内存中常驻 `RuntimeIndex` 结构，提供最高速的路径匹配。
* 向 App 提供命名管道（Named Pipe `"SwiftListPipe"`）通信服务。
* **多用户共享**：数据库和内存结构系统唯一。

### SwiftList.Service (钩子模式 - 用户权限)
* **职责**：系统级全局输入与焦点监听器（通过 `Service.exe --hook` 运行）。
* 负责注册 Windows 全局键盘快捷键（Keyboard Hooks）拦截，触发快速唤醒。
* 追踪当前焦点窗口类型（Win32 Hooks）并动态搜集活跃路径（如 Explorer、Terminal 物理路径）。
* 向 App 提供本地命名管道连接，实现系统按键消息的低延迟回传。
* **单用户会话绑定**：在每个登录用户的独立 Windows 会话中独立启动和运行。

### SwiftList.App (用户权限)
* 提供轻量化的 WPF 搜索框，处理用户输入与界面呈现。
* 向 Service 发送 `SearchRequestMessage` 查询指令，并通过 `AsyncLocal` 的查询上下文返回给各用户定制的检索结果。
* 加载用户启用的插件，如拼音首字母别名扩展插件等。