# 系统设置插件 (SystemSettings)

系统设置插件是一个内置的 `ISearchableItemProvider`，用于在 SwiftList 中快速检索 Windows 系统控制面板和“上帝模式”中的各种设置项。

## 实现细节

该插件通过访问 Windows 的 **“上帝模式”（GodMode）虚拟文件夹 CLSID**：
`shell:::{ED7BA470-8E54-465E-825C-99712043E01C}`

并通过 COM 对象 `Shell.Application` 动态读取该命名空间下的所有 `Items()`。

```csharp
public class SystemSettingsItemProvider : ISearchableItemProvider
{
    private const string GodModePath = "shell:::{ED7BA470-8E54-465E-825C-99712043E01C}";

    public IEnumerable<SearchableItem> GetSearchableItems()
    {
        // 动态获取 COM 实例并读取
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        dynamic dShell = Activator.CreateInstance(shellType);
        dynamic folder = dShell.NameSpace(GodModePath);
        
        foreach (var item in folder.Items())
        {
            // 解析名称、路径，获取图标并返回 SearchableItem...
        }
    }
}
```

## 优点与特性
1. **自动多语言**：获取出来的名称会自动匹配用户的系统语言，完全不需要手动维护翻译对照表。
2. **轻量与缓存**：在主程序中通过 `SearchableItemMapper` 对其结果进行内存级缓存，仅在每次主程序启动或首次搜索时加载一次，查询效率极高。
