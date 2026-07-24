using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using VSAgent.Models;
using VSAgent.Services;
using VSAgent.Services.Omp;
using VSAgent.Services.VisualStudio;
using VSAgent.Ui;

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
        private string currentResponseBuffer = string.Empty;
        private Border userMessageCard;
        private Border assistantMessageCard;
        private FlowDocumentScrollViewer currentResponseView;
        private readonly Dictionary<string, ToolCallCard> activeToolCards = new Dictionary<string, ToolCallCard>();
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
            BuildSkillsTab();
            BuildToolsTab();
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
                settingsView = new SettingsView(
                    VSAgentPackage.Credentials,
                    VSAgentPackage.Skills,
                    VSAgentPackage.ActiveSkills,
                    VSAgentPackage.WebSearchConfig,
                    () => VSAgentPackage.Env,
                    env =>
                    {
                        VSAgentPackage.Env = env;
                        VSAgentPackage.WebSearch.Save(env == null ? VSAgentPackage.WebSearchConfig : ApplyWebSearch(env, VSAgentPackage.WebSearchConfig));
                        ApplyEnvToHost();
                    })
                {
                    Margin = new Thickness(0)
                };
                settingsView.SettingsChanged += OnSettingsChanged;
                var tab = new TabItem { Header = "Settings", Content = settingsView };
                ReplacePlaceholderTab("SkillsTab", null);
                ReplacePlaceholderTab("ToolsTab", null);
                MainTabControl.Items.Add(tab);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to build settings tab: " + ex);
            }
        }

        private void BuildSkillsTab()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var skillsView = new SkillsView(VSAgentPackage.Skills, VSAgentPackage.ActiveSkills) { Margin = new Thickness(0) };
                ReplacePlaceholderTab("SkillsTab", skillsView);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to build skills tab: " + ex);
            }
        }

        private void BuildToolsTab()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var toolsView = new CustomToolsView(VSAgentPackage.CustomTools) { Margin = new Thickness(0) };
                ReplacePlaceholderTab("ToolsTab", toolsView);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to build tools tab: " + ex);
            }
        }

        private void ReplacePlaceholderTab(string name, object newContent)
        {
            foreach (var item in MainTabControl.Items)
            {
                if (item is TabItem ti && ti.Name == name)
                {
                    if (newContent != null) ti.Content = newContent;
                    else { MainTabControl.Items.Remove(ti); return; }
                }
            }
            if (newContent != null)
            {
                var header = name == "SkillsTab" ? "Skills" : "Tools";
                MainTabControl.Items.Add(new TabItem { Header = header, Content = newContent });
            }
        }

        private static WebSearchConfig ApplyWebSearch(OmpEnvironment env, WebSearchConfig current)
        {
            current.Provider = env.SearchProvider ?? current.Provider ?? "native";
            return current;
        }

        private void ApplyEnvToHost()
        {
            try
            {
                var host = VSAgentPackage.AgentHost;
                if (host != null && settingsView != null)
                {
                    var env = VSAgentPackage.Env;
                    host.ModelProvider = env?.ActiveProvider;
                    host.ModelName = env?.ActiveModel;
                    host.AutoCompactThresholdPercent = env?.AutoCompactThresholdPercent ?? 0;
                }
            }
            catch { }
        }

        private void AttachHostEvents()
        {
            if (hostEventsAttached || VSAgentPackage.AgentHost == null) return;
            var host = VSAgentPackage.AgentHost;
            host.StatusChanged += AgentHost_StatusChanged;
            host.TextReceived += AgentHost_TextReceived;
            var client = TryGetOmpClient(host);
            if (client != null) client.ToolCallReceived += AgentHost_ToolCallReceived;
            hostEventsAttached = true;
        }

        private static OmpAcpClient TryGetOmpClient(AgentHostService host)
        {
            try
            {
                var f = typeof(AgentHostService).GetField("client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return f?.GetValue(host) as OmpAcpClient;
            }
            catch { return null; }
        }

        private void DetachHostEvents()
        {
            if (!hostEventsAttached || VSAgentPackage.AgentHost == null) return;
            var host = VSAgentPackage.AgentHost;
            host.StatusChanged -= AgentHost_StatusChanged;
            host.TextReceived -= AgentHost_TextReceived;
            var client = TryGetOmpClient(host);
            if (client != null) client.ToolCallReceived -= AgentHost_ToolCallReceived;
            hostEventsAttached = false;
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            try { settingsView?.Commit(); } catch { }
        }

        private void CancelCurrent()
        {
            currentCancellationTokenSource?.Cancel();
            SetTask("Cancelling...");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => CancelCurrent();



        private void AgentHost_StatusChanged(object sender, string status) =>
            Dispatcher.BeginInvoke(new Action(() => SetTask(status)));

        private void AgentHost_TextReceived(object sender, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Dispatcher.BeginInvoke(new Action(() => AppendAssistantText(text)));
        }

        private void AgentHost_ToolCallReceived(object sender, AcpToolCall call)
        {
            Dispatcher.BeginInvoke(new Action(() => AddToolCallCard(call)));
        }

        private void AppendAssistantText(string text)
        {
            if (WelcomeOverlay != null && WelcomeOverlay.Visibility != Visibility.Collapsed)
                WelcomeOverlay.Visibility = Visibility.Collapsed;
            EnsureAssistantCard();
            currentResponseBuffer += text;
            currentResponseView.Document = Markdown.Parse(currentResponseBuffer);
            ResponseScrollViewer.ScrollToEnd();
            contextUsage.AddOutput(text);
            UpdateCtxDisplay();
        }

        private void AddToolCallCard(AcpToolCall call)
        {
            if (WelcomeOverlay != null && WelcomeOverlay.Visibility != Visibility.Collapsed)
                WelcomeOverlay.Visibility = Visibility.Collapsed;
            if (string.IsNullOrEmpty(call.Id)) call.Id = Guid.NewGuid().ToString("N");
            if (activeToolCards.TryGetValue(call.Id, out var existing))
            {
                existing.Call = call;
                // Replace the card so the markdown output re-renders
                int idx = ChatTranscript.Children.IndexOf(existing);
                if (idx >= 0)
                {
                    var fresh = new ToolCallCard(call);
                    ChatTranscript.Children[idx] = fresh;
                    activeToolCards[call.Id] = fresh;
                }
            }
            else
            {
                var card = new ToolCallCard(call);
                ChatTranscript.Children.Add(card);
                activeToolCards[call.Id] = card;
            }
            ResponseScrollViewer.ScrollToEnd();
        }

        private void EnsureAssistantCard()
        {
            if (currentResponseView != null) return;
            var header = new TextBlock
            {
                Text = "oh-my-pi",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 0, 0, 4),
                Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7))
            };
            currentResponseView = new FlowDocumentScrollViewer
            {
                Document = Markdown.Parse(string.Empty),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var stack = new StackPanel { Margin = new Thickness(6, 4, 6, 8) };
            stack.Children.Add(header);
            stack.Children.Add(currentResponseView);
            assistantMessageCard = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x45)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(6, 4, 6, 4),
                Child = stack
            };
            ChatTranscript.Children.Add(assistantMessageCard);
        }

        private void BeginUserTurn(string prompt)
        {
            userMessageCard = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x6B, 0xD9)),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(60, 4, 6, 4),
                Padding = new Thickness(8, 6, 8, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = new TextBlock
                {
                    Text = prompt,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Segoe UI")
                }
            };
            ChatTranscript.Children.Add(userMessageCard);
            currentResponseBuffer = string.Empty;
            currentResponseView = null;
            activeToolCards.Clear();
        }

        private void ClearTranscript()
        {
            ChatTranscript.Children.Clear();
            currentResponseBuffer = string.Empty;
            currentResponseView = null;
            activeToolCards.Clear();
            if (WelcomeOverlay != null) WelcomeOverlay.Visibility = Visibility.Visible;
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
                BeginUserTurn(prompt);
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
                    ClearTranscript();
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
                if (WelcomeOverlay != null) WelcomeOverlay.Visibility = Visibility.Collapsed;
                BeginUserTurn(title);
                currentResponseBuffer = content ?? string.Empty;
                EnsureAssistantCard();
                currentResponseView.Document = Markdown.Parse(content ?? string.Empty);
                MainTabControl.SelectedItem = ChatTab;
                ResponseScrollViewer.ScrollToEnd();
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
