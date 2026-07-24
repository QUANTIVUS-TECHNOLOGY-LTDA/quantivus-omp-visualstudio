using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VSAgent.Models;

namespace VSAgent.Services
{
    /// <summary>
    /// In-memory list of currently active skills. Skills are added via
    /// /skill &lt;name&gt; and prepended to subsequent prompts.
    /// </summary>
    public sealed class ActiveSkillRegistry
    {
        private readonly object sync = new();
        private readonly List<Skill> active = new();
        private readonly SkillStore store;

        public event EventHandler Changed;

        public ActiveSkillRegistry(SkillStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            store.Changed += (_, __) => PruneMissing();
        }

        public IReadOnlyList<Skill> Snapshot()
        {
            lock (sync) return active.ToList();
        }

        public int Count { get { lock (sync) return active.Count; } }

        public bool Activate(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var match = store.FindByName(name);
            if (match == null) return false;
            lock (sync)
            {
                if (active.Any(s => s.Id == match.Id)) return true;
                active.Add(match);
            }
            RaiseChanged();
            return true;
        }

        public bool Deactivate(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var normalized = SkillStore.NormalizeName(name);
            lock (sync)
            {
                var existing = active.FirstOrDefault(s => SkillStore.NormalizeName(s.Name) == normalized);
                if (existing == null) return false;
                active.Remove(existing);
            }
            RaiseChanged();
            return true;
        }

        public void Clear()
        {
            lock (sync) active.Clear();
            RaiseChanged();
        }

        /// <summary>
        /// Returns a prompt section that lists the active skills' content,
        /// or null if no skills are active. Intended to be prepended to the
        /// user's prompt so the agent treats the skills as standing context.
        /// </summary>
        public string BuildPromptSection()
        {
            var snapshot = Snapshot();
            if (snapshot.Count == 0) return null;
            var sb = new StringBuilder();
            sb.AppendLine("[Active skills]");
            foreach (var s in snapshot)
            {
                sb.Append("- ").Append(s.Name).Append(": ");
                if (!string.IsNullOrEmpty(s.Description)) sb.Append(s.Description).Append(" — ");
                sb.AppendLine(s.Content);
            }
            return sb.ToString();
        }

        private void PruneMissing()
        {
            var ids = store.Snapshot().Select(s => s.Id).ToHashSet();
            lock (sync)
            {
                active.RemoveAll(a => !ids.Contains(a.Id));
            }
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            var h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
