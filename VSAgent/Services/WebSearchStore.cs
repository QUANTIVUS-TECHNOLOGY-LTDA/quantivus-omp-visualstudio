using System;
using System.IO;
using System.Text.Json;

namespace VSAgent.Services
{
    /// <summary>
    /// Persists WebSearchConfig (which provider is active + API keys) to
    /// %LOCALAPPDATA%\QuantivusOMP\websearch.json. Keys are stored in plaintext
    /// here; in practice they should live in CredentialStore for real secrets.
    /// </summary>
    public sealed class WebSearchStore
    {
        private readonly string filePath;
        private readonly object sync = new();

        public WebSearchStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuantivusOMP");
            Directory.CreateDirectory(dir);
            filePath = Path.Combine(dir, "websearch.json");
        }

        public string FilePath => filePath;

        public WebSearchConfig Load()
        {
            lock (sync)
            {
                if (!File.Exists(filePath)) return new WebSearchConfig();
                try
                {
                    var json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<WebSearchConfig>(json) ?? new WebSearchConfig();
                }
                catch { return new WebSearchConfig(); }
            }
        }

        public void Save(WebSearchConfig cfg)
        {
            if (cfg == null) return;
            lock (sync)
            {
                var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
        }
    }
}
