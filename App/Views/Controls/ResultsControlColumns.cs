using System.Windows;
using System.Windows.Controls;
using SwiftList.App.Services;
using SwiftList.Core;

using SwiftList.App.Services.Plugin;
namespace SwiftList.App.Views.Controls;

// Grid-mode (full/main window) dynamic column population and header-click sorting -- split out of
// ResultsControl.xaml.cs to keep that file under the project's line limit. Unrelated to the shared
// list-mode result list (quick/inline windows) the rest of that file deals with.
internal static class ResultsControlColumns
{
    public static void PopulateDynamicColumns(System.Windows.Controls.ListView lstGridResults)
    {
        if (lstGridResults.View is not GridView gridView) return;

        foreach (var provider in PluginManager.Instance.ResultColumnProviders)
        {
            foreach (var colDef in provider.GetColumns())
            {
                var gvc = new GridViewColumn
                {
                    Header = colDef.HeaderText,
                    Width = colDef.Width
                };

                var binding = new System.Windows.Data.Binding($"[{colDef.ColumnId}]")
                {
                    Mode = System.Windows.Data.BindingMode.OneWay
                };
                var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
                textBlockFactory.SetBinding(TextBlock.TextProperty, binding);
                textBlockFactory.SetValue(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextSecondary2"));
                textBlockFactory.SetValue(TextBlock.FontSizeProperty, 12.0);
                textBlockFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                // A TextBlock with no Background is only hit-testable over its rendered glyphs (WPF's
                // usual "empty space in an unpainted element passes mouse input through" rule), which is
                // why hovering past the end of a short value (or above/below it) swallowed the mouse
                // wheel instead of scrolling the list -- see the matching fix in ResultsControl.xaml.
                textBlockFactory.SetValue(TextBlock.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
                textBlockFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
                gvc.CellTemplate = new DataTemplate { VisualTree = textBlockFactory };
                gridView.Columns.Add(gvc);
            }
        }
    }

    // Re-resolves every plugin-provided column's header text in the now-current language and re-applies
    // it in place -- called on TranslationManager language switches so these headers don't stay stuck in
    // whatever language was active when PopulateDynamicColumns ran, without needing to tear down and
    // rebuild the columns themselves. GridViewColumn is a DependencyObject, not a FrameworkElement (no
    // Tag to stash an identity on), so this correlates by position instead: dynamic columns are always
    // appended after the fixed built-in ones, in this same provider/GetColumns() order every time,
    // matching how PopulateDynamicColumns built them. If the dynamic column count no longer matches
    // (a plugin was enabled/disabled mid-session), skip rather than risk relabeling the wrong column.
    // Preserves an existing sort-arrow suffix (see HandleColumnHeaderClick below) so an active sort
    // indicator survives the relabel.
    public static void RefreshDynamicColumnHeaders(System.Windows.Controls.ListView lstGridResults)
    {
        if (lstGridResults.View is not GridView gridView) return;

        var freshHeaders = new List<string>();
        foreach (var provider in PluginManager.Instance.ResultColumnProviders)
            foreach (var colDef in provider.GetColumns())
                freshHeaders.Add(colDef.HeaderText);

        var dynamicStartIndex = gridView.Columns.Count - freshHeaders.Count;
        if (dynamicStartIndex < 0) return;

        for (var i = 0; i < freshHeaders.Count; i++)
        {
            var col = gridView.Columns[dynamicStartIndex + i];
            var current = col.Header as string ?? string.Empty;
            var suffix = current.EndsWith(" ▲") ? " ▲" : current.EndsWith(" ▼") ? " ▼" : string.Empty;
            col.Header = freshHeaders[i] + suffix;
        }
    }

    public static void HandleColumnHeaderClick(GridViewColumnHeader? headerClicked, object? dataContext, System.Windows.Controls.ListView lstGridResults)
    {
        // Null, or missing a Column, whenever the click resolved to something other than a header cell
        // (e.g. the resize gripper) -- not an error, just nothing to sort by.
        if (headerClicked is not { Column: not null })
            return;

        var headerText = headerClicked.Column.Header as string ?? string.Empty;
        if (string.IsNullOrEmpty(headerText) || dataContext == null)
            return;

        var cleanHeader = headerText.Replace(" ▲", "").Replace(" ▼", "");
        dynamic vm = dataContext;
        try
        {
            vm.SortByColumn(cleanHeader);
            bool isAsc = vm.IsSortAscending;

            if (lstGridResults.View is not GridView gridView) return;

            foreach (var col in gridView.Columns)
            {
                if (col.Header is not string colHeaderText) continue;

                var cleanColHeader = colHeaderText.Replace(" ▲", "").Replace(" ▼", "");
                col.Header = cleanColHeader == cleanHeader
                    ? cleanColHeader + (isAsc ? " ▲" : " ▼")
                    : cleanColHeader;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ResultsControlColumns] HandleColumnHeaderClick failed for header '{cleanHeader}': {ex}", LogLevel.Error);
        }
    }
}
