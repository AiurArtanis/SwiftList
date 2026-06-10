using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SwiftList.Tutorial.Helpers;

public static class MarkdownTextHelper
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.RegisterAttached(
            "Markdown",
            typeof(string),
            typeof(MarkdownTextHelper),
            new PropertyMetadata(null, OnMarkdownChanged));

    public static string GetMarkdown(DependencyObject obj) => (string)obj.GetValue(MarkdownProperty);

    public static void SetMarkdown(DependencyObject obj, string value) => obj.SetValue(MarkdownProperty, value);

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
        {
            textBlock.Inlines.Clear();
            var text = e.NewValue as string;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var i = 0;
            while (i < text.Length)
            {
                if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
                {
                    var end = text.IndexOf("**", i + 2);
                    if (end != -1)
                    {
                        var content = text.Substring(i + 2, end - (i + 2));
                        textBlock.Inlines.Add(new Bold(new Run(content)));
                        i = end + 2;
                    }
                    else
                    {
                        textBlock.Inlines.Add(new Run(text.Substring(i)));
                        break;
                    }
                }
                else if (text[i] == '`')
                {
                    var end = text.IndexOf('`', i + 1);
                    if (end != -1)
                    {
                        var content = text.Substring(i + 1, end - (i + 1));
                        var run = new Run(content)
                        {
                            FontFamily = new FontFamily("Consolas, Courier New"),
                            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4f46e5")),
                            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e1e7ff"))
                        };
                        textBlock.Inlines.Add(run);
                        i = end + 1;
                    }
                    else
                    {
                        textBlock.Inlines.Add(new Run(text.Substring(i)));
                        break;
                    }
                }
                else
                {
                    var nextBold = text.IndexOf("**", i);
                    var nextCode = text.IndexOf('`', i);
                    int nextSpecial;
                    
                    if (nextBold != -1 && nextCode != -1)
                        nextSpecial = Math.Min(nextBold, nextCode);
                    else if (nextBold != -1)
                        nextSpecial = nextBold;
                    else
                        nextSpecial = nextCode;

                    if (nextSpecial == -1)
                    {
                        textBlock.Inlines.Add(new Run(text.Substring(i)));
                        break;
                    }
                    else
                    {
                        textBlock.Inlines.Add(new Run(text.Substring(i, nextSpecial - i)));
                        i = nextSpecial;
                    }
                }
            }
        }
    }
}
