using System;
using System.Collections.Generic;
using System.Linq;
using SwiftList.Core;
using SwiftList.App.ViewModels.Settings.Plugins;
using SwiftList.App.Services.PluginManagerCore;

namespace SwiftList.App.Services
{
    /// <summary>
    /// Central hub for plugin lifecycle management: loading, registration,
    /// filtering by enabled state, search action dispatch, and instant result execution.
    /// <para>
    /// Loading is delegated to <see cref="PluginLoader"/>;
    /// component enable/disable state is managed by <see cref="ComponentFilter"/>.
    /// </para>
    /// </summary>
    public class PluginManager : PluginRegistry
    {
        private static readonly Lazy<PluginManager> _instance = new(() => new PluginManager());

        /// <summary>Gets the singleton instance of the PluginManager.</summary>
        public static PluginManager Instance => _instance.Value;

        private readonly List<SwiftList.PluginSdk.IActionPlugin> _plugins = new();
        private readonly List<PluginActionRegistration> _actions = new();
        private readonly List<SwiftList.PluginSdk.IDynamicActionProvider> _dynamicProviders = new();
        private readonly List<SwiftList.PluginSdk.IInstantResultProvider> _instantResultProviders = new();
        private readonly List<SwiftList.PluginSdk.ISidebarFilterProvider> _sidebarFilterProviders = new();
        private readonly List<SwiftList.PluginSdk.IResultColumnProvider> _resultColumnProviders = new();
        private readonly List<SwiftList.PluginSdk.ITranslationProvider> _translationProviders = new();
        private readonly List<SwiftList.PluginSdk.IThemeProvider> _themeProviders = new();
        private readonly List<SwiftList.PluginSdk.IActivePathCollector> _pathCollectors = new();
        private uint _nextRuntimeActionId = 0x80000000;

        private readonly ComponentFilter _filter = new();

        private PluginManager()
        {
            _filter.Refresh();

            // Wire up the dynamic filtering delegate for alias providers in the Core indexer
            AliasProviderRegistry.FilterFunc = prov =>
                _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.AliasProvider, prov.GetType().Name);

            // Wire up the dynamic filtering delegate for active path collectors
            SwiftList.PluginSdk.ActivePathCollectorRegistry.FilterFunc = prov =>
                _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.ActivePathCollector, prov.GetType().Name);

            // Wire up the dynamic filtering delegate for file dialog adapters
            SwiftList.PluginSdk.FileDialogAdapterRegistry.FilterFunc = prov =>
                _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.FileDialogAdapter, prov.GetType().Name);

            PluginLoader.Load(this);
        }

        // ── PluginRegistry callbacks ──────────────────────────────────────────

        void PluginRegistry.RegisterPlugin(SwiftList.PluginSdk.IActionPlugin plugin) => RegisterPlugin(plugin);

        void PluginRegistry.AddInstantResultProvider(SwiftList.PluginSdk.IInstantResultProvider p) => _instantResultProviders.Add(p);
        void PluginRegistry.AddSidebarFilterProvider(SwiftList.PluginSdk.ISidebarFilterProvider p) => _sidebarFilterProviders.Add(p);
        void PluginRegistry.AddResultColumnProvider(SwiftList.PluginSdk.IResultColumnProvider p) => _resultColumnProviders.Add(p);
        void PluginRegistry.AddTranslationProvider(SwiftList.PluginSdk.ITranslationProvider p) => _translationProviders.Add(p);
        void PluginRegistry.AddThemeProvider(SwiftList.PluginSdk.IThemeProvider p) => _themeProviders.Add(p);
        void PluginRegistry.AddActivePathCollector(SwiftList.PluginSdk.IActivePathCollector p)
        {
            _pathCollectors.Add(p);
            SwiftList.PluginSdk.ActivePathCollectorRegistry.Register(p);
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void RefreshDisabledComponents() => _filter.Refresh();

        public bool IsComponentEnabled(string dllName, PluginComponentType type, string name)
            => _filter.IsEnabled(dllName, type, name);

        /// <summary>Registers a plugin and loads its actions and dynamic providers.</summary>
        public void RegisterPlugin(SwiftList.PluginSdk.IActionPlugin plugin)
        {
            if (plugin == null) return;
            _plugins.Add(plugin);
            foreach (var action in plugin.GetActions())
                _actions.Add(new PluginActionRegistration(_nextRuntimeActionId++, plugin, action));
            foreach (var provider in plugin.GetDynamicProviders())
                _dynamicProviders.Add(provider);
        }

        // ── Filtered collections (active components only) ─────────────────────

        public IEnumerable<SwiftList.PluginSdk.IActionPlugin> Plugins => _plugins;

        public IEnumerable<PluginActionRegistration> Actions
            => _actions.Where(a => _filter.IsEnabled(ComponentFilter.GetDllName(a.Plugin), PluginComponentType.Action, a.Action.Id));

        public IEnumerable<SwiftList.PluginSdk.IDynamicActionProvider> DynamicProviders
            => _dynamicProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.DynamicProvider, p.GetType().Name));

        public IEnumerable<SwiftList.PluginSdk.IInstantResultProvider> InstantResultProviders
            => _instantResultProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.InstantProvider, p.Id));

        public IEnumerable<SwiftList.PluginSdk.ISidebarFilterProvider> SidebarFilterProviders
        {
            get
            {
                foreach (var p in _sidebarFilterProviders)
                    yield return new FilteredSidebarFilterProvider(p, ComponentFilter.GetDllName(p), this);
            }
        }

        public IEnumerable<SwiftList.PluginSdk.IResultColumnProvider> ResultColumnProviders
        {
            get
            {
                foreach (var p in _resultColumnProviders)
                    yield return new FilteredResultColumnProvider(p, ComponentFilter.GetDllName(p), this);
            }
        }

        public IEnumerable<SwiftList.PluginSdk.ITranslationProvider> TranslationProviders => _translationProviders;
        public IEnumerable<SwiftList.PluginSdk.IThemeProvider> ThemeProviders => _themeProviders;
        public IEnumerable<SwiftList.PluginSdk.IActivePathCollector> ActivePathCollectors => _pathCollectors;

        // ── Unfiltered collections (settings UI ?show disabled as unchecked) ─

        public IEnumerable<PluginActionRegistration> AllActions => _actions;
        public IEnumerable<SwiftList.PluginSdk.IDynamicActionProvider> AllDynamicProviders => _dynamicProviders;
        public IEnumerable<SwiftList.PluginSdk.IInstantResultProvider> AllInstantResultProviders => _instantResultProviders;
        public IEnumerable<SwiftList.PluginSdk.ISidebarFilterProvider> AllSidebarFilterProviders => _sidebarFilterProviders;
        public IEnumerable<SwiftList.PluginSdk.IResultColumnProvider> AllResultColumnProviders => _resultColumnProviders;
        public IEnumerable<SwiftList.PluginSdk.ITranslationProvider> AllTranslationProviders => _translationProviders;
        public IEnumerable<SwiftList.PluginSdk.IThemeProvider> AllThemeProviders => _themeProviders;

        // ── Search and execution ──────────────────────────────────────────────

        public IEnumerable<PluginSearchActionMatch> SearchActionItems(string query, bool isInlineWindow)
        {
            if (string.IsNullOrWhiteSpace(query)) yield break;
            if (isInlineWindow && InlineSearchManager.Instance.ExplorerTracker.IsActiveWindowDialog) yield break;

            foreach (var action in _actions)
            {
                if (action.Action.Keywords.Count == 0) continue;
                if (action.Action.InlineWindowOnly && !isInlineWindow) continue;
                if (!_filter.IsEnabled(ComponentFilter.GetDllName(action.Plugin), PluginComponentType.Action, action.Action.Id)) continue;

                var match = KeywordMatcher.TryMatchKeyword(query, action.Action.Keywords);
                if (match == null) continue;

                yield return new PluginSearchActionMatch(action, match.Value.Keyword, match.Value.ArgumentText);
            }
        }

        public bool TryExecuteSearchAction(AppSearchResult result, SwiftList.PluginSdk.IPluginSearchWindow view)
        {
            if (result.IsInstantResult)
            {
                try
                {
                    if (result.InstantResultActionType == "Copy")
                        System.Windows.Clipboard.SetText(result.InstantResultActionArgument);
                    else if (result.InstantResultActionType == "Execute")
                    {
                        string arg = result.InstantResultActionArgument.Trim();
                        bool runAsAdmin = false;
                        if (arg.StartsWith("runas:", StringComparison.OrdinalIgnoreCase))
                        {
                            runAsAdmin = true;
                            arg = arg.Substring(6).Trim();
                        }

                        string fileName = arg;
                        string arguments = "";
                        if (arg.StartsWith("\""))
                        {
                            int endQuote = arg.IndexOf('\"', 1);
                            if (endQuote > 0)
                            {
                                fileName = arg.Substring(1, endQuote - 1);
                                arguments = arg.Substring(endQuote + 1).Trim();
                            }
                        }
                        else
                        {
                            int firstSpace = arg.IndexOf(' ');
                            if (firstSpace > 0)
                            {
                                fileName = arg.Substring(0, firstSpace);
                                arguments = arg.Substring(firstSpace + 1).Trim();
                            }
                        }

                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = fileName,
                            Arguments = arguments,
                            UseShellExecute = true
                        };
                        if (runAsAdmin)
                        {
                            psi.Verb = "runas";
                        }
                        System.Diagnostics.Process.Start(psi);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[PluginManager] Failed to execute instant result action: {ex.Message}", SwiftList.Core.LogLevel.Error);
                }
                return true;
            }

            if (!result.IsPluginSearchAction || result.IsSearchSectionHeader) return false;

            var registration = _actions.FirstOrDefault(x => x.RuntimeActionId == result.PluginActionId);
            if (registration == null)
            {
                Logger.Log($"[PluginManager] Plugin search action not found: {result.PluginActionId}", SwiftList.Core.LogLevel.Warn);
                return false;
            }

            registration.Action.Execute(
                new PluginSearchResult(result.Name, result.PluginActionArgumentText, result.ContextDirectory), view);
            return true;
        }

        public PluginActionRegistration? GetActionByRuntimeId(uint runtimeActionId)
            => _actions.FirstOrDefault(x => x.RuntimeActionId == runtimeActionId);
    }
}
