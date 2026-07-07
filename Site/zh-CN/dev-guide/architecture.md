# 架构设计

![SwiftList 架构图](/architecture-zh-CN.svg)

## 进程拆分

SwiftList 运行为三个独立进程，按权限级别和生命周期有意拆分:

- **`SwiftList.Service`** —— 一个以 `LocalSystem` 身份运行的 Windows 服务。它负责全部文件索引工
  作:读取本地磁盘的 NTFS USN 日志与 MFT、扫描并缓存网络共享，并通过命名管道回答搜索查询。在
  SYSTEM 级别运行意味着它可以读取所有用户账户都被允许看到的原始卷元数据，而不需要让交互式的 App
  进程获得它本不需要的提升权限。
- **`SwiftList.App`** —— 用户态、Session 级别的 WPF 应用:搜索窗口、设置窗口、热键处理、动作菜
  单/QuickLook 界面都在这里。它通过命名管道(`Core.Services` 里的 `SearchService`/
  `UsnServicePipeServer`)和 Service 通信，从不直接访问磁盘索引。
- **`SwiftList.Service --hook`** —— 一个独立的小进程，专门托管低层级全局键盘钩子，这样钩子崩溃
  或者某个前台应用行为异常都不会连累主 App 进程。

## 共享的 Core

`Core` 是一个被 Service 和 App 同时引用的类库。它包含:

- 搜索引擎(`Core/SearchIndex/Fzf/*`)—— 一套仿照 `fzf` 命令行工具算法实现的模糊匹配引擎，配合
  一个查询解析器(`SearchQueryParser`)处理盘符定向和路径模式搜索。
- 运行时索引(`Core/SearchIndex/RecordIndex/*`、`RecordSearch/*`)—— 由 USN/MFT 读取结果构建的
  内存结构，随变更增量更新。
- IPC 契约(`SearchRequestMessage`、`SearchResponseBinarySerializer` 等)—— App 和 Service 两边
  完全共用同一份定义，保证双方对线路格式的理解始终一致。
- `Logger` —— 写入各进程独立的日志文件(`service.log`、`app.log`、`hook.log`)，都可以在 App 的
  设置 → 运行状态 日志查看器里读取(但不是都能直接写入)。

## 插件在架构中的位置

插件是引用 `PluginSdk` 的 `.dll` 程序集，由 App 进程加载(见[快速上手](./getting-started)和
[打包与发布](./packaging))。SwiftList 自带两个插件作为一等公民示例——
`SwiftList.Plugins.CoreExtensions`(内置文件动作与 Shell 右键菜单集成)和
`SwiftList.Plugins.PinyinAlias`(中文文件名拼音别名)——参见[插件示例](./examples)了解这两个插
件的详细走读。

插件从不直接和 Service 通信;它们通过插件 SDK 参考里记录的接口和 App 交互，如果需要索引自定义目
录，则通过 `DirectoryIndexerService` 代为向 Service 转发请求。
