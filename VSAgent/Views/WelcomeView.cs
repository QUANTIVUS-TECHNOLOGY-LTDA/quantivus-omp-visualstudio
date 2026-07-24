using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;

namespace VSAgent.Views
{
    /// <summary>
    /// Welcome screen. Big gradient π + Q wordmark, then the brand tagline,
    /// status and hint. Replaced by the agent response on the first prompt.
    /// </summary>
    public class WelcomeView : UserControl
    {
        private readonly TextBlock piSymbol;
        private readonly TextBlock plusText;
        private readonly TextBlock qSymbol;
        private readonly TextBlock tagline;
        private readonly TextBlock statusText;
        private readonly TextBlock hintText;
        private readonly DispatcherTimer revealTimer;
        private int revealStep;

        public event EventHandler AnimationComplete;

        public WelcomeView()
        {
            IsHitTestVisible = false;

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.VerticalAlignment = VerticalAlignment.Center;

            var formula = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 18)
            };

            piSymbol = MakeBigSymbol("\u03C0", 96);
            plusText = MakeOperator("+", 56);
            qSymbol = MakeBigSymbol("Q", 96, useGradient: true);

            formula.Children.Add(piSymbol);
            formula.Children.Add(plusText);
            tagline = new TextBlock
            {
                Text = "\u03C0  +  Quantivus for Visual Studio",
                FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),

                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0,
                Margin = new Thickness(0, 0, 0, 4)
            };
            tagline.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);

            statusText = new TextBlock
            {
                Text = "Initializing oh-my-pi...",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0,
                Margin = new Thickness(0, 24, 0, 2)
            };
            statusText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);

            hintText = new TextBlock
            {
                Text = "Type a prompt and press Ctrl+Enter to send.  Esc cancels.  / for commands.",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0
            };
            hintText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);

            Grid.SetRow(formula, 0);
            Grid.SetRow(tagline, 1);
            Grid.SetRow(statusText, 2);
            Grid.SetRow(hintText, 3);

            root.Children.Add(formula);
            root.Children.Add(tagline);
            root.Children.Add(statusText);
            root.Children.Add(hintText);

            Content = root;

            revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(85) };
            revealTimer.Tick += RevealTick;
        }

        public void SetStatus(string text)
        {
            statusText.Text = text;
        }

        public void Start()
        {
            revealStep = 0;
            revealTimer.Start();
        }

        public void Skip()
        {
            revealTimer.Stop();
            piSymbol.Opacity = 1;
            plusText.Opacity = 1;
            qSymbol.Opacity = 1;
            tagline.Opacity = 1;
            statusText.Opacity = 1;
            hintText.Opacity = 1;
            AnimationComplete?.Invoke(this, EventArgs.Empty);
        }

        private void RevealTick(object sender, EventArgs e)
        {
            revealStep++;
            switch (revealStep)
            {
                case 1: FadeIn(piSymbol, 0); break;
                case 2: FadeIn(plusText, 80); break;
                case 3: FadeIn(qSymbol, 80); break;
                case 4: FadeIn(tagline, 240); break;
                case 5: FadeIn(statusText, 360); break;
                case 6: FadeIn(hintText, 480);
                    revealTimer.Stop();
                    AnimationComplete?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        private TextBlock MakeBigSymbol(string text, double size, bool useGradient = false)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Cascadia Mono, Segoe UI, Consolas, Arial"),
                FontSize = size,
                FontWeight = FontWeights.Light,
                Margin = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0
            };
            if (useGradient) tb.Foreground = MakeGradientBrush();
            else tb.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            return tb;
        }

        private TextBlock MakeOperator(string text, double size)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Segoe UI Light, Segoe UI"),
                FontSize = size,
                FontWeight = FontWeights.Light,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            return tb;
        }

        private static Brush MakeGradientBrush()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x6F, 0xC5), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xC8, 0x6A, 0xF0), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x4F, 0xC3, 0xF7), 1.0));
            brush.Freeze();
            return brush;
        }

        private static void FadeIn(UIElement element, int delayMs)
        {
            if (delayMs > 0)
            {
                var waitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
                waitTimer.Tick += (s, args) =>
                {
                    waitTimer.Stop();
                    BeginFade(element);
                };
                waitTimer.Start();
            }
            else
            {
                BeginFade(element);
            }
        }

        private static void BeginFade(UIElement element)
        {
            var fade = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(280)
            };
            element.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
