using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using VSAgent.Models;
using VSAgent.Services;
using VSAgent.Ui;

namespace VSAgent.Views
{
    /// <summary>
    ///   Models   - active provider, model, smol/slow/plan role assignments
    ///   Auth     - per-provider API key / OAuth / base-URL / mTLS (DPAPI-encrypted)
    ///   Search   - web-search providers (Exa, Brave, Perplexity, Tavily, ...)
    ///   Context  - auto-compact threshold, context window
    ///   Runtime  - omp.exe path, profile, alias, approval mode, enabled tools
    /// </summary>
    public class SettingsView : UserControl
    {
        private readonly CredentialStore credentials;
        private readonly SkillStore skills;
        private readonly ActiveSkillRegistry activeSkills;
        private readonly WebSearchConfig search;
        private readonly Func<OmpEnvironment> getEnv;
        private readonly Action<OmpEnvironment> applyEnv;

        private TabControl subTabs;
        private ComboBox activeProviderCombo;
        private TextBox activeModelBox;
        private TextBox smolModelBox;
        private TextBox slowModelBox;
        private TextBox planModelBox;
        private TextBox ompPathBox;
        private TextBox profileBox;
        private ComboBox approvalModeCombo;
        private CheckBox autoApproveCheck;
        private Slider thresholdSlider;
        private TextBlock thresholdLabel;
        private TextBlock thresholdStatus;
        private ListBox enabledToolsList;
        private ListBox providersList;
        private StackPanel providerDetailPanel;
        private ComboBox webSearchProviderCombo;
        private Dictionary<string, CheckBox> toolChecks;
        private ProviderSpec currentProviderDetail;

        public event EventHandler SettingsChanged;

        public SettingsView(
            CredentialStore credentials,
            SkillStore skills,
            ActiveSkillRegistry activeSkills,
            WebSearchConfig search,
            Func<OmpEnvironment> getEnv,
            Action<OmpEnvironment> applyEnv)
        {
            this.credentials = credentials ?? new CredentialStore();
            this.skills = skills ?? new SkillStore();
            this.activeSkills = activeSkills ?? new ActiveSkillRegistry(new SkillStore());
            this.search = search ?? new WebSearchConfig();
            this.getEnv = getEnv ?? (() => new OmpEnvironment());
            this.applyEnv = applyEnv ?? (_ => { });
            BuildLayout();
            LoadFromConfig();
            ApplyGlobalStyles();
        }

        private void ApplyGlobalStyles()
        {
            // Recursively style every control inside the settings tree once.
            Resources[typeof(Button)] = StyleFactory.ButtonStyle();
            Resources[typeof(TextBox)] = StyleFactory.TextBoxStyle();
            Resources[typeof(PasswordBox)] = StyleFactory.PasswordBoxStyle();
            Resources[typeof(ComboBox)] = StyleFactory.ComboBoxStyle();
            Resources[typeof(CheckBox)] = StyleFactory.CheckBoxStyle();
            Resources[typeof(GroupBox)] = StyleFactory.GroupBoxStyle();
            Resources[typeof(ListBox)] = StyleFactory.ListBoxStyle();
            Resources[typeof(ListBoxItem)] = StyleFactory.ListBoxItemStyle();
            Resources[typeof(Expander)] = StyleFactory.ExpanderStyle();
        }

        private void BuildLayout()
        {
            subTabs = new TabControl
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                Background = Brushes.Transparent
            };
            subTabs.SetResourceReference(TabControl.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            subTabs.SetResourceReference(TabControl.ForegroundProperty, VsBrushes.ToolWindowTextKey);

            subTabs.Items.Add(MakeTab("Models", BuildModelsTab()));
            subTabs.Items.Add(MakeTab("Authentication", BuildAuthTab()));
            subTabs.Items.Add(MakeTab("Web search", BuildSearchTab()));
            subTabs.Items.Add(MakeTab("Context", BuildContextTab()));
            subTabs.Items.Add(MakeTab("Runtime", BuildRuntimeTab()));

            Content = subTabs;
        }

        private TabItem MakeTab(string header, object content)
        {
            var ti = new TabItem { Header = header, Content = content };
            return ti;
        }

        private FrameworkElement BuildModelsTab()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.Children.Add(HeaderText("Active model", 16, 0));

            var providerLabel = SubLabel("Provider:", 1);
            grid.Children.Add(providerLabel);

            activeProviderCombo = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 8),
                IsEditable = false
            };
            foreach (var p in ProviderCatalog.All) activeProviderCombo.Items.Add(p);
            activeProviderCombo.SelectionChanged += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            Grid.SetRow(activeProviderCombo, 1);
            Grid.SetColumn(activeProviderCombo, 1);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(activeProviderCombo);

            grid.Children.Add(SubLabel("Active model (fuzzy: opus, gpt-5.4, or provider/model):", 2));
            activeModelBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
            activeModelBox.TextChanged += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            Grid.SetRow(activeModelBox, 2);
            Grid.SetColumn(activeModelBox, 1);
            grid.Children.Add(activeModelBox);

            grid.Children.Add(SubLabel("Smol / fast model (PI_SMOL_MODEL):", 3));
            smolModelBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
            smolModelBox.TextChanged += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            Grid.SetRow(smolModelBox, 3);
            Grid.SetColumn(smolModelBox, 1);
            grid.Children.Add(smolModelBox);

            grid.Children.Add(SubLabel("Slow / reasoning model (PI_SLOW_MODEL):", 4));
            slowModelBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
            slowModelBox.TextChanged += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            Grid.SetRow(slowModelBox, 4);
            Grid.SetColumn(slowModelBox, 1);
            grid.Children.Add(slowModelBox);

            grid.Children.Add(SubLabel("Plan model (PI_PLAN_MODEL):", 5));
            planModelBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
            planModelBox.TextChanged += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            Grid.SetRow(planModelBox, 5);
            Grid.SetColumn(planModelBox, 1);
            grid.Children.Add(planModelBox);

            var hint = new TextBlock
            {
                Text = "Tip: leave individual role fields empty to let oh-my-pi use the active model for that role. " +
                       "Run `omp models <provider>` in a terminal to list exact model names.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 16, 0, 0),
                Opacity = 0.75
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            Grid.SetRow(hint, 7);
            Grid.SetColumnSpan(hint, 2);
            grid.Children.Add(hint);

            return grid;
        }

        private FrameworkElement BuildAuthTab()
        {
            var grid = new Grid { Margin = new Thickness(0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            providersList = new ListBox
            {
                Margin = new Thickness(8, 8, 0, 8)
            };
            foreach (var p in ProviderCatalog.All) providersList.Items.Add(p);
            providersList.SelectionChanged += (_, __) =>
            {
                currentProviderDetail = providersList.SelectedItem as ProviderSpec;
                RebuildProviderDetail();
            };
            Grid.SetColumn(providersList, 0);
            grid.Children.Add(providersList);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 8, 8, 8)
            };
            providerDetailPanel = new StackPanel();
            scroll.Content = providerDetailPanel;
            Grid.SetColumn(scroll, 2);
            grid.Children.Add(scroll);

            return grid;
        }

        private void RebuildProviderDetail()
        {
            providerDetailPanel.Children.Clear();
            if (currentProviderDetail == null) return;
            var p = currentProviderDetail;

            var title = new TextBlock
            {
                Text = p.DisplayName,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            providerDetailPanel.Children.Add(title);

            var desc = new TextBlock
            {
                Text = p.Description,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 12)
            };
            desc.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            providerDetailPanel.Children.Add(desc);

            // API key
            if (!string.IsNullOrEmpty(p.ApiKeyEnvVar))
            {
                providerDetailPanel.Children.Add(SubLabel("API key (env " + p.ApiKeyEnvVar + "):", 0));
                var apiBox = new PasswordBox { Margin = new Thickness(0, 0, 0, 8), FontFamily = new FontFamily("Consolas") };
                apiBox.Password = credentials.GetApiKey(p.Id) ?? string.Empty;
                apiBox.PasswordChanged += (_, __) =>
                {
                    credentials.SetApiKey(p.Id, apiBox.Password);
                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                };
                providerDetailPanel.Children.Add(apiBox);
            }

            // OAuth
            if (!string.IsNullOrEmpty(p.OAuthEnvVar))
            {
                providerDetailPanel.Children.Add(SubLabel("OAuth token (env " + p.OAuthEnvVar + "):", 0));
                var oauthBox = new PasswordBox { Margin = new Thickness(0, 0, 0, 8), FontFamily = new FontFamily("Consolas") };
                oauthBox.Password = credentials.GetOAuthToken(p.Id) ?? string.Empty;
                oauthBox.PasswordChanged += (_, __) =>
                {
                    credentials.SetOAuthToken(p.Id, oauthBox.Password);
                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                };
                providerDetailPanel.Children.Add(oauthBox);
            }

            // Base URL
            if (!string.IsNullOrEmpty(p.BaseUrlEnvVar))
            {
                providerDetailPanel.Children.Add(SubLabel("Base URL (env " + p.BaseUrlEnvVar + "):", 0));
                var baseBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
                baseBox.Text = credentials.GetExtra(p.Id, "base_url") ?? string.Empty;
                baseBox.TextChanged += (_, __) =>
                {
                    credentials.SetExtra(p.Id, "base_url", baseBox.Text);
                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                };
                providerDetailPanel.Children.Add(baseBox);
            }

            // mTLS (Anthropic Foundry)
            if (p.Kind == ProviderKind.AnthropicFoundry)
            {
                providerDetailPanel.Children.Add(SubLabel("Client certificate (env CLAUDE_CODE_CLIENT_CERT):", 0));
                var certBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
                certBox.Text = credentials.GetExtra(p.Id, "client_cert") ?? string.Empty;
                certBox.TextChanged += (_, __) => credentials.SetExtra(p.Id, "client_cert", certBox.Text);
                providerDetailPanel.Children.Add(certBox);

                providerDetailPanel.Children.Add(SubLabel("Client private key (env CLAUDE_CODE_CLIENT_KEY):", 0));
                var keyBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
                keyBox.Text = credentials.GetExtra(p.Id, "client_key") ?? string.Empty;
                keyBox.TextChanged += (_, __) => credentials.SetExtra(p.Id, "client_key", keyBox.Text);
                providerDetailPanel.Children.Add(keyBox);
            }

            // Custom headers (Anthropic custom)
            if (p.Kind == ProviderKind.Anthropic || p.Kind == ProviderKind.AnthropicFoundry)
            {
                providerDetailPanel.Children.Add(SubLabel("Custom headers (env ANTHROPIC_CUSTOM_HEADERS):", 0));
                var hdrBox = new TextBox { Margin = new Thickness(0, 0, 0, 8), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 50 };
                hdrBox.Text = credentials.GetExtra(p.Id, "custom_headers") ?? string.Empty;
                hdrBox.TextChanged += (_, __) => credentials.SetExtra(p.Id, "custom_headers", hdrBox.Text);
                providerDetailPanel.Children.Add(hdrBox);
            }

            // AWS Bedrock specifics
            if (p.Kind == ProviderKind.AwsBedrock)
            {
                providerDetailPanel.Children.Add(SubLabel("AWS region (optional, env AWS_REGION):", 0));
                var regionBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
                regionBox.Text = credentials.GetExtra(p.Id, "aws_region") ?? string.Empty;
                regionBox.TextChanged += (_, __) => credentials.SetExtra(p.Id, "aws_region", regionBox.Text);
                providerDetailPanel.Children.Add(regionBox);
            }

            // Vertex specifics
            if (p.Kind == ProviderKind.GoogleVertex)
            {
                providerDetailPanel.Children.Add(SubLabel("Service account JSON path (env GOOGLE_APPLICATION_CREDENTIALS):", 0));
                var saBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
                saBox.Text = credentials.GetExtra(p.Id, "service_account") ?? string.Empty;
                saBox.TextChanged += (_, __) => credentials.SetExtra(p.Id, "service_account", saBox.Text);
                providerDetailPanel.Children.Add(saBox);

                providerDetailPanel.Children.Add(SubLabel("Cloud location (env GOOGLE_CLOUD_LOCATION):", 0));
                var locBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
                locBox.Text = credentials.GetExtra(p.Id, "cloud_location") ?? string.Empty;
                locBox.TextChanged += (_, __) => credentials.SetExtra(p.Id, "cloud_location", locBox.Text);
                providerDetailPanel.Children.Add(locBox);
            }

            // CA cert (TLS)
            providerDetailPanel.Children.Add(SubLabel("CA bundle path (env NODE_EXTRA_CA_CERTS, optional):", 0));
            var caBox = new TextBox { Margin = new Thickness(0, 0, 0, 12) };
            caBox.Text = credentials.GetExtra(p.Id, "ca_cert") ?? string.Empty;
            caBox.TextChanged += (_, __) => credentials.SetExtra(p.Id, "ca_cert", caBox.Text);
            providerDetailPanel.Children.Add(caBox);

            var clearBtn = new Button
            {
                Content = "Clear credentials for this provider",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0)
            };
            clearBtn.Click += (_, __) =>
            {
                credentials.Clear(p.Id);
                RebuildProviderDetail();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            };
            providerDetailPanel.Children.Add(clearBtn);

            var footer = new TextBlock
            {
                Text = "All credentials are encrypted with Windows DPAPI (CurrentUser) and stored in\n" + credentials.FilePath,
                FontSize = 10,
                Margin = new Thickness(0, 12, 0, 0),
                Opacity = 0.6
            };
            footer.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            providerDetailPanel.Children.Add(footer);
        }

        private FrameworkElement BuildSearchTab()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.Children.Add(HeaderText("Web search providers", 16, 0));

            grid.Children.Add(SubLabel("Active search provider:", 1));
            webSearchProviderCombo = new ComboBox { IsEditable = false, Margin = new Thickness(0, 0, 0, 8) };
            webSearchProviderCombo.Items.Add("native");
            webSearchProviderCombo.Items.Add("exa");
            webSearchProviderCombo.Items.Add("brave");
            webSearchProviderCombo.Items.Add("perplexity");
            webSearchProviderCombo.Items.Add("tavily");
            webSearchProviderCombo.Items.Add("tinyfish");
            webSearchProviderCombo.Items.Add("firecrawl");
            webSearchProviderCombo.SelectionChanged += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            Grid.SetRow(webSearchProviderCombo, 1);
            Grid.SetColumn(webSearchProviderCombo, 1);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(webSearchProviderCombo);

            AddSearchField(grid, 2, "Exa", "EXA_API_KEY", s => s.ExaApiKey = s.ExaApiKey, v => search.ExaApiKey = v);
            AddSearchField(grid, 3, "Brave", "BRAVE_API_KEY", null, v => search.BraveApiKey = v);
            AddSearchField(grid, 4, "Perplexity (API)", "PERPLEXITY_API_KEY", null, v => search.PerplexityApiKey = v);
            AddSearchField(grid, 5, "Perplexity cookies", "PERPLEXITY_COOKIES", null, v => search.PerplexityCookies = v);
            AddSearchField(grid, 6, "Tavily", "TAVILY_API_KEY", null, v => search.TavilyApiKey = v);
            AddSearchField(grid, 7, "TinyFish", "TINYFISH_API_KEY", null, v => search.TinyFishApiKey = v);
            AddSearchField(grid, 8, "Firecrawl", "FIRECRAWL_API_KEY", null, v => search.FirecrawlApiKey = v);

            return grid;
        }

        private void AddSearchField(Grid grid, int row, string label, string envVar, Func<WebSearchConfig, string> getter, Action<string> setter)
        {
            grid.Children.Add(SubLabel(label + " (" + envVar + "):", row));
            var box = new PasswordBox { Margin = new Thickness(0, 0, 0, 8), FontFamily = new FontFamily("Consolas") };
            box.Password = getter?.Invoke(search) ?? string.Empty;
            box.PasswordChanged += (_, __) => setter(box.Password);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
        }

        private FrameworkElement BuildContextTab()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(HeaderText("Context management", 16, 0));

            thresholdLabel = new TextBlock { Text = "Auto-compact threshold: 80%", Margin = new Thickness(0, 12, 0, 4) };
            thresholdLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            grid.Children.Add(thresholdLabel);

            thresholdSlider = new Slider
            {
                Minimum = 0,
                Maximum = 95,
                TickFrequency = 5,
                IsSnapToTickEnabled = true,
                Value = 80
            };
            thresholdSlider.ValueChanged += (_, __) =>
            {
                int v = (int)thresholdSlider.Value;
                thresholdLabel.Text = v == 0 ? "Auto-compact threshold: off" : $"Auto-compact threshold: {v}%";
                thresholdStatus.Text = v == 0 ? "Status: disabled" : $"Status: enqueues /compact when {v}% reached";
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            };
            Grid.SetRow(thresholdSlider, 1);
            grid.Children.Add(thresholdSlider);

            thresholdStatus = new TextBlock { Text = "Status: enqueues /compact when 80% reached", Margin = new Thickness(0, 4, 0, 16), FontSize = 11 };
            thresholdStatus.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            Grid.SetRow(thresholdStatus, 2);
            grid.Children.Add(thresholdStatus);

            autoApproveCheck = new CheckBox
            {
                Content = "Auto-approve read-only permission requests (matches old 'AutoApproveReadOnly')",
                Margin = new Thickness(0, 8, 0, 8)
            };
            autoApproveCheck.Checked += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            autoApproveCheck.Unchecked += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            Grid.SetRow(autoApproveCheck, 3);
            grid.Children.Add(autoApproveCheck);

            return grid;
        }

        private FrameworkElement BuildRuntimeTab()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.Children.Add(HeaderText("oh-my-pi runtime", 16, 0));

            grid.Children.Add(SubLabel("omp.exe path (empty = Runtime or PATH):", 1));
            ompPathBox = new TextBox { Margin = new Thickness(0, 0, 0, 8), FontFamily = new FontFamily("Consolas") };
            ompPathBox.TextChanged += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            Grid.SetRow(ompPathBox, 1);
            Grid.SetColumn(ompPathBox, 1);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(ompPathBox);

            grid.Children.Add(SubLabel("Profile (env OMP_PROFILE):", 2));
            profileBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
            profileBox.TextChanged += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            Grid.SetRow(profileBox, 2);
            Grid.SetColumn(profileBox, 1);
            grid.Children.Add(profileBox);

            grid.Children.Add(SubLabel("Approval mode:", 3));
            approvalModeCombo = new ComboBox { IsEditable = false, Margin = new Thickness(0, 0, 0, 8) };
            approvalModeCombo.Items.Add("always-ask");
            approvalModeCombo.Items.Add("write");
            approvalModeCombo.Items.Add("yolo");
            approvalModeCombo.SelectionChanged += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            Grid.SetRow(approvalModeCombo, 3);
            Grid.SetColumn(approvalModeCombo, 1);
            grid.Children.Add(approvalModeCombo);

            grid.Children.Add(SubLabel("Enabled tools (--tools):", 5));

            var toolsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 280,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var toolsStack = new StackPanel { Margin = new Thickness(4) };
            toolChecks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in new[] { "read", "bash", "edit", "write", "grep", "glob", "lsp", "python", "notebook", "inspect_image", "browser", "task", "todo", "web_search", "ask" })
            {
                var cb = new CheckBox { Content = t, IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
                cb.Checked += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
                cb.Unchecked += (_, __) => SettingsChanged?.Invoke(this, EventArgs.Empty);
                toolChecks[t] = cb;
                toolsStack.Children.Add(cb);
            }
            toolsScroll.Content = toolsStack;
            Grid.SetRow(toolsScroll, 5);
            Grid.SetColumn(toolsScroll, 1);
            grid.Children.Add(toolsScroll);

            var hint = new TextBlock
            {
                Text = "Settings are applied the next time oh-my-pi starts. To apply immediately use View -\\> Other Windows -\\> " +
                       "Quantivus OMP, close the tool window and reopen it (or right-click the active model field).",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0),
                Opacity = 0.6
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            Grid.SetRow(hint, 7);
            Grid.SetColumnSpan(hint, 2);
            grid.Children.Add(hint);

            return grid;
        }

        private static TextBlock HeaderText(string text, double size, int row)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            if (row > 0) Grid.SetRow(tb, row);
            return tb;
        }

        private static TextBlock SubLabel(string text, int row)
        {
            var tb = new TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            if (row > 0) Grid.SetRow(tb, row);
            return tb;
        }

        private void LoadFromConfig()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                credentials.Load();
                var env = getEnv();
                if (env != null)
                {
                    activeProviderCombo.SelectedItem = ProviderCatalog.FindById(env.ActiveProvider) ?? ProviderCatalog.All[0];
                    activeModelBox.Text = env.ActiveModel ?? string.Empty;
                    smolModelBox.Text = env.SmolModel ?? string.Empty;
                    slowModelBox.Text = env.SlowModel ?? string.Empty;
                    planModelBox.Text = env.PlanModel ?? string.Empty;
                    profileBox.Text = env.OmpProfile ?? string.Empty;
                    thresholdSlider.Value = Math.Max(0, Math.Min(95, env.AutoCompactThresholdPercent));
                }
                else
                {
                    activeProviderCombo.SelectedIndex = 0;
                }

                var page = VSAgentPackage.GetOptions<OptionsProvider.GeneralOptions>();
                if (page != null)
                {
                    ompPathBox.Text = page.OmpExecutablePath ?? string.Empty;
                    autoApproveCheck.IsChecked = page.AutoApproveReadOnly;
                }

                webSearchProviderCombo.SelectedItem = search.Provider ?? "native";
            }
            catch { /* defaults remain */ }
        }

        public void ApplyToOptions()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var page = VSAgentPackage.GetOptions<OptionsProvider.GeneralOptions>();
                if (page != null)
                {
                    page.OmpExecutablePath = ompPathBox?.Text ?? string.Empty;
                    page.AutoApproveReadOnly = autoApproveCheck?.IsChecked == true;
                    page.ContextCompactThresholdPercent = thresholdSlider?.Value ?? 80;
                }
            }
            catch { }
        }

        public OmpEnvironment Collect()
        {
            var env = new OmpEnvironment
            {
                ActiveProvider = (activeProviderCombo?.SelectedItem as ProviderSpec)?.Id,
                ActiveModel = activeModelBox?.Text ?? string.Empty,
                SmolModel = smolModelBox?.Text ?? string.Empty,
                SlowModel = slowModelBox?.Text ?? string.Empty,
                PlanModel = planModelBox?.Text ?? string.Empty,
                OmpProfile = profileBox?.Text ?? string.Empty,
                AutoCompactThresholdPercent = thresholdSlider?.Value ?? 0,
                SearchProvider = (webSearchProviderCombo?.SelectedItem as string) ?? "native"
            };
            return env;
        }

        public List<string> CollectEnabledTools()
        {
            if (toolChecks == null) return new List<string> { "read", "bash", "edit", "write", "grep", "glob", "lsp" };
            var list = new List<string>();
            foreach (var kv in toolChecks) if (kv.Value.IsChecked == true) list.Add(kv.Key);
            return list;
        }

        public string GetApprovalMode() => approvalModeCombo?.SelectedItem as string ?? "always-ask";

        public void Commit()
        {
            ApplyToOptions();
            applyEnv(Collect());
        }
    }
}
