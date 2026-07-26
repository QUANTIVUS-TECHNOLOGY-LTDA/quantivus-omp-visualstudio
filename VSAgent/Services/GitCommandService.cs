using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VSAgent.Services
{
    public enum GitCommandRisk
    {
        ReadOnly,
        WritesWorkingTree,
        Destructive
    }

    public sealed class GitProcessResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; } = string.Empty;
        public string StandardError { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public bool Succeeded => ExitCode == 0;

        public string CombinedOutput
        {
            get
            {
                if (string.IsNullOrWhiteSpace(StandardError)) return StandardOutput ?? string.Empty;
                if (string.IsNullOrWhiteSpace(StandardOutput)) return StandardError ?? string.Empty;
                return StandardOutput.TrimEnd() + Environment.NewLine + StandardError.TrimEnd();
            }
        }
    }

    public sealed class GitChangedFile
    {
        public string Path { get; set; }
        public string IndexStatus { get; set; }
        public string WorkTreeStatus { get; set; }
        public bool IsStaged => !string.IsNullOrWhiteSpace(IndexStatus) && IndexStatus != "?";
        public bool IsUntracked => IndexStatus == "?" && WorkTreeStatus == "?";
        public string StatusText => (IndexStatus ?? " ") + (WorkTreeStatus ?? " ");
        public override string ToString() => StatusText + "  " + Path;
    }

    public sealed class GitWorkspaceStatus
    {
        public string RootDirectory { get; set; }
        public string Branch { get; set; }
        public int Ahead { get; set; }
        public int Behind { get; set; }
        public List<GitChangedFile> Files { get; set; } = new List<GitChangedFile>();
        public string Error { get; set; }
        public bool IsRepository => string.IsNullOrWhiteSpace(Error) && !string.IsNullOrWhiteSpace(RootDirectory);
        public bool IsDirty => Files.Count > 0;
    }

    /// <summary>
    /// Executes explicit Git commands without a shell. Output is bounded, calls
    /// are cancellable and write/destructive commands can be classified before use.
    /// </summary>
    public sealed class GitCommandService
    {
        private const int MaximumCapturedCharacters = 2_000_000;

        public static string FindRepositoryRoot(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return null;
            try
            {
                var fullPath = Path.GetFullPath(directory);
                var current = File.Exists(fullPath)
                    ? new FileInfo(fullPath).Directory
                    : new DirectoryInfo(fullPath);
                while (current != null)
                {
                    if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git")))
                        return current.FullName;
                    current = current.Parent;
                }
            }
            catch { }
            return null;
        }

        public static GitCommandRisk ClassifyRisk(params string[] arguments)
        {
            if (arguments == null || arguments.Length == 0) return GitCommandRisk.ReadOnly;
            var normalized = arguments.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
            if (normalized.Length == 0) return GitCommandRisk.ReadOnly;
            var command = normalized[0].Trim().ToLowerInvariant();
            var all = string.Join(" ", normalized).ToLowerInvariant();

            if (all.Contains("--force") || all.Contains(" -f") || all.Contains("--hard") ||
                command == "clean" || all.Contains("branch -d") ||
                (command == "checkout" && all.Contains(" -- ")) ||
                (command == "restore" && !all.Contains("--staged")))
                return GitCommandRisk.Destructive;

            switch (command)
            {
                case "status":
                case "diff":
                case "log":
                case "show":
                case "rev-parse":
                case "ls-files":
                case "remote":
                case "tag":
                case "--version":
                    return GitCommandRisk.ReadOnly;
                case "branch":
                    return normalized.Length == 1 || all.Contains("--show-current") || all.Contains("--list")
                        ? GitCommandRisk.ReadOnly
                        : GitCommandRisk.WritesWorkingTree;
                default:
                    return GitCommandRisk.WritesWorkingTree;
            }
        }

        public async Task<GitWorkspaceStatus> GetStatusAsync(string directory, CancellationToken cancellationToken)
        {
            var root = FindRepositoryRoot(directory);
            if (root == null)
                return new GitWorkspaceStatus { Error = "The active solution is not inside a Git repository." };

            var result = await RunAsync(root, cancellationToken, "status", "--porcelain=v1", "--branch", "--untracked-files=all").ConfigureAwait(false);
            if (!result.Succeeded)
                return new GitWorkspaceStatus { RootDirectory = root, Error = result.CombinedOutput.Trim() };

            var status = new GitWorkspaceStatus { RootDirectory = root };
            foreach (var line in SplitLines(result.StandardOutput))
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    ParseBranchLine(status, line.Substring(3));
                    continue;
                }
                if (line.Length < 3) continue;
                var path = line.Substring(3).Trim();
                var renameSeparator = path.IndexOf(" -> ", StringComparison.Ordinal);
                if (renameSeparator >= 0) path = path.Substring(renameSeparator + 4);
                status.Files.Add(new GitChangedFile
                {
                    IndexStatus = line[0].ToString(),
                    WorkTreeStatus = line[1].ToString(),
                    Path = path
                });
            }
            return status;
        }

        public Task<GitProcessResult> GetDiffAsync(string directory, string path, bool staged, CancellationToken cancellationToken)
        {
            var args = new List<string> { "diff", "--no-ext-diff", "--unified=4" };
            if (staged) args.Add("--cached");
            if (!string.IsNullOrWhiteSpace(path))
            {
                args.Add("--");
                args.Add(path);
            }
            return RunAsync(RequireRoot(directory), cancellationToken, args.ToArray());
        }

        public Task<GitProcessResult> StageAsync(string directory, IEnumerable<string> paths, CancellationToken cancellationToken) =>
            RunPathsAsync(directory, cancellationToken, new[] { "add", "--" }, paths);

        public Task<GitProcessResult> UnstageAsync(string directory, IEnumerable<string> paths, CancellationToken cancellationToken) =>
            RunPathsAsync(directory, cancellationToken, new[] { "restore", "--staged", "--" }, paths);

        public Task<GitProcessResult> DiscardAsync(string directory, IEnumerable<string> paths, CancellationToken cancellationToken) =>
            RunPathsAsync(directory, cancellationToken, new[] { "restore", "--worktree", "--" }, paths);

        public Task<GitProcessResult> CommitAsync(string directory, string message, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A commit message is required.", nameof(message));
            return RunAsync(RequireRoot(directory), cancellationToken, "commit", "-m", message.Trim());
        }

        public Task<GitProcessResult> PullAsync(string directory, CancellationToken cancellationToken) =>
            RunAsync(RequireRoot(directory), cancellationToken, "pull", "--ff-only");

        public Task<GitProcessResult> PushAsync(string directory, CancellationToken cancellationToken) =>
            RunAsync(RequireRoot(directory), cancellationToken, "push");

        public Task<GitProcessResult> CreateBranchAsync(string directory, string branchName, CancellationToken cancellationToken)
        {
            ValidateBranchName(branchName);
            return RunAsync(RequireRoot(directory), cancellationToken, "switch", "-c", branchName.Trim());
        }

        public Task<GitProcessResult> SwitchBranchAsync(string directory, string branchName, CancellationToken cancellationToken)
        {
            ValidateBranchName(branchName);
            return RunAsync(RequireRoot(directory), cancellationToken, "switch", branchName.Trim());
        }

        public Task<GitProcessResult> RunAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));
            if (!Directory.Exists(workingDirectory)) throw new DirectoryNotFoundException(workingDirectory);
            if (arguments == null || arguments.Length == 0) throw new ArgumentException("A Git command is required.", nameof(arguments));

            var completion = new TaskCompletionSource<GitProcessResult>();
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var started = Stopwatch.StartNew();
            Process process = null;
            CancellationTokenRegistration registration = default(CancellationTokenRegistration);

            try
            {
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git.exe",
                        Arguments = JoinArguments(arguments),
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    },
                    EnableRaisingEvents = true
                };
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) AppendBounded(stdout, e.Data);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) AppendBounded(stderr, e.Data);
                };
                process.Exited += delegate
                {
                    try
                    {
                        process.WaitForExit();
                        started.Stop();
                        completion.TrySetResult(new GitProcessResult
                        {
                            ExitCode = process.ExitCode,
                            StandardOutput = stdout.ToString(),
                            StandardError = stderr.ToString(),
                            Duration = started.Elapsed
                        });
                    }
                    catch (Exception ex) { completion.TrySetException(ex); }
                    finally
                    {
                        registration.Dispose();
                        process.Dispose();
                    }
                };

                if (!process.Start()) throw new InvalidOperationException("Git could not be started.");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                registration = cancellationToken.Register(delegate
                {
                    try { if (!process.HasExited) process.Kill(); } catch { }
                    completion.TrySetCanceled();
                });
            }
            catch (Exception ex)
            {
                registration.Dispose();
                process?.Dispose();
                completion.TrySetException(ex);
            }
            return completion.Task;
        }

        private Task<GitProcessResult> RunPathsAsync(string directory, CancellationToken cancellationToken, IEnumerable<string> prefix, IEnumerable<string> paths)
        {
            var args = prefix.ToList();
            args.AddRange(NormalizePaths(paths));
            return RunAsync(RequireRoot(directory), cancellationToken, args.ToArray());
        }

        private static string RequireRoot(string directory)
        {
            var root = FindRepositoryRoot(directory);
            if (root == null) throw new InvalidOperationException("The active solution is not inside a Git repository.");
            return root;
        }

        private static IEnumerable<string> NormalizePaths(IEnumerable<string> paths)
        {
            var normalized = (paths ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalized.Count == 0) throw new InvalidOperationException("Select at least one changed file.");
            return normalized;
        }

        private static void ValidateBranchName(string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName)) throw new ArgumentException("A branch name is required.", nameof(branchName));
            var invalid = new[] { "..", "~", "^", ":", "?", "*", "[", "\\", " ", "@{" };
            if (invalid.Any(branchName.Contains) || branchName.StartsWith("-", StringComparison.Ordinal) ||
                branchName.EndsWith("/", StringComparison.Ordinal) || branchName.EndsWith(".", StringComparison.Ordinal))
                throw new ArgumentException("The branch name is not valid.", nameof(branchName));
        }

        private static void ParseBranchLine(GitWorkspaceStatus status, string text)
        {
            var branchPart = text;
            var detailStart = text.IndexOf(" [", StringComparison.Ordinal);
            if (detailStart >= 0)
            {
                branchPart = text.Substring(0, detailStart);
                var detail = text.Substring(detailStart + 2).TrimEnd(']');
                foreach (var part in detail.Split(','))
                {
                    var value = part.Trim();
                    int parsed;
                    if (value.StartsWith("ahead ", StringComparison.OrdinalIgnoreCase) && int.TryParse(value.Substring(6), out parsed))
                        status.Ahead = parsed;
                    else if (value.StartsWith("behind ", StringComparison.OrdinalIgnoreCase) && int.TryParse(value.Substring(7), out parsed))
                        status.Behind = parsed;
                }
            }
            var dots = branchPart.IndexOf("...", StringComparison.Ordinal);
            status.Branch = dots >= 0 ? branchPart.Substring(0, dots) : branchPart;
            if (status.Branch == "HEAD (no branch)") status.Branch = "detached HEAD";
        }

        private static string JoinArguments(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(QuoteArgument));

        private static string QuoteArgument(string value)
        {
            if (value == null) return "\"\"";
            if (value.Length > 0 && value.All(c => !char.IsWhiteSpace(c) && c != '"')) return value;
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string[] SplitLines(string value) =>
            (value ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        private static void AppendBounded(StringBuilder builder, string line)
        {
            lock (builder)
            {
                if (builder.Length >= MaximumCapturedCharacters) return;
                var remaining = MaximumCapturedCharacters - builder.Length;
                if (line.Length > remaining) line = line.Substring(0, remaining);
                builder.AppendLine(line);
            }
        }
    }
}
