using System.Windows;
using System.Windows.Controls;

namespace VSAgent.Views
{
    public partial class VSAgentControl : UserControl
    {
        private TextBox ResponseTextBox;
        private TextBox PromptTextBox;
        private Button SendButton;
        private Button CancelButton;
        private TextBlock StatusTextBlock;
        private ListBox HistoryListBox;
        private TabControl MainTabControl;
        private TabItem ChatTab;
        private ScrollViewer ResponseScrollViewer;

        private void InitializeComponent()
        {
            Width = 800;
            Height = 600;

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            MainTabControl = new TabControl();
            Grid.SetRow(MainTabControl, 0);

            ChatTab = new TabItem { Header = "Agent" };
            var chatGrid = new Grid();
            chatGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
            chatGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            ResponseScrollViewer = new ScrollViewer();
            ResponseTextBox = new TextBox
            {
                Text = "Quantivus OMP is loading. Open a solution and ask the agent to build, debug, inspect, or change it.",
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                AcceptsReturn = true,
                Margin = new Thickness(5)
            };
            ResponseScrollViewer.Content = ResponseTextBox;
            Grid.SetRow(ResponseScrollViewer, 0);
            chatGrid.Children.Add(ResponseScrollViewer);

            var inputGrid = new Grid();
            inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inputGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var promptLabel = new TextBlock { Text = "Ask oh-my-pi:", Margin = new Thickness(5, 5, 5, 0) };
            Grid.SetRow(promptLabel, 0);
            inputGrid.Children.Add(promptLabel);

            PromptTextBox = new TextBox
            {
                Text = "Describe the task... (Ctrl+Enter to send)",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(5)
            };
            PromptTextBox.KeyDown += PromptTextBox_KeyDown;
            Grid.SetRow(PromptTextBox, 1);
            inputGrid.Children.Add(PromptTextBox);
            Grid.SetRow(inputGrid, 1);
            chatGrid.Children.Add(inputGrid);
            ChatTab.Content = chatGrid;
            MainTabControl.Items.Add(ChatTab);

            var historyTab = new TabItem { Header = "History" };
            HistoryListBox = new ListBox { Margin = new Thickness(5) };
            HistoryListBox.SelectionChanged += HistoryListBox_SelectionChanged;
            historyTab.Content = HistoryListBox;
            MainTabControl.Items.Add(historyTab);
            mainGrid.Children.Add(MainTabControl);

            StatusTextBlock = new TextBlock
            {
                Text = "Initializing oh-my-pi...",
                Padding = new Thickness(5, 3, 5, 3),
                Background = SystemColors.ControlBrush
            };
            Grid.SetRow(StatusTextBlock, 1);
            mainGrid.Children.Add(StatusTextBlock);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(5)
            };
            SendButton = new Button
            {
                Content = "Send",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 5, 0),
                IsDefault = true
            };
            SendButton.Click += SendButton_Click;
            buttonPanel.Children.Add(SendButton);

            CancelButton = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(10, 5, 10, 5),
                IsEnabled = false
            };
            CancelButton.Click += CancelButton_Click;
            buttonPanel.Children.Add(CancelButton);
            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);
            Content = mainGrid;
        }
    }
}
