using System;

namespace VSAgent.Models
{
    public sealed class QueuedMessage
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Text { get; set; }
        public DateTime AddedAt { get; set; }
    }
}
