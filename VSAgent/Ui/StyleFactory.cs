using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace VSAgent.Ui
{
    /// <summary>
    /// Builds reusable Styles for Buttons, TextBoxes, ComboBoxes, CheckBoxes,
    /// GroupBoxes, and TabItems that look good in light + dark VS themes.
    /// </summary>
    public static class StyleFactory
    {
        public static Style ButtonStyle()
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 4, 4, 4)));
            s.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 72.0));
            s.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 28.0));
            s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 4, 12, 4)));
            s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60))));
            s.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30))));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            s.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            s.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
            s.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));

            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);

            border.AppendChild(content);
            template.VisualTree = border;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x44)), "Bd"));
            hover.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), "Bd"));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22)), "Bd"));
            template.Triggers.Add(pressed);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Control.OpacityProperty, 0.4));
            template.Triggers.Add(disabled);

            s.Setters.Add(new Setter(Control.TemplateProperty, template));
            return s;
        }

        public static Style AccentButtonStyle()
        {
            var s = ButtonStyle();
            s.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x00, 0x6B, 0xD9))));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x00, 0x55, 0xB3))));
            return s;
        }

        public static Style TextBoxStyle()
        {
            var s = new Style(typeof(TextBox));
            s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
            s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            s.Setters.Add(new Setter(Control.MinHeightProperty, 24.0));
            s.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, Brushes.White));
            s.Setters.Add(new Setter(TextBox.SelectionBrushProperty, new SolidColorBrush(Color.FromRgb(0x00, 0x6B, 0xD9))));
            s.Setters.Add(new Setter(TextBox.SelectionTextBrushProperty, Brushes.White));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60))));
            s.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x22))));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            s.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7))));
            s.Triggers.Add(focus);
            return s;
        }

        public static Style PasswordBoxStyle()
        {
            var s = new Style(typeof(PasswordBox));
            s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
            s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            s.Setters.Add(new Setter(Control.MinHeightProperty, 24.0));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60))));
            s.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x22))));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            return s;
        }

        public static Style ComboBoxStyle()
        {
            var s = new Style(typeof(ComboBox));
            s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
            s.Setters.Add(new Setter(Control.MinHeightProperty, 28.0));
            s.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x22))));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60))));
            s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            return s;
        }

        public static Style CheckBoxStyle()
        {
            var s = new Style(typeof(CheckBox));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 4)));
            s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            return s;
        }

        public static Style GroupBoxStyle()
        {
            var s = new Style(typeof(GroupBox));
            s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
            s.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 8)));
            s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x55))));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1A))));
            s.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            return s;
        }

        public static Style ListBoxStyle()
        {
            var s = new Style(typeof(ListBox));
            s.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x22))));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x55))));
            return s;
        }

        public static Style ListBoxItemStyle()
        {
            var s = new Style(typeof(ListBoxItem));
            s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            var sel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x00, 0x6B, 0xD9))));
            sel.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Triggers.Add(sel);
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x3A))));
            s.Triggers.Add(hover);
            return s;
        }

        public static Style ExpanderStyle()
        {
            var s = new Style(typeof(Expander));
            s.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 4)));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x55))));
            return s;
        }
    }
}
