using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.VisualStudio.Shell;
using VSAgent.Models;

namespace VSAgent.Views
{
    public class CommandCompletionPopup
    {
        private readonly Popup popup;
        private readonly ListBox listBox;
        private readonly List<SlashCommand> allCommands;
        private readonly ObservableCollection<SlashCommand> filtered;

        public bool IsOpen => popup.IsOpen;
        public SlashCommand SelectedCommand => listBox.SelectedItem as SlashCommand;

        public CommandCompletionPopup(UIElement placementTarget, IEnumerable<SlashCommand> commands)
        {
            allCommands = commands.ToList();
            filtered = new ObservableCollection<SlashCommand>();

            listBox = new ListBox
            {
                ItemsSource = filtered,
                MinWidth = 360,
                MaxHeight = 220,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Focusable = false,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            listBox.SetResourceReference(ListBox.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            listBox.SetResourceReference(ListBox.ForegroundProperty, VsBrushes.ToolWindowTextKey);

            var template = new DataTemplate(typeof(SlashCommand));
            var stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            stack.SetValue(StackPanel.MarginProperty, new Thickness(0));

            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            name.SetValue(TextBlock.WidthProperty, 110.0);
            name.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            stack.AppendChild(name);

            var desc = new FrameworkElementFactory(typeof(TextBlock));
            desc.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            desc.SetBinding(TextBlock.TextProperty, new Binding("Description"));
            stack.AppendChild(desc);

            template.VisualTree = stack;
            listBox.ItemTemplate = template;

            // Mouse selection: double-click or single-click via MouseLeftButtonUp
            listBox.MouseLeftButtonUp += (_, __) => Commit();

            var outerBorder = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 8,
                    ShadowDepth = 2,
                    Opacity = 0.35,
                    Color = Colors.Black
                },
                Child = listBox
            };
            outerBorder.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            outerBorder.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);

            popup = new Popup
            {
                PlacementTarget = placementTarget,
                Placement = PlacementMode.Bottom,
                PlacementRectangle = new Rect(0, 0, 0, 0),
                HorizontalOffset = 0,
                VerticalOffset = 0,
                StaysOpen = false,
                AllowsTransparency = false,
                Focusable = false,
                IsOpen = false,
                Child = outerBorder
            };
        }

        public void Show(string filter)
        {
            filtered.Clear();
            foreach (var cmd in allCommands)
            {
                if (string.IsNullOrEmpty(filter) || cmd.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
                    filtered.Add(cmd);
            }
            if (filtered.Count == 0)
            {
                popup.IsOpen = false;
                return;
            }
            listBox.SelectedIndex = 0;
            if (!popup.IsOpen) popup.IsOpen = true;
        }

        public void Hide() => popup.IsOpen = false;

        public void SelectNext()
        {
            if (listBox.SelectedIndex < filtered.Count - 1) listBox.SelectedIndex++;
        }

        public void SelectPrevious()
        {
            if (listBox.SelectedIndex > 0) listBox.SelectedIndex--;
        }

        public event Action<SlashCommand> Committed;

        public void Commit()
        {
            if (SelectedCommand == null) return;
            var cmd = SelectedCommand;
            popup.IsOpen = false;
            Committed?.Invoke(cmd);
        }
    }
}
