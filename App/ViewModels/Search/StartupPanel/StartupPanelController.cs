using System.Collections.ObjectModel;
using System.Windows;
using SwiftList.Core;

using SwiftList.Core.Services.Search;

using SwiftList.App.Services.Plugin;
namespace SwiftList.App.ViewModels.Search.StartupPanel;

/// <summary>
/// Owns the Startup Panel shown above the quick window's results when the search box is
/// empty: the tab strip (built-in "Recent Files" plus whatever IStartupPanelTabProvider plugins are
/// enabled) and the fetch that populates results through the same Results/ResultsControl pipeline a
/// normal search uses -- <paramref name="applyResults"/> is <see cref="SearchExecutionViewModel"/>'s
/// own ReplaceResults. A tab whose source returns zero items is left out of the strip entirely.
/// </summary>
public class StartupPanelController : ViewModelBase
{
    private sealed class ActiveTab
    {
        public required StartupPanelTabViewModel ViewModel { get; init; }
        public required ITabSource Source { get; init; }
        public required List<AppSearchResult> Items { get; init; }
    }

    private readonly SearchService _searchService;
    private readonly Action<IEnumerable<AppSearchResult>> _applyResults;
    private readonly List<ActiveTab> _activeTabs = new();

    // Bumped every time the panel is (re)activated or deactivated so a fetch that's still in flight
    // when the user starts typing (or the panel is hidden/disabled) knows to discard its result.
    private int _requestId;

    // Remembers which tab the user last picked (by label, since ITabSource instances are rebuilt fresh
    // on every activation -- see BuildCandidateSources) so re-showing the window after hiding it on,
    // say, the "History" tab reopens onto History again instead of always resetting to the first tab.
    private string? _lastSelectedLabel;

    public StartupPanelController(SearchService searchService, Action<IEnumerable<AppSearchResult>> applyResults)
    {
        _searchService = searchService;
        _applyResults = applyResults;
    }

    public ObservableCollection<StartupPanelTabViewModel> Tabs { get; } = new();

    private Visibility _visibility = Visibility.Collapsed;
    public Visibility Visibility
    {
        get => _visibility;
        set => SetProperty(ref _visibility, value);
    }

    /// <summary>Fetches every enabled tab source in parallel, keeps only the non-empty ones, and shows
    /// the first as selected. Returns whether the panel should show at all.</summary>
    public async Task<bool> TryActivateAsync()
    {
        if (!UserSettings.Load().StartupPanel.Enabled)
        {
            _requestId++; // invalidate any fetch still in flight from a prior activation
            Visibility = Visibility.Collapsed;
            return false;
        }

        var requestId = ++_requestId;
        var sources = BuildCandidateSources();
        var itemLists = await Task.WhenAll(sources.Select(FetchSafeAsync));

        if (requestId != _requestId)
            return false; // superseded by a newer activation/deactivation while fetching

        _activeTabs.Clear();
        Tabs.Clear();
        for (var i = 0; i < sources.Count; i++)
        {
            if (itemLists[i].Count == 0)
                continue;

            var source = sources[i];
            var tabVm = new StartupPanelTabViewModel(source.Label, () => CloseTab(source), () => SelectTab(source));
            _activeTabs.Add(new ActiveTab { ViewModel = tabVm, Source = source, Items = itemLists[i] });
            Tabs.Add(tabVm);
        }

        if (_activeTabs.Count == 0)
        {
            Visibility = Visibility.Collapsed;
            return false;
        }

        Visibility = Visibility.Visible;
        var toSelect = _activeTabs.FirstOrDefault(t => t.Source.Label == _lastSelectedLabel) ?? _activeTabs[0];
        SelectTab(toSelect.Source);
        return true;
    }

    /// <summary>Hides the panel (an explorer-jump suggestion is taking its slot, a real query started,
    /// or the window is closing) and discards any in-flight fetch.</summary>
    public void Deactivate()
    {
        _requestId++;
        Visibility = Visibility.Collapsed;
    }

    private List<ITabSource> BuildCandidateSources()
    {
        var sources = new List<ITabSource>();
        if (UserSettings.Load().StartupPanel.RecentFilesEnabled)
            sources.Add(new RecentFilesTabSource(_searchService));
        if (UserSettings.Load().StartupPanel.LastDirectoryEnabled)
            sources.Add(new LastDirectoryTabSource());

        // StartupPanelTabProviders already excludes plugin components disabled via Plugin Management;
        // ClosedTabIds is the separate, panel-local "user hid this one" list -- see PluginTabSource.Close.
        var closedIds = UserSettings.Load().StartupPanel.ClosedTabIds;
        foreach (var provider in PluginManager.Instance.StartupPanelTabProviders)
        {
            if (!closedIds.Contains(PluginTabSource.ComponentId(provider), StringComparer.OrdinalIgnoreCase))
                sources.Add(new PluginTabSource(provider));
        }

        // Reordered per StartupPanel.TabOrder (position = priority, most-preferred first), covering
        // both built-ins and plugin tabs -- a source whose id isn't listed there yet falls back to
        // int.MaxValue, which (List<T>.Sort/OrderBy are both stable) lands it after every listed source
        // while preserving its built-in-then-plugin-discovery-order position relative to any OTHER
        // unlisted source, rather than an arbitrary reshuffle. Same pattern as
        // PluginManager.QuickNavigationProviders' own ordering.
        var order = UserSettings.Load().StartupPanel.TabOrder;
        return sources
            .OrderBy(s =>
            {
                var rank = order.IndexOf(s.Id);
                return rank >= 0 ? rank : int.MaxValue;
            })
            .ToList();
    }

    private static async Task<List<AppSearchResult>> FetchSafeAsync(ITabSource source)
    {
        try
        {
            return await source.LoadItemsAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"[StartupPanelController] '{source.Label}' fetch failed: {ex.Message}", LogLevel.Error);
            return new List<AppSearchResult>();
        }
    }

    /// <summary>Moves the selection to the next tab, wrapping from the last back to the first. A no-op
    /// with 0 or 1 active tabs (nothing to cycle to).</summary>
    public void SelectNextTab() => ShiftSelectedTab(1);

    /// <summary>Moves the selection to the previous tab, wrapping from the first back to the last.</summary>
    public void SelectPreviousTab() => ShiftSelectedTab(-1);

    private void ShiftSelectedTab(int direction)
    {
        if (_activeTabs.Count < 2)
            return;

        var currentIndex = _activeTabs.FindIndex(t => t.ViewModel.IsSelected);
        if (currentIndex < 0)
            currentIndex = 0;

        var nextIndex = (currentIndex + direction + _activeTabs.Count) % _activeTabs.Count;
        SelectTab(_activeTabs[nextIndex].Source);
    }

    private void SelectTab(ITabSource source)
    {
        var match = _activeTabs.FirstOrDefault(t => ReferenceEquals(t.Source, source));
        if (match == null)
            return;

        foreach (var tab in _activeTabs)
            tab.ViewModel.IsSelected = ReferenceEquals(tab, match);

        _lastSelectedLabel = match.Source.Label;
        _applyResults(match.Items);
    }

    private void CloseTab(ITabSource source)
    {
        source.Close();

        var match = _activeTabs.FirstOrDefault(t => ReferenceEquals(t.Source, source));
        if (match == null)
            return;

        var wasSelected = match.ViewModel.IsSelected;
        _activeTabs.Remove(match);
        Tabs.Remove(match.ViewModel);

        if (_activeTabs.Count == 0)
        {
            Visibility = Visibility.Collapsed;
            _applyResults(Array.Empty<AppSearchResult>());
            return;
        }

        if (wasSelected)
            SelectTab(_activeTabs[0].Source);
    }
}
