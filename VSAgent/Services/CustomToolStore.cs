using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VSAgent.Models;

namespace VSAgent.Services
{
    /// <summary>
    /// Persists user-defined MCP tools to %LOCALAPPDATA%\QuantivusOMP\custom-tools.json.
    /// </summary>
    public sealed class CustomToolStore
    {
        private readonly string filePath;
        private readonly object sync = new();
        private List<CustomMcpTool> tools = new();

        public event EventHandler Changed;

        public CustomToolStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuantivusOMP");
            Directory.CreateDirectory(dir);
            filePath = Path.Combine(dir, "custom-tools.json");
        }

        public string FilePath => filePath;

        public IReadOnlyList<CustomMcpTool> Snapshot()
        {
            lock (sync) { return tools.ToList(); }
        }

        public void Load()
        {
            lock (sync)
            {
                tools.Clear();
                if (!File.Exists(filePath)) return;
                try
                {
                    var json = File.ReadAllText(filePath);
                    tools = JsonSerializer.Deserialize<List<CustomMcpTool>>(json) ?? new();
                }
                catch
                {
                    tools = new();
                }
            }
        }

        public void Save()
        {
            lock (sync)
            {
                var json = JsonSerializer.Serialize(tools, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Add(CustomMcpTool t)
        {
            if (t == null) return;
            t.CreatedAt = t.CreatedAt == default ? DateTime.UtcNow : t.CreatedAt;
            t.UpdatedAt = DateTime.UtcNow;
            lock (sync) tools.Add(t);
            Save();
        }

        public void Update(CustomMcpTool t)
        {
            if (t == null) return;
            t.UpdatedAt = DateTime.UtcNow;
            lock (sync)
            {
                var idx = tools.FindIndex(x => x.Id == t.Id);
                if (idx >= 0) tools[idx] = t;
                else tools.Add(t);
            }
            Save();
        }

        public void Remove(string id)
        {
            lock (sync) tools.RemoveAll(x => x.Id == id);
            Save();
        }

        public CustomMcpTool FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            lock (sync) { return tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)); }
        }
    }
}
