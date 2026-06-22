using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace SwiftList.App.Converters;

/// <summary>
/// Attached behavior that highlights matching portions of text in a TextBlock.
/// Usage: local:TextHighlighter.Text="{Binding Name}" local:TextHighlighter.HighlightText="{Binding SearchQuery}"
/// </summary>
public static class TextHighlighter
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(TextHighlighter),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty HighlightTextProperty =
        DependencyProperty.RegisterAttached("HighlightText", typeof(string), typeof(TextHighlighter),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty HighlightBrushProperty =
        DependencyProperty.RegisterAttached("HighlightBrush", typeof(Brush), typeof(TextHighlighter),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF))));

    public static string GetText(DependencyObject obj) => (string)obj.GetValue(TextProperty);
    public static void SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);

    public static string GetHighlightText(DependencyObject obj) => (string)obj.GetValue(HighlightTextProperty);
    public static void SetHighlightText(DependencyObject obj, string value) => obj.SetValue(HighlightTextProperty, value);

    public static Brush GetHighlightBrush(DependencyObject obj) => (Brush)obj.GetValue(HighlightBrushProperty);
    public static void SetHighlightBrush(DependencyObject obj, Brush value) => obj.SetValue(HighlightBrushProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock) return;

        var fullText = GetText(textBlock);
        var highlight = GetHighlightText(textBlock);
        var highlightBrush = GetHighlightBrush(textBlock);

        textBlock.Inlines.Clear();

        if (string.IsNullOrEmpty(fullText))
            return;

        if (string.IsNullOrEmpty(highlight))
        {
            textBlock.Inlines.Add(new Run(fullText));
            return;
        }

        bool[]? highlights = null;
        if (textBlock.DataContext is PluginSdk.ISearchResult searchResult)
        {
            try
            {
                var mask = searchResult.GetHighlightMask(fullText, highlight);
                if (mask != null && mask.Length == fullText.Length)
                {
                    highlights = mask;
                }
            }
            catch (Exception ex)
            {
                Core.Logger.Log($"[TextHighlighter] Custom highlighting error: {ex.Message}", Core.LogLevel.Error);
            }
        }

        if (highlights == null)
        {
            // Split highlight into terms and normalize them (same logic as Search)
            string? targetDrive = null;
            var termsList = new List<string>();
            var normalizedHighlight = NormalizePathSeparators(highlight.Trim()).ToLowerInvariant();

            if (ContainsPathSeparator(normalizedHighlight))
            {
                if (TryNormalizeDrivePath(normalizedHighlight, out _, out var normalizedDrivePath))
                {
                    termsList.Add(normalizedDrivePath);
                }
                else
                {
                    termsList.Add(normalizedHighlight);
                }
            }
            else
            {
                var rawTerms = normalizedHighlight.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                foreach (var rawTerm in rawTerms)
                {
                    if (rawTerm.Length >= 2 && char.IsLetter(rawTerm[0]) && rawTerm[1] == Path.VolumeSeparatorChar)
                    {
                        targetDrive = rawTerm[0].ToString();
                    }
                    else
                    {
                        termsList.Add(rawTerm);
                    }
                }

                if (targetDrive != null)
                {
                    termsList.Add(targetDrive + Path.VolumeSeparatorChar);
                }
            }

            var terms = termsList.ToArray();

            // Build a list of highlight ranges
            highlights = new bool[fullText.Length];
            var fullTextLower = fullText.ToLowerInvariant();

            foreach (var term in terms)
            {
                var termLower = term;
                var foundAny = false;
                var startIdx = 0;
                while (startIdx < fullTextLower.Length)
                {
                    var foundIdx = fullTextLower.IndexOf(termLower, startIdx, StringComparison.Ordinal);
                    if (foundIdx < 0) break;

                    for (var i = foundIdx; i < foundIdx + termLower.Length && i < highlights.Length; i++)
                        highlights[i] = true;

                    foundAny = true;
                    startIdx = foundIdx + 1;
                }

                if (!foundAny)
                {
                    FuzzyHighlightMatcher.MarkFuzzyMatch(fullTextLower, termLower, highlights);
                }
            }
        }

        // Generate Runs from highlight map
        var pos = 0;
        while (pos < fullText.Length)
        {
            var isHighlighted = highlights[pos];
            var end = pos;
            while (end < fullText.Length && highlights[end] == isHighlighted)
                end++;

            var segment = fullText.Substring(pos, end - pos);
            var run = new Run(segment);
            if (isHighlighted)
            {
                run.Foreground = highlightBrush;
                run.FontWeight = FontWeights.SemiBold;
            }

            textBlock.Inlines.Add(run);
            pos = end;
        }
    }

    private static bool ContainsPathSeparator(string text) => text.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
               (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar &&
                text.IndexOf(Path.AltDirectorySeparatorChar) >= 0);

    private static string NormalizePathSeparators(string text) => Path.AltDirectorySeparatorChar == Path.DirectorySeparatorChar
            ? text
            : text.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static bool TryNormalizeDrivePath(string path, out string? drive, out string normalizedPath)
    {
        drive = null;
        normalizedPath = path;

        if (path.Length < 2 || !char.IsLetter(path[0]))
            return false;

        if (path[1] == Path.VolumeSeparatorChar)
        {
            drive = path[0].ToString();
            normalizedPath = drive + Path.VolumeSeparatorChar + path.Substring(2);
            return true;
        }

        if (path[1] == Path.DirectorySeparatorChar)
        {
            drive = path[0].ToString();
            normalizedPath = drive + Path.VolumeSeparatorChar + Path.DirectorySeparatorChar + path.Substring(2).TrimStart(Path.DirectorySeparatorChar);
            return true;
        }

        return false;
    }
}

public class SplitColumnsConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string str)
        {
            return str.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
        return Array.Empty<string>();
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}

public static class ScrollViewerHelper
{
    public static readonly DependencyProperty ShiftWheelScrollsHorizontallyProperty =
        DependencyProperty.RegisterAttached("ShiftWheelScrollsHorizontally", typeof(bool), typeof(ScrollViewerHelper),
            new PropertyMetadata(false, OnShiftWheelScrollsHorizontallyChanged));

    public static bool GetShiftWheelScrollsHorizontally(DependencyObject obj) => (bool)obj.GetValue(ShiftWheelScrollsHorizontallyProperty);
    public static void SetShiftWheelScrollsHorizontally(DependencyObject obj, bool value) => obj.SetValue(ShiftWheelScrollsHorizontallyProperty, value);

    private static void OnShiftWheelScrollsHorizontallyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer) return;

        scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        if ((bool)e.NewValue)
        {
            scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            if (e.Delta > 0)
            {
                scrollViewer.LineLeft();
            }
            else
            {
                scrollViewer.LineRight();
            }
            e.Handled = true;
        }
    }
}
