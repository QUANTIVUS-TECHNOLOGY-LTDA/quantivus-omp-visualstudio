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

    internal sealed class DebuggerSnapshot
    {
        public string Mode { get; set; } = "unknown";
        public string Solution { get; set; } = string.Empty;
        public string StartupProjects { get; set; } = string.Empty;
        public bool IsSolutionOpen { get; set; }
    }
}
