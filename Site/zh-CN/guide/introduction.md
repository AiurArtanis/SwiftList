# SwiftList 简介

SwiftList 是一款针对 Windows 系统设计的高性能、低消耗、可扩展的全局检索工具，旨在作为 **Listary** 的高颜值、支持深度定制的开源替代方案。它能以极快的速度建立和维护本地物理文件索引，并提供开放的插件 SDK 方便开发者扩展各项功能。

## 下载与安装

SwiftList 提供官方打包的安装器与免安装便携版（最新稳定版）：

* 💾 **[下载安装包](https://github.com/SwiftList/SwiftList/releases/latest/download/SwiftList-Setup.exe)** — 全功能安装包（支持开机自启和 Windows 服务后台运行）。
* 📦 **[下载便携版](https://github.com/SwiftList/SwiftList/releases/latest/download/SwiftList-Portable.zip)** — 绿色免安装，解压即用。
* 🔍 或者前往 **[GitHub Releases 历史版本页面](https://github.com/SwiftList/SwiftList/releases)** 查看所有发布版本。

## 核心设计理念

1. **YAGNI (You Aren't Gonna Need It)**：拒绝繁琐的多余设计，保持逻辑的简洁与代码的高效。
2. **多进程与架构隔离**：
   * **`SwiftList.Service` (系统服务 - `--service`)**：运行在 Session 0 的 Windows 服务，直接对接 NTFS MFT 和 USN 日志进行索引数据库维护，全局共享。
   * **`SwiftList.Service` (钩子进程 - `--hook`)**：运行在用户会话中，拦截全局系统热键并动态搜集活动窗口的焦点与物理路径。
   * **`SwiftList.App` (用户界面)**：运行在用户会话中的 WPF 应用程序，承载界面交互、展示搜索结果，并按需加载用户私有插件。
3. **极速 FZF 模糊匹配**：采用优化的匹配算法，支持 FZF 模糊搜索以及插件提供的丰富别名检索。

