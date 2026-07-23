using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VSAgent.Models;
using VSAgent.Services;
using VSAgent.Services.VisualStudio;

namespace VSAgent.Views
{
    public partial class VSAgentControl : UserControl
    {
        private const string PromptPlaceholder = "Describe the task... (Ctrl+Enter to send)";
        private readonly ChatGPTService agentService;
        private readonly ObservableCollection<ChatHistoryEntry> chatHistory;
        private CancellationTokenSource currentCancellationTokenSource;
        private bool hostEventsAttached;

        public VSAgentControl()
        {
            InitializeComponent();
            agentService = new ChatGPTService();
            chatHistory = new ObservableCollection<ChatHistoryEntry>();
            HistoryListBox.ItemsSource = chatHistory;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AttachHostEvents();
            SetBusyState(false, VSAgentPackage.AgentHost?.IsReady == true
                ? "oh-my-pi connected."
                : "oh-my-pi is not connected. Check the Runtime or PATH configuration.");
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DetachHostEvents();
            currentCancellationTokenSource?.Cancel();
        }

        private void AttachHostEvents()
        {
            if (hostEventsAttached || VSAgentPackage.AgentHost == null) return;
            VSAgentPackage.AgentHost.StatusChanged += AgentHost_StatusChanged;
            VSAgentPackage.AgentHost.TextReceived += AgentHost_TextReceived;
            hostEventsAttached = true;
        }

        private void DetachHostEvents()
        {
            if (!hostEventsAttached || VSAgentPackage.AgentHost == null) return;
            VSAgentPackage.AgentHost.StatusChanged -= AgentHost_StatusChanged;
            VSAgentPackage.AgentHost.TextReceived -= AgentHost_TextReceived;
            hostEventsAttached = false;
        }

        private void AgentHost_StatusChanged(object sender, string status) =>
            Dispatcher.BeginInvoke(new Action(() => StatusTextBlock.Text = status));

        private void AgentHost_TextReceived(object sender, string text)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!string.IsNullOrEmpty(text))
                {
                    ResponseTextBox.AppendText(text);
                    ResponseScrollViewer.ScrollToEnd();
                }
            }));
        }

        private void SendButton_Click(object sender, RoutedEventArgs e) => _ = SendPromptAsync();

        private void PromptTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                _ = SendPromptAsync();
            }
        }

        private async Task SendPromptAsync()
        {
            var prompt = PromptTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(prompt) || string.Equals(prompt, PromptPlaceholder, StringComparison.Ordinal)) return;

            currentCancellationTokenSource?.Cancel();
            currentCancellationTokenSource?.Dispose();
            currentCancellationTokenSource = new CancellationTokenSource();
            var token = currentCancellationTokenSource.Token;

            try
            {
                AttachHostEvents();
                SetBusyState(true, "oh-my-pi is working...");
                ResponseTextBox.Clear();
                var context = GetActiveEditorContext();
                var response = await agentService.SendCustomPromptAsync(prompt, context, token);
                chatHistory.Insert(0, new ChatHistoryEntry
                {
                    Prompt = prompt,
                    Response = response,
                    CodeContext = context,
                    OperationType = "OMP Agent"
                });
                ShowResult("oh-my-pi", response);
                PromptTextBox.Text = PromptPlaceholder;
                SetBusyState(false, "Request completed.");
            }
            catch (OperationCanceledException)
            {
                SetBusyState(false, "Request cancelled.");
            }
            catch (Exception ex)
            {
                SetBusyState(false, "Error: " + ex.Message);
                ShowResult("Agent error", "Error: " + ex.Message);
            }
            finally
            {
                currentCancellationTokenSource?.Dispose();
                currentCancellationTokenSource = null;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => currentCancellationTokenSource?.Cancel();

        private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HistoryListBox.SelectedItem is ChatHistoryEntry entry)
            {
                ShowResult(entry.OperationType + " - " + entry.Timestamp.ToString("g"), entry.Response);
                PromptTextBox.Text = entry.Prompt;
            }
        }

        public void ShowResult(string title, string content)
        {
            Dispatcher.Invoke(() =>
            {
                ResponseTextBox.Text = "[" + title + "]" + Environment.NewLine + Environment.NewLine + content;
                MainTabControl.SelectedItem = ChatTab;
                ResponseScrollViewer.ScrollToTop();
            });
        }

        private void SetBusyState(bool isBusy, string statusMessage)
        {
            Dispatcher.Invoke(() =>
            {
                SendButton.IsEnabled = !isBusy;
                CancelButton.IsEnabled = isBusy;
                StatusTextBlock.Text = statusMessage;
            });
        }

        private string GetActiveEditorContext()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
                return dte == null ? string.Empty : new EditorContextService(dte).GetActiveDocumentContext();
            }
            catch { return string.Empty; }
        }
    }
}
