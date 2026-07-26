using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using VSAgent.Models;
using VSAgent.Services;

namespace VSAgent.Workbench.Tests
{
    [TestClass]
    public sealed class WorkbenchStoreTests
    {
        private string directory = null!;

        [TestInitialize]
        public void Initialize()
        {
            directory = Path.Combine(Path.GetTempPath(), "QuantivusOMP.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(directory, true); } catch { }
        }

        [TestMethod]
        public void SeedsBuiltInPromptsAndProfiles()
        {
            var store = new WorkbenchStore(directory);

            Assert.IsTrue(store.SnapshotPrompts().Count >= 5);
            Assert.IsTrue(store.SnapshotAgentProfiles().Count >= 9);
            Assert.IsTrue(store.SnapshotPrompts().All(p => !string.IsNullOrWhiteSpace(p.Name)));
            Assert.IsTrue(store.SnapshotAgentProfiles().All(p => !string.IsNullOrWhiteSpace(p.SystemPrompt)));
        }

        [TestMethod]
        public void RoundTripsSessionAndPreferences()
        {
            var store = new WorkbenchStore(directory);
            var session = new ChatSession
            {
                Name = "Persistence test",
                SolutionPath = @"C:\src\sample\sample.sln",
                Branch = "feature/test",
                Provider = "OpenAI",
                Model = "test-model"
            };
            session.Entries.Add(new ChatHistoryEntry
            {
                Prompt = "Analyze this code",
                Response = "Analysis complete",
                CodeContext = "class C {}",
                OperationType = "OMP Agent"
            });

            store.UpsertSession(session);
            store.UpdatePreferences(p =>
            {
                p.ActiveSessionId = session.Id;
                p.IncludeGitDiff = true;
                p.MaximumContextCharacters = 64000;
            });

            var reloaded = new WorkbenchStore(directory);
            var restored = reloaded.GetSession(session.Id);

            Assert.IsNotNull(restored);
            Assert.AreEqual("Persistence test", restored.Name);
            Assert.AreEqual("feature/test", restored.Branch);
            Assert.AreEqual(1, restored.Entries.Count);
            Assert.AreEqual("Analyze this code", restored.Entries[0].Prompt);
            Assert.AreEqual(session.Id, reloaded.Preferences.ActiveSessionId);
            Assert.IsTrue(reloaded.Preferences.IncludeGitDiff);
            Assert.AreEqual(64000, reloaded.Preferences.MaximumContextCharacters);
        }

        [TestMethod]
        public void BuiltInsCannotBeDeletedButCustomItemsCan()
        {
            var store = new WorkbenchStore(directory);
            var builtInPrompt = store.SnapshotPrompts().First(p => p.IsBuiltIn);
            var builtInProfile = store.SnapshotAgentProfiles().First(p => p.IsBuiltIn);

            Assert.IsFalse(store.DeletePrompt(builtInPrompt.Id));
            Assert.IsFalse(store.DeleteAgentProfile(builtInProfile.Id));

            var prompt = store.UpsertPrompt(new PromptTemplate
            {
                Name = "Custom",
                Content = "Do the work",
                IsBuiltIn = false
            });
            var profile = store.UpsertAgentProfile(new AgentProfile
            {
                Name = "Custom Agent",
                SystemPrompt = "Act carefully",
                IsBuiltIn = false
            });

            Assert.IsTrue(store.DeletePrompt(prompt.Id));
            Assert.IsTrue(store.DeleteAgentProfile(profile.Id));
            Assert.IsFalse(store.SnapshotPrompts().Any(p => p.Id == prompt.Id));
            Assert.IsFalse(store.SnapshotAgentProfiles().Any(p => p.Id == profile.Id));
        }

        [TestMethod]
        public void CorruptStateFallsBackToDefaults()
        {
            File.WriteAllText(Path.Combine(directory, "workbench.json"), "not-json");
            var store = new WorkbenchStore(directory);

            Assert.IsTrue(store.SnapshotPrompts().Count > 0);
            Assert.IsTrue(Directory.GetFiles(directory, "workbench.json.corrupt-*").Length == 1);
        }
    }
}
