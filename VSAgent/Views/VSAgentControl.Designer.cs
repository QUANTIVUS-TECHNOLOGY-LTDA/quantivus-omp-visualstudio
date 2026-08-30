using Microsoft.VisualStudio.Shell;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using VSAgent.Ui;

namespace VSAgent.Views
{
    public partial class VSAgentControl : UserControl
    {
        private TextBox PromptTextBox;
        private Button SendButton;
        private Button CancelButton;
        private TextBlock TaskTextBlock;
        private TextBlock BranchTextBlock;
        private TextBlock CtxTextBlock;
        private ListBox HistoryListBox;
        private TabControl MainTabControl;
        private TabItem ChatTab;
        private ScrollViewer ResponseScrollViewer;
        private StackPanel ChatTranscript;
        private Border StatusBar;
        private Border QueuePanel;
        private TextBlock QueueHeader;
        private StackPanel QueueItemsPanel;
        private WelcomeView WelcomeOverlay;

        private void InitializeComponent()
        {
            MinWidth = 540;
            MinHeight = 420;
            EnsureWorkbenchStore();

            SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            SetResourceReference(ForegroundProperty, VsBrushes.ToolWindowTextKey);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(BuildWorkbenchHeader());

            var body = new Grid();
            NavigationColumn = new ColumnDefinition { Width = new GridLength(220) };
            body.ColumnDefinitions.Add(NavigationColumn);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var navigationBorder = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(6, 8, 6, 8)
            };
            navigationBorder.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            navigationBorder.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            WorkbenchNavigation = BuildNavigation();
            navigationBorder.Child = WorkbenchNavigation;
            body.Children.Add(navigationBorder);

            var splitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ShowsPreview = true
            };
            splitter.SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBorderKey);
            Grid.SetColumn(splitter, 1);
            body.Children.Add(splitter);

            MainTabControl = BuildMainTabControl();
            Grid.SetColumn(MainTabControl, 2);
            body.Children.Add(MainTabControl);

            StatusBar = BuildStatusBar();
            Grid.SetRow(StatusBar, 2);
            root.Children.Add(StatusBar);

            Content = root;
            InitializeWorkbenchBehaviors();
        }

        private UIElement BuildWorkbenchHeader()
        {
            var border = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 7, 10, 7)
            };
            border.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            border.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var menuButton = WorkbenchUi.Button("☰", delegate { ToggleNavigation(); }, false, "Collapse or expand navigation");
            menuButton.Width = 36;
            menuButton.MinWidth = 36;
            menuButton.Margin = new Thickness(0, 0, 10, 0);
            grid.Children.Add(menuButton);

            var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            var title = new TextBlock
            {
                Text = "Quantivus OMP",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            titleRow.Children.Add(title);
            WorkbenchSectionTitleTextBlock = new TextBlock
            {
                Text = "Chat",
                FontSize = 12,
                Margin = new Thickness(10, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            WorkbenchSectionTitleTextBlock.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            titleRow.Children.Add(WorkbenchSectionTitleTextBlock);
            identity.Children.Add(titleRow);

            var stateRow = new StackPanel { Orientation = Orientation.Horizontal };
            HeaderStatusTextBlock = HeaderMeta("Idle");
            ProviderTextBlock = HeaderMeta("Default provider");
            ProviderTextBlock.Margin = new Thickness(12, 0, 0, 0);
            stateRow.Children.Add(HeaderStatusTextBlock);
            stateRow.Children.Add(ProviderTextBlock);
            identity.Children.Add(stateRow);
            Grid.SetColumn(identity, 1);
            grid.Children.Add(identity);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var newSession = WorkbenchUi.Button("New", delegate { CreateNewSession(); }, false, "Start a new persistent chat session");
            newSession.MinWidth = 58;
            actions.Children.Add(newSession);
            RestartAgentButton = WorkbenchUi.Button("Restart", async delegate { await RestartAgentAsync(); }, false, "Restart the local oh-my-pi process");
            RestartAgentButton.MinWidth = 66;
            actions.Children.Add(RestartAgentButton);
            StopAgentButton = WorkbenchUi.Button("Stop", delegate { StopAgent(); }, false, "Stop the local oh-my-pi process");
            StopAgentButton.MinWidth = 54;
            actions.Children.Add(StopAgentButton);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            border.Child = grid;
            return border;
        }

        private ListBox BuildNavigation()
        {
            var list = new ListBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                SelectionMode = SelectionMode.Single
            };
            list.SetResourceReference(ForegroundProperty, VsBrushes.ToolWindowTextKey);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);

            AddNavigationItem(list, "Chat", "CH", "Conversation and live tools");
            AddNavigationItem(list, "Tasks", "TS", "Runs and follow-up queue");
            AddNavigationItem(list, "Agents", "AG", "Focused agent profiles");
            AddNavigationItem(list, "Repository", "RP", "Solution and project overview");
            AddNavigationItem(list, "Changes", "Δ", "Git diff and commits");
            AddNavigationItem(list, "Terminal", ">_", "Cancellable shell commands");
            AddNavigationItem(list, "Context", "CX", "Inspectable prompt context");
            AddNavigationItem(list, "Prompts", "PR", "Reusable prompt library");
            AddNavigationItem(list, "Sessions", "SE", "Restore and export sessions");
            AddNavigationItem(list, "Kanban", "KB", "Sequential task queue with agent profiles");
            AddNavigationItem(list, "Skills", "SK", "OMP skills");
            AddNavigationItem(list, "Settings", "ST", "Provider and extension settings");
            AddNavigationItem(list, "Diagnostics", "DX", "Runtime and installation checks");

            list.SelectionChanged += delegate
            {
                if (list.SelectedItem is ListBoxItem selected) SelectWorkbenchTab(selected.Tag as string);
            };
            list.SelectedIndex = 0;
            return list;
        }

        private static void AddNavigationItem(ListBox list, string header, string glyph, string subtitle)
        {
            var grid = new Grid { Margin = new Thickness(1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontSize = glyph.Length > 1 ? 10 : 14,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            icon.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            icon.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            ((TextBlock)icon.Child).SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            grid.Children.Add(icon);

            var text = new StackPanel { Margin = new Thickness(6, 3, 4, 3), VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = header, FontSize = 12, FontWeight = FontWeights.SemiBold };
            name.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            var description = new TextBlock
            {
                Text = subtitle,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            description.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            text.Children.Add(name);
            text.Children.Add(description);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var item = new ListBoxItem
            {
                Content = grid,
                Tag = header,
                Padding = new Thickness(2),
                Margin = new Thickness(0, 1, 0, 1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            list.Items.Add(item);
        }

        private TabControl BuildMainTabControl()
        {
            var tabs = new TabControl
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0)
            };
            tabs.SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            tabs.SetResourceReference(ForegroundProperty, VsBrushes.ToolWindowTextKey);
            tabs.Template = BuildContentOnlyTabTemplate();
            tabs.SelectionChanged += delegate { SyncNavigationFromTab(); };

            HistoryListBox = WorkbenchUi.ListBox();
            HistoryListBox.DisplayMemberPath = "Prompt";
            HistoryListBox.SelectionChanged += HistoryListBox_SelectionChanged;

            ChatTab = AddTab(tabs, "Chat", BuildChatPage());
            AddTab(tabs, "Tasks", CreateTaskCenterPage());
            AddTab(tabs, "Agents", CreateAgentWorkspacePage());
            AddTab(tabs, "Repository", CreateRepositoryPage());
            AddTab(tabs, "Changes", CreateChangesPage());
            AddTab(tabs, "Terminal", CreateTerminalPage());
            AddTab(tabs, "Context", CreateContextPage());
            AddTab(tabs, "Prompts", CreatePromptsPage());
            AddTab(tabs, "Sessions", CreateSessionsPage());
            AddTab(tabs, "Kanban", CreateKanbanPage());

            var skills = AddTab(tabs, "Skills", new SkillsPlaceholder());
            skills.Name = "SkillsTab";
            var tools = AddTab(tabs, "Tools", new ToolsPlaceholder());

            tabs.SelectedItem = ChatTab;
            return tabs;
        }

        private static ControlTemplate BuildContentOnlyTabTemplate()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            var items = new FrameworkElementFactory(typeof(ItemsPresenter));
            items.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            grid.AppendChild(items);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.MarginProperty, new Thickness(0));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            content.SetBinding(ContentPresenter.ContentProperty, new Binding("SelectedContent")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            content.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("SelectedContentTemplate")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            grid.AppendChild(content);
            return new ControlTemplate(typeof(TabControl)) { VisualTree = grid };
        }

        private static TabItem AddTab(TabControl tabs, string header, UIElement content)
        {
            var item = new TabItem { Header = header, Content = content };
            tabs.Items.Add(item);
            return item;
        }

        private UIElement BuildChatPage()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 150 });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var toolbar = new Grid { Margin = new Thickness(10, 8, 10, 6) };
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ChatSearchTextBox = WorkbenchUi.TextBox();
            ChatSearchTextBox.ToolTip = "Filter visible chat and tool-call cards";
            ChatSearchTextBox.TextChanged += delegate { FilterTranscript(); };
            toolbar.Children.Add(ChatSearchTextBox);
            var transcriptActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            transcriptActions.Children.Add(CompactButton("Copy", delegate { CopyConversation(); }));
            transcriptActions.Children.Add(CompactButton("Export", delegate { ExportConversation(); }));
            transcriptActions.Children.Add(CompactButton("Clear", delegate
            {
                ClearTranscript();
                chatHistory?.Clear();
                contextUsage.Reset();
                appliedContextCharacters = 0;
                UpdateCtxDisplay();
                SetTask("Conversation cleared");
            }));
            Grid.SetColumn(transcriptActions, 1);
            toolbar.Children.Add(transcriptActions);
            root.Children.Add(toolbar);

            var transcriptHost = new Grid { Margin = new Thickness(10, 0, 10, 6) };
            ResponseScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5),
                CanContentScroll = true
            };
            ResponseScrollViewer.SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            ResponseScrollViewer.SetResourceReference(BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            ChatTranscript = new StackPanel { Margin = new Thickness(2) };
            ResponseScrollViewer.Content = ChatTranscript;
            transcriptHost.Children.Add(ResponseScrollViewer);
            WelcomeOverlay = new WelcomeView();
            transcriptHost.Children.Add(WelcomeOverlay);
            Grid.SetRow(transcriptHost, 1);
            root.Children.Add(transcriptHost);

            QueuePanel = BuildQueuePanel();
            Grid.SetRow(QueuePanel, 2);
            root.Children.Add(QueuePanel);

            var composer = BuildComposer();
            Grid.SetRow(composer, 3);
            root.Children.Add(composer);
            return root;
        }

        private Border BuildQueuePanel()
        {
            var panel = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(10, 5, 10, 5),
                Visibility = Visibility.Collapsed
            };
            panel.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            panel.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MaxHeight = 110 });
            QueueHeader = new TextBlock
            {
                Text = "Queued follow-up messages",
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            };
            QueueHeader.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            root.Children.Add(QueueHeader);
            QueueItemsPanel = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = QueueItemsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            panel.Child = root;
            return panel;
        }

        private UIElement BuildComposer()
        {
            var border = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(10, 7, 10, 8)
            };
            border.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            border.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 90, MaxHeight = 320 });

            var attachments = new StackPanel { Orientation = Orientation.Horizontal };
            attachments.Children.Add(CompactButton("Selection", delegate { AttachSelectionToPrompt(); }, "Attach selected editor text"));
            attachments.Children.Add(CompactButton("Current file", delegate { AttachCurrentFileToPrompt(); }, "Reference the active document"));
            attachments.Children.Add(CompactButton("Open files", delegate { AttachOpenDocumentsToPrompt(); }, "Reference all open documents"));
            var hint = HeaderMeta("Ctrl+Enter sends • / opens commands • Esc cancels • files can be dropped here");
            hint.Margin = new Thickness(8, 6, 0, 0);
            attachments.Children.Add(hint);
            root.Children.Add(attachments);

            PromptTextBox = WorkbenchUi.TextBox("Describe the task... (Ctrl+Enter to send, type / for commands, Esc to cancel). Drop images or files here — paste with Ctrl+V.", true);
            PromptTextBox.Margin = new Thickness(0, 5, 0, 5);
            PromptTextBox.FontSize = 12;
            PromptTextBox.AcceptsTab = true;
            PromptTextBox.MaxLength = 0; // unlimited — long prompts must not be truncated
            PromptTextBox.TextWrapping = TextWrapping.Wrap;
            PromptTextBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            PromptTextBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            PromptTextBox.AllowDrop = true;
            PromptTextBox.ToolTip = "Describe the task. The context inspector shows additional data that will be prepended. Paste images with Ctrl+V or drop files/images here.";
            root.Children.Add(PromptTextBox);

            var actionRow = new Grid();
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var activeContext = HeaderMeta("Inspectable context is controlled in Context");
            activeContext.VerticalAlignment = VerticalAlignment.Center;
            actionRow.Children.Add(activeContext);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            SendButton = WorkbenchUi.Button("Send", SendButton_Click, true, "Send prompt (Ctrl+Enter)");
            SendButton.MinWidth = 84;
            SendButton.IsDefault = true;
            CancelButton = WorkbenchUi.Button("Cancel", CancelButton_Click, false, "Cancel current request (Esc)");
            CancelButton.MinWidth = 76;
            CancelButton.IsEnabled = false;
            buttons.Children.Add(SendButton);
            buttons.Children.Add(CancelButton);
            Grid.SetColumn(buttons, 1);
            actionRow.Children.Add(buttons);
            Grid.SetRow(actionRow, 2);
            root.Children.Add(actionRow);

            border.Child = root;
            return border;
        }

        private Border BuildStatusBar()
        {
            var border = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(10, 4, 10, 4)
            };
            border.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            border.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TaskTextBlock = StatusText("Idle", FontWeights.SemiBold);
            TaskTextBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            grid.Children.Add(TaskTextBlock);
            BranchTextBlock = StatusText("—", FontWeights.Normal);
            BranchTextBlock.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(BranchTextBlock, 1);
            grid.Children.Add(BranchTextBlock);
            ChangedFilesTextBlock = StatusText("0 changed", FontWeights.Normal);
            ChangedFilesTextBlock.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(ChangedFilesTextBlock, 2);
            grid.Children.Add(ChangedFilesTextBlock);
            CtxTextBlock = StatusText("ctx: 0.0%/1M", FontWeights.Normal);
            CtxTextBlock.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(CtxTextBlock, 3);
            grid.Children.Add(CtxTextBlock);
            DurationTextBlock = StatusText("00:00", FontWeights.Normal);
            DurationTextBlock.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(DurationTextBlock, 4);
            grid.Children.Add(DurationTextBlock);

            border.Child = grid;
            return border;
        }

        private static Button CompactButton(string text, RoutedEventHandler click, string toolTip = null)
        {
            var button = WorkbenchUi.Button(text, click, false, toolTip);
            button.MinWidth = 0;
            button.MinHeight = 24;
            button.Padding = new Thickness(8, 2, 8, 2);
            button.Margin = new Thickness(0, 0, 4, 0);
            return button;
        }

        private static TextBlock HeaderMeta(string text)
        {
            var value = new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            return value;
        }

        private static TextBlock StatusText(string text, FontWeight weight)
        {
            var value = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = weight,
                VerticalAlignment = VerticalAlignment.Center
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            return value;
        }
    }
}
