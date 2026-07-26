using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VSAgent.Services.VisualStudio;

namespace VSAgent.Services
{
    public sealed class ContextSelection
    {
        public bool IncludeActiveEditor { get; set; } = true;
        public bool IncludeOpenDocuments { get; set; }
        public bool IncludeGitDiff { get; set; }
        public bool IncludeBuildErrors { get; set; }
        public int MaximumCharacters { get; set; } = 120000;
        public List<string> SelectedFiles { get; set; } = new List<string>();
    }

    public sealed class WorkspaceContextSnapshot
    {
        public string SolutionPath { get; set; }
        public string RootDirectory { get; set; }
        public string ActiveDocument { get; set; }
        public string ActiveEditorContext { get; set; }
        public List<string> OpenDocuments { get; set; } = new List<string>();
        public List<string> RepositoryFiles { get; set; } = new List<string>();
        public List<string> BuildErrors { get; set; } = new List<string>();
        public int ApproximateCharacters { get; set; }
        public int ApproximateTokens => ApproximateCharacters / 4;
    }

    /// <summary>
    /// Builds a transparent context package that the user can preview before it
    /// is activated as a normal OMP skill. Explicit files are always visible in
    /// the UI and repository enumeration respects .gitignore and .ompignore.
    /// </summary>
    public sealed class WorkspaceContextService
    {
        private readonly DTE2 dte;
        private readonly GitCommandService git = new GitCommandService();

        public WorkspaceContextService(DTE2 dte)
        {
            this.dte = dte ?? throw new ArgumentNullException(nameof(dte));
        }

        public WorkspaceContextSnapshot CaptureSnapshot()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var solutionPath = dte.Solution?.FullName;
            var solutionDirectory = string.IsNullOrWhiteSpace(solutionPath) ? null : Path.GetDirectoryName(solutionPath);
            var active = dte.ActiveDocument?.FullName;
            var editor = new EditorContextService(dte);
            var activeContext = editor.GetActiveDocumentContext(40000);

            return new WorkspaceContextSnapshot
            {
                SolutionPath = solutionPath,
                RootDirectory = GitCommandService.FindRepositoryRoot(solutionDirectory) ?? solutionDirectory,
                ActiveDocument = active,
                ActiveEditorContext = activeContext,
                OpenDocuments = GetOpenDocumentPaths(),
                BuildErrors = GetBuildErrors()
            };
        }

        public async Task<IReadOnlyList<string>> EnumerateRepositoryFilesAsync(string rootDirectory, int maximumFiles, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
                return new List<string>();
            maximumFiles = Math.Max(1, Math.Min(10000, maximumFiles));

            return await Task.Run(() =>
            {
                var matcher = ContextIgnoreMatcher.Load(rootDirectory);
                var output = new List<string>();
                var pending = new Stack<string>();
                pending.Push(rootDirectory);

                while (pending.Count > 0 && output.Count < maximumFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var directory = pending.Pop();
                    IEnumerable<string> directories;
                    IEnumerable<string> files;
                    try
                    {
                        directories = Directory.EnumerateDirectories(directory);
                        files = Directory.EnumerateFiles(directory);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var child in directories)
                    {
                        var relative = MakeRelative(rootDirectory, child);
                        if (!matcher.IsIgnored(relative, true)) pending.Push(child);
                    }
                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relative = MakeRelative(rootDirectory, file);
                        if (matcher.IsIgnored(relative, false) || IsLikelyBinaryByExtension(file)) continue;
                        output.Add(relative);
                        if (output.Count >= maximumFiles) break;
                    }
                }

                output.Sort(StringComparer.OrdinalIgnoreCase);
                return (IReadOnlyList<string>)output;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> BuildContextAsync(WorkspaceContextSnapshot snapshot, ContextSelection selection, CancellationToken cancellationToken)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            var maximum = Math.Max(4000, Math.Min(500000, selection.MaximumCharacters));
            var builder = new BoundedContextBuilder(maximum);

            builder.AppendHeading("Quantivus OMP workbench context");
            builder.AppendLine("Solution: " + (snapshot.SolutionPath ?? "(none)"));
            builder.AppendLine("Root: " + (snapshot.RootDirectory ?? "(none)"));

            if (selection.IncludeActiveEditor && !string.IsNullOrWhiteSpace(snapshot.ActiveEditorContext))
            {
                builder.AppendHeading("Active editor");
                builder.AppendBlock(snapshot.ActiveEditorContext);
            }

            if (selection.IncludeOpenDocuments && snapshot.OpenDocuments.Count > 0)
            {
                builder.AppendHeading("Open documents");
                foreach (var file in snapshot.OpenDocuments) builder.AppendLine("- " + file);
            }

            var root = snapshot.RootDirectory;
            foreach (var relative in (selection.SelectedFiles ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (builder.IsFull || string.IsNullOrWhiteSpace(root)) break;
                var fullPath = Path.GetFullPath(Path.Combine(root, relative));
                if (!IsInside(root, fullPath) || !File.Exists(fullPath)) continue;
                var content = await ReadTextFileAsync(fullPath, 250000, cancellationToken).ConfigureAwait(false);
                if (content == null) continue;
                builder.AppendHeading("Repository file: " + relative);
                builder.AppendBlock(content);
            }

            if (selection.IncludeGitDiff && !string.IsNullOrWhiteSpace(root) && !builder.IsFull)
            {
                var diff = await git.GetDiffAsync(root, null, false, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(diff.CombinedOutput))
                {
                    builder.AppendHeading("Unstaged Git diff");
                    builder.AppendBlock(diff.CombinedOutput);
                }
                var staged = await git.GetDiffAsync(root, null, true, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(staged.CombinedOutput))
                {
                    builder.AppendHeading("Staged Git diff");
                    builder.AppendBlock(staged.CombinedOutput);
                }
            }

            if (selection.IncludeBuildErrors && snapshot.BuildErrors.Count > 0 && !builder.IsFull)
            {
                builder.AppendHeading("Visual Studio errors and warnings");
                foreach (var error in snapshot.BuildErrors) builder.AppendLine("- " + error);
            }

            return builder.ToString();
        }

        private List<string> GetOpenDocumentPaths()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new List<string>();
            try
            {
                foreach (Document document in dte.Documents)
                {
                    if (!string.IsNullOrWhiteSpace(document.FullName)) result.Add(document.FullName);
                }
            }
            catch { }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private List<string> GetBuildErrors()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var output = new List<string>();
            try
            {
                var items = dte.ToolWindows.ErrorList.ErrorItems;
                for (var i = 1; i <= items.Count && output.Count < 200; i++)
                {
                    ErrorItem item;
                    try { item = items.Item(i); }
                    catch { continue; }
                    if (item == null) continue;
                    var location = string.IsNullOrWhiteSpace(item.FileName)
                        ? string.Empty
                        : item.FileName + (item.Line > 0 ? ":" + item.Line : string.Empty);
                    output.Add((item.ErrorLevel == vsBuildErrorLevel.vsBuildErrorLevelHigh ? "error" : "warning") +
                               (string.IsNullOrWhiteSpace(location) ? string.Empty : " " + location) +
                               ": " + item.Description);
                }
            }
            catch { }
            return output;
        }

        private static async Task<string> ReadTextFileAsync(string path, int maximumCharacters, CancellationToken cancellationToken)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > 4 * 1024 * 1024) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192, true))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    var buffer = new char[Math.Min(maximumCharacters + 1, 262144)];
                    var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (read == 0) return string.Empty;
                    for (var i = 0; i < Math.Min(read, 4096); i++) if (buffer[i] == '\0') return null;
                    var value = new string(buffer, 0, Math.Min(read, maximumCharacters));
                    if (read > maximumCharacters) value += Environment.NewLine + "[file truncated]";
                    return value;
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool IsInside(string root, string path)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(path);
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLikelyBinaryByExtension(string path)
        {
            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            return BinaryExtensions.Contains(extension ?? string.Empty);
        }

        private static readonly HashSet<string> BinaryExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".pdb", ".zip", ".7z", ".rar", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico",
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".mp3", ".mp4", ".wav", ".avi",
            ".db", ".sqlite", ".suo", ".user", ".nupkg", ".snk", ".pfx", ".cer"
        };

        private static string MakeRelative(string root, string path)
        {
            var rootUri = new Uri(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var pathUri = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }
    }

    public sealed class ContextIgnoreMatcher
    {
        private readonly List<IgnoreRule> rules = new List<IgnoreRule>();

        public static ContextIgnoreMatcher Load(string rootDirectory)
        {
            var matcher = new ContextIgnoreMatcher();
            matcher.AddDefaults();
            matcher.LoadFile(Path.Combine(rootDirectory, ".gitignore"));
            matcher.LoadFile(Path.Combine(rootDirectory, ".ompignore"));
            return matcher;
        }

        public bool IsIgnored(string relativePath, bool isDirectory)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return false;
            var normalized = relativePath.Replace('\\', '/').TrimStart('/');
            var ignored = false;
            foreach (var rule in rules)
            {
                if (rule.DirectoryOnly && !isDirectory) continue;
                if (rule.Regex.IsMatch(normalized)) ignored = !rule.Negated;
            }
            return ignored;
        }

        private void AddDefaults()
        {
            foreach (var value in new[]
            {
                ".git/", ".vs/", "bin/", "obj/", "node_modules/", "packages/", "TestResults/", "artifacts/", "coverage/",
                "*.pfx", "*.snk", "*.key", "*.pem", "*.cer", "*.p12", "*.db", "*.sqlite", "*.user", "*.suo",
                "appsettings.*.json", ".env", ".env.*", "secrets.json"
            }) AddRule(value);
        }

        private void LoadFile(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                foreach (var line in File.ReadAllLines(path)) AddRule(line);
            }
            catch { }
        }

        private void AddRule(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            var value = raw.Trim();
            if (value.StartsWith("#", StringComparison.Ordinal)) return;
            var negated = value.StartsWith("!", StringComparison.Ordinal);
            if (negated) value = value.Substring(1);
            if (string.IsNullOrWhiteSpace(value)) return;
            var directoryOnly = value.EndsWith("/", StringComparison.Ordinal);
            value = value.TrimEnd('/');
            var anchored = value.StartsWith("/", StringComparison.Ordinal);
            value = value.TrimStart('/').Replace("\\", "/");

            var pattern = Regex.Escape(value)
                .Replace("\\*\\*", ".*")
                .Replace("\\*", "[^/]*")
                .Replace("\\?", "[^/]");
            var prefix = anchored ? "^" : "(^|.*/)";
            var suffix = directoryOnly ? "(/.*)?$" : "$";
            rules.Add(new IgnoreRule(new Regex(prefix + pattern + suffix, RegexOptions.IgnoreCase | RegexOptions.Compiled), negated, directoryOnly));
        }

        private sealed class IgnoreRule
        {
            public IgnoreRule(Regex regex, bool negated, bool directoryOnly)
            {
                Regex = regex;
                Negated = negated;
                DirectoryOnly = directoryOnly;
            }

            public Regex Regex { get; }
            public bool Negated { get; }
            public bool DirectoryOnly { get; }
        }
    }

    internal sealed class BoundedContextBuilder
    {
        private readonly int maximum;
        private readonly StringBuilder builder = new StringBuilder();

        public BoundedContextBuilder(int maximum)
        {
            this.maximum = maximum;
        }

        public bool IsFull => builder.Length >= maximum;

        public void AppendHeading(string heading)
        {
            AppendLine(string.Empty);
            AppendLine("## " + heading);
        }

        public void AppendLine(string value)
        {
            Append(value ?? string.Empty);
            Append(Environment.NewLine);
        }

        public void AppendBlock(string value)
        {
            AppendLine("```");
            AppendLine(value ?? string.Empty);
            AppendLine("```");
        }

        private void Append(string value)
        {
            if (IsFull || string.IsNullOrEmpty(value)) return;
            var remaining = maximum - builder.Length;
            if (value.Length <= remaining) builder.Append(value);
            else
            {
                builder.Append(value.Substring(0, remaining));
                builder.Append(Environment.NewLine + "[context limit reached]");
            }
        }

        public override string ToString() => builder.ToString();
    }
}
