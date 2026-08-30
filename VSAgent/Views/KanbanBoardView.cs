using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VSAgent.Models;
using VSAgent.Services;
using VSAgent.Ui;

namespace VSAgent.Views
{
    /// <summary>
    /// Kanban board for the agent queue. Cards move Backlog -> InProgress ->
    /// Done (or Failed) when processed sequentially through the
    /// <see cref="KanbanQueueService"/>.
    /// </summary>
    internal sealed class KanbanBoardView : UserControl
    {
        private readonly WorkbenchStore store;
        private readonly KanbanQueueService queue;
        private readonly AgentHostService host;
        private readonly TextBlock statusText;
        private readonly TextBlock summaryText;
        private readonly StackPanel backlogColumn;
        private readonly StackPanel inProgressColumn;
        private readonly StackPanel doneColumn;
        private readonly StackPanel failedColumn;
        private readonly CheckBox continueOnFailureBox;
        private CancellationTokenSource refreshGate;

        public KanbanBoardView(WorkbenchStore store, KanbanQueueService queue, AgentHostService host)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            WorkbenchUi.ApplyToolWindowTheme(this);

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(WorkbenchUi.Button("New card", delegate { NewCard(); }, true,
                "Create a new kanban card in the backlog."));
            actions.Children.Add(WorkbenchUi.Button("Run next", async delegate { await RunNextAsync(); },
                true, "Move the next backlog card to InProgress and run it."));
            actions.Children.Add(WorkbenchUi.Button("Run queue", async delegate { await RunQueueAsync(); },
                true, "Drain the entire backlog sequentially."));
            actions.Children.Add(WorkbenchUi.Button("Stop", delegate { queue.Stop(); },
                false, "Cancel the currently running kanban card or drain."));
            actions.Children.Add(WorkbenchUi.Button("Refresh", delegate { Refresh(); }));
            root.Children.Add(WorkbenchUi.PageHeader("Kanban queue",
                "Sequentially run backlog cards through oh-my-pi. Each card may use its own agent profile.", actions));

            continueOnFailureBox = WorkbenchUi.CheckBox("Continue on failure", false);
            continueOnFailureBox.Margin = new Thickness(0, 4, 0, 8);
            continueOnFailureBox.ToolTip = "Keep draining the backlog even when a card ends in the Failed column.";
            root.Children.Add(continueOnFailureBox);

            var board = new Grid();
            board.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            board.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            board.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            board.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            board.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            board.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            board.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(board, 2);
            root.Children.Add(board);

            backlogColumn = BuildColumn("Backlog");
            inProgressColumn = BuildColumn("In progress");
            doneColumn = BuildColumn("Done");
            failedColumn = BuildColumn("Failed");
            Grid.SetColumn(backlogColumn, 0);
            Grid.SetColumn(inProgressColumn, 2);
            Grid.SetColumn(doneColumn, 4);
            Grid.SetColumn(failedColumn, 6);
            board.Children.Add(backlogColumn);
            board.Children.Add(inProgressColumn);
            board.Children.Add(doneColumn);
            board.Children.Add(failedColumn);

            var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            statusText = WorkbenchUi.Subtitle("oh-my-pi is ready when the agent host is connected.");
            statusText.Margin = new Thickness(0);
            summaryText = WorkbenchUi.Subtitle("0 cards total.");
            summaryText.Margin = new Thickness(12, 0, 0, 0);
            statusRow.Children.Add(statusText);
            statusRow.Children.Add(summaryText);
            Grid.SetRow(statusRow, 3);
            root.Children.Add(statusRow);

            Content = root;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private StackPanel BuildColumn(string header)
        {
            var column = new StackPanel();
            column.Tag = header;

            var titleBar = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(4)
            };
            titleBar.SetResourceReference(Border.BackgroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowBorderKey);
            var title = new TextBlock
            {
                Text = header,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);
            titleBar.Child = title;
            column.Children.Add(titleBar);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0)
            };
            scroll.SetResourceReference(BackgroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowBackgroundKey);
            var host = new StackPanel();
            host.Tag = header;
            scroll.Content = host;
            column.Children.Add(scroll);
            return column;
        }

        private StackPanel ColumnHost(KanbanStatus status)
        {
            var column = status == KanbanStatus.Backlog ? backlogColumn
                : status == KanbanStatus.InProgress ? inProgressColumn
                : status == KanbanStatus.Done ? doneColumn
                : failedColumn;
            var scroll = column.Children.OfType<ScrollViewer>().FirstOrDefault();
            return scroll?.Content as StackPanel;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            store.Changed += Store_Changed;
            queue.StatusChanged += Queue_StatusChanged;
            queue.CardUpdated += Queue_CardUpdated;
            Refresh();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            store.Changed -= Store_Changed;
            queue.StatusChanged -= Queue_StatusChanged;
            queue.CardUpdated -= Queue_CardUpdated;
            refreshGate?.Cancel();
            refreshGate?.Dispose();
            refreshGate = null;
        }

        private void Store_Changed(object sender, EventArgs e)
        {
            if (IsLoaded) Dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void Queue_StatusChanged(object sender, string status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (statusText != null) statusText.Text = status ?? string.Empty;
            }));
        }

        private void Queue_CardUpdated(object sender, KanbanCard card)
        {
            if (IsLoaded) Dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void Refresh()
        {
            if (!IsLoaded) return;
            foreach (var status in new[] { KanbanStatus.Backlog, KanbanStatus.InProgress, KanbanStatus.Done, KanbanStatus.Failed })
            {
                var host = ColumnHost(status);
                if (host == null) continue;
                host.Children.Clear();
                var cards = queue.SnapshotCards().Where(c => c.Status == status).ToList();
                foreach (var card in cards) host.Children.Add(BuildCard(card));
            }
            var all = queue.SnapshotCards();
            summaryText.Text = $"{all.Count} card(s) total — {all.Count(c => c.Status == KanbanStatus.Backlog)} backlog, {all.Count(c => c.Status == KanbanStatus.InProgress)} running.";
        }

        private UIElement BuildCard(KanbanCard card)
        {
            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1)
            };
            border.SetResourceReference(Border.BackgroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowBackgroundKey);
            border.SetResourceReference(Border.BorderBrushProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowBorderKey);

            var panel = new StackPanel();

            var titleBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(card.Title) ? "(untitled card)" : card.Title,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            titleBlock.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);
            panel.Children.Add(titleBlock);

            if (!string.IsNullOrWhiteSpace(card.Description))
            {
                var desc = new TextBlock
                {
                    Text = Truncate(card.Description, 180),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                    FontSize = 11
                };
                desc.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.GrayTextKey);
                panel.Children.Add(desc);
            }

            var profile = ResolveProfile(card.AgentProfileId);
            if (profile != null)
            {
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    BorderThickness = new Thickness(1)
                };
                badge.SetResourceReference(Border.BackgroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowBorderKey);
                badge.SetResourceReference(Border.BorderBrushProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowBorderKey);
                var badgeText = new TextBlock
                {
                    Text = "Agent: " + profile.Name,
                    FontSize = 10
                };
                badgeText.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);
                badge.Child = badgeText;
                panel.Children.Add(badge);
            }

            if (card.RunCount > 0 || card.StartedAt.HasValue || card.FinishedAt.HasValue)
            {
                var meta = new TextBlock
                {
                    FontSize = 10,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                meta.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.GrayTextKey);
                var parts = new List<string>();
                if (card.RunCount > 0) parts.Add("runs: " + card.RunCount);
                if (card.StartedAt.HasValue) parts.Add("started " + card.StartedAt.Value.ToLocalTime().ToString("HH:mm:ss"));
                if (card.FinishedAt.HasValue) parts.Add("finished " + card.FinishedAt.Value.ToLocalTime().ToString("HH:mm:ss"));
                meta.Text = string.Join(" • ", parts);
                panel.Children.Add(meta);
            }

            if (!string.IsNullOrWhiteSpace(card.LastError))
            {
                var err = new TextBlock
                {
                    Text = "Error: " + card.LastError,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                err.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.GrayTextKey);
                panel.Children.Add(err);
            }
            else if (!string.IsNullOrWhiteSpace(card.LastResponseExcerpt))
            {
                var resp = new TextBlock
                {
                    Text = card.LastResponseExcerpt,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                resp.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.GrayTextKey);
                panel.Children.Add(resp);
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            buttons.Children.Add(WorkbenchUi.Button("Edit", delegate { EditCard(card); }));
            buttons.Children.Add(WorkbenchUi.Button("Run", async delegate { await RunSingleAsync(card); }));
            if (card.Status == KanbanStatus.Backlog || card.Status == KanbanStatus.Failed)
            {
                buttons.Children.Add(WorkbenchUi.Button("→ Done", delegate { MoveCard(card, KanbanStatus.Done); }));
                if (card.Status != KanbanStatus.Failed)
                    buttons.Children.Add(WorkbenchUi.Button("→ Failed", delegate { MoveCard(card, KanbanStatus.Failed); }));
                else
                    buttons.Children.Add(WorkbenchUi.Button("→ Backlog", delegate { MoveCard(card, KanbanStatus.Backlog); }));
            }
            else if (card.Status == KanbanStatus.InProgress)
            {
                buttons.Children.Add(WorkbenchUi.Button("Stop & fail", delegate { MoveCard(card, KanbanStatus.Failed); }));
            }
            else if (card.Status == KanbanStatus.Done)
            {
                buttons.Children.Add(WorkbenchUi.Button("Reopen", delegate { MoveCard(card, KanbanStatus.Backlog); }));
            }
            buttons.Children.Add(WorkbenchUi.Button("Delete", delegate { DeleteCard(card); }));
            panel.Children.Add(buttons);

            border.Child = panel;
            return border;
        }

        private AgentProfile ResolveProfile(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return store.SnapshotAgentProfiles()
                .FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private void NewCard() => EditCard(new KanbanCard { Status = KanbanStatus.Backlog });

        private void EditCard(KanbanCard card)
        {
            var editor = new KanbanCardEditorDialog(store, card) { Owner = Window.GetWindow(this) };
            var ok = editor.ShowDialog() == true;
            if (!ok) return;
            queue.Upsert(editor.Result);
        }

        private void MoveCard(KanbanCard card, KanbanStatus status)
        {
            queue.MoveTo(card.Id, status);
        }

        private void DeleteCard(KanbanCard card)
        {
            if (MessageBox.Show("Delete kanban card \"" + card.Title + "\"?", "Quantivus OMP",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            queue.Delete(card.Id);
        }

        private async Task RunSingleAsync(KanbanCard card)
        {
            if (card.Status != KanbanStatus.Backlog)
            {
                queue.MoveTo(card.Id, KanbanStatus.Backlog);
            }
            await queue.RunNextAsync(CancellationToken.None);
        }

        private async Task RunNextAsync()
        {
            await queue.RunNextAsync(CancellationToken.None);
        }

        private async Task RunQueueAsync()
        {
            await queue.DrainAsync(continueOnFailureBox.IsChecked == true, CancellationToken.None);
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= max ? flat : flat.Substring(0, max - 1) + "…";
        }
    }
}