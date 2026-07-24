using System;
using System.Collections.Generic;
using System.Text.Json;

namespace VSAgent.Services.Omp
{
    /// <summary>
    /// Snapshot of an ACP tool_call / tool_call_update that the agent emitted.
    /// Carries everything the UI needs to render the call and its result.
    /// </summary>
    public sealed class AcpToolCall
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string Status { get; set; }      // "running" | "completed" | "failed"
        public string Kind { get; set; }        // "read" | "edit" | "execute" | "other"
        public Dictionary<string, object> Input { get; set; }
        public string Output { get; set; }
        public string RawTitle { get; set; }     // full title (may contain code/output)
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

        public string Preview
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Title))
                {
                    var first = Title.Split(new[] { '\r', '\n' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (first.Length > 0) return first[0].Trim();
                }
                return Name ?? "tool call";
            }
        }

        public string InputJson
        {
            get
            {
                if (Input == null || Input.Count == 0) return string.Empty;
                try { return JsonSerializer.Serialize(Input, new JsonSerializerOptions { WriteIndented = true }); }
                catch { return string.Empty; }
            }
        }
    }
}
