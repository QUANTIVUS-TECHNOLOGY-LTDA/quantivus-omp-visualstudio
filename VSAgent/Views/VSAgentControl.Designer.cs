using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.VisualStudio.Shell;

namespace VSAgent.Views
{
    public partial class VSAgentControl : UserControl
    {
        private TextBox ResponseTextBox;
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
        private FlowDocumentScrollViewer LastResponseView;


        private Border StatusBar;

        private Border QueuePanel;
        private TextBlock QueueHeader;
        private StackPanel QueueItemsPanel;
        private WelcomeView WelcomeOverlay;

        private void InitializeComponent()
        {
            MinWidth = 420;
            MinHeight = 320;

            BuildTabItemStyle();

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- Tab area: Agent / History ----
            MainTabControl = new TabControl
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            MainTabControl.SetResourceReference(TabControl.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            MainTabControl.SetResourceReference(TabControl.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            MainTabControl.SetResourceReference(TabControl.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            Grid.SetRow(MainTabControl, 0);

            ChatTab = new TabItem { Header = "Agent" };
            var chatGrid = new Grid { Margin = new Thickness(0) };
            chatGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 120 });
            chatGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            chatGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 });
            chatGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            ResponseScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(8, 8, 8, 4),
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x45)),
                BorderThickness = new Thickness(1)
            };

            ChatTranscript = new StackPanel { Margin = new Thickness(4) };
            ResponseScrollViewer.Content = ChatTranscript;
            Grid.SetRow(ResponseScrollViewer, 0);

            // Welcome overlay sits above the empty transcript
            var responseGrid = new Grid();
            responseGrid.Children.Add(ResponseScrollViewer);
            WelcomeOverlay = new WelcomeView();
            responseGrid.Children.Add(WelcomeOverlay);
            chatGrid.Children.Add(responseGrid);
            Grid.SetRow(responseGrid, 0);
            ChatTab.Content = chatGrid;

            var promptLabel = new TextBlock
            {
                Text = "Ask oh-my-pi:",
                Margin = new Thickness(8, 4, 8, 2),
                FontWeight = FontWeights.SemiBold
            };
            promptLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            Grid.SetRow(promptLabel, 1);
            chatGrid.Children.Add(promptLabel);

            PromptTextBox = new TextBox
            {
                Text = "Describe the task... (Ctrl+Enter to send, type / for commands, Esc to cancel)",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(8, 0, 8, 8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                FontSize = 12
            };
            PromptTextBox.SetResourceReference(TextBox.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            PromptTextBox.SetResourceReference(TextBox.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            PromptTextBox.SetResourceReference(TextBox.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            Grid.SetRow(PromptTextBox, 2);
            chatGrid.Children.Add(PromptTextBox);

            // ---- Queue panel ----
            QueuePanel = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(8, 4, 8, 4),
                Visibility = Visibility.Collapsed
            };
            QueuePanel.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            QueuePanel.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);

            var queueRoot = new Grid();
            queueRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            queueRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MaxHeight = 110 });

            QueueHeader = new TextBlock
            {
                Text = "Queued follow-up messages",
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            };
            QueueHeader.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            Grid.SetRow(QueueHeader, 0);
            queueRoot.Children.Add(QueueHeader);

            QueueItemsPanel = new StackPanel { Orientation = Orientation.Vertical };
            var queueScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = QueueItemsPanel
            };
            Grid.SetRow(queueScroller, 1);
            queueRoot.Children.Add(queueScroller);
            QueuePanel.Child = queueRoot;
            Grid.SetRow(QueuePanel, 3);
            chatGrid.Children.Add(QueuePanel);


            MainTabControl.Items.Add(ChatTab);

            var historyTab = new TabItem { Header = "History" };
            HistoryListBox = new ListBox { Margin = new Thickness(8) };
            HistoryListBox.SetResourceReference(ListBox.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            HistoryListBox.SetResourceReference(ListBox.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            HistoryListBox.SetResourceReference(ListBox.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            HistoryListBox.SelectionChanged += HistoryListBox_SelectionChanged;
            historyTab.Content = HistoryListBox;
            MainTabControl.Items.Add(historyTab);

            var skillsTab = new TabItem { Header = "Skills", Content = new SkillsPlaceholder() };
            skillsTab.Name = "SkillsTab";
            MainTabControl.Items.Add(skillsTab);

            var toolsTab = new TabItem { Header = "Tools", Content = new ToolsPlaceholder() };
            toolsTab.Name = "ToolsTab";
            MainTabControl.Items.Add(toolsTab);

            mainGrid.Children.Add(MainTabControl);

            // ---- Footer ----
            StatusBar = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(8, 4, 8, 4)
            };
            StatusBar.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            StatusBar.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);

            var footer = new Grid();
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TaskTextBlock = MakeStatusText("Idle", FontWeights.SemiBold);
            TaskTextBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(TaskTextBlock, 0);
            footer.Children.Add(TaskTextBlock);

            BranchTextBlock = MakeStatusText("", FontWeights.Normal);
            BranchTextBlock.Margin = new Thickness(12, 0, 12, 0);
            BranchTextBlock.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(BranchTextBlock, 1);
            footer.Children.Add(BranchTextBlock);

            CtxTextBlock = MakeStatusText("ctx: 0.0%/1M", FontWeights.Normal);
            CtxTextBlock.Margin = new Thickness(0, 0, 12, 0);
            CtxTextBlock.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(CtxTextBlock, 2);
            footer.Children.Add(CtxTextBlock);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            SendButton = new Button
            {
                Content = "Send",
                Padding = new Thickness(14, 4, 14, 4),
                Margin = new Thickness(0, 0, 6, 0),
                MinWidth = 80,
                IsDefault = true
            };
            SendButton.Click += SendButton_Click;
            buttonPanel.Children.Add(SendButton);

            CancelButton = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(14, 4, 14, 4),
                MinWidth = 80,
                IsEnabled = false
            };
            CancelButton.Click += CancelButton_Click;
            buttonPanel.Children.Add(CancelButton);

            Grid.SetColumn(buttonPanel, 3);
            footer.Children.Add(buttonPanel);

            StatusBar.Child = footer;
            Grid.SetRow(StatusBar, 1);
            mainGrid.Children.Add(StatusBar);

            Content = mainGrid;
        }

        private void BuildTabItemStyle()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Colors.Transparent));
            border.SetValue(Border.PaddingProperty, new Thickness(14, 6, 14, 6));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Header")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            contentPresenter.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("HeaderTemplate")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            contentPresenter.SetBinding(TextElement.ForegroundProperty, new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            contentPresenter.SetBinding(TextBlock.FontWeightProperty, new Binding("FontWeight")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            contentPresenter.SetBinding(TextBlock.FontSizeProperty, new Binding("FontSize")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            border.AppendChild(contentPresenter);

            var template = new ControlTemplate(typeof(TabItem)) { VisualTree = border };

            var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BorderBrushProperty,
                new DynamicResourceExtension(VsBrushes.AccentMediumKey), "Bd"));
            template.Triggers.Add(selected);

            var unselected = new Trigger { Property = TabItem.IsSelectedProperty, Value = false };
            unselected.Setters.Add(new Setter(Border.BorderBrushProperty,
                new SolidColorBrush(Colors.Transparent), "Bd"));
            template.Triggers.Add(unselected);

            var disabled = new Trigger { Property = TabItem.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(TextElement.ForegroundProperty,
                new DynamicResourceExtension(VsBrushes.ToolWindowTextKey), "Bd"));
            template.Triggers.Add(disabled);

            var style = new Style(typeof(TabItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(TabItem.BackgroundProperty,
                new DynamicResourceExtension(VsBrushes.ToolWindowBackgroundKey)));
            style.Setters.Add(new Setter(TabItem.ForegroundProperty,
                new DynamicResourceExtension(VsBrushes.ToolWindowTextKey)));
            style.Setters.Add(new Setter(TabItem.BorderBrushProperty,
                new SolidColorBrush(Colors.Transparent)));
            style.Setters.Add(new Setter(TabItem.FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(TabItem.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(TabItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(TabItem.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
            style.Setters.Add(new Setter(TabItem.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(TabItem.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(System.Windows.Controls.TextBlock.ForegroundProperty,
                new DynamicResourceExtension(VsBrushes.ToolWindowTextKey)));
            Resources.Add(typeof(TabItem), style);
        }

        private static TextBlock MakeStatusText(string text, FontWeight weight)
        {
            var tb = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                FontWeight = weight
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            return tb;
        }
    }
}
