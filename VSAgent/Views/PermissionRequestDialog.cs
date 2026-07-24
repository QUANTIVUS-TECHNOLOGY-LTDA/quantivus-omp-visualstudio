using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using VSAgent.Models;

namespace VSAgent.Views
{
    public class PermissionRequestDialog : Window
    {
        public string SelectedOptionId { get; private set; }

        private readonly List<PermissionOption> options;
        private readonly StackPanel optionPanel;

        public PermissionRequestDialog(string summary, string description, List<PermissionOption> options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            this.options = options;

            Title = "Quantivus OMP - Permission Required";
            Width = 580;
            Height = 420;
            MinWidth = 420;
            MinHeight = 280;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResize;

            SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            SetResourceReference(ForegroundProperty, VsBrushes.ToolWindowTextKey);

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "oh-my-pi wants to perform an action in Visual Studio",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var summaryBlock = new TextBlock
            {
                Text = summary,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            summaryBlock.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            Grid.SetRow(summaryBlock, 1);
            root.Children.Add(summaryBlock);

            var descriptionBorder = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 12),
                MaxHeight = 180
            };
            descriptionBorder.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            descriptionBorder.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            var descBlock = new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
                FontSize = 12,
                Padding = new Thickness(8)
            };
            descBlock.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            var descScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = descBlock
            };
            descriptionBorder.Child = descScroller;
            Grid.SetRow(descriptionBorder, 2);
            root.Children.Add(descriptionBorder);

            var optionsHeader = new TextBlock
            {
                Text = "Choose how to respond:",
                Margin = new Thickness(0, 0, 0, 4),
                FontWeight = FontWeights.SemiBold
            };
            optionsHeader.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            Grid.SetRow(optionsHeader, 3);
            root.Children.Add(optionsHeader);

            optionPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var first = true;
            foreach (var opt in options)
            {
                var radio = new RadioButton
                {
                    Content = opt.Name,
                    Tag = opt,
                    Margin = new Thickness(0, 2, 0, 2),
                    IsChecked = first,
                    GroupName = "PermissionOptions"
                };
                radio.SetResourceReference(RadioButton.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                first = false;
                optionPanel.Children.Add(radio);
            }
            Grid.SetRow(optionPanel, 4);
            root.Children.Add(optionPanel);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var rejectButton = new Button
            {
                Content = "Reject",
                Padding = new Thickness(14, 4, 14, 4),
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 90,
                IsCancel = true
            };
            rejectButton.Click += (_, __) => { DialogResult = false; };
            buttonPanel.Children.Add(rejectButton);

            var allowButton = new Button
            {
                Content = "Allow",
                Padding = new Thickness(14, 4, 14, 4),
                MinWidth = 90,
                IsDefault = true
            };
            allowButton.Click += (_, __) =>
            {
                foreach (var child in optionPanel.Children)
                {
                    if (child is RadioButton rb && rb.IsChecked == true && rb.Tag is PermissionOption opt)
                    {
                        SelectedOptionId = opt.OptionId;
                        DialogResult = true;
                        return;
                    }
                }
            };
            buttonPanel.Children.Add(allowButton);

            Grid.SetRow(buttonPanel, 5);
            root.Children.Add(buttonPanel);

            Content = root;
        }
    }
}
