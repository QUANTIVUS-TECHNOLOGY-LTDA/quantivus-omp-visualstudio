using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
        private const string PromptPlaceholder = "Describe the task... (Ctrl+Enter to send, type / for commands, Esc to cancel)";

        private readonly ChatGPTService agentService;
        private readonly ObservableCollection<ChatHistoryEntry> chatHistory;
        private readonly ContextUsageTracker contextUsage = new ContextUsageTracker();
        private readonly CommandCompletionPopup commandPopup;
        private SettingsView settingsView;

        private CancellationTokenSource currentCancellationTokenSource;
        private bool hostEventsAttached;
        private string currentTask = "Idle";
        private string currentBranch;

        public VSAgentControl()
        {
            InitializeComponent();
            agentService = new ChatGPTService();
            chatHistory = new ObservableCollection<ChatHistoryEntry>();
            HistoryListBox.ItemsSource = chatHistory;

            commandPopup = new CommandCompletionPopup(PromptTextBox, SlashCommand.All);
            commandPopup.Committed += OnCommandCommitted;

            var host = VSAgentPackage.AgentHost;
            if (host != null)
            {
                host.Queue.Changed += (_, __) => Dispatcher.BeginInvoke(new Action(RefreshQueueUi));
                RefreshQueueUi();
            }

            PromptTextBox.KeyDown += PromptTextBox_KeyDown;
            PromptTextBox.PreviewKeyDown += PromptTextBox_PreviewKeyDown;
            PromptTextBox.TextChanged += PromptTextBox_TextChanged;
            PromptTextBox.GotFocus += (_, __) => PromptTextBox.Text = StripPlaceholder(PromptTextBox.Text);
            PromptTextBox.LostFocus += (_, __) => { if (string.IsNullOrWhiteSpace(PromptTextBox.Text)) PromptTextBox.Text = PromptPlaceholder; };

            PreviewKeyDown += VSAgentControl_PreviewKeyDown;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AttachHostEvents();
            RefreshGitBranch();
            UpdateCtxDisplay();
            SetTask("Idle");
            WelcomeOverlay?.Start();
            BuildSettingsTab();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DetachHostEvents();
            currentCancellationTokenSource?.Cancel();
            commandPopup.Hide();
        }

        private void BuildSettingsTab()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                settingsView = new SettingsView()
                {
                    Margin = new Thickness(0)
                };
                settingsView.SettingsChanged += OnSettingsChanged;
                var tab = new TabItem { Header = "Settings", Content = settingsView };
                MainTabControl.Items.Add(tab);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to build settings tab: " + ex);
            }
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            // Persist to dialog page on every change
            try { settingsView?.ApplyToOptions(); } catch { }
            // Push model + threshold into the running AgentHost
            try { settingsView?.ApplyToAgentHost(VSAgentPackage.AgentHost); } catch { }
        }

        // ---- Host events ----
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
            Dispatcher.BeginInvoke(new Action(() => SetTask(status)));

        private void AgentHost_TextReceived(object sender, string text)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (string.IsNullOrEmpty(text)) return;
                ResponseTextBox.AppendText(text);
                ResponseScrollViewer.ScrollToEnd();
                contextUsage.AddOutput(text);
                UpdateCtxDisplay();
            }));
        }

        // ---- Status bar ----
        private void SetTask(string task)
        {
            currentTask = string.IsNullOrWhiteSpace(task) ? "Idle" : task;
            TaskTextBlock.Text = currentTask;
        }

        private void UpdateCtxDisplay() => CtxTextBlock.Text = contextUsage.FormatUsage();

        private void RefreshGitBranch()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = (DTE2)ServiceProvider.GlobalProvider.GetService(typeof(DTE));
                var solutionPath = dte?.Solution?.FullName;
                if (string.IsNullOrEmpty(solutionPath))
                {
                    currentBranch = "no solution";
                }
                else
                {
                    var dir = Path.GetDirectoryName(solutionPath);
                    var status = GitBranchService.GetStatus(dir);
                    currentBranch = status == null ? "no git" : (status.Detached ? $"detached:{status.Branch}" : status.Branch);
                }
            }
            catch { currentBranch = "—"; }
            BranchTextBlock.Text = currentBranch ?? "—";
        }

        // ---- Send / Cancel ----
        private void SendButton_Click(object sender, RoutedEventArgs e) => _ = SendPromptAsync();

        private void CancelButton_Click(object sender, RoutedEventArgs e) => CancelCurrent();

        private void CancelCurrent()
        {
            currentCancellationTokenSource?.Cancel();
            SetTask("Cancelling...");
        }

        private async Task SendPromptAsync()
        {
            var prompt = PromptTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(prompt) || string.Equals(prompt, PromptPlaceholder, StringComparison.Ordinal)) return;

            commandPopup.Hide();

            currentCancellationTokenSource?.Cancel();
            currentCancellationTokenSource?.Dispose();
            currentCancellationTokenSource = new CancellationTokenSource();
            var token = currentCancellationTokenSource.Token;

            try
            {
                AttachHostEvents();
                SetBusyState(true, "oh-my-pi is working...");
                if (WelcomeOverlay != null) WelcomeOverlay.Visibility = Visibility.Collapsed;
                ResponseTextBox.Clear();
                var context = GetActiveEditorContext();
                var totalInput = (prompt.Length) + (context?.Length ?? 0);
                contextUsage.AddInput(prompt);
                contextUsage.AddInput(context ?? string.Empty);
                UpdateCtxDisplay();
                VSAgentPackage.AgentHost?.AddInputChars(totalInput);
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
                SetTask("Idle");
            }
            catch (OperationCanceledException)
            {
                SetTask("Cancelled");
            }
            catch (Exception ex)
            {
                SetTask("Error: " + ex.Message);
                ShowResult("Agent error", "Error: " + ex.Message);
            }
            finally
            {
                currentCancellationTokenSource?.Dispose();
                currentCancellationTokenSource = null;
                SetBusyState(false, currentTask);
            }
        }

        // ---- Keys ----
        private void PromptTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (commandPopup.IsOpen)
            {
                switch (e.Key)
                {
                    case Key.Enter:
                    case Key.Tab:
                        commandPopup.Commit();
                        e.Handled = true;
                        return;
                    case Key.Escape:
                        commandPopup.Hide();
                        e.Handled = true;
                        return;
                    case Key.Down:
                        commandPopup.SelectNext();
                        e.Handled = true;
                        return;
                    case Key.Up:
                        commandPopup.SelectPrevious();
                        e.Handled = true;
                        return;
                }
            }

            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                _ = SendPromptAsync();
                return;
            }

            if (e.Key == Key.Escape && currentCancellationTokenSource != null && !currentCancellationTokenSource.IsCancellationRequested)
            {
                CancelCurrent();
                e.Handled = true;
            }
        }

        private void PromptTextBox_KeyDown(object sender, KeyEventArgs e) { /* preview handles everything */ }

        private void VSAgentControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && currentCancellationTokenSource != null && !currentCancellationTokenSource.IsCancellationRequested)
            {
                CancelCurrent();
                e.Handled = true;
            }
        }

        private void PromptTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = PromptTextBox.Text ?? "";
            if (string.Equals(text, PromptPlaceholder, StringComparison.Ordinal)) return;

            var caret = PromptTextBox.CaretIndex;
            int slashPos = -1;
            for (int i = caret - 1; i >= 0; i--)
            {
                char c = text[i];
                if (c == '/') { slashPos = i; break; }
                if (char.IsWhiteSpace(c) || c == '\n' || c == '\r') break;
            }
            if (slashPos < 0)
            {
                commandPopup.Hide();
                return;
            }

            var filter = text.Substring(slashPos, caret - slashPos);
            if (slashPos > 0 && !char.IsWhiteSpace(text[slashPos - 1]))
            {
                commandPopup.Hide();
                return;
            }
            commandPopup.Show(filter);
        }

        // ---- Slash command routing ----
        private async void OnCommandCommitted(SlashCommand cmd)
        {
            if (cmd == null) return;
            switch (cmd.Kind)
            {
                case SlashCommandKind.LocalClear:
                    ResponseTextBox.Clear();
                    chatHistory.Clear();
                    contextUsage.Reset();
                    UpdateCtxDisplay();
                    SetTask("Cleared");
                    ResetPromptToPlaceholder();
                    return;

                case SlashCommandKind.LocalCancel:
                    CancelCurrent();
                    ResetPromptToPlaceholder();
                    return;

                case SlashCommandKind.SteerImmediate:
                    await ExecuteSteerImmediate(cmd);
                    return;

                case SlashCommandKind.QueueAdd:
                    await ExecuteQueueAdd(cmd);
                    return;

                case SlashCommandKind.LocalClearQueue:
                {
                    var host = VSAgentPackage.AgentHost;
                    var n = host?.Queue.Count ?? 0;
                    host?.Queue.Clear();
                    SetTask(n > 0 ? $"Queue cleared ({n} items removed)." : "Queue was already empty.");
                    ResetPromptToPlaceholder();
                    return;
                }

                case SlashCommandKind.SkillActivate:
                    ExecuteSkillActivate(cmd);
                    ResetPromptToPlaceholder();
                    return;

                case SlashCommandKind.SkillDeactivate:
                    ExecuteSkillDeactivate(cmd);
                    ResetPromptToPlaceholder();
                    return;

                case SlashCommandKind.SkillClear:
                    VSAgentPackage.AgentHost?.ActiveSkills.Clear();
                    SetTask("All active skills cleared.");
                    ResetPromptToPlaceholder();
                    return;

                case SlashCommandKind.Remote:
                default:
                    ReplacePromptWith(cmd.PromptText ?? "");
                    return;
            }
        }

        private void ExecuteSkillActivate(SlashCommand cmd)
        {
            var name = ExtractAfterCommand(cmd);
            if (string.IsNullOrEmpty(name))
            {
                var list = VSAgentPackage.AgentHost?.ActiveSkills.Snapshot();
                SetTask(list == null || list.Count == 0
                    ? "No active skills. Type /skill <name> to activate one."
                    : "Active skills: " + string.Join(", ", list.Select(s => s.Name)));
                return;
            }
            var ok = VSAgentPackage.AgentHost?.ActiveSkills.Activate(name) ?? false;
            SetTask(ok ? $"Skill '{name}' activated." : $"No skill named '{name}'. Type /skill <name>.");
        }

        private void ExecuteSkillDeactivate(SlashCommand cmd)
        {
            var name = ExtractAfterCommand(cmd);
            if (string.IsNullOrEmpty(name))
            {
                SetTask("Usage: /skill-off <name>");
                return;
            }
            var ok = VSAgentPackage.AgentHost?.ActiveSkills.Deactivate(name) ?? false;
            SetTask(ok ? $"Skill '{name}' deactivated." : $"No active skill named '{name}'.");
        }

        private async Task ExecuteSteerImmediate(SlashCommand cmd)
        {
            var message = ExtractAfterCommand(cmd);
            if (string.IsNullOrEmpty(message))
            {
                SetTask("Steer: type a message after /steer");
                ResetPromptToPlaceholder();
                return;
            }

            contextUsage.AddInput(message);
            UpdateCtxDisplay();
            SetTask("Steering the agent...");
            try
            {
                await agentService.SteerAsync(message, currentCancellationTokenSource?.Token ?? CancellationToken.None);
                SetTask("Steering message delivered.");
            }
            catch (Exception ex)
            {
                SetTask("Steer failed: " + ex.Message);
            }
            ResetPromptToPlaceholder();
        }

        private async Task ExecuteQueueAdd(SlashCommand cmd)
        {
            var text = ExtractAfterCommand(cmd);
            if (string.IsNullOrEmpty(text))
            {
                var count = VSAgentPackage.AgentHost?.Queue.Count ?? 0;
                SetTask(count == 0
                    ? "Queue is empty."
                    : $"Queue has {count} message(s). Type \"/queue <msg>\" to add, \"/queue-clear\" to clear.");
                ResetPromptToPlaceholder();
                return;
            }

            var msg = VSAgentPackage.AgentHost?.Queue.Enqueue(text);
            if (msg == null)
            {
                SetTask("Queue unavailable (agent host not initialized).");
            }
            else
            {
                contextUsage.AddInput(text);
                UpdateCtxDisplay();
                SetTask($"Queued: {Truncate(text, 60)}");
            }
            ResetPromptToPlaceholder();
        }

        private string ExtractAfterCommand(SlashCommand cmd)
        {
            var text = PromptTextBox.Text ?? "";
            var caret = PromptTextBox.CaretIndex;
            int slashPos = -1;
            for (int i = caret - 1; i >= 0; i--)
            {
                char c = text[i];
                if (c == '/') { slashPos = i; break; }
                if (char.IsWhiteSpace(c) || c == '\n' || c == '\r') break;
            }
            if (slashPos < 0) return string.Empty;
            var msgStart = slashPos + cmd.Name.Length;
            if (msgStart < text.Length && text[msgStart] == ' ') msgStart++;
            return text.Substring(msgStart).Trim();
        }

        private void ResetPromptToPlaceholder()
        {
            PromptTextBox.Text = PromptPlaceholder;
            commandPopup.Hide();
        }

        private void ReplacePromptWith(string replacement)
        {
            var text = PromptTextBox.Text ?? "";
            var caret = PromptTextBox.CaretIndex;
            int slashPos = -1;
            for (int i = caret - 1; i >= 0; i--)
            {
                char c = text[i];
                if (c == '/') { slashPos = i; break; }
                if (char.IsWhiteSpace(c) || c == '\n' || c == '\r') break;
            }
            if (slashPos < 0) slashPos = 0;
            var newText = text.Substring(0, slashPos) + replacement + text.Substring(caret);
            PromptTextBox.Text = newText;
            PromptTextBox.CaretIndex = slashPos + replacement.Length;
            PromptTextBox.Focus();
        }

        private static string StripPlaceholder(string text) =>
            text == PromptPlaceholder ? string.Empty : text;

        // ---- Queue UI ----
        private void RefreshQueueUi()
        {
            var host = VSAgentPackage.AgentHost;
            if (host == null || QueuePanel == null) return;
            var items = host.Queue.Snapshot();
            if (items.Count == 0)
            {
                QueuePanel.Visibility = Visibility.Collapsed;
                QueueItemsPanel.Children.Clear();
                return;
            }
            QueuePanel.Visibility = Visibility.Visible;
            QueueHeader.Text = $"Queued follow-up messages ({items.Count}) \u2014 sent as the next turn after the current run";
            QueueItemsPanel.Children.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                QueueItemsPanel.Children.Add(BuildQueueItemRow(i + 1, items[i]));
            }
        }

        private FrameworkElement BuildQueueItemRow(int displayIndex, QueuedMessage msg)
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), LastChildFill = true };

            var removeBtn = new Button
            {
                Content = "X",
                Width = 22,
                Height = 20,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(0),
                ToolTip = "Remove from queue",
                FontSize = 11
            };
            var capturedId = msg.Id;
            removeBtn.Click += (_, __) => VSAgentPackage.AgentHost?.Queue.Remove(capturedId);
            DockPanel.SetDock(removeBtn, Dock.Right);
            row.Children.Add(removeBtn);

            var preview = new TextBlock
            {
                Text = $"{displayIndex}.  {Truncate(msg.Text, 140)}",
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            preview.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            row.Children.Add(preview);

            return row;
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length <= max ? text : text.Substring(0, Math.Max(0, max - 1)) + "\u2026";
        }

        // ---- History ----
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
                CancelButton.IsEnabled = isBusy || (currentCancellationTokenSource != null);
                SetTask(statusMessage);
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
