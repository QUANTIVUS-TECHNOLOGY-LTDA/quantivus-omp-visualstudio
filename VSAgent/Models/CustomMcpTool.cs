using System;
using System.Text.Json.Serialization;

namespace VSAgent.Models
{
    /// <summary>
    /// A user-defined MCP tool. The McpHost loads these at startup and exposes
    /// them as tools/&lt;name&gt; for the oh-my-pi agent. The Code property holds
    /// the C# source that implements the tool; it is compiled with Roslyn at
    /// load time and invoked on the McpHost.
    /// </summary>
    public sealed class CustomMcpTool
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
