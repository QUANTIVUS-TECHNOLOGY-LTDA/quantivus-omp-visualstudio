using EnvDTE;
using EnvDTE80;
using Microsoft.Win32;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VSAgent.Models;
using VSAgent.Services;
using VSAgent.Services.Omp;
using VSAgent.Services.VisualStudio;
using VSAgent.Ui;

namespace VSAgent.Views
{
    public partial class VSAgentControl
    {
        private ListBox WorkbenchNavigation;
        private ColumnDefinition NavigationColumn;
        private TextBlock WorkbenchSectionTitleTextBlock;
        private TextBlock HeaderStatusTextBlock;
        private TextBlock ProviderTextBlock;
        private TextBlock DurationTextBlock;
        private TextBlock ChangedFilesTextBlock;
        private TextBox ChatSearchTextBox;
        private Button RestartAgentButton;
        private Button StopAgentButton;
        private WorkbenchStore workbenchStore;
        private TaskCenterView taskCenterView;
        private AgentWorkspaceView agentWorkspaceView;
        private RepositoryOverviewView repositoryOverviewView;
        private GitChangesView gitChangesView;
        private TerminalView terminalView;
        private ContextInspectorView contextInspectorView;
        private PromptLibraryView promptLibraryView;
        private SessionsWorkspaceView sessionsWorkspaceView;
        private DiagnosticsView diagnosticsView;
        private DispatcherTimer workbenchTimer;
        private ChatSession currentSession;
        private DateTime sessionStartedAt = DateTime.Now;
        private bool restoringSession;
        private bool workbenchEventsAttached;
        private int appliedContextCharacters;

        private void EnsureWorkbenchStore()
        {
            if (workbenchStore == null) workbenchStore = new WorkbenchStore();
        }

        private UIElement CreateTaskCenterPage()
        {
            taskCenterView = new TaskCenterView(VSAgentPackage.AgentHost);
            taskCenterView.CancelRequested += delegate { CancelCurrent(); };
            return taskCenterView;
        }

        private UIElement CreateAgentWorkspacePage()
        {
            EnsureWorkbenchStore();
            agentWorkspaceView = new AgentWorkspaceView(workbenchStore, VSAgentPackage.Skills, VSAgentPackage.ActiveSkills);
            agentWorkspaceView.ProfileActivated += AgentWorkspace_ProfileActivated;
            return agentWorkspaceView;
        }

        private UIElement CreateRepositoryPage()
        {
            repositoryOverviewView = new RepositoryOverviewView();
            return repositoryOverviewView;
        }

        private UIElement CreateChangesPage()
        {
            gitChangesView = new GitChangesView();
            gitChangesView.ChangedFileCountChanged += delegate(object sender, int count)
            {
                if (ChangedFilesTextBlock != null) ChangedFilesTextBlock.Text = count + " changed";
            };
            return gitChangesView;
        }

        private UIElement CreateTerminalPage()
        {
            terminalView = new TerminalView();
            return terminalView;
        }

        private UIElement CreateContextPage()
        {
            EnsureWorkbenchStore();
            contextInspectorView = new ContextInspectorView(workbenchStore, VSAgentPackage.Skills, VSAgentPackage.ActiveSkills);
            contextInspectorView.ContextSizeChanged += delegate(object sender, int characters)
            {
                appliedContextCharacters = characters;
                UpdateCtxDisplayWithWorkbench();
            };
            return contextInspectorView;
        }

        private UIElement CreatePromptsPage()
        {
            EnsureWorkbenchStore();
            promptLibraryView = new PromptLibraryView(workbenchStore);
            promptLibraryView.PromptSelected += PromptLibrary_PromptSelected;
            return promptLibraryView;
        }

        private UIElement CreateSessionsPage()
        {
            EnsureWorkbenchStore();
            sessionsWorkspaceView = new SessionsWorkspaceView(
                workbenchStore,
                HistoryListBox,
                CaptureCurrentSession,
                RestoreSession,
                CreateNewSession);
            return sessionsWorkspaceView;
        }

        private UIElement CreateDiagnosticsPage()
        {
            EnsureWorkbenchStore();
            diagnosticsView = new DiagnosticsView(VSAgentPackage.AgentHost, workbenchStore);
            diagnosticsView.RestartRequested += async delegate { await RestartAgentAsync(); };
            diagnosticsView.StopRequested += delegate { StopAgent(); };
            return diagnosticsView;
        }

        private void InitializeWorkbenchBehaviors()
        {
            EnsureWorkbenchStore();
            AllowDrop = true;
            DragOver += Workbench_DragOver;
            Drop += Workbench_Drop;
            Loaded += delegate
            {
                Dispatcher.BeginInvoke(new Action(WorkbenchLoaded), DispatcherPriority.ContextIdle);
            };
            Unloaded += delegate { WorkbenchUnloaded(); };
            if (ChatTranscript != null) ChatTranscript.LayoutUpdated += delegate { ApplyTranscriptTheme(); };
        }

        private void WorkbenchLoaded()
        {
            if (workbenchEventsAttached) return;
            workbenchEventsAttached = true;
            if (VSAgentPackage.AgentHost != null)
                VSAgentPackage.AgentHost.StatusChanged += WorkbenchHost_StatusChanged;
            if (chatHistory != null)
                chatHistory.CollectionChanged += ChatHistory_CollectionChanged;

            var preferences = workbenchStore.Preferences;
            SetNavigationCollapsed(preferences.NavigationCollapsed);
            sessionStartedAt = DateTime.Now;
            workbenchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            workbenchTimer.Tick += WorkbenchTimer_Tick;
            workbenchTimer.Start();
            SyncNavigationFromTab();
            UpdateWorkbenchHeader();
            ApplyTranscriptTheme();

            if (chatHistory.Count == 0 && !string.IsNullOrWhiteSpace(preferences.ActiveSessionId))
            {
                var saved = workbenchStore.GetSession(preferences.ActiveSessionId);
                if (saved != null && saved.Entries.Count > 0) RestoreSession(saved);
            }
        }

        private void WorkbenchUnloaded()
        {
            if (!workbenchEventsAttached) return;
            workbenchEventsAttached = false;
            if (VSAgentPackage.AgentHost != null)
                VSAgentPackage.AgentHost.StatusChanged -= WorkbenchHost_StatusChanged;
            if (chatHistory != null)
                chatHistory.CollectionChanged -= ChatHistory_CollectionChanged;
            workbenchTimer?.Stop();
            workbenchTimer = null;
            SaveCurrentSession(false);
        }

        private void WorkbenchHost_StatusChanged(object sender, string status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (HeaderStatusTextBlock != null) HeaderStatusTextBlock.Text = string.IsNullOrWhiteSpace(status) ? "Idle" : status;
            }));
        }

        private void WorkbenchTimer_Tick(object sender, EventArgs e)
        {
            var elapsed = DateTime.Now - sessionStartedAt;
            if (DurationTextBlock != null) DurationTextBlock.Text = elapsed.TotalHours >= 1
                ? elapsed.ToString(@"hh\:mm\:ss")
                : elapsed.ToString(@"mm\:ss");
            UpdateWorkbenchHeader();
        }

        private void UpdateWorkbenchHeader()
        {
            if (HeaderStatusTextBlock != null) HeaderStatusTextBlock.Text = currentTask ?? "Idle";
            if (ProviderTextBlock != null)
            {
                var provider = VSAgentPackage.Env?.ActiveProvider;
                var model = VSAgentPackage.Env?.ActiveModel;
                ProviderTextBlock.Text = string.IsNullOrWhiteSpace(provider) ? "Default provider" : provider;
                if (!string.IsNullOrWhiteSpace(model)) ProviderTextBlock.Text += " / " + model;
            }
            if (BranchTextBlock != null && string.IsNullOrWhiteSpace(BranchTextBlock.Text)) RefreshGitBranch();
        }

        private void UpdateCtxDisplayWithWorkbench()
        {
            UpdateCtxDisplay();
            if (appliedContextCharacters > 0)
                CtxTextBlock.Text += " + " + (appliedContextCharacters / 4).ToString("N0") + " ctx tokens";
        }

        private void ChatHistory_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!restoringSession) SaveCurrentSession(false);
            sessionsWorkspaceView?.Refresh();
        }

        private void SelectWorkbenchTab(string header)
        {
            if (string.IsNullOrWhiteSpace(header) || MainTabControl == null) return;
            foreach (var item in MainTabControl.Items)
            {
                if (item is TabItem tab && string.Equals(tab.Header?.ToString(), header, StringComparison.OrdinalIgnoreCase))
                {
                    MainTabControl.SelectedItem = tab;
                    if (WorkbenchSectionTitleTextBlock != null) WorkbenchSectionTitleTextBlock.Text = header;
                    return;
                }
            }
        }

        private void SyncNavigationFromTab()
        {
            if (!(MainTabControl?.SelectedItem is TabItem tab) || WorkbenchNavigation == null) return;
            var header = tab.Header?.ToString();
            foreach (var item in WorkbenchNavigation.Items.OfType<ListBoxItem>())
            {
                if (string.Equals(item.Tag as string, header, StringComparison.OrdinalIgnoreCase))
                {
                    WorkbenchNavigation.SelectedItem = item;
                    break;
                }
            }
            if (WorkbenchSectionTitleTextBlock != null) WorkbenchSectionTitleTextBlock.Text = header ?? "Workbench";
        }

        private void ToggleNavigation()
        {
            var collapsed = NavigationColumn != null && NavigationColumn.Width.Value > 80;
            SetNavigationCollapsed(collapsed);
            workbenchStore.UpdatePreferences(p => p.NavigationCollapsed = collapsed);
        }

        private void SetNavigationCollapsed(bool collapsed)
        {
            if (NavigationColumn == null || WorkbenchNavigation == null) return;
            NavigationColumn.Width = new GridLength(collapsed ? 64 : 220);
            foreach (var item in WorkbenchNavigation.Items.OfType<ListBoxItem>())
            {
                if (item.Content is Grid grid && grid.Children.OfType<StackPanel>().FirstOrDefault() is StackPanel textPanel)
                    textPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                item.ToolTip = collapsed ? item.Tag as string : null;
            }
        }

        private void FilterTranscript()
        {
            if (ChatTranscript == null || ChatSearchTextBox == null) return;
            var query = ChatSearchTextBox.Text?.Trim();
            foreach (UIElement child in ChatTranscript.Children)
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    child.Visibility = Visibility.Visible;
                    continue;
                }
                var text = ExtractText(child);
                child.Visibility = text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private static string ExtractText(object value)
        {
            if (value == null) return string.Empty;
            if (value is TextBlock textBlock) return textBlock.Text ?? string.Empty;
            if (value is TextBox textBox) return textBox.Text ?? string.Empty;
            if (value is FlowDocumentScrollViewer viewer && viewer.Document != null)
                return new TextRange(viewer.Document.ContentStart, viewer.Document.ContentEnd).Text;
            if (value is ContentControl contentControl) return ExtractText(contentControl.Content);
            if (value is Panel panel)
            {
                var builder = new StringBuilder();
                foreach (UIElement child in panel.Children) builder.AppendLine(ExtractText(child));
                return builder.ToString();
            }
            if (value is Decorator decorator) return ExtractText(decorator.Child);
            return value.ToString() ?? string.Empty;
        }

        private void CopyConversation()
        {
            var session = CaptureCurrentSession();
            if (session == null) return;
            Clipboard.SetText(SessionToMarkdown(session));
            SetTask("Conversation copied.");
        }

        private void ExportConversation()
        {
            var session = CaptureCurrentSession();
            if (session == null) return;
            var dialog = new SaveFileDialog
            {
                Title = "Export OMP conversation",
                Filter = "Markdown (*.md)|*.md|JSON (*.json)|*.json",
                FileName = MakeSafeFileName(session.Name) + ".md",
                AddExtension = true
            };
            if (dialog.ShowDialog() != true) return;
            if (string.Equals(Path.GetExtension(dialog.FileName), ".json", StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(dialog.FileName, JsonConvert.SerializeObject(session, Formatting.Indented), new UTF8Encoding(false));
            else
                File.WriteAllText(dialog.FileName, SessionToMarkdown(session), new UTF8Encoding(false));
            SetTask("Conversation exported.");
        }

        private void CreateNewSession()
        {
            SaveCurrentSession(false);
            restoringSession = true;
            try
            {
                chatHistory.Clear();
                ClearTranscript();
                contextUsage.Reset();
                appliedContextCharacters = 0;
                UpdateCtxDisplay();
                currentSession = new ChatSession { Name = "New session" };
                sessionStartedAt = DateTime.Now;
                PromptTextBox.Text = PromptPlaceholder;
                SelectWorkbenchTab("Chat");
                workbenchStore.UpdatePreferences(p => p.ActiveSessionId = currentSession.Id);
                SetTask("New session");
            }
            finally { restoringSession = false; }
        }

        private ChatSession CaptureCurrentSession()
        {
            if (chatHistory == null || chatHistory.Count == 0) return currentSession;
            ThreadHelper.ThrowIfNotOnUIThread();
            if (currentSession == null) currentSession = new ChatSession();
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
            currentSession.Name = string.IsNullOrWhiteSpace(currentSession.Name) || currentSession.Name == "New session"
                ? DeriveSessionName()
                : currentSession.Name;
            currentSession.SolutionPath = dte?.Solution?.FullName;
            currentSession.Branch = currentBranch;
            currentSession.Provider = VSAgentPackage.Env?.ActiveProvider;
            currentSession.Model = VSAgentPackage.Env?.ActiveModel;
            var profileId = workbenchStore.Preferences.ActiveAgentProfileId;
            currentSession.ActiveAgentProfile = workbenchStore.SnapshotAgentProfiles().FirstOrDefault(p => p.Id == profileId)?.Name;
            currentSession.Entries = chatHistory.OrderBy(e => e.Timestamp).Select(CloneHistoryEntry).ToList();
            return currentSession;
        }

        private void SaveCurrentSession(bool includeEmpty)
        {
            if (restoringSession || workbenchStore == null || chatHistory == null) return;
            if (!includeEmpty && chatHistory.Count == 0) return;
            try
            {
                var value = CaptureCurrentSession();
                if (value == null) return;
                currentSession = workbenchStore.UpsertSession(value);
                workbenchStore.UpdatePreferences(p => p.ActiveSessionId = currentSession.Id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Session autosave failed: " + ex);
            }
        }

        private void RestoreSession(ChatSession session)
        {
            if (session == null) return;
            restoringSession = true;
            try
            {
                currentSession = session;
                chatHistory.Clear();
                ClearTranscript();
                contextUsage.Reset();
                foreach (var entry in session.Entries.OrderBy(e => e.Timestamp))
                {
                    var clone = CloneHistoryEntry(entry);
                    chatHistory.Add(clone);
                    BeginUserTurn(clone.Prompt ?? string.Empty);
                    currentResponseBuffer = clone.Response ?? string.Empty;
                    EnsureAssistantCard();
                    currentResponseView.Document = Markdown.Parse(currentResponseBuffer);
                    contextUsage.AddInput(clone.Prompt ?? string.Empty);
                    contextUsage.AddOutput(clone.Response ?? string.Empty);
                }
                PromptTextBox.Text = PromptPlaceholder;
                UpdateCtxDisplayWithWorkbench();
                sessionStartedAt = DateTime.Now;
                SelectWorkbenchTab("Chat");
                ResponseScrollViewer.ScrollToEnd();
                SetTask("Session restored: " + session.Name);
            }
            finally
            {
                restoringSession = false;
                workbenchStore.UpdatePreferences(p => p.ActiveSessionId = session.Id);
            }
        }

        private async void PromptLibrary_PromptSelected(object sender, PromptTemplate prompt)
        {
            if (prompt == null) return;
            try
            {
                var expanded = await ExpandPromptVariablesAsync(prompt.Content ?? string.Empty);
                PromptTextBox.Text = expanded;
                PromptTextBox.CaretIndex = PromptTextBox.Text.Length;
                SelectWorkbenchTab("Chat");
                PromptTextBox.Focus();
                SetTask("Prompt loaded: " + prompt.Name);
            }
            catch (Exception ex) { SetTask("Prompt expansion failed: " + ex.Message); }
        }

        private async Task<string> ExpandPromptVariablesAsync(string value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
            var solution = dte?.Solution?.FullName ?? string.Empty;
            var project = dte?.ActiveSolutionProjects is Array projects && projects.Length > 0 && projects.GetValue(0) is Project p ? p.Name : string.Empty;
            var file = dte?.ActiveDocument?.FullName ?? string.Empty;
            var editor = dte == null ? null : new EditorContextService(dte);
            var selection = editor?.GetSelectedText() ?? string.Empty;
            var branch = currentBranch ?? string.Empty;
            var directory = string.IsNullOrWhiteSpace(solution) ? null : Path.GetDirectoryName(solution);
            var diff = string.Empty;
            if (value.IndexOf("{{diff}}", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrWhiteSpace(directory))
            {
                var root = GitCommandService.FindRepositoryRoot(directory);
                if (root != null)
                {
                    var result = await new GitCommandService().GetDiffAsync(root, null, false, CancellationToken.None);
                    diff = result.CombinedOutput;
                }
            }
            var errors = string.Empty;
            if (value.IndexOf("{{buildErrors}}", StringComparison.OrdinalIgnoreCase) >= 0 && dte != null)
            {
                var snapshot = new WorkspaceContextService(dte).CaptureSnapshot();
                errors = string.Join(Environment.NewLine, snapshot.BuildErrors);
            }
            return ReplaceVariable(ReplaceVariable(ReplaceVariable(ReplaceVariable(ReplaceVariable(ReplaceVariable(ReplaceVariable(
                value, "solution", solution), "project", project), "file", file), "selection", selection), "branch", branch), "diff", diff), "buildErrors", errors);
        }

        private static string ReplaceVariable(string text, string name, string value) =>
            (text ?? string.Empty).Replace("{{" + name + "}}", value ?? string.Empty);

        private void AgentWorkspace_ProfileActivated(object sender, AgentProfile profile)
        {
            if (profile == null) return;
            if (!string.IsNullOrWhiteSpace(profile.PreferredModel))
            {
                VSAgentPackage.Env.ActiveModel = profile.PreferredModel;
                if (VSAgentPackage.AgentHost != null) VSAgentPackage.AgentHost.ModelName = profile.PreferredModel;
            }
            UpdateWorkbenchHeader();
            SetTask("Agent profile active: " + profile.Name);
        }

        private async Task RestartAgentAsync()
        {
            var host = VSAgentPackage.AgentHost;
            if (host == null) return;
            try
            {
                SetTask("Restarting oh-my-pi…");
                TryGetOmpClient(host)?.Dispose();
                var method = typeof(AgentHostService).GetMethod("EnsureStartedAsync", BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null) throw new MissingMethodException("Agent restart method is unavailable.");
                var task = method.Invoke(host, new object[] { CancellationToken.None }) as Task;
                if (task != null) await task;
                SetTask(host.IsReady ? "oh-my-pi connected" : "oh-my-pi could not be started");
                diagnosticsView?.Refresh();
            }
            catch (Exception ex) { SetTask("Restart failed: " + (ex.InnerException?.Message ?? ex.Message)); }
        }

        private void StopAgent()
        {
            try
            {
                TryGetOmpClient(VSAgentPackage.AgentHost)?.Dispose();
                SetTask("oh-my-pi stopped");
                diagnosticsView?.Refresh();
            }
            catch (Exception ex) { SetTask("Stop failed: " + ex.Message); }
        }

        private void AttachSelectionToPrompt()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
            var selected = dte == null ? string.Empty : new EditorContextService(dte).GetSelectedText();
            if (string.IsNullOrWhiteSpace(selected))
            {
                SetTask("No editor selection.");
                return;
            }
            AppendToPrompt("\r\n\r\nSelected code from " + (dte.ActiveDocument?.FullName ?? "active editor") + ":\r\n```\r\n" + selected + "\r\n```");
        }

        private void AttachCurrentFileToPrompt()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
            var path = dte?.ActiveDocument?.FullName;
            if (string.IsNullOrWhiteSpace(path)) { SetTask("No active document."); return; }
            AppendToPrompt("\r\n\r\nUse this repository file: `" + path + "`");
        }

        private void AttachOpenDocumentsToPrompt()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
            if (dte == null) return;
            var paths = new List<string>();
            foreach (Document document in dte.Documents)
                if (!string.IsNullOrWhiteSpace(document.FullName)) paths.Add(document.FullName);
            if (paths.Count == 0) { SetTask("No open documents."); return; }
            AppendToPrompt("\r\n\r\nOpen Visual Studio documents:\r\n" + string.Join("\r\n", paths.Select(p => "- `" + p + "`")));
        }

        private void AppendToPrompt(string value)
        {
            var current = PromptTextBox.Text == PromptPlaceholder ? string.Empty : PromptTextBox.Text;
            PromptTextBox.Text = (current ?? string.Empty).TrimEnd() + value;
            PromptTextBox.CaretIndex = PromptTextBox.Text.Length;
            PromptTextBox.Focus();
        }

        private void Workbench_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Workbench_Drop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] paths) || paths.Length == 0) return;
            AppendToPrompt("\r\n\r\nReferenced files:\r\n" + string.Join("\r\n", paths.Select(p => "- `" + p + "`")));
            SelectWorkbenchTab("Chat");
            SetTask(paths.Length + " file(s) attached.");
        }

        private void ApplyTranscriptTheme()
        {
            if (ChatTranscript == null) return;
            foreach (var border in ChatTranscript.Children.OfType<Border>())
            {
                if (border.Child is StackPanel)
                {
                    border.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
                    border.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
                }
            }
        }

        private string DeriveSessionName()
        {
            var first = chatHistory.OrderBy(e => e.Timestamp).FirstOrDefault()?.Prompt;
            if (string.IsNullOrWhiteSpace(first)) return "Session " + DateTime.Now.ToString("yyyy-MM-dd HH-mm");
            first = first.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return first.Length <= 60 ? first : first.Substring(0, 59) + "…";
        }

        private static ChatHistoryEntry CloneHistoryEntry(ChatHistoryEntry entry)
        {
            return new ChatHistoryEntry
            {
                Id = entry.Id,
                Timestamp = entry.Timestamp,
                Prompt = entry.Prompt,
                Response = entry.Response,
                CodeContext = entry.CodeContext,
                OperationType = entry.OperationType,
                IsFavorite = entry.IsFavorite
            };
        }

        private static string SessionToMarkdown(ChatSession session)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# " + (session.Name ?? "OMP session"));
            builder.AppendLine();
            foreach (var entry in (session.Entries ?? new List<ChatHistoryEntry>()).OrderBy(e => e.Timestamp))
            {
                builder.AppendLine("## User");
                builder.AppendLine();
                builder.AppendLine(entry.Prompt ?? string.Empty);
                builder.AppendLine();
                builder.AppendLine("## oh-my-pi");
                builder.AppendLine();
                builder.AppendLine(entry.Response ?? string.Empty);
                builder.AppendLine();
            }
            return builder.ToString();
        }

        private static string MakeSafeFileName(string value)
        {
            var name = string.IsNullOrWhiteSpace(value) ? "omp-session" : value;
            foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '-');
            return name;
        }
    }
}
