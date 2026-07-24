using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;

namespace VSAgent.Ui
{
    /// <summary>
    /// Centralized brushes + resources. Wraps VS resource keys so views can use
    /// a single static helper instead of repeating SetResourceReference calls.
    /// </summary>
    public static class VsTheme
    {
        public static readonly object BackgroundKey = VsBrushes.ToolWindowBackgroundKey;
        public static readonly object ForegroundKey = VsBrushes.ToolWindowTextKey;
        public static readonly object BorderKey = VsBrushes.ToolWindowBorderKey;
        public static readonly object SubtleKey = VsBrushes.GrayTextKey;
        public static readonly object AccentKey = VsBrushes.AccentMediumKey;
        public static readonly object AccentDarkKey = VsBrushes.AccentDarkKey;
        public static readonly object AccentPaleKey = VsBrushes.AccentPaleKey;
        public static readonly object PanelKey = VsBrushes.PanelHyperlinkKey;

        public static Brush Brush(object key) => (Brush)Application.Current.FindResource(key);

        public static void Apply(Control c)
        {
            c.SetResourceReference(Control.BackgroundProperty, BackgroundKey);
            c.SetResourceReference(Control.ForegroundProperty, ForegroundKey);
            c.SetResourceReference(Control.BorderBrushProperty, BorderKey);
        }

        public static void ApplyText(TextBlock t)
        {
            t.SetResourceReference(TextBlock.ForegroundProperty, ForegroundKey);
        }

        public static void ApplySubtle(TextBlock t)
        {
            t.SetResourceReference(TextBlock.ForegroundProperty, SubtleKey);
        }
    }
}
