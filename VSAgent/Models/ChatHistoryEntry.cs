using System;

namespace VSAgent.Models
{
    public class ChatHistoryEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Prompt { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public string? CodeContext { get; set; }
        public string OperationType { get; set; } = "Custom";
        public bool IsFavorite { get; set; }
    }
}
