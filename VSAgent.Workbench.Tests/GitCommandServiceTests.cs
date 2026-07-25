using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using VSAgent.Services;

namespace VSAgent.Workbench.Tests
{
    [TestClass]
    public sealed class GitCommandServiceTests
    {
        [DataTestMethod]
        [DataRow("status")]
        [DataRow("diff")]
        [DataRow("log")]
        [DataRow("--version")]
        public void ClassifiesReadOnlyCommands(string command)
        {
            Assert.AreEqual(GitCommandRisk.ReadOnly, GitCommandService.ClassifyRisk(command));
        }

        [DataTestMethod]
        [DataRow("add")]
        [DataRow("commit")]
        [DataRow("push")]
        [DataRow("switch")]
        public void ClassifiesStateChangingCommands(string command)
        {
            Assert.AreEqual(GitCommandRisk.WritesWorkingTree, GitCommandService.ClassifyRisk(command));
        }

        [DataTestMethod]
        [DataRow("reset", "--hard")]
        [DataRow("clean", "-fd")]
        [DataRow("push", "--force")]
        [DataRow("restore", "--worktree", "--", "file.cs")]
        public void ClassifiesDestructiveCommands(string first, string second, string? third = null, string? fourth = null)
        {
            Assert.AreEqual(GitCommandRisk.Destructive,
                GitCommandService.ClassifyRisk(first, second, third ?? string.Empty, fourth ?? string.Empty));
        }

        [TestMethod]
        public void FindsRepositoryRootFromNestedDirectory()
        {
            var root = Path.Combine(Path.GetTempPath(), "QuantivusOMP.Tests", Guid.NewGuid().ToString("N"));
            var nested = Path.Combine(root, "src", "nested");
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(nested);
            try
            {
                Assert.AreEqual(root, GitCommandService.FindRepositoryRoot(nested));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }
}
