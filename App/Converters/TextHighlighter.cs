using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SwiftList.Core;
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

    // OnTextChanged as the callback here too: it re-reads Text/HighlightText/HighlightBrush fresh and
    // rebuilds the Runs regardless of which of the three actually changed, so a DynamicResource-driven
    // brush swap (theme change) re-renders existing results with the new color -- without this, a
    // window whose results are already visible when the theme changes keeps the highlighted matches
    // frozen at the old color until the next time the query itself changes.
    public static readonly DependencyProperty HighlightBrushProperty =
        DependencyProperty.RegisterAttached("HighlightBrush", typeof(Brush), typeof(TextHighlighter),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF)), OnTextChanged));

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
        if (textBlock.DataContext is PluginSdk.Abstractions.ISearchResult searchResult)
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
                Logger.Log($"[TextHighlighter] Custom highlighting error: {ex.Message}", LogLevel.Error);
            }
        }

        if (highlights == null)
        {
            // Path mode: the whole (drive-normalized) query is one literal-ish term that must NOT
            // be split on spaces (folder/file names can contain them) -- everything else goes
            // through Core's real FzfPattern-based term splitting, so display highlighting is
            // provably the same computation the ranking weight scores against (HighlightMask).
            var normalizedHighlight = NormalizePathSeparators(highlight.Trim()).ToLowerInvariant();

            if (ContainsPathSeparator(normalizedHighlight))
            {
                var term = TryNormalizeDrivePath(normalizedHighlight, out _, out var normalizedDrivePath)
                    ? normalizedDrivePath
                    : normalizedHighlight;

                // Mirrors Core's real path-mode split (PathSearchFuzzy.SearchStreaming): everything
                // after the LAST separator is the file-part query (its own multi-term match against a
                // name), everything before is the directory-part query (matched against ancestor
                // segments). Treating the whole term -- separators and all -- as one literal string
                // almost never matched anything (e.g. "soft \ rename fz" has no literal "\" inside any
                // real file/folder name), so a real path-mode match ranked correctly but highlighted
                // nothing at all. Both parts are tried against whatever text this call is for (Name or
                // Path column) and unioned -- the file part naturally lights up the Name column, the
                // directory part the Path column.
                var lastSep = term.LastIndexOf(Path.DirectorySeparatorChar);
                var dirPart = lastSep >= 0 ? term[..lastSep].Trim() : string.Empty;
                var filePart = (lastSep >= 0 ? term[(lastSep + 1)..] : term).Trim();

                highlights = new bool[fullText.Length];
                if (!string.IsNullOrEmpty(filePart))
                    OrInto(highlights, FuzzyMatcher.ComputeHighlightMask(fullText, filePart));
                if (!string.IsNullOrEmpty(dirPart))
                    OrInto(highlights, FuzzyMatcher.ComputeHighlightMask(fullText, dirPart));
            }
            else
            {
                highlights = FuzzyMatcher.ComputeHighlightMask(fullText, normalizedHighlight);
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
            }

            textBlock.Inlines.Add(run);
            pos = end;
        }
    }

    private static void OrInto(bool[] target, bool[] source)
    {
        for (var i = 0; i < target.Length && i < source.Length; i++)
        {
            if (source[i])
                target[i] = true;
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
