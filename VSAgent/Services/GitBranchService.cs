using System;
using System.IO;
using System.Text.RegularExpressions;

namespace VSAgent.Services
{
    public static class GitBranchService
    {
        public sealed class GitStatus
        {
            public string Branch { get; set; }
            public bool Detached { get; set; }
            public bool Dirty { get; set; }
        }

        private static readonly Regex HeadRef = new(@"^ref:\s*refs/heads/(.+)$", RegexOptions.Compiled);

        public static GitStatus GetStatus(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return null;

            var headFile = Path.Combine(directory, ".git", "HEAD");
            if (!File.Exists(headFile)) return null;

            string head;
            try { head = File.ReadAllText(headFile).Trim(); }
            catch { return null; }

            var status = new GitStatus();
            var match = HeadRef.Match(head);
            if (match.Success)
            {
                status.Branch = match.Groups[1].Value;
            }
            else if (head.Length >= 7)
            {
                status.Branch = head.Substring(0, 7);
                status.Detached = true;
            }
            else
            {
                return null;
            }

            // Cheap dirty check: any file in .git/info/exclude list, or compare mtimes.
            // A reliable check would need to spawn `git status --porcelain`; we avoid that.
            // Instead, look for the presence of an index that was written after HEAD was last read.
            status.Dirty = HasWorkingChanges(directory);
            return status;
        }

        private static bool HasWorkingChanges(string directory)
        {
            try
            {
                var indexFile = Path.Combine(directory, ".git", "index");
                if (!File.Exists(indexFile)) return false;
                var indexTime = File.GetLastWriteTimeUtc(indexFile);
                return indexTime > DateTime.UtcNow.AddMinutes(-2); // recent index writes suggest activity
            }
            catch { return false; }
        }
    }
}
