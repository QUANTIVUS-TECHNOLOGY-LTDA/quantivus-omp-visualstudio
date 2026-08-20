using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VSAgent.Models;

namespace VSAgent.Services
{
    /// <summary>
    /// Persists the state of the modern OMP workbench. The store is intentionally
    /// independent from Visual Studio services so it can be tested and recovered
    /// even when the VS shell or the OMP process is unavailable.
    /// </summary>
    public sealed class WorkbenchStore
    {
        private readonly object sync = new object();
        private readonly string filePath;
        private WorkbenchState state = new WorkbenchState();
        private bool loaded;

        public WorkbenchStore(string rootDirectory = null)
        {
            var root = string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuantivusOMP")
                : rootDirectory;
            Directory.CreateDirectory(root);
            filePath = Path.Combine(root, "workbench.json");
        }

        public event EventHandler Changed;

        public string FilePath => filePath;

        public WorkbenchPreferences Preferences
        {
            get
            {
                EnsureLoaded();
                lock (sync) return Clone(state.Preferences ?? new WorkbenchPreferences());
            }
        }

        public IReadOnlyList<ChatSession> SnapshotSessions()
        {
            EnsureLoaded();
            lock (sync)
            {
                return state.Sessions
                    .OrderByDescending(s => s.UpdatedAt)
                    .Select(Clone)
                    .ToList();
            }
        }

        public IReadOnlyList<PromptTemplate> SnapshotPrompts()
        {
            EnsureLoaded();
            lock (sync)
            {
                return state.Prompts
                    .OrderBy(p => p.Category)
                    .ThenBy(p => p.Name)
                    .Select(Clone)
                    .ToList();
            }
        }

        public IReadOnlyList<AgentProfile> SnapshotAgentProfiles()
        {
            EnsureLoaded();
            lock (sync)
            {
                return state.AgentProfiles
                    .Where(p => p.Enabled)
                    .OrderBy(p => p.Name)
                    .Select(Clone)
                    .ToList();
            }
        }

        public ChatSession GetSession(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            EnsureLoaded();
            lock (sync) return Clone(state.Sessions.FirstOrDefault(s => s.Id == id));
        }

        public ChatSession UpsertSession(ChatSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            EnsureLoaded();
            lock (sync)
            {
                if (string.IsNullOrWhiteSpace(session.Id)) session.Id = Guid.NewGuid().ToString("N");
                if (session.CreatedAt == default(DateTime)) session.CreatedAt = DateTime.UtcNow;
                session.UpdatedAt = DateTime.UtcNow;
                session.Entries = session.Entries ?? new List<ChatHistoryEntry>();

                var index = state.Sessions.FindIndex(s => s.Id == session.Id);
                if (index >= 0) state.Sessions[index] = Clone(session);
                else state.Sessions.Add(Clone(session));

                state.LastSessionId = session.Id;
                state.Sessions = state.Sessions
                    .OrderByDescending(s => s.UpdatedAt)
                    .Take(100)
                    .ToList();
                SaveUnsafe();
            }
            RaiseChanged();
            return Clone(session);
        }

        public bool DeleteSession(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            EnsureLoaded();
            bool removed;
            lock (sync)
            {
                removed = state.Sessions.RemoveAll(s => s.Id == id) > 0;
                if (!removed) return false;
                if (state.LastSessionId == id) state.LastSessionId = null;
                SaveUnsafe();
            }
            RaiseChanged();
            return true;
        }

        public PromptTemplate UpsertPrompt(PromptTemplate prompt)
        {
            if (prompt == null) throw new ArgumentNullException(nameof(prompt));
            EnsureLoaded();
            lock (sync)
            {
                if (string.IsNullOrWhiteSpace(prompt.Id)) prompt.Id = Guid.NewGuid().ToString("N");
                prompt.UpdatedAt = DateTime.UtcNow;
                var index = state.Prompts.FindIndex(p => p.Id == prompt.Id);
                if (index >= 0) state.Prompts[index] = Clone(prompt);
                else state.Prompts.Add(Clone(prompt));
                SaveUnsafe();
            }
            RaiseChanged();
            return Clone(prompt);
        }

        public bool DeletePrompt(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            EnsureLoaded();
            bool removed;
            lock (sync)
            {
                removed = state.Prompts.RemoveAll(p => p.Id == id && !p.IsBuiltIn) > 0;
                if (removed) SaveUnsafe();
            }
            if (removed) RaiseChanged();
            return removed;
        }

        public AgentProfile UpsertAgentProfile(AgentProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            EnsureLoaded();
            lock (sync)
            {
                if (string.IsNullOrWhiteSpace(profile.Id)) profile.Id = Guid.NewGuid().ToString("N");
                profile.UpdatedAt = DateTime.UtcNow;
                var index = state.AgentProfiles.FindIndex(p => p.Id == profile.Id);
                if (index >= 0) state.AgentProfiles[index] = Clone(profile);
                else state.AgentProfiles.Add(Clone(profile));
                SaveUnsafe();
            }
            RaiseChanged();
            return Clone(profile);
        }

        public bool DeleteAgentProfile(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            EnsureLoaded();
            bool removed;
            lock (sync)
            {
                removed = state.AgentProfiles.RemoveAll(p => p.Id == id && !p.IsBuiltIn) > 0;
                if (removed)
                {
                    if (state.Preferences.ActiveAgentProfileId == id)
                        state.Preferences.ActiveAgentProfileId = null;
                    SaveUnsafe();
                }
            }
            if (removed) RaiseChanged();
            return removed;
        }

        public void UpdatePreferences(Action<WorkbenchPreferences> update)
        {
            if (update == null) return;
            EnsureLoaded();
            lock (sync)
            {
                if (state.Preferences == null) state.Preferences = new WorkbenchPreferences();
                update(state.Preferences);
                SaveUnsafe();
            }
            RaiseChanged();
        }

        public void Reload()
        {
            lock (sync)
            {
                loaded = false;
                state = new WorkbenchState();
            }
            EnsureLoaded();
            RaiseChanged();
        }

        private void EnsureLoaded()
        {
            if (loaded) return;
            lock (sync)
            {
                if (loaded) return;
                state = ReadState();
                SeedDefaults(state);
                loaded = true;
                SaveUnsafe();
            }
        }

        private WorkbenchState ReadState()
        {
            if (!File.Exists(filePath)) return new WorkbenchState();
            try
            {
                var json = File.ReadAllText(filePath, Encoding.UTF8);
                return JsonConvert.DeserializeObject<WorkbenchState>(json) ?? new WorkbenchState();
            }
            catch
            {
                try
                {
                    var backup = filePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                    File.Copy(filePath, backup, true);
                }
                catch { }
                return new WorkbenchState();
            }
        }

        private void SaveUnsafe()
        {
            state.Sessions = state.Sessions ?? new List<ChatSession>();
            state.Prompts = state.Prompts ?? new List<PromptTemplate>();
            state.AgentProfiles = state.AgentProfiles ?? new List<AgentProfile>();
            state.Preferences = state.Preferences ?? new WorkbenchPreferences();

            var json = JsonConvert.SerializeObject(state, Formatting.Indented);
            var temp = filePath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(filePath))
            {
                var backup = filePath + ".bak";
                try { File.Replace(temp, filePath, backup, true); }
                catch
                {
                    File.Copy(temp, filePath, true);
                    File.Delete(temp);
                }
            }
            else
            {
                File.Move(temp, filePath);
            }
        }

        private static void SeedDefaults(WorkbenchState value)
        {
            if (value.Sessions == null) value.Sessions = new List<ChatSession>();
            if (value.Prompts == null) value.Prompts = new List<PromptTemplate>();
            if (value.AgentProfiles == null) value.AgentProfiles = new List<AgentProfile>();
            if (value.Preferences == null) value.Preferences = new WorkbenchPreferences();

            foreach (var prompt in DefaultPrompts())
            {
                if (!value.Prompts.Any(p => string.Equals(p.Id, prompt.Id, StringComparison.OrdinalIgnoreCase)))
                    value.Prompts.Add(prompt);
            }

            foreach (var profile in DefaultAgentProfiles())
            {
                if (!value.AgentProfiles.Any(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase)))
                    value.AgentProfiles.Add(profile);
            }
        }

        private static IEnumerable<PromptTemplate> DefaultPrompts()
        {
            yield return BuiltInPrompt("builtin-review", "Deep code review", "Review",
                "Review the selected code and its surrounding architecture. Find correctness, security, concurrency, performance and maintainability issues. Rank findings by severity and propose concrete patches.");
            yield return BuiltInPrompt("builtin-debug", "Debug failing behavior", "Debugging",
                "Analyze the current failure using the active editor, build output and debugger context. Identify the most likely root cause, prepare the smallest safe fix and add a regression test.");
            yield return BuiltInPrompt("builtin-refactor", "Safe refactor", "Refactoring",
                "Refactor the active code for clarity and maintainability while preserving public behavior. Keep the change focused, build the solution and run relevant tests.");
            yield return BuiltInPrompt("builtin-tests", "Generate meaningful tests", "Testing",
                "Inspect the active implementation and existing test conventions. Add meaningful unit and integration tests for normal, edge and failure cases. Avoid tests that merely repeat implementation details.");
            yield return BuiltInPrompt("builtin-security", "Security assessment", "Security",
                "Perform a threat-focused review of the selected component. Check trust boundaries, authentication, authorization, secrets, injection, unsafe process execution, file access and logging of sensitive data. Produce implementable mitigations.");
        }

        private static PromptTemplate BuiltInPrompt(string id, string name, string category, string content)
        {
            return new PromptTemplate
            {
                Id = id,
                Name = name,
                Category = category,
                Content = content,
                Description = content,
                IsBuiltIn = true,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private static IEnumerable<AgentProfile> DefaultAgentProfiles()
        {
            yield return BuiltInProfile("builtin-architect", "Software Architect", "Architecture and cross-project design",
                "Act as the software architect for this repository. Understand the existing architecture before changing it. Prefer cohesive boundaries, explicit contracts, migration-safe decisions and documented trade-offs. Never replace working subsystems without a concrete reason.");
            yield return BuiltInProfile("builtin-csharp", "C# Developer", "Production-grade .NET implementation",
                "Act as a senior C# and .NET developer. Follow the repository's language version and framework constraints. Use async and cancellation correctly, preserve nullable contracts, avoid UI-thread blocking and keep code testable.");
            yield return BuiltInProfile("builtin-reviewer", "Code Reviewer", "Correctness and maintainability review",
                "Act as a strict code reviewer. Prioritize correctness, regressions, security, resource lifetime, race conditions, error handling and test gaps. Provide concrete evidence and patches, not generic advice.");
            yield return BuiltInProfile("builtin-test", "Test Engineer", "Regression and integration testing",
                "Act as a test engineer. Derive tests from behavior and risk. Cover edge cases, cancellation, persistence failures and process errors. Keep tests deterministic and explain any platform constraints.");
            yield return BuiltInProfile("builtin-security", "Security Reviewer", "Threat modeling and secure defaults",
                "Act as a security reviewer. Treat external processes, repositories, terminals, credentials and generated code as trust boundaries. Require explicit confirmation for destructive actions and avoid exposing secrets in logs or prompts.");
            yield return BuiltInProfile("builtin-performance", "Performance Analyst", "Responsiveness and resource efficiency",
                "Act as a performance analyst. Look for UI-thread blocking, excessive allocations, unbounded collections, repeated file scans, process leaks and non-virtualized UI. Measure or justify optimizations and preserve readability.");
            yield return BuiltInProfile("builtin-docs", "Documentation Agent", "Accurate technical documentation",
                "Act as a technical documentation engineer. Keep documentation aligned with the implementation, include prerequisites and failure modes, and never document features that do not exist.");
            yield return BuiltInProfile("builtin-devops", "DevOps Agent", "Build, CI and release engineering",
                "Act as a DevOps engineer. Make builds reproducible, keep CI least-privileged, upload useful diagnostics, validate release artifacts and avoid hidden manual steps where automation is possible.");
            yield return BuiltInProfile("builtin-debugger", "Debugging Agent", "Root-cause analysis",
                "Act as a debugging specialist. Reproduce or inspect evidence first, separate symptoms from causes, make the smallest safe correction and add a regression test. Do not guess when logs or debugger state can answer the question.");
        }

        private static AgentProfile BuiltInProfile(string id, string name, string description, string systemPrompt)
        {
            return new AgentProfile
            {
                Id = id,
                Name = name,
                Description = description,
                SystemPrompt = systemPrompt,
                IsBuiltIn = true,
                Enabled = true,
                ConfirmFileWrites = true,
                ConfirmTerminalCommands = true,
                ConfirmGitWrites = true,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private static T Clone<T>(T value)
        {
            if (ReferenceEquals(value, null)) return default(T);
            var json = JsonConvert.SerializeObject(value);
            return JsonConvert.DeserializeObject<T>(json);
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }

    public sealed class WorkbenchState
    {
        [JsonProperty("sessions")]
        public List<ChatSession> Sessions { get; set; } = new List<ChatSession>();

        [JsonProperty("prompts")]
        public List<PromptTemplate> Prompts { get; set; } = new List<PromptTemplate>();

        [JsonProperty("agent_profiles")]
        public List<AgentProfile> AgentProfiles { get; set; } = new List<AgentProfile>();

        [JsonProperty("preferences")]
        public WorkbenchPreferences Preferences { get; set; } = new WorkbenchPreferences();

        [JsonProperty("last_session_id")]
        public string LastSessionId { get; set; }
    }

    public sealed class ChatSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "New session";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string SolutionPath { get; set; }
        public string Branch { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public string ActiveAgentProfile { get; set; }
        public List<ChatHistoryEntry> Entries { get; set; } = new List<ChatHistoryEntry>();
    }

    public sealed class PromptTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Untitled prompt";
        public string Category { get; set; } = "General";
        public string Description { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Tags { get; set; }
        public bool IsBuiltIn { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public override string ToString() => Name;
    }

    public sealed class AgentProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Custom Agent";
        public string Description { get; set; }
        public string SystemPrompt { get; set; } = string.Empty;
        public string PreferredModel { get; set; }
        public bool ConfirmFileWrites { get; set; } = true;
        public bool ConfirmTerminalCommands { get; set; } = true;
        public bool ConfirmGitWrites { get; set; } = true;
        public bool IsBuiltIn { get; set; }
        public bool Enabled { get; set; } = true;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public override string ToString() => Name;
    }

    public sealed class WorkbenchPreferences
    {
        public string ActiveAgentProfileId { get; set; }
        public string ActiveSessionId { get; set; }
        public bool NavigationCollapsed { get; set; }
        public bool IncludeActiveEditorContext { get; set; } = true;
        public bool IncludeGitDiff { get; set; }
        public bool IncludeBuildErrors { get; set; }
        public int MaximumContextCharacters { get; set; } = 120000;
        public string Language { get; set; } = "Auto";
        public System.Collections.Generic.List<string> RecentAssemblies { get; set; } = new();
    }
}
