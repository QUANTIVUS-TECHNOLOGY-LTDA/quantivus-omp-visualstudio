using Newtonsoft.Json;
using System;

namespace VSAgent.Models
{
    /// <summary>
    /// Lifecycle of a Kanban card. Values are persisted as strings in JSON so
    /// adding new states later stays backwards compatible.
    /// </summary>
    public enum KanbanStatus
    {
        Backlog = 0,
        InProgress = 1,
        Done = 2,
        Failed = 3
    }

    public sealed class KanbanCard
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("status")]
        public KanbanStatus Status { get; set; } = KanbanStatus.Backlog;

        [JsonProperty("agent_profile_id")]
        public string AgentProfileId { get; set; }

        [JsonProperty("order")]
        public int Order { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [JsonProperty("started_at")]
        public DateTime? StartedAt { get; set; }

        [JsonProperty("finished_at")]
        public DateTime? FinishedAt { get; set; }

        [JsonProperty("last_response_excerpt")]
        public string LastResponseExcerpt { get; set; }

        [JsonProperty("last_error")]
        public string LastError { get; set; }

        [JsonProperty("run_count")]
        public int RunCount { get; set; }

        public bool IsTerminal =>
            Status == KanbanStatus.Done || Status == KanbanStatus.Failed;

        public override string ToString() =>
            string.IsNullOrWhiteSpace(Title) ? "(untitled card)" : Title;
    }
}