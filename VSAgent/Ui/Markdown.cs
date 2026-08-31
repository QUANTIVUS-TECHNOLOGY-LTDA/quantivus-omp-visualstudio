using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace VSAgent.Ui
{
    /// <summary>
    /// Minimal markdown-to-FlowDocument converter with Visual Studio
    /// theme-aware text and code surfaces.
    /// </summary>
    public static class Markdown
    {
        public static FlowDocument Parse(string text)
        {
            var document = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                PagePadding = new Thickness(0),
                ColumnWidth = double.PositiveInfinity,
                Background = Brushes.Transparent
            };
            document.SetResourceReference(FlowDocument.ForegroundProperty, VsTheme.ForegroundKey);
            if (string.IsNullOrEmpty(text)) return document;

            var lines = text.Replace("\r\n", "\n").Split('\n');
            var index = 0;
            while (index < lines.Length)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                {
                    index++;
                    continue;
                }

                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    var language = line.Substring(3).Trim();
                    var body = new List<string>();
                    index++;
                    while (index < lines.Length && !lines[index].StartsWith("```", StringComparison.Ordinal))
                    {
                        body.Add(lines[index]);
                        index++;
                    }

                    if (index < lines.Length) index++;
                    document.Blocks.Add(CodeBlock(string.Join("\n", body), language));
                    continue;
                }

                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    var level = 0;
                    while (level < line.Length && line[level] == '#') level++;
                    if (level > 6) level = 6;
                    document.Blocks.Add(Heading(level, line.Substring(level).Trim()));
                    index++;
                    continue;
                }

                if (line.StartsWith("- ", StringComparison.Ordinal) ||
                    line.StartsWith("* ", StringComparison.Ordinal))
                {
                    var list = new List();
                    while (index < lines.Length &&
                           (lines[index].StartsWith("- ", StringComparison.Ordinal) ||
                            lines[index].StartsWith("* ", StringComparison.Ordinal)))
                    {
                        var paragraph = new Paragraph(new Run(lines[index].Substring(2).Trim()))
                        {
                            Margin = new Thickness(0)
                        };
                        ReplaceInlines(paragraph);
                        list.ListItems.Add(new ListItem(paragraph));
                        index++;
                    }

                    document.Blocks.Add(list);
                    continue;
                }

                if (Regex.IsMatch(line, @"^\d+\.\s"))
                {
                    var list = new List { MarkerStyle = TextMarkerStyle.Decimal };
                    while (index < lines.Length && Regex.IsMatch(lines[index], @"^\d+\.\s"))
                    {
                        var paragraph = new Paragraph(new Run(Regex.Replace(lines[index], @"^\d+\.\s", string.Empty)))
                        {
                            Margin = new Thickness(0)
                        };
                        ReplaceInlines(paragraph);
                        list.ListItems.Add(new ListItem(paragraph));
                        index++;
                    }

                    document.Blocks.Add(list);
                    continue;
                }

                var textParagraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                textParagraph.Inlines.Add(new Run(line));
                index++;
                while (index < lines.Length &&
                       !string.IsNullOrWhiteSpace(lines[index]) &&
                       !lines[index].StartsWith("#", StringComparison.Ordinal) &&
                       !lines[index].StartsWith("```", StringComparison.Ordinal) &&
                       !lines[index].StartsWith("- ", StringComparison.Ordinal) &&
                       !lines[index].StartsWith("* ", StringComparison.Ordinal) &&
                       !Regex.IsMatch(lines[index], @"^\d+\.\s"))
                {
                    textParagraph.Inlines.Add(new Run(" " + lines[index]));
                    index++;
                }

                ReplaceInlines(textParagraph);
                document.Blocks.Add(textParagraph);
            }

            return document;
        }

        private static Paragraph Heading(int level, string text)
        {
            var size = level switch { 1 => 20, 2 => 17, _ => 14 };
            var paragraph = new Paragraph(new Run(text))
            {
                FontWeight = FontWeights.Bold,
                FontSize = size,
                Margin = new Thickness(0, 8, 0, 4)
            };
            ReplaceInlines(paragraph);
            return paragraph;
        }

        private static Block CodeBlock(string code, string language)
        {
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 8),
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                ToolTip = string.IsNullOrWhiteSpace(language) ? null : language
            };
            border.SetResourceReference(Border.BackgroundProperty, VsTheme.BackgroundKey);
            border.SetResourceReference(Border.BorderBrushProperty, VsTheme.BorderKey);

            var text = new TextBlock
            {
                Text = code,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, VsTheme.ForegroundKey);
            border.Child = text;
            return new BlockUIContainer(border) { Margin = new Thickness(0) };
        }

        private static void ReplaceInlines(Paragraph paragraph)
        {
            var text = paragraph.Inlines.ToString();
            if (string.IsNullOrEmpty(text)) return;

            paragraph.Inlines.Clear();
            var index = 0;
            while (index < text.Length)
            {
                if (text[index] == '`')
                {
                    var end = text.IndexOf('`', index + 1);
                    if (end > index)
                    {
                        paragraph.Inlines.Add(InlineCode(text.Substring(index + 1, end - index - 1)));
                        index = end + 1;
                        continue;
                    }
                }

                if (index + 1 < text.Length && text[index] == '*' && text[index + 1] == '*')
                {
                    var end = text.IndexOf("**", index + 2, StringComparison.Ordinal);
                    if (end > index + 1)
                    {
                        paragraph.Inlines.Add(new Run(text.Substring(index + 2, end - index - 2))
                        {
                            FontWeight = FontWeights.Bold
                        });
                        index = end + 2;
                        continue;
                    }
                }

                if (text[index] == '*')
                {
                    var end = text.IndexOf('*', index + 1);
                    if (end > index)
                    {
                        paragraph.Inlines.Add(new Run(text.Substring(index + 1, end - index - 1))
                        {
                            FontStyle = FontStyles.Italic
                        });
                        index = end + 1;
                        continue;
                    }
                }

                var next = text.Length;
                for (var candidate = index + 1; candidate < text.Length; candidate++)
                {
                    if (text[candidate] == '`' || text[candidate] == '*')
                    {
                        next = candidate;
                        break;
                    }
                }

                paragraph.Inlines.Add(new Run(text.Substring(index, next - index)));
                index = next;
            }
        }

        private static Inline InlineCode(string content)
        {
            var run = new Run(content)
            {
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.SemiBold
            };
            run.SetResourceReference(TextElement.BackgroundProperty, VsTheme.AccentPaleKey);
            run.SetResourceReference(TextElement.ForegroundProperty, VsTheme.ForegroundKey);
            return run;
        }
    }
}
