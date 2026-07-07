# 插件示例

SwiftList 自带两个插件，都是很有参考价值的真实案例——都在 SwiftList 仓库的 `Plugins/` 目录下。

## CoreExtensions —— 动作与 Shell 右键菜单

`CoreExtensionsPlugin` 同时实现了三个接口:`IPlugin`、`IActionProvider`、`IConfigurable`。

- **`IActionProvider.GetActions()`** 返回十个内置的 `ISearchResultAction`——打开、在资源管理器中
  定位、复制路径、复制/剪切文件本身、在其所在位置打开命令提示符、touch/mkdir，以及打开和命令提
  示符的提权(以管理员身份运行)变体。
- **`IActionProvider.GetDynamicProviders()`** 返回一个 `IDynamicActionProvider`——
  `ShellMenuActionProvider`——正是它让真正的 Windows 右键菜单(包括"发送到"这类级联子菜单)出现
  在 SwiftList 自己的动作菜单里。如果你想在 SwiftList 里呈现*任何*外部、动态构建的菜单，而不是
  一份固定的动作列表，这是值得照抄的模式。
- **`IConfigurable.GetConfigSchema()`** 展示了带嵌套字段分组和 `StringList` 字段类型的配置模式
  ——如果你的插件在设置 → 插件的配置对话框里需要的不只是一份扁平的布尔值列表，值得读一下这部分。
- `FavoritesTabProvider` 和 `HistoryTabProvider` 各自实现了
  [`IStartupPanelTabProvider`](./sdk/ui-extensions#istartuppaneltabprovider)，把已有的列表以标签
  的形式呈现在[初始面板](../user-guide/settings/startup-panel)里——是这个接口的一个最简参考实现，
  因为两者都只是把一份已经查询好的列表包一层，自己没有额外的状态。

## PinyinAlias —— 中文文件名拼音别名

`PinyinAliasProvider` 同时实现了 `IAliasProvider` 和 `ITranslationProvider`——一个插件可以自由组
合多个相关的 SDK 角色，这是个很好的参考模板:

- **`IAliasProvider.CanHandle(text)`** 会先扫描是否存在任意中文字符，再决定要不要做实际工作，所
  以非中文文件名会完全跳过别名生成。
- **`IAliasProvider.GetAliases(text)`** 先构建一张按字符划分的音节表(每个汉字映射到它可能的拼
  音读音)，然后产出一个全拼别名和一个首字母别名。对于含多音字(有一种以上有效读音)的文件名，会
  为每种常见读音组合都生成别名——上限 32 种组合，防止极端输入引发组合爆炸——用 `|` 连接各个备选
  项，这样搜索引擎会把每一个都当作候选，而不是要求它们同时全部匹配。
- **`ITranslationProvider`** 实现在*同一个*类上，纯粹是为了给这个插件自己的界面文本(比如它的显
  示名称)提供翻译，通过 `TranslationService.LoadEmbeddedTranslations` 实现——这两个接口用途上并
  无关联，只是碰巧在这个体量很小的单文件插件里放在了同一个类型上。
- 用一个 `lock` 保护的 `Dictionary<string, Dictionary<string, string>>` 缓存避免了每次调用
  `GetTranslations` 都重新解析内嵌的翻译 JSON——这是任何在 `GetTranslations` 里做了非平凡工作的
  插件都该采用的标准模式。

把这两个插件对照着看，是理解[插件 SDK 参考](./sdk/core-search-actions)里各个部分如何在实践中配合
起来最快的方式。
