using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VSAgent.Services
{
    /// <summary>
    /// Stores provider API keys and OAuth tokens encrypted with DPAPI (CurrentUser).
    /// File lives in %LOCALAPPDATA%\QuantivusOMP\credentials.bin.
    /// </summary>
    public sealed class CredentialStore
    {
        private readonly string filePath;
        private readonly object sync = new();
        private Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler Changed;

        public CredentialStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuantivusOMP");
            Directory.CreateDirectory(dir);
            filePath = Path.Combine(dir, "credentials.bin");
        }

        public string FilePath => filePath;

        public void Load()
        {
            lock (sync)
            {
                entries.Clear();
                if (!File.Exists(filePath)) return;
                try
                {
                    var encrypted = File.ReadAllBytes(filePath);
                    var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    var json = Encoding.UTF8.GetString(plain);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(json) ?? new();
                    entries = new Dictionary<string, Entry>(loaded, StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                    entries = new(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        public void Save()
        {
            lock (sync)
            {
                var json = JsonSerializer.Serialize(entries);
                var plain = Encoding.UTF8.GetBytes(json);
                var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(filePath, encrypted);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public string GetApiKey(string providerId)
        {
            lock (sync) { return entries.TryGetValue(MakeKey(providerId, "api_key"), out var e) ? e.Value : null; }
        }

        public string GetOAuthToken(string providerId)
        {
            lock (sync) { return entries.TryGetValue(MakeKey(providerId, "oauth"), out var e) ? e.Value : null; }
        }

        public string GetExtra(string providerId, string key)
        {
            lock (sync) { return entries.TryGetValue(MakeKey(providerId, "extra:" + key), out var e) ? e.Value : null; }
        }

        public void SetApiKey(string providerId, string value) => Set(MakeKey(providerId, "api_key"), value);
        public void SetOAuthToken(string providerId, string value) => Set(MakeKey(providerId, "oauth"), value);
        public void SetExtra(string providerId, string key, string value) => Set(MakeKey(providerId, "extra:" + key), value);

        public void Clear(string providerId)
        {
            lock (sync)
            {
                var prefix = providerId + ":";
                var keys = entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var k in keys) entries.Remove(k);
            }
            Save();
        }

        private static string MakeKey(string providerId, string suffix) => providerId + ":" + suffix;

        private void Set(string key, string value)
        {
            lock (sync)
            {
                if (string.IsNullOrEmpty(value)) entries.Remove(key);
                else entries[key] = new Entry { Value = value, UpdatedAt = DateTime.UtcNow };
            }
            Save();
        }

        public bool HasApiKey(string providerId) => !string.IsNullOrEmpty(GetApiKey(providerId));
        public bool HasOAuthToken(string providerId) => !string.IsNullOrEmpty(GetOAuthToken(providerId));

        public sealed class Entry
        {
            [JsonPropertyName("value")]
            public string Value { get; set; }
            [JsonPropertyName("updated_at")]
            public DateTime UpdatedAt { get; set; }
        }
    }
}
