namespace SwiftList.App.Views.QuickSearchWindow.Helpers;

/// <summary>
/// Owns the search box's keyword-history navigation session: wires the TextChanged (manual-edit reset)
/// and PreviewMouseWheel (scroll-to-navigate) hookups, and applies Previous/Next results from the
/// underlying <see cref="KeywordHistoryNavigator"/>.
/// </summary>
internal sealed class QuickSearchKeywordHistoryController
{
    private readonly SwiftList.App.QuickSearchWindow _window;
    private readonly KeywordHistoryNavigator _navigator = new();
    private bool _isApplyingHistory;

    public QuickSearchKeywordHistoryController(SwiftList.App.QuickSearchWindow window)
    {
        _window = window;
        _window.TxtSearch.TextChanged += (s, e) =>
        {
            if (!_isApplyingHistory)
                _navigator.Reset();
        };
        _window.TxtSearch.PreviewMouseWheel += (s, e) =>
        {
            Navigate(previous: e.Delta > 0);
            e.Handled = true;
        };
    }

    /// <summary>Ends the current navigation session (call when the quick window hides).</summary>
    public void Reset() => _navigator.Reset();

    /// <summary>Steps the search box through keyword history (hotkey or mouse-wheel driven).</summary>
    public void Navigate(bool previous)
    {
        var value = previous ? _navigator.Previous(_window.ViewModel.SearchQuery) : _navigator.Next();
        if (value == null) return;

        _isApplyingHistory = true;
        try
        {
            _window.ViewModel.SearchQuery = value;
            _window.TxtSearch.CaretIndex = _window.TxtSearch.Text.Length;
        }
        finally
        {
            _isApplyingHistory = false;
        }
    }
}
