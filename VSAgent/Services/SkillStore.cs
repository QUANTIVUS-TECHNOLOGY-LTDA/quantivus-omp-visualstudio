using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using VSAgent.Models;

namespace VSAgent.Services
{
    /// <summary>
    /// Persists Skills to %LOCALAPPDATA%\QuantivusOMP\skills.json.
    /// Thread-safe; lazy-loads on first access.
    /// </summary>
    public sealed class SkillStore
    {
        private readonly string filePath;
        private readonly object sync = new();
        private List<Skill> skills = new();
        private bool loaded;

        public event EventHandler Changed;

        public SkillStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuantivusOMP");
            Directory.CreateDirectory(dir);
            filePath = Path.Combine(dir, "skills.json");
        }

        public string FilePath => filePath;

        public IReadOnlyList<Skill> Snapshot()
        {
            EnsureLoaded();
            lock (sync) return skills.ToList();
        }

        public Skill Add(Skill skill)
        {
            if (skill == null) return null;
            EnsureLoaded();
            lock (sync)
            {
                if (string.IsNullOrWhiteSpace(skill.Name)) skill.Name = "Untitled";
                if (skill.Id == Guid.Empty) skill.Id = Guid.NewGuid();
                skills.Add(skill);
                Save();
            }
            RaiseChanged();
            return skill;
        }

        public bool Update(Skill skill)
        {
            if (skill == null) return false;
            EnsureLoaded();
            lock (sync)
            {
                var existing = skills.FirstOrDefault(s => s.Id == skill.Id);
                if (existing == null) return false;
                existing.Name = skill.Name;
                existing.Description = skill.Description;
                existing.Content = skill.Content;
                existing.IsEnabled = skill.IsEnabled;
                Save();
            }
            RaiseChanged();
            return true;
        }

        public bool Remove(Guid id)
        {
            EnsureLoaded();
            lock (sync)
            {
                var existing = skills.FirstOrDefault(s => s.Id == id);
                if (existing == null) return false;
                skills.Remove(existing);
                Save();
            }
            RaiseChanged();
            return true;
        }

        public Skill FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            EnsureLoaded();
            lock (sync)
            {
                var normalized = NormalizeName(name);
                return skills.FirstOrDefault(s => NormalizeName(s.Name) == normalized);
            }
        }

        public static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return name.Trim().TrimStart('/').ToLowerInvariant();
        }

        private void EnsureLoaded()
        {
            if (loaded) return;
            lock (sync)
            {
                if (loaded) return;
                Load();
                loaded = true;
            }
        }

        private void Load()
        {
            if (!File.Exists(filePath)) return;
            try
            {
                var json = File.ReadAllText(filePath, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<SkillFile>(json);
                if (data?.Skills != null) skills = data.Skills;
            }
            catch { /* corrupt file: ignore, start fresh */ }
        }

        private void Save()
        {
            try
            {
                var data = new SkillFile { Skills = skills.ToList() };
                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(filePath, json, Encoding.UTF8);
            }
            catch { /* best-effort */ }
        }

        private void RaiseChanged()
        {
            var h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        private class SkillFile
        {
            [JsonProperty("skills")]
            public List<Skill> Skills { get; set; }
        }
    }
}
