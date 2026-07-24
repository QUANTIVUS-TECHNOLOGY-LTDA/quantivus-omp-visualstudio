using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using VSAgent.Models;
using VSAgent.Services;

namespace VSAgent.Views
{
    public class SettingsView : UserControl
    {
        private CheckBox autoApproveReadOnlyCheck;
        private Slider compactThresholdSlider;
        private TextBlock compactThresholdLabel;
        private TextBlock autoCompactStatus;
        private ComboBox modelProviderCombo;
        private TextBox modelNameBox;
        private TextBox ompPathBox;

        public event EventHandler SettingsChanged;

        public SettingsView()
        {
            BuildLayout();
            LoadFromOptions();
        }

        private void BuildLayout()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(BuildBehaviorSection());
            root.Children.Add(BuildContextSection());
            root.Children.Add(BuildModelSection());
            scroll.Content = root;
            Content = scroll;
        }

        private FrameworkElement BuildBehaviorSection()
        {
            var group = MakeGroupBox("Behavior");
            var stack = new StackPanel { Margin = new Thickness(8) };
            autoApproveReadOnlyCheck = new CheckBox
            {
                Content = "Auto-approve read-only permission requests",
                Margin = new Thickness(0, 4, 0, 4)
            };
            autoApproveReadOnlyCheck.Checked += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            autoApproveReadOnlyCheck.Unchecked += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            stack.Children.Add(autoApproveReadOnlyCheck);

            var ompLabel = new TextBlock { Text = "omp.exe path (leave empty for default discovery):", Margin = new Thickness(0, 8, 0, 2) };
            ompLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            stack.Children.Add(ompLabel);

            ompPathBox = new TextBox { Margin = new Thickness(0, 0, 0, 4) };
            ompPathBox.TextChanged += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            stack.Children.Add(ompPathBox);

            group.Content = stack;
            return group;
        }

        private FrameworkElement BuildContextSection()
        {
            var group = MakeGroupBox("Context management");
            var stack = new StackPanel { Margin = new Thickness(8) };
            compactThresholdLabel = new TextBlock { Text = "Auto-compact threshold: 80%", Margin = new Thickness(0, 4, 0, 2) };
            compactThresholdLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            stack.Children.Add(compactThresholdLabel);

            compactThresholdSlider = new Slider
            {
                Minimum = 0,
                Maximum = 95,
                TickFrequency = 5,
                IsSnapToTickEnabled = true,
                Value = 80
            };
            compactThresholdSlider.ValueChanged += (_, __) =>
            {
                int v = (int)compactThresholdSlider.Value;
                compactThresholdLabel.Text = v == 0
                    ? "Auto-compact threshold: off"
                    : $"Auto-compact threshold: {v}% (enqueues /compact when ctx crosses this)";
                autoCompactStatus.Text = v == 0
                    ? "Status: disabled"
                    : $"Status: enqueues /compact when {v}% reached";
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            };
            stack.Children.Add(compactThresholdSlider);

            autoCompactStatus = new TextBlock { Text = "Status: enqueues /compact when 80% reached", Margin = new Thickness(0, 4, 0, 0), FontSize = 11 };
            autoCompactStatus.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            stack.Children.Add(autoCompactStatus);

            group.Content = stack;
            return group;
        }

        private FrameworkElement BuildModelSection()
        {
            var group = MakeGroupBox("Model (passed to oh-my-pi via OMP_PROVIDER / OMP_MODEL)");
            var stack = new StackPanel { Margin = new Thickness(8) };
            var providerLabel = new TextBlock { Text = "Provider:", Margin = new Thickness(0, 4, 0, 2) };
            providerLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            stack.Children.Add(providerLabel);

            modelProviderCombo = new ComboBox { IsEditable = false, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var p in new[] { "Anthropic", "OpenAI", "Google", "Azure", "Custom" })
                modelProviderCombo.Items.Add(p);
            modelProviderCombo.SelectionChanged += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            stack.Children.Add(modelProviderCombo);

            var modelLabel = new TextBlock { Text = "Model name (leave empty for provider default):", Margin = new Thickness(0, 8, 0, 2) };
            modelLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            stack.Children.Add(modelLabel);

            modelNameBox = new TextBox();
            modelNameBox.TextChanged += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            stack.Children.Add(modelNameBox);

            group.Content = stack;
            return group;
        }

        private static GroupBox MakeGroupBox(string header)
        {
            return new GroupBox
            {
                Header = header,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(4)
            };
        }

        private void LoadFromOptions()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var page = VSAgentPackage.GetOptions<OptionsProvider.GeneralOptions>();
                if (page == null) return;
                autoApproveReadOnlyCheck.IsChecked = page.AutoApproveReadOnly;
                ompPathBox.Text = page.OmpExecutablePath ?? string.Empty;
                compactThresholdSlider.Value = Math.Max(0, Math.Min(95, page.ContextCompactThresholdPercent));
                compactThresholdLabel.Text = page.ContextCompactThresholdPercent == 0
                    ? "Auto-compact threshold: off"
                    : $"Auto-compact threshold: {page.ContextCompactThresholdPercent:0}% (enqueues /compact when ctx crosses this)";
                autoCompactStatus.Text = page.ContextCompactThresholdPercent == 0
                    ? "Status: disabled"
                    : $"Status: enqueues /compact when {page.ContextCompactThresholdPercent:0}% reached";
                var provider = page.ModelProvider ?? "Anthropic";
                int idx = -1;
                for (int i = 0; i < modelProviderCombo.Items.Count; i++)
                    if (string.Equals((string)modelProviderCombo.Items[i], provider, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                if (idx >= 0) modelProviderCombo.SelectedIndex = idx;
                else { modelProviderCombo.Items.Insert(0, provider); modelProviderCombo.SelectedIndex = 0; }
                modelNameBox.Text = page.ModelName ?? string.Empty;
            }
            catch { }
        }

        public void ApplyToOptions()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var page = VSAgentPackage.GetOptions<OptionsProvider.GeneralOptions>();
                if (page == null) return;
                page.AutoApproveReadOnly = autoApproveReadOnlyCheck.IsChecked == true;
                page.OmpExecutablePath = ompPathBox.Text ?? string.Empty;
                page.ContextCompactThresholdPercent = compactThresholdSlider.Value;
                page.ModelProvider = (modelProviderCombo.SelectedItem as string) ?? "Anthropic";
                page.ModelName = modelNameBox.Text ?? string.Empty;
            }
            catch { }
        }

        internal void ApplyToAgentHost(AgentHostService host)
        {
            if (host == null) return;
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var page = VSAgentPackage.GetOptions<OptionsProvider.GeneralOptions>();
                host.ModelProvider = page?.ModelProvider;
                host.ModelName = page?.ModelName;
                host.AutoCompactThresholdPercent = page?.ContextCompactThresholdPercent ?? 0;
            }
            catch { }
        }
    }
}
