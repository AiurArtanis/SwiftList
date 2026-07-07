# 打包与发布

## 插件是如何被发现的

App 启动时会扫描自己的 `Plugins/` 文件夹(与 `SwiftList.App.exe` 同级)里的每一个 `.dll`，查找实
现了 `IPlugin` 的类型。没有单独的清单文件——程序集本身，加上它的类型实现了哪些 SDK 接口，就是完
整的契约。

## 开发时自动化复制

SwiftList 自带的插件(`CoreExtensions`、`PinyinAlias`)都在各自的 `.csproj` 里用一个 PostBuild 目
标自动化了部署步骤，把刚编译好的 DLL 直接复制到 App 自己输出目录下的 `Plugins/` 文件夹，这样重新
编译后下次启动就能立刻生效:

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
</Target>
```

把目标路径改成你自己的构建输出和 SwiftList App 安装位置实际所在的路径即可。

## 内嵌语言包

如果插件实现了 `ITranslationProvider`(见[界面与预览扩展](./sdk/ui-extensions))，把语言包 JSON
文件作为内嵌资源打包，而不是散落的独立文件，这样它们才会跟着 DLL 一起分发:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

`TranslationService.LoadEmbeddedTranslations`(见[宿主服务](./sdk/services))会在运行时按文化名
称从程序集里把它们读出来。

## 版本号

给插件的 `.csproj` 加上 `<Version>`；它会显示在**设置 → 插件**里对应插件的卡片上，旁边还会显示
你的插件是针对哪个 `PluginSdk` 版本编译的——在 SDK 接口发生变化时，这对确认兼容性很有用。
