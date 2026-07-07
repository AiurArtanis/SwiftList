# Packaging & Deployment

## How plugins are discovered

The App loads every `.dll` it finds in its own `Plugins/` folder (next to `SwiftList.App.exe`) at
startup, looking for types implementing `IPlugin`. There's no separate manifest file — the
assembly itself, plus whichever SDK interfaces its types implement, is the full contract.

## Automating the copy during development

The plugins shipped with SwiftList itself (`CoreExtensions`, `PinyinAlias`) automate deployment
with a post-build target in their `.csproj`, copying the freshly-built DLL straight into the App's
own output `Plugins/` folder so a rebuild is immediately picked up on the next launch:

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
</Target>
```

Adapt the destination path to wherever your own build output and the SwiftList App installation
actually live.

## Embedded translations

If your plugin implements `ITranslationProvider` (see
[UI & Preview Extensions](./sdk/ui-extensions)), ship its translation JSON files as embedded
resources rather than loose files, so they travel with the DLL:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

`TranslationService.LoadEmbeddedTranslations` (see [Host Services](./sdk/services)) reads them back
out of the assembly at runtime by culture name.

## Versioning

Give your plugin's `.csproj` a `<Version>`; it's shown to users on its card under
**Settings → Plugins**, alongside the `PluginSdk` version your plugin was built against — useful
for confirming compatibility when the SDK surface changes.
