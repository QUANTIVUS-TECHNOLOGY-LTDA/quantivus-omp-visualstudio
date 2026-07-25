using Microsoft.VisualStudio.Shell;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace VSAgent.Ui
{
    internal static class WorkbenchUi
    {
        public static Border Card(UIElement child, Thickness? margin = null, Thickness? padding = null)
        {
            var card = new Border
            {
                Child = child,
                Margin = margin ?? new Thickness(0, 0, 0, 10),
                Padding = padding ?? new Thickness(12),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                SnapsToDevicePixels = true
            };
            card.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            card.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            return card;
        }

        public static TextBlock Title(string text, double size = 18)
        {
            var value = new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 3)
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            return value;
        }

        public static TextBlock Subtitle(string text)
        {
            var value = new TextBlock
            {
                Text = text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            return value;
        }

        public static TextBlock Label(string text)
        {
            var value = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 3)
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            return value;
        }

        public static Button Button(string text, RoutedEventHandler click = null, bool accent = false, string toolTip = null)
        {
            var value = new Button
            {
                Content = text,
                Style = accent ? StyleFactory.AccentButtonStyle() : StyleFactory.ButtonStyle(),
                ToolTip = toolTip
            };
            if (click != null) value.Click += click;
            return value;
        }

        public static TextBox TextBox(string text = null, bool multiLine = false)
        {
            return new TextBox
            {
                Text = text ?? string.Empty,
                Style = StyleFactory.TextBoxStyle(),
                AcceptsReturn = multiLine,
                TextWrapping = multiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
                VerticalScrollBarVisibility = multiLine ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = multiLine ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled
            };
        }

        public static CheckBox CheckBox(string text, bool isChecked = false)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Style = StyleFactory.CheckBoxStyle()
            };
        }

        public static ComboBox ComboBox() => new ComboBox { Style = StyleFactory.ComboBoxStyle() };

        public static ListBox ListBox(SelectionMode selectionMode = SelectionMode.Single)
        {
            var value = new ListBox
            {
                Style = StyleFactory.ListBoxStyle(),
                ItemContainerStyle = StyleFactory.ListBoxItemStyle(),
                SelectionMode = selectionMode
            };
            ScrollViewer.SetVerticalScrollBarVisibility(value, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(value, ScrollBarVisibility.Auto);
            VirtualizingStackPanel.SetIsVirtualizing(value, true);
            VirtualizingStackPanel.SetVirtualizationMode(value, VirtualizationMode.Recycling);
            return value;
        }

        public static Border Badge(string text)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            var border = new Border
            {
                Child = label,
                Padding = new Thickness(7, 2, 7, 2),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 6, 0)
            };
            border.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            border.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            return border;
        }

        public static Grid PageHeader(string title, string subtitle, UIElement actions = null)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(Title(title));
            text.Children.Add(Subtitle(subtitle));
            grid.Children.Add(text);
            if (actions != null)
            {
                Grid.SetColumn(actions, 1);
                if (actions is FrameworkElement element) element.VerticalAlignment = VerticalAlignment.Top;
                grid.Children.Add(actions);
            }
            return grid;
        }

        public static ScrollViewer PageScroll(UIElement child)
        {
            return new ScrollViewer
            {
                Content = child,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(14)
            };
        }

        public static void ApplyToolWindowTheme(Control control)
        {
            control.SetResourceReference(Control.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            control.SetResourceReference(Control.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            control.SetResourceReference(Control.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
        }

        public static string Truncate(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maximum) return value ?? string.Empty;
            return value.Substring(0, Math.Max(0, maximum - 1)) + "…";
        }
    }
}
