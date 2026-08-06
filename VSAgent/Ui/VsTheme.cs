using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;

namespace VSAgent.Ui
{
    /// <summary>
    /// Central Visual Studio theme resources and accessibility helpers.
    /// </summary>
    public static class VsTheme
    {
        public static readonly object BackgroundKey = VsBrushes.ToolWindowBackgroundKey;
        public static readonly object ForegroundKey = VsBrushes.ToolWindowTextKey;
        public static readonly object BorderKey = VsBrushes.ToolWindowBorderKey;
        public static readonly object SubtleKey = VsBrushes.GrayTextKey;
        public static readonly object AccentMediumKey = VsBrushes.AccentMediumKey;
        public static readonly object AccentDarkKey = VsBrushes.AccentDarkKey;
        public static readonly object AccentPaleKey = VsBrushes.AccentPaleKey;
        public static readonly object PanelKey = VsBrushes.PanelHyperlinkKey;

        public static bool IsHighContrast => SystemParameters.HighContrast;

        public static Brush Brush(object key, Brush fallback = null)
        {
            var value = Application.Current?.TryFindResource(key) as Brush;
            return value ?? fallback ?? Brushes.Transparent;
        }

        public static void Apply(Control control)
        {
            if (control == null) return;
            control.SetResourceReference(Control.BackgroundProperty, BackgroundKey);
            control.SetResourceReference(Control.ForegroundProperty, ForegroundKey);
            control.SetResourceReference(Control.BorderBrushProperty, BorderKey);
            control.SnapsToDevicePixels = true;
            control.UseLayoutRounding = true;
        }

        public static void ApplyText(TextBlock text)
        {
            if (text == null) return;
            text.SetResourceReference(TextBlock.ForegroundProperty, ForegroundKey);
            text.UseLayoutRounding = true;
        }

        public static void ApplySecondaryText(TextBlock text)
        {
            if (text == null) return;
            text.SetResourceReference(TextBlock.ForegroundProperty, ForegroundKey);
            text.Opacity = IsHighContrast ? 1.0 : 0.86;
            text.UseLayoutRounding = true;
        }

        public static void ApplySubtle(TextBlock text)
        {
            if (text == null) return;
            text.SetResourceReference(TextBlock.ForegroundProperty, IsHighContrast ? ForegroundKey : SubtleKey);
            text.UseLayoutRounding = true;
        }

        /// <summary>
        /// Computes WCAG relative contrast for solid brushes. Returns 1 when
        /// either brush cannot be evaluated.
        /// </summary>
        public static double ContrastRatio(Brush foreground, Brush background)
        {
            if (!(foreground is SolidColorBrush foregroundBrush) ||
                !(background is SolidColorBrush backgroundBrush))
            {
                return 1.0;
            }

            var first = RelativeLuminance(foregroundBrush.Color);
            var second = RelativeLuminance(backgroundBrush.Color);
            var lighter = Math.Max(first, second);
            var darker = Math.Min(first, second);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double RelativeLuminance(Color color)
        {
            var red = Linearize(color.R / 255.0);
            var green = Linearize(color.G / 255.0);
            var blue = Linearize(color.B / 255.0);
            return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
        }

        private static double Linearize(double component) =>
            component <= 0.03928
                ? component / 12.92
                : Math.Pow((component + 0.055) / 1.055, 2.4);
    }
}
