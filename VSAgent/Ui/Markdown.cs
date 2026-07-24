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
    /// Minimal markdown-to-FlowDocument converter. Supports:
    ///   - # / ## / ### headers
    ///   - fenced ``` code blocks
    ///   - **bold**, *italic*, `inline code`
    ///   - unordered lists (- or *), ordered lists (1. 2. ...)
    ///   - paragraph breaks
    /// No external dependency. Result is a FlowDocument that renders inside
    /// a FlowDocumentScrollViewer / RichTextBox.
    /// </summary>
    public static class Markdown
    {
        public static FlowDocument Parse(string text)
        {
            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                PagePadding = new Thickness(0),
                ColumnWidth = double.PositiveInfinity,
                Background = Brushes.Transparent,
                Foreground = Brushes.White
            };
            if (string.IsNullOrEmpty(text)) return doc;

            var lines = text.Replace("\r\n", "\n").Split('\n');
            int i = 0;
            while (i < lines.Length)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    i++;
                    continue;
                }

                // fenced code block
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    var lang = line.Substring(3).Trim();
                    var body = new List<string>();
                    i++;
                    while (i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal))
                    {
                        body.Add(lines[i]);
                        i++;
                    }
                    if (i < lines.Length) i++; // skip closing ```
                    doc.Blocks.Add(CodeBlock(string.Join("\n", body), lang));
                    continue;
                }

                // heading
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    int level = 0;
                    while (level < line.Length && line[level] == '#') level++;
                    if (level > 6) level = 6;
                    var content = line.Substring(level).Trim();
                    doc.Blocks.Add(Heading(level, content));
                    i++;
                    continue;
                }

                // unordered list
                if (line.StartsWith("- ", StringComparison.Ordinal) ||
                    line.StartsWith("* ", StringComparison.Ordinal))
                {
                    var list = new List();
                    while (i < lines.Length && (lines[i].StartsWith("- ", StringComparison.Ordinal) || lines[i].StartsWith("* ", StringComparison.Ordinal)))
                    {
                        var item = lines[i].Substring(2).Trim();
                        var p = new Paragraph(new Run(item)) { Margin = new Thickness(0) };
                        ReplaceInlines(p);
                        list.ListItems.Add(new ListItem(p));
                        i++;
                    }
                    doc.Blocks.Add(list);
                    continue;
                }

                // ordered list
                if (Regex.IsMatch(line, @"^\d+\.\s"))
                {
                    var list = new List { MarkerStyle = TextMarkerStyle.Decimal };
                    while (i < lines.Length && Regex.IsMatch(lines[i], @"^\d+\.\s"))
                    {
                        var item = Regex.Replace(lines[i], @"^\d+\.\s", "");
                        var p = new Paragraph(new Run(item)) { Margin = new Thickness(0) };
                        ReplaceInlines(p);
                        list.ListItems.Add(new ListItem(p));
                        i++;
                    }
                    doc.Blocks.Add(list);
                    continue;
                }

                // paragraph (consume until blank line)
                var para = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                para.Inlines.Add(new Run(line));
                i++;
                while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) &&
                       !lines[i].StartsWith("#", StringComparison.Ordinal) &&
                       !lines[i].StartsWith("```", StringComparison.Ordinal) &&
                       !lines[i].StartsWith("- ", StringComparison.Ordinal) &&
                       !lines[i].StartsWith("* ", StringComparison.Ordinal) &&
                       !Regex.IsMatch(lines[i], @"^\d+\.\s"))
                {
                    para.Inlines.Add(new Run(" " + lines[i]));
                    i++;
                }
                ReplaceInlines(para);
                doc.Blocks.Add(para);
            }
            return doc;
        }

        private static Paragraph Heading(int level, string text)
        {
            double size = level switch { 1 => 20, 2 => 17, _ => 14 };
            var p = new Paragraph(new Run(text))
            {
                FontWeight = FontWeights.Bold,
                FontSize = size,
                Margin = new Thickness(0, 8, 0, 4)
            };
            ReplaceInlines(p);
            return p;
        }

        private static Block CodeBlock(string code, string lang)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x45)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var tb = new TextBlock
            {
                Text = code,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF)),
                TextWrapping = TextWrapping.Wrap
            };
            border.Child = tb;
            var container = new BlockUIContainer(border) { Margin = new Thickness(0) };
            return container;
        }

        private static void ReplaceInlines(Paragraph p)
        {
            var text = p.Inlines.ToString();
            if (string.IsNullOrEmpty(text)) return;
            p.Inlines.Clear();
            int idx = 0;
            while (idx < text.Length)
            {
                // inline code `x`
                if (text[idx] == '`')
                {
                    int end = text.IndexOf('`', idx + 1);
                    if (end > idx)
                    {
                        var content = text.Substring(idx + 1, end - idx - 1);
                        p.Inlines.Add(InlineCode(content));
                        idx = end + 1;
                        continue;
                    }
                }
                // bold **x**
                if (idx + 1 < text.Length && text[idx] == '*' && text[idx + 1] == '*')
                {
                    int end = text.IndexOf("**", idx + 2, StringComparison.Ordinal);
                    if (end > idx + 1)
                    {
                        var content = text.Substring(idx + 2, end - idx - 2);
                        p.Inlines.Add(new Run(content) { FontWeight = FontWeights.Bold });
                        idx = end + 2;
                        continue;
                    }
                }
                // italic *x*
                if (text[idx] == '*')
                {
                    int end = text.IndexOf('*', idx + 1);
                    if (end > idx)
                    {
                        var content = text.Substring(idx + 1, end - idx - 1);
                        p.Inlines.Add(new Run(content) { FontStyle = FontStyles.Italic });
                        idx = end + 1;
                        continue;
                    }
                }
                // accumulate up to next marker
                int next = text.Length;
                for (int k = idx + 1; k < text.Length; k++)
                {
                    if (text[k] == '`' || text[k] == '*') { next = k; break; }
                }
                p.Inlines.Add(new Run(text.Substring(idx, next - idx)));
                idx = next;
            }
        }

        private static Inline InlineCode(string content)
        {
            var r = new Run(content)
            {
                FontFamily = new FontFamily("Consolas"),
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x7A))
            };
            return r;
        }
    }
}
