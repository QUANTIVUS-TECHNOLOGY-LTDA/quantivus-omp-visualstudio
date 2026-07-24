using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VSAgent.Services.Omp;
using VSAgent.Ui;

namespace VSAgent.Views
{
    /// <summary>
    /// Compact, expandable card that renders an ACP tool call inside the
    /// chat transcript. Defaults to collapsed; user can expand to see input
    /// (rendered as JSON) and output (rendered as Markdown-ish block).
    /// </summary>
    public class ToolCallCard : Expander
    {
        public AcpToolCall Call { get; set; }
        private TextBlock statusBadge;
        private TextBlock titleText;
        private TextBlock metaText;
        private TextBox inputView;
        private FlowDocumentScrollViewer outputView;

        public ToolCallCard(AcpToolCall call)
        {
            Call = call;
            Margin = new Thickness(0, 4, 0, 4);
            Padding = new Thickness(0);
            BorderThickness = new Thickness(1);
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x50));
            Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x22));
            Foreground = Brushes.White;
            IsExpanded = false;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;

            BuildHeader();
            BuildBody();
        }

        private void BuildHeader()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new Ellipse
            {
                Width = 8,
                Height = 8,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(6, 0, 2, 0),
                Fill = StatusBrush()
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            titleText = new TextBlock
            {
                Text = string.IsNullOrEmpty(Call.Name) ? Call.Preview : (Call.Name + " · " + Call.Preview),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleText, 1);
            grid.Children.Add(titleText);

            statusBadge = new TextBlock
            {
                Text = Call.Status ?? "running",
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(6, 0, 6, 0),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = StatusBrush(),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(statusBadge, 2);
            grid.Children.Add(statusBadge);

            var kind = new TextBlock
            {
                Text = (Call.Kind ?? "other").ToUpperInvariant(),
                FontSize = 10,
                Padding = new Thickness(4, 1, 4, 1),
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x3A)),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(kind, 3);
            grid.Children.Add(kind);

            Header = grid;
        }

        private void BuildBody()
        {
            var stack = new StackPanel { Margin = new Thickness(8, 4, 8, 8) };

            if (!string.IsNullOrEmpty(Call.InputJson))
            {
                var inputLabel = new TextBlock
                {
                    Text = "Input",
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 2),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
                };
                stack.Children.Add(inputLabel);

                inputView = new TextBox
                {
                    Text = Call.InputJson,
                    IsReadOnly = true,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x45)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 4, 6, 4),
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 120,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                stack.Children.Add(inputView);
            }

            var outputLabel = new TextBlock
            {
                Text = "Output",
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 2),
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
            };
            stack.Children.Add(outputLabel);

            outputView = new FlowDocumentScrollViewer
            {
                Document = Markdown.Parse(string.IsNullOrEmpty(Call.Output) ? "_(no output yet)_" : Call.Output),
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x45)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 6, 4),
                MaxHeight = 240,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            stack.Children.Add(outputView);

            Content = stack;
        }

        private Brush StatusBrush()
        {
            var s = (Call.Status ?? "running").ToLowerInvariant();
            if (s.Contains("complete") || s.Contains("success")) return new SolidColorBrush(Color.FromRgb(0x2E, 0x9F, 0x4D));
            if (s.Contains("fail") || s.Contains("error")) return new SolidColorBrush(Color.FromRgb(0xD9, 0x36, 0x36));
            return new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));
        }
    }
}
