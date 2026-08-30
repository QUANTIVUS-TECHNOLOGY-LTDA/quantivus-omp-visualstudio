using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VSAgent.Models;

namespace VSAgent.Services
{
    /// <summary>
    /// Sequentially processes Kanban cards through oh-my-pi. The service owns
    /// the lifecycle of a card while it is being executed: it moves the card to
    /// InProgress, activates the requested agent profile, invokes the agent and
    /// then records the outcome as Done or Failed on the persistent store.
    /// </summary>
    internal sealed class KanbanQueueService
    {
        private readonly WorkbenchStore store;
        private readonly AgentHostService host;
        private readonly SemaphoreSlim runLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource runtime;

        public KanbanQueueService(WorkbenchStore store, AgentHostService host)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public event EventHandler<string> StatusChanged;
        public event EventHandler<KanbanCard> CardUpdated;

        public bool IsRunning => runtime != null && !runtime.IsCancellationRequested;

        public IReadOnlyList<KanbanCard> SnapshotCards() => store.SnapshotKanbanCards();

        public KanbanCard Upsert(KanbanCard card)
        {
            if (card == null) return null;
            var saved = store.UpsertKanbanCard(card);
            CardUpdated?.Invoke(this, saved);
            return saved;
        }

        public bool Delete(string id)
        {
            var removed = store.DeleteKanbanCard(id);
            if (removed) CardUpdated?.Invoke(this, null);
            return removed;
        }

        public bool MoveTo(string id, KanbanStatus status)
        {
            var moved = store.MoveKanbanCard(id, status);
            if (moved) CardUpdated?.Invoke(this, store.GetKanbanCard(id));
            return moved;
        }

        /// <summary>
        /// Picks the next Backlog card (lowest Order, oldest first), moves it to
        /// InProgress, executes it via oh-my-pi, and records the outcome. Returns
        /// the executed card or null when there is nothing to do or the host is
        /// unavailable.
        /// </summary>
        public async Task<KanbanCard> RunNextAsync(CancellationToken cancellationToken)
        {
            await runLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (host == null || !host.IsReady)
                {
                    OnStatus("oh-my-pi is not connected.");
                    return null;
                }

                var card = store.SnapshotKanbanCards()
                    .Where(c => c.Status == KanbanStatus.Backlog)
                    .OrderBy(c => c.Order)
                    .ThenBy(c => c.CreatedAt)
                    .FirstOrDefault();

                if (card == null)
                {
                    OnStatus("Kanban backlog is empty.");
                    return null;
                }

                return await ExecuteAsync(card, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                runLock.Release();
            }
        }

        /// <summary>
        /// Drains the entire backlog sequentially. Cards that move to Failed stop
        /// the drain unless <paramref name="continueOnFailure"/> is true.
        /// </summary>
        public async Task<int> DrainAsync(bool continueOnFailure, CancellationToken cancellationToken)
        {
            if (runtime != null && !runtime.IsCancellationRequested)
            {
                OnStatus("Kanban queue is already running.");
                return 0;
            }

            runtime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            int processed = 0;
            try
            {
                while (!runtime.IsCancellationRequested)
                {
                    var card = await RunNextAsync(runtime.Token).ConfigureAwait(false);
                    if (card == null) break;
                    processed++;
                    if (!continueOnFailure && card.Status == KanbanStatus.Failed) break;
                }
            }
            finally
            {
                OnStatus(processed == 0
                    ? "Kanban queue finished without running any cards."
                    : $"Kanban queue finished. Processed {processed} card(s).");
                runtime.Dispose();
                runtime = null;
            }
            return processed;
        }

        public void Stop()
        {
            runtime?.Cancel();
        }

        private async Task<KanbanCard> ExecuteAsync(KanbanCard card, CancellationToken cancellationToken)
        {
            store.MoveKanbanCard(card.Id, KanbanStatus.InProgress);
            CardUpdated?.Invoke(this, store.GetKanbanCard(card.Id));
            OnStatus($"Running kanban card: {card.Title}");

            string responseText = string.Empty;
            try
            {
                var profile = ResolveProfile(card.AgentProfileId);
                if (profile != null && !string.IsNullOrWhiteSpace(profile.PreferredModel))
                    host.ModelName = profile.PreferredModel;

                var prompt = BuildCardPrompt(card, profile);
                responseText = await host.PromptAsync(prompt, string.Empty, cancellationToken).ConfigureAwait(false);
                store.MoveKanbanCard(card.Id, KanbanStatus.Done);
                OnStatus($"Card finished: {card.Title}");
            }
            catch (OperationCanceledException)
            {
                store.RecordKanbanRun(card.Id, null, "cancelled");
                store.MoveKanbanCard(card.Id, KanbanStatus.Failed);
                OnStatus($"Card cancelled: {card.Title}");
            }
            catch (Exception ex)
            {
                store.RecordKanbanRun(card.Id, null, ex.Message);
                store.MoveKanbanCard(card.Id, KanbanStatus.Failed);
                OnStatus($"Card failed: {card.Title} ({ex.Message})");
            }
            var activated = store.GetKanbanCard(card.Id);
            CardUpdated?.Invoke(this, activated);
            return activated;
        }

        private AgentProfile ResolveProfile(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId)) return null;
            return store.SnapshotAgentProfiles()
                .FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildCardPrompt(KanbanCard card, AgentProfile profile)
        {
            var instructions = string.IsNullOrWhiteSpace(card.Description)
                ? card.Title
                : card.Title + Environment.NewLine + Environment.NewLine + card.Description;

            if (profile != null && !string.IsNullOrWhiteSpace(profile.SystemPrompt))
            {
                return profile.SystemPrompt
                    + Environment.NewLine + Environment.NewLine
                    + "---" + Environment.NewLine
                    + "Kanban task:" + Environment.NewLine
                    + instructions;
            }
            return instructions;
        }

        private static string Excerpt(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= max ? flat : flat.Substring(0, max - 1) + "…";
        }

        private void OnStatus(string status) => StatusChanged?.Invoke(this, status);
    }
}