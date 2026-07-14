using System.Windows;
using System.Windows.Controls;
using SwiftList.App.Services;

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
                gvc.CellTemplate = new DataTemplate { VisualTree = textBlockFactory };
                gridView.Columns.Add(gvc);
            }
        }
    }

    public static void HandleColumnHeaderClick(object sender, object? dataContext, System.Windows.Controls.ListView lstGridResults)
    {
        if (sender is not GridViewColumnHeader { Column: not null } headerClicked) return;

        var headerText = headerClicked.Column.Header as string ?? string.Empty;
        if (string.IsNullOrEmpty(headerText) || dataContext == null) return;

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
        catch { }
    }
}
