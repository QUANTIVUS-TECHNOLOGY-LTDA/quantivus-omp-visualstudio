using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VSAgent.Models;

namespace VSAgent.Services
{
    /// <summary>
    /// Thread-safe FIFO of follow-up user messages. Items are sent as new
    /// turns automatically after the current run finishes.
    /// </summary>
    public sealed class MessageQueue
    {
        private readonly List<QueuedMessage> items = new();
        private readonly object sync = new();

        public event EventHandler Changed;

        public int Count
        {
            get { lock (sync) return items.Count; }
        }

        public IReadOnlyList<QueuedMessage> Snapshot()
        {
            lock (sync) return items.ToList();
        }

        public QueuedMessage Enqueue(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var msg = new QueuedMessage { Text = text.Trim(), AddedAt = DateTime.Now };
            lock (sync) items.Add(msg);
            RaiseChanged();
            return msg;
        }

        public QueuedMessage Peek()
        {
            lock (sync) return items.Count > 0 ? items[0] : null;
        }

        public QueuedMessage Dequeue()
        {
            QueuedMessage msg;
            lock (sync)
            {
                if (items.Count == 0) return null;
                msg = items[0];
                items.RemoveAt(0);
            }
            RaiseChanged();
            return msg;
        }

        public bool Remove(Guid id)
        {
            lock (sync)
            {
                var idx = items.FindIndex(m => m.Id == id);
                if (idx < 0) return false;
                items.RemoveAt(idx);
            }
            RaiseChanged();
            return true;
        }

        public void Clear()
        {
            lock (sync) items.Clear();
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
