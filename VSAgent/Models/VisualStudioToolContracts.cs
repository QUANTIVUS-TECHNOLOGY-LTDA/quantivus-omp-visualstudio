using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VSAgent.Models
{
    internal sealed class VisualStudioToolRequest
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("tool")]
        public string Tool { get; set; } = string.Empty;

        [JsonProperty("arguments")]
        public JObject Arguments { get; set; } = new JObject();
    }

    internal sealed class VisualStudioToolResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("result")]
        public object Result { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        public static VisualStudioToolResponse Ok(string id, object result) =>
            new VisualStudioToolResponse { Id = id, Success = true, Result = result };

        public static VisualStudioToolResponse Fail(string id, string error) =>
            new VisualStudioToolResponse { Id = id, Success = false, Error = error };
    }

    /// <summary>
    /// Detailed snapshot of the Visual Studio debugger. Used by vs_get_status
    /// so the agent can reason about whether the debuggee is running, paused
    /// or detached without triggering another tool call.
    /// </summary>
    internal sealed class DebuggerSnapshot
    {
        public string Mode { get; set; } = "unknown";
        public string Solution { get; set; } = string.Empty;
        public string StartupProjects { get; set; } = string.Empty;
        public bool IsSolutionOpen { get; set; }
        public string LastBreakReason { get; set; } = string.Empty;
        public bool AllExceptionsBreakWhenThrown { get; set; }
        public bool JustMyCode { get; set; }
        public string CurrentProcessName { get; set; } = string.Empty;
        public int CurrentProcessId { get; set; }
        public string CurrentThreadId { get; set; } = string.Empty;
        public string CurrentFrame { get; set; } = string.Empty;
        public int DebuggedProcessCount { get; set; }
        public int BreakpointCount { get; set; }
    }
}
