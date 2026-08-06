using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using VSAgent.Services.Omp;
using VSAgent.Ui;

namespace VSAgent.Views
{
    /// <summary>
    /// Compact theme-aware ACP tool-call card. Colored status indicators are
    /// decorative only; all state is also represented as text.
    /// </summary>
    public class ToolCallCard : Expander
    {
        public AcpToolCall Call { get; set; }

        private TextBlock statusBadge;
        private TextBlock titleText;
        private TextBox inputView;
        private FlowDocumentScrollViewer outputView;
        private Ellipse statusIndicator;

        public ToolCallCard(AcpToolCall call)
        {
            Call = call ?? throw new ArgumentNullException(nameof(call));
            Margin = new Thickness(0, 4, 0, 4);
            Padding = new Thickness(0);
            BorderThickness = new Thickness(1);
            IsExpanded = false;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Style = StyleFactory.ExpanderStyle();

            AutomationProperties.SetName(this, "Tool call " + (Call.Name ?? Call.Preview ?? string.Empty));
            BuildHeader();
            BuildBody();
        }

        private void BuildHeader()
        {
            var grid = new Grid
            {
                Margin = new Thickness(2),
                UseLayoutRounding = true
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            statusIndicator = new Ellipse
            {
                Width = 8,
                Height = 8,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(6, 0, 2, 0),
                Fill = StatusBrush()
            };
            AutomationProperties.SetName(statusIndicator, Call.Status ?? "running");
            Grid.SetColumn(statusIndicator, 0);
            grid.Children.Add(statusIndicator);

            titleText = new TextBlock
            {
                Text = string.IsNullOrEmpty(Call.Name) ? Call.Preview : (Call.Name + " · " + Call.Preview),
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 4, 4, 4)
            };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, VsTheme.ForegroundKey);
            Grid.SetColumn(titleText, 1);
            grid.Children.Add(titleText);

            statusBadge = new TextBlock
            {
                Text = Call.Status ?? "running",
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(6, 0, 6, 0),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            statusBadge.SetResourceReference(TextBlock.ForegroundProperty, VsTheme.ForegroundKey);
            statusBadge.SetResourceReference(TextBlock.BackgroundProperty, VsTheme.AccentPaleKey);
            AutomationProperties.SetName(statusBadge, "Status " + statusBadge.Text);
            Grid.SetColumn(statusBadge, 2);
            grid.Children.Add(statusBadge);

            var kind = new TextBlock
            {
                Text = (Call.Kind ?? "other").ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(5, 2, 5, 2),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            kind.SetResourceReference(TextBlock.ForegroundProperty, VsTheme.ForegroundKey);
            kind.SetResourceReference(TextBlock.BackgroundProperty, VsTheme.BackgroundKey);
            Grid.SetColumn(kind, 3);
            grid.Children.Add(kind);

            Header = grid;
        }

        private void BuildBody()
        {
            var stack = new StackPanel { Margin = new Thickness(8, 4, 8, 8) };

            if (!string.IsNullOrEmpty(Call.InputJson))
            {
                var inputLabel = WorkbenchUi.Label("Input");
                inputLabel.FontSize = 11;
                inputLabel.Margin = new Thickness(0, 4, 0, 2);
                stack.Children.Add(inputLabel);

                inputView = new TextBox
                {
                    Text = Call.InputJson,
                    IsReadOnly = true,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Style = StyleFactory.TextBoxStyle(),
                    Padding = new Thickness(7, 5, 7, 5),
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 140,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                AutomationProperties.SetName(inputView, "Tool input");
                stack.Children.Add(inputView);
            }

            var outputLabel = WorkbenchUi.Label("Output");
            outputLabel.FontSize = 11;
            outputLabel.Margin = new Thickness(0, 7, 0, 2);
            stack.Children.Add(outputLabel);

            outputView = new FlowDocumentScrollViewer
            {
                Document = Markdown.Parse(string.IsNullOrEmpty(Call.Output) ? "_(no output yet)_" : Call.Output),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 5, 7, 5),
                MaxHeight = 260,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                IsToolBarVisible = false,
                UseLayoutRounding = true
            };
            outputView.SetResourceReference(FlowDocumentScrollViewer.BackgroundProperty, VsTheme.BackgroundKey);
            outputView.SetResourceReference(FlowDocumentScrollViewer.ForegroundProperty, VsTheme.ForegroundKey);
            outputView.SetResourceReference(FlowDocumentScrollViewer.BorderBrushProperty, VsTheme.BorderKey);
            AutomationProperties.SetName(outputView, "Tool output");
            stack.Children.Add(outputView);

            Content = stack;
        }

        private Brush StatusBrush()
        {
            if (VsTheme.IsHighContrast) return SystemColors.HighlightBrush;

            var status = (Call.Status ?? "running").ToLowerInvariant();
            if (status.Contains("complete") || status.Contains("success"))
                return new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x57));
            if (status.Contains("fail") || status.Contains("error"))
                return new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
            return VsTheme.Brush(VsTheme.AccentMediumKey, SystemColors.HighlightBrush);
        }
    }
}
