using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using VSAgent.Services;
using VSAgent.Ui;

namespace VSAgent.Views
{
    internal sealed class SessionsWorkspaceView : UserControl
    {
        private readonly WorkbenchStore store;
        private readonly Func<ChatSession> captureCurrent;
        private readonly Action<ChatSession> restore;
        private readonly Action createNew;
        private readonly ListBox savedSessions;
        private readonly ListBox currentHistory;
        private readonly TextBlock detailsText;
        private readonly TextBlock statusText;

        public SessionsWorkspaceView(
            WorkbenchStore store,
            ListBox currentHistory,
            Func<ChatSession> captureCurrent,
            Action<ChatSession> restore,
            Action createNew)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.currentHistory = currentHistory ?? throw new ArgumentNullException(nameof(currentHistory));
            this.captureCurrent = captureCurrent ?? throw new ArgumentNullException(nameof(captureCurrent));
            this.restore = restore ?? throw new ArgumentNullException(nameof(restore));
            this.createNew = createNew ?? throw new ArgumentNullException(nameof(createNew));
            WorkbenchUi.ApplyToolWindowTheme(this);

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(WorkbenchUi.Button("New session", delegate { createNew(); }, true));
            actions.Children.Add(WorkbenchUi.Button("Save snapshot", delegate { SaveSnapshot(); }));
            actions.Children.Add(WorkbenchUi.Button("Restore", delegate { RestoreSelected(); }));
            actions.Children.Add(WorkbenchUi.Button("Export", delegate { ExportSelected(); }));
            root.Children.Add(WorkbenchUi.PageHeader("Sessions",
                "Persistent conversations are tied to solution, branch, provider and agent profile and can be restored after restarting Visual Studio.", actions));

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var savedPanel = new DockPanel();
            savedPanel.Children.Add(WorkbenchUi.Title("Saved sessions", 14));
            savedSessions = WorkbenchUi.ListBox();
            savedSessions.DisplayMemberPath = "Name";
            savedSessions.SelectionChanged += SavedSessions_SelectionChanged;
            savedSessions.MouseDoubleClick += delegate { RestoreSelected(); };
            DockPanel.SetDock(savedSessions, Dock.Top);
            savedPanel.Children.Add(savedSessions);
            var delete = WorkbenchUi.Button("Delete selected", delegate { DeleteSelected(); });
            DockPanel.SetDock(delete, Dock.Bottom);
            savedPanel.Children.Add(delete);
            body.Children.Add(WorkbenchUi.Card(savedPanel, new Thickness(0), new Thickness(8)));

            var splitter = new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns
            };
            Grid.SetColumn(splitter, 1);
            body.Children.Add(splitter);

            var currentPanel = new Grid();
            currentPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            currentPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            currentPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            currentPanel.Children.Add(WorkbenchUi.Title("Current conversation", 14));
            currentHistory.Margin = new Thickness(0, 8, 0, 8);
            Grid.SetRow(currentHistory, 1);
            currentPanel.Children.Add(currentHistory);
            detailsText = WorkbenchUi.Subtitle("Select a saved session to inspect its metadata.");
            Grid.SetRow(detailsText, 2);
            currentPanel.Children.Add(detailsText);
            Grid.SetColumn(currentPanel, 2);
            body.Children.Add(WorkbenchUi.Card(currentPanel, new Thickness(0), new Thickness(8)));

            statusText = WorkbenchUi.Subtitle("Session data: " + store.FilePath);
            statusText.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(statusText, 2);
            root.Children.Add(statusText);

            Content = root;
            Loaded += delegate { Refresh(); };
            store.Changed += delegate { if (IsLoaded) Dispatcher.BeginInvoke(new Action(Refresh)); };
        }

        public void Refresh()
        {
            var selectedId = (savedSessions.SelectedItem as ChatSession)?.Id;
            var values = store.SnapshotSessions();
            savedSessions.ItemsSource = values;
            var match = values.FirstOrDefault(s => s.Id == selectedId) ?? values.FirstOrDefault();
            if (match != null) savedSessions.SelectedItem = match;
        }

        private void SavedSessions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(savedSessions.SelectedItem is ChatSession session))
            {
                detailsText.Text = "Select a saved session to inspect its metadata.";
                return;
            }
            detailsText.Text = session.Entries.Count + " turn(s) • updated " + session.UpdatedAt.ToLocalTime().ToString("g") +
                               "\r\n" + (session.Branch ?? "no branch") + " • " + (session.Provider ?? "default provider") +
                               (string.IsNullOrWhiteSpace(session.Model) ? string.Empty : " / " + session.Model) +
                               "\r\n" + (session.SolutionPath ?? "No solution path");
        }

        private void SaveSnapshot()
        {
            var session = captureCurrent();
            if (session == null)
            {
                statusText.Text = "Nothing to save.";
                return;
            }
            var proposed = string.IsNullOrWhiteSpace(session.Name) || session.Name == "New session"
                ? DeriveName(session)
                : session.Name;
            var name = TextPromptWindow.Ask("Save session", "Session name:");
            if (!string.IsNullOrWhiteSpace(name)) proposed = name;
            session.Name = proposed;
            store.UpsertSession(session);
            statusText.Text = "Saved '" + session.Name + "'.";
            Refresh();
        }

        private void RestoreSelected()
        {
            if (!(savedSessions.SelectedItem is ChatSession session)) return;
            restore(store.GetSession(session.Id));
            store.UpdatePreferences(p => p.ActiveSessionId = session.Id);
            statusText.Text = "Restored '" + session.Name + "'.";
        }

        private void DeleteSelected()
        {
            if (!(savedSessions.SelectedItem is ChatSession session)) return;
            if (MessageBox.Show("Delete saved session '" + session.Name + "'?", "Quantivus OMP",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            store.DeleteSession(session.Id);
            statusText.Text = "Session deleted.";
            Refresh();
        }

        private void ExportSelected()
        {
            var session = savedSessions.SelectedItem as ChatSession ?? captureCurrent();
            if (session == null) return;
            var dialog = new SaveFileDialog
            {
                Title = "Export OMP session",
                Filter = "Markdown (*.md)|*.md|JSON (*.json)|*.json",
                FileName = SafeFileName(session.Name) + ".md",
                AddExtension = true
            };
            if (dialog.ShowDialog() != true) return;
            if (string.Equals(Path.GetExtension(dialog.FileName), ".json", StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(dialog.FileName, JsonConvert.SerializeObject(session, Formatting.Indented), new UTF8Encoding(false));
            else
                File.WriteAllText(dialog.FileName, ToMarkdown(session), new UTF8Encoding(false));
            statusText.Text = "Exported to " + dialog.FileName;
        }

        private static string ToMarkdown(ChatSession session)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# " + session.Name);
            builder.AppendLine();
            builder.AppendLine("- Updated: " + session.UpdatedAt.ToLocalTime().ToString("u"));
            builder.AppendLine("- Solution: " + (session.SolutionPath ?? "n/a"));
            builder.AppendLine("- Branch: " + (session.Branch ?? "n/a"));
            builder.AppendLine("- Provider/model: " + (session.Provider ?? "default") + " / " + (session.Model ?? "default"));
            builder.AppendLine();
            foreach (var entry in session.Entries.OrderBy(e => e.Timestamp))
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

        private static string DeriveName(ChatSession session)
        {
            var first = session.Entries.OrderBy(e => e.Timestamp).FirstOrDefault()?.Prompt;
            if (string.IsNullOrWhiteSpace(first)) return "Session " + DateTime.Now.ToString("yyyy-MM-dd HH-mm");
            first = first.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return first.Length <= 60 ? first : first.Substring(0, 59) + "…";
        }

        private static string SafeFileName(string value)
        {
            var name = string.IsNullOrWhiteSpace(value) ? "omp-session" : value;
            foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '-');
            return name;
        }
    }
}
