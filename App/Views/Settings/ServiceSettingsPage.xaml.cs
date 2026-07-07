using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using SwiftList.App.ViewModels.Settings;
using SwiftList.Core;

namespace SwiftList.App.Views.Settings;

public partial class ServiceSettingsPage : System.Windows.Controls.UserControl
{
    public ServiceSettingsPage()
    {
        InitializeComponent();
        DataContextChanged += (s, e) =>
        {
            if (e.NewValue is SettingsViewModel vm)
            {
                vm.Log.Lines.CollectionChanged += (_, _) => OnLogLinesChanged(vm.Log.Lines);
                OnLogLinesChanged(vm.Log.Lines);
            }
        };
    }

    private void OnLogLinesChanged(IEnumerable<LogLineViewModel> lines)
    {
        // Only auto-follow to the newest lines if the user was already at (or near) the bottom --
        // otherwise a periodic refresh would yank them away while reading older lines. Checked against
        // the scroll extent from before this update, i.e. where the user actually was.
        var wasNearBottom = LogTextBox.VerticalOffset >= LogTextBox.ExtentHeight - LogTextBox.ViewportHeight - 20;
        RebuildLogDocument(lines);
        if (wasNearBottom)
            LogTextBox.ScrollToEnd();
    }

    private void RebuildLogDocument(IEnumerable<LogLineViewModel> lines)
    {
        // A very wide fixed page width is FlowDocument's usual trick for "don't wrap this text, let
        // long lines scroll horizontally instead" -- there's no direct TextWrapping=NoWrap for it.
        var document = new FlowDocument { PagePadding = new Thickness(0), PageWidth = 20000 };
        var paragraph = new Paragraph { Margin = new Thickness(0) };

        foreach (var line in lines)
        {
            var run = new Run(line.Text);
            run.SetResourceReference(TextElement.ForegroundProperty, ForegroundKeyFor(line.Level));
            paragraph.Inlines.Add(run);
            paragraph.Inlines.Add(new LineBreak());
        }

        document.Blocks.Add(paragraph);
        LogTextBox.Document = document;
    }

    private static string ForegroundKeyFor(LogLevel level) => level switch
    {
        LogLevel.Error => "ErrorBrush",
        LogLevel.Warn => "WarningBrush",
        LogLevel.Debug => "TextSecondary2",
        _ => "TextPrimary2"
    };

    // Shift+wheel scrolls the log horizontally instead of vertically -- there is no built-in WPF
    // gesture for this, so it's handled manually.
    private void LogTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Shift) return;
        LogTextBox.ScrollToHorizontalOffset(LogTextBox.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
}
