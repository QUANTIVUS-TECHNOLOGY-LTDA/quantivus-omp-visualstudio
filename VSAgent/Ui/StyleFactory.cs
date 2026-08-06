using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace VSAgent.Ui
{
    /// <summary>
    /// Creates reusable controls that follow the active Visual Studio theme.
    /// No light/dark colors are hard-coded: DynamicResource references update
    /// automatically when Visual Studio changes theme or Windows enters
    /// high-contrast mode.
    /// </summary>
    public static class StyleFactory
    {
        public static Style ButtonStyle()
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 4, 4, 4)));
            style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 72.0));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 5, 12, 5)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(VsTheme.BorderKey)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Resource(VsTheme.BackgroundKey)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Resource(VsTheme.ForegroundKey)));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));
            style.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, true));

            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Border";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            border.AppendChild(content);
            template.VisualTree = border;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, Resource(VsTheme.AccentPaleKey), "Border"));
            hover.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(VsTheme.AccentMediumKey), "Border"));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Control.BackgroundProperty, Resource(VsTheme.AccentMediumKey), "Border"));
            pressed.Setters.Add(new Setter(Control.ForegroundProperty, Resource(SystemColors.HighlightTextBrushKey)));
            template.Triggers.Add(pressed);

            var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(SystemColors.HighlightBrushKey), "Border"));
            focused.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2), "Border"));
            template.Triggers.Add(focused);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Control.ForegroundProperty, Resource(VsTheme.SubtleKey)));
            disabled.Setters.Add(new Setter(Control.OpacityProperty, 0.72));
            template.Triggers.Add(disabled);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        public static Style AccentButtonStyle()
        {
            var style = ButtonStyle();
            style.Setters.Add(new Setter(Control.BackgroundProperty, Resource(VsTheme.AccentMediumKey)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Resource(SystemColors.HighlightTextBrushKey)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(VsTheme.AccentDarkKey)));
            return style;
        }

        public static Style TextBoxStyle()
        {
            var style = new Style(typeof(TextBox));
            AddInputSetters(style);
            style.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, Resource(VsTheme.ForegroundKey)));
            style.Setters.Add(new Setter(TextBox.SelectionBrushProperty, Resource(SystemColors.HighlightBrushKey)));
            style.Setters.Add(new Setter(TextBox.SelectionTextBrushProperty, Resource(SystemColors.HighlightTextBrushKey)));

            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(SystemColors.HighlightBrushKey)));
            focus.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
            style.Triggers.Add(focus);

            var readOnly = new Trigger { Property = TextBoxBase.IsReadOnlyProperty, Value = true };
            readOnly.Setters.Add(new Setter(Control.BackgroundProperty, Resource(VsTheme.BackgroundKey)));
            readOnly.Setters.Add(new Setter(Control.ForegroundProperty, Resource(VsTheme.ForegroundKey)));
            style.Triggers.Add(readOnly);
            return style;
        }

        public static Style PasswordBoxStyle()
        {
            var style = new Style(typeof(PasswordBox));
            AddInputSetters(style);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(SystemColors.HighlightBrushKey)));
            focus.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
            style.Triggers.Add(focus);
            return style;
        }

        public static Style ComboBoxStyle()
        {
            var style = new Style(typeof(ComboBox));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 4, 7, 4)));
            style.Setters.Add(new Setter(Control.MinHeightProperty, 30.0));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Resource(VsTheme.BackgroundKey)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Resource(VsTheme.ForegroundKey)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(VsTheme.BorderKey)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));
            style.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, true));

            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(SystemColors.HighlightBrushKey)));
            style.Triggers.Add(focus);
            return style;
        }

        public static Style CheckBoxStyle()
        {
            var style = new Style(typeof(CheckBox));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Resource(VsTheme.ForegroundKey)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 5)));
            style.Setters.Add(new Setter(Control.MinHeightProperty, 24.0));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            return style;
        }

        public static Style GroupBoxStyle()
        {
            var style = new Style(typeof(GroupBox));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
            style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 8)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(VsTheme.BorderKey)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Resource(VsTheme.ForegroundKey)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Resource(VsTheme.BackgroundKey)));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, true));
            return style;
        }

        public static Style ListBoxStyle()
        {
            var style = new Style(typeof(ListBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Resource(VsTheme.BackgroundKey)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Resource(VsTheme.ForegroundKey)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(VsTheme.BorderKey)));
            style.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));
            return style;
        }

        public static Style ListBoxItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 5, 7, 5)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Resource(VsTheme.ForegroundKey)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, Resource(SystemColors.HighlightBrushKey)));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Resource(SystemColors.HighlightTextBrushKey)));
            selected.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(SystemColors.HighlightBrushKey)));
            style.Triggers.Add(selected);

            var hover = new MultiTrigger();
            hover.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            hover.Conditions.Add(new Condition(ListBoxItem.IsSelectedProperty, false));
            hover.Setters.Add(new Setter(Control.BackgroundProperty, Resource(VsTheme.AccentPaleKey)));
            hover.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(VsTheme.AccentMediumKey)));
            style.Triggers.Add(hover);

            var focused = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focused.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(SystemColors.HighlightBrushKey)));
            style.Triggers.Add(focused);
            return style;
        }

        public static Style ExpanderStyle()
        {
            var style = new Style(typeof(Expander));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 4)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Resource(VsTheme.ForegroundKey)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Resource(VsTheme.BackgroundKey)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(VsTheme.BorderKey)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            return style;
        }

        private static void AddInputSetters(Style style)
        {
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 5, 7, 5)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.MinHeightProperty, 30.0));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Resource(VsTheme.BorderKey)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Resource(VsTheme.BackgroundKey)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Resource(VsTheme.ForegroundKey)));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));
            style.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, true));
        }

        private static DynamicResourceExtension Resource(object key) => new DynamicResourceExtension(key);
    }
}
