using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using VSAgent.Models;
using VSAgent.Services;
using VSAgent.Ui;

namespace VSAgent.Views
{
    internal sealed class TaskCenterView : UserControl
    {
        private readonly AgentHostService host;
        private readonly TextBlock connectionText;
        private readonly TextBlock statusText;
        private readonly TextBlock inputCountText;
        private readonly ListBox queueList;
        private readonly TextBox queueInput;
        private CancellationTokenSource drainCancellation;
        private string latestStatus = "Idle";

        public TaskCenterView(AgentHostService host)
        {
            this.host = host;
            WorkbenchUi.ApplyToolWindowTheme(this);

            var root = new StackPanel();
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(WorkbenchUi.Button("Cancel run", delegate { CancelRequested?.Invoke(this, EventArgs.Empty); }, false,
                "Cancel the request currently controlled by the chat view."));
            actions.Children.Add(WorkbenchUi.Button("Drain queue", async delegate { await DrainQueueAsync(); }, true));
            root.Children.Add(WorkbenchUi.PageHeader("Task center",
                "Live OMP state, queued follow-up messages and cancellation controls.", actions));

            var summary = new Grid();
            summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            connectionText = SummaryValue(summary, 0, "Connection", "Unknown");
            statusText = SummaryValue(summary, 1, "Current activity", latestStatus);
            inputCountText = SummaryValue(summary, 2, "Approx. input", "0 chars");
            root.Children.Add(WorkbenchUi.Card(summary));

            var queueCard = new StackPanel();
            queueCard.Children.Add(WorkbenchUi.Title("Follow-up queue", 14));
            queueCard.Children.Add(WorkbenchUi.Subtitle("Messages are delivered in order after the active OMP turn finishes."));
            queueList = WorkbenchUi.ListBox(SelectionMode.Single);
            queueList.MinHeight = 160;
            queueList.DisplayMemberPath = "Text";
            queueCard.Children.Add(queueList);

            queueInput = WorkbenchUi.TextBox();
            queueInput.Margin = new Thickness(0, 8, 0, 0);
            queueInput.ToolTip = "Enter a follow-up instruction.";
            queueCard.Children.Add(queueInput);

            var queueActions = new StackPanel { Orientation = Orientation.Horizontal };
            queueActions.Children.Add(WorkbenchUi.Button("Add", delegate { AddQueueMessage(); }, true));
            queueActions.Children.Add(WorkbenchUi.Button("Remove selected", delegate { RemoveSelected(); }));
            queueActions.Children.Add(WorkbenchUi.Button("Clear", delegate { ClearQueue(); }));
            queueCard.Children.Add(queueActions);
            root.Children.Add(WorkbenchUi.Card(queueCard));

            Content = WorkbenchUi.PageScroll(root);
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        public event EventHandler CancelRequested;

        private static TextBlock SummaryValue(Grid parent, int column, string label, string initial)
        {
            var stack = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 10, 0, 0, 0) };
            stack.Children.Add(WorkbenchUi.Subtitle(label));
            var value = WorkbenchUi.Title(initial, 14);
            value.Margin = new Thickness(0);
            stack.Children.Add(value);
            Grid.SetColumn(stack, column);
            parent.Children.Add(stack);
            return value;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (host != null)
            {
                host.StatusChanged += Host_StatusChanged;
                host.Queue.Changed += Queue_Changed;
            }
            Refresh();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (host != null)
            {
                host.StatusChanged -= Host_StatusChanged;
                host.Queue.Changed -= Queue_Changed;
            }
            drainCancellation?.Cancel();
            drainCancellation?.Dispose();
            drainCancellation = null;
        }

        private void Host_StatusChanged(object sender, string e)
        {
            latestStatus = string.IsNullOrWhiteSpace(e) ? "Idle" : e;
            Dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void Queue_Changed(object sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(Refresh));

        private void Refresh()
        {
            connectionText.Text = host?.IsReady == true ? "Connected" : "Disconnected";
            statusText.Text = latestStatus;
            inputCountText.Text = (host?.TotalInputChars ?? 0).ToString("N0") + " chars";
            queueList.ItemsSource = host?.Queue.Snapshot().ToList();
        }

        private void AddQueueMessage()
        {
            var text = queueInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            host?.Queue.Enqueue(text);
            queueInput.Clear();
            Refresh();
        }

        private void RemoveSelected()
        {
            if (queueList.SelectedItem is QueuedMessage message) host?.Queue.Remove(message.Id);
        }

        private void ClearQueue()
        {
            if ((host?.Queue.Count ?? 0) == 0) return;
            if (MessageBox.Show("Remove every queued follow-up message?", "Quantivus OMP",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                host.Queue.Clear();
        }

        private async System.Threading.Tasks.Task DrainQueueAsync()
        {
            if (host == null || host.Queue.Count == 0) return;
            drainCancellation?.Cancel();
            drainCancellation?.Dispose();
            drainCancellation = new CancellationTokenSource();
            try
            {
                await host.DrainQueueAsync(drainCancellation.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                statusText.Text = "Queue error: " + ex.Message;
            }
            finally
            {
                Refresh();
            }
        }
    }
}
