using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VSAgent.Services;
using VSAgent.Ui;

namespace VSAgent.Views
{
    internal sealed class RepositoryOverviewView : UserControl
    {
        private readonly TextBlock solutionText;
        private readonly TextBlock rootText;
        private readonly TextBlock branchText;
        private readonly TextBlock summaryText;
        private readonly ListBox projectsList;
        private readonly GitCommandService git = new GitCommandService();
        private CancellationTokenSource refreshCancellation;

        public RepositoryOverviewView()
        {
            WorkbenchUi.ApplyToolWindowTheme(this);
            var root = new StackPanel();
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(WorkbenchUi.Button("Refresh", async delegate { await RefreshAsync(); }, true));
            actions.Children.Add(WorkbenchUi.Button("Build", delegate { ExecuteVsCommand("Build.BuildSolution"); }));
            actions.Children.Add(WorkbenchUi.Button("Rebuild", delegate { ExecuteVsCommand("Build.RebuildSolution"); }));
            actions.Children.Add(WorkbenchUi.Button("Open folder", delegate { OpenFolder(); }));
            root.Children.Add(WorkbenchUi.PageHeader("Repository",
                "Solution structure, active Git branch and project inventory from the current Visual Studio instance.", actions));

            var facts = new Grid();
            facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            solutionText = AddFact(facts, 0, "Solution");
            rootText = AddFact(facts, 1, "Repository root");
            branchText = AddFact(facts, 2, "Branch");
            root.Children.Add(WorkbenchUi.Card(facts));

            var projectsCard = new StackPanel();
            projectsCard.Children.Add(WorkbenchUi.Title("Projects", 14));
            summaryText = WorkbenchUi.Subtitle("No solution loaded.");
            projectsCard.Children.Add(summaryText);
            projectsList = WorkbenchUi.ListBox();
            projectsList.MinHeight = 220;
            projectsCard.Children.Add(projectsList);
            root.Children.Add(WorkbenchUi.Card(projectsCard));

            Content = WorkbenchUi.PageScroll(root);
            Loaded += async delegate { await RefreshAsync(); };
            Unloaded += delegate { refreshCancellation?.Cancel(); };
        }

        private static TextBlock AddFact(Grid grid, int row, string label)
        {
            var line = new Grid { Margin = new Thickness(0, row == 0 ? 0 : 6, 0, 0) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var name = WorkbenchUi.Label(label);
            name.Margin = new Thickness(0);
            line.Children.Add(name);
            var value = WorkbenchUi.Subtitle("—");
            value.Margin = new Thickness(0);
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(value, 1);
            line.Children.Add(value);
            Grid.SetRow(line, row);
            grid.Children.Add(line);
            return value;
        }

        private async Task RefreshAsync()
        {
            refreshCancellation?.Cancel();
            refreshCancellation?.Dispose();
            refreshCancellation = new CancellationTokenSource();
            var token = refreshCancellation.Token;
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
                var solution = dte?.Solution;
                var solutionPath = solution?.FullName;
                var directory = string.IsNullOrWhiteSpace(solutionPath) ? null : Path.GetDirectoryName(solutionPath);
                solutionText.Text = string.IsNullOrWhiteSpace(solutionPath) ? "No solution loaded" : solutionPath;
                projectsList.ItemsSource = EnumerateProjects(solution).ToList();
                summaryText.Text = projectsList.Items.Count + " project(s) loaded";

                var repositoryRoot = GitCommandService.FindRepositoryRoot(directory);
                rootText.Text = repositoryRoot ?? "Not a Git repository";
                if (repositoryRoot == null)
                {
                    branchText.Text = "—";
                    return;
                }

                var status = await git.GetStatusAsync(repositoryRoot, token);
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    branchText.Text = status.IsRepository
                        ? status.Branch + (status.IsDirty ? "  •  " + status.Files.Count + " changed" : "  •  clean")
                        : status.Error;
                }));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                summaryText.Text = "Refresh failed: " + ex.Message;
            }
        }

        private static IEnumerable<string> EnumerateProjects(Solution solution)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (solution == null) yield break;
            foreach (Project project in solution.Projects)
            {
                foreach (var value in EnumerateProject(project, 0)) yield return value;
            }
        }

        private static IEnumerable<string> EnumerateProject(Project project, int depth)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null) yield break;
            var prefix = new string(' ', Math.Max(0, depth) * 2);
            if (!string.IsNullOrWhiteSpace(project.Name)) yield return prefix + project.Name;
            ProjectItems items = null;
            try { items = project.ProjectItems; } catch { }
            if (items == null) yield break;
            foreach (ProjectItem item in items)
            {
                Project nested = null;
                try { nested = item.SubProject; } catch { }
                if (nested == null) continue;
                foreach (var value in EnumerateProject(nested, depth + 1)) yield return value;
            }
        }

        private void ExecuteVsCommand(string command)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
                dte?.ExecuteCommand(command);
                summaryText.Text = command.EndsWith("RebuildSolution", StringComparison.Ordinal) ? "Rebuild started." : "Build started.";
            }
            catch (Exception ex)
            {
                summaryText.Text = "Command failed: " + ex.Message;
            }
        }

        private void OpenFolder()
        {
            var path = rootText.Text;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { summaryText.Text = "Could not open folder: " + ex.Message; }
        }
    }

    internal sealed class GitChangesView : UserControl
    {
        private readonly GitCommandService git = new GitCommandService();
        private readonly ListBox filesList;
        private readonly TextBox diffBox;
        private readonly TextBox commitMessage;
        private readonly TextBlock statusText;
        private readonly TextBlock branchText;
        private CancellationTokenSource operationCancellation;
        private GitWorkspaceStatus currentStatus;

        public GitChangesView()
        {
            WorkbenchUi.ApplyToolWindowTheme(this);
            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headerActions = new StackPanel { Orientation = Orientation.Horizontal };
            headerActions.Children.Add(WorkbenchUi.Button("Refresh", async delegate { await RefreshAsync(); }, true));
            headerActions.Children.Add(WorkbenchUi.Button("Pull", async delegate { await PullAsync(); }));
            headerActions.Children.Add(WorkbenchUi.Button("Push", async delegate { await PushAsync(); }));
            root.Children.Add(WorkbenchUi.PageHeader("Changes",
                "Review diffs, stage individual files, commit intentionally and keep destructive operations confirmed.", headerActions));

            var branchBar = new Grid();
            branchBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            branchBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            branchText = WorkbenchUi.Title("No repository", 13);
            branchText.VerticalAlignment = VerticalAlignment.Center;
            branchBar.Children.Add(branchText);
            var branchActions = new StackPanel { Orientation = Orientation.Horizontal };
            branchActions.Children.Add(WorkbenchUi.Button("New branch", async delegate { await CreateBranchAsync(); }));
            branchActions.Children.Add(WorkbenchUi.Button("Switch branch", async delegate { await SwitchBranchAsync(); }));
            Grid.SetColumn(branchActions, 1);
            branchBar.Children.Add(branchActions);
            Grid.SetRow(branchBar, 1);
            root.Children.Add(WorkbenchUi.Card(branchBar, new Thickness(0, 0, 0, 8), new Thickness(8)));

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 2);
            root.Children.Add(body);

            var left = new DockPanel();
            filesList = WorkbenchUi.ListBox(SelectionMode.Extended);
            filesList.SelectionChanged += async delegate { await ShowDiffAsync(); };
            left.Children.Add(filesList);
            var fileActions = new WrapPanel { Orientation = Orientation.Horizontal };
            fileActions.Children.Add(WorkbenchUi.Button("Stage", async delegate { await StageAsync(); }, true));
            fileActions.Children.Add(WorkbenchUi.Button("Unstage", async delegate { await UnstageAsync(); }));
            fileActions.Children.Add(WorkbenchUi.Button("Discard", async delegate { await DiscardAsync(); }));
            DockPanel.SetDock(fileActions, Dock.Bottom);
            left.Children.Add(fileActions);
            body.Children.Add(WorkbenchUi.Card(left, new Thickness(0), new Thickness(8)));

            var splitter = new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns
            };
            Grid.SetColumn(splitter, 1);
            body.Children.Add(splitter);

            diffBox = WorkbenchUi.TextBox(null, true);
            diffBox.IsReadOnly = true;
            diffBox.FontFamily = new FontFamily("Consolas");
            diffBox.FontSize = 12;
            diffBox.AcceptsTab = true;
            diffBox.Text = "Select a changed file to inspect its staged and unstaged diff.";
            Grid.SetColumn(diffBox, 2);
            body.Children.Add(diffBox);

            var commitBar = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            commitBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            commitBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            commitMessage = WorkbenchUi.TextBox();
            commitMessage.ToolTip = "Commit message for currently staged files.";
            commitBar.Children.Add(commitMessage);
            var commitButton = WorkbenchUi.Button("Commit staged", async delegate { await CommitAsync(); }, true);
            Grid.SetColumn(commitButton, 1);
            commitBar.Children.Add(commitButton);
            statusText = WorkbenchUi.Subtitle("Ready.");
            statusText.Margin = new Thickness(0, 4, 0, 0);
            var footer = new StackPanel();
            footer.Children.Add(commitBar);
            footer.Children.Add(statusText);
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;
            Loaded += async delegate { await RefreshAsync(); };
            Unloaded += delegate { operationCancellation?.Cancel(); };
        }

        public event EventHandler<int> ChangedFileCountChanged;

        private string GetWorkingDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
            var solutionPath = dte?.Solution?.FullName;
            return string.IsNullOrWhiteSpace(solutionPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : Path.GetDirectoryName(solutionPath);
        }

        private async Task RefreshAsync()
        {
            var token = BeginOperation("Refreshing Git status…");
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                var directory = GetWorkingDirectory();
                currentStatus = await git.GetStatusAsync(directory, token);
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!currentStatus.IsRepository)
                    {
                        branchText.Text = "No repository";
                        filesList.ItemsSource = null;
                        statusText.Text = currentStatus.Error;
                        ChangedFileCountChanged?.Invoke(this, 0);
                        return;
                    }
                    branchText.Text = currentStatus.Branch +
                                      (currentStatus.Ahead > 0 ? "  ↑" + currentStatus.Ahead : string.Empty) +
                                      (currentStatus.Behind > 0 ? "  ↓" + currentStatus.Behind : string.Empty);
                    filesList.ItemsSource = currentStatus.Files;
                    statusText.Text = currentStatus.IsDirty ? currentStatus.Files.Count + " changed file(s)." : "Working tree clean.";
                    ChangedFileCountChanged?.Invoke(this, currentStatus.Files.Count);
                    if (currentStatus.Files.Count == 0) diffBox.Text = "Working tree clean.";
                }));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { statusText.Text = "Git status failed: " + ex.Message; }
        }

        private async Task ShowDiffAsync()
        {
            if (!(filesList.SelectedItem is GitChangedFile file) || currentStatus == null) return;
            var token = BeginOperation("Loading diff…", false);
            try
            {
                var unstaged = await git.GetDiffAsync(currentStatus.RootDirectory, file.Path, false, token);
                var staged = await git.GetDiffAsync(currentStatus.RootDirectory, file.Path, true, token);
                var text = string.Empty;
                if (!string.IsNullOrWhiteSpace(staged.CombinedOutput)) text += "# STAGED\r\n" + staged.CombinedOutput.TrimEnd() + "\r\n\r\n";
                if (!string.IsNullOrWhiteSpace(unstaged.CombinedOutput)) text += "# WORKING TREE\r\n" + unstaged.CombinedOutput.TrimEnd();
                if (string.IsNullOrWhiteSpace(text) && file.IsUntracked)
                {
                    var fullPath = Path.Combine(currentStatus.RootDirectory, file.Path);
                    text = File.Exists(fullPath) ? File.ReadAllText(fullPath) : "Untracked file is no longer present.";
                }
                diffBox.Text = string.IsNullOrWhiteSpace(text) ? "No textual diff is available." : text;
                statusText.Text = file.Path;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { statusText.Text = "Diff failed: " + ex.Message; }
        }

        private async Task StageAsync()
        {
            var paths = SelectedPaths();
            if (paths.Count == 0 || currentStatus == null) return;
            await RunWriteAsync("Staging files…", () => git.StageAsync(currentStatus.RootDirectory, paths, operationCancellation.Token));
        }

        private async Task UnstageAsync()
        {
            var paths = SelectedPaths();
            if (paths.Count == 0 || currentStatus == null) return;
            await RunWriteAsync("Unstaging files…", () => git.UnstageAsync(currentStatus.RootDirectory, paths, operationCancellation.Token));
        }

        private async Task DiscardAsync()
        {
            var paths = SelectedPaths();
            if (paths.Count == 0 || currentStatus == null) return;
            if (MessageBox.Show("Discard working-tree changes in the selected file(s)? This cannot be undone by the extension.",
                "Quantivus OMP", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await RunWriteAsync("Discarding changes…", () => git.DiscardAsync(currentStatus.RootDirectory, paths, operationCancellation.Token));
        }

        private async Task CommitAsync()
        {
            if (currentStatus == null || string.IsNullOrWhiteSpace(commitMessage.Text))
            {
                statusText.Text = "Enter a commit message.";
                return;
            }
            if (MessageBox.Show("Create a Git commit with the currently staged changes?", "Quantivus OMP",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var message = commitMessage.Text.Trim();
            await RunWriteAsync("Creating commit…", () => git.CommitAsync(currentStatus.RootDirectory, message, operationCancellation.Token));
            commitMessage.Clear();
        }

        private async Task PullAsync()
        {
            if (currentStatus == null) await RefreshAsync();
            if (currentStatus == null || !currentStatus.IsRepository) return;
            if (MessageBox.Show("Pull remote changes with --ff-only?", "Quantivus OMP",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await RunWriteAsync("Pulling…", () => git.PullAsync(currentStatus.RootDirectory, operationCancellation.Token));
        }

        private async Task PushAsync()
        {
            if (currentStatus == null) await RefreshAsync();
            if (currentStatus == null || !currentStatus.IsRepository) return;
            if (MessageBox.Show("Push the current branch to its configured remote?", "Quantivus OMP",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await RunWriteAsync("Pushing…", () => git.PushAsync(currentStatus.RootDirectory, operationCancellation.Token));
        }

        private async Task CreateBranchAsync()
        {
            if (currentStatus == null) await RefreshAsync();
            if (currentStatus == null || !currentStatus.IsRepository) return;
            var name = TextPromptWindow.Ask("Create branch", "New branch name:");
            if (string.IsNullOrWhiteSpace(name)) return;
            await RunWriteAsync("Creating branch…", () => git.CreateBranchAsync(currentStatus.RootDirectory, name, operationCancellation.Token));
        }

        private async Task SwitchBranchAsync()
        {
            if (currentStatus == null) await RefreshAsync();
            if (currentStatus == null || !currentStatus.IsRepository) return;
            var name = TextPromptWindow.Ask("Switch branch", "Existing branch name:");
            if (string.IsNullOrWhiteSpace(name)) return;
            if (currentStatus.IsDirty && MessageBox.Show("The working tree has changes. Continue switching branches?", "Quantivus OMP",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await RunWriteAsync("Switching branch…", () => git.SwitchBranchAsync(currentStatus.RootDirectory, name, operationCancellation.Token));
        }

        private async Task RunWriteAsync(string status, Func<Task<GitProcessResult>> action)
        {
            BeginOperation(status);
            try
            {
                var result = await action();
                statusText.Text = result.Succeeded
                    ? (string.IsNullOrWhiteSpace(result.StandardOutput) ? "Git command completed." : result.StandardOutput.Trim())
                    : "Git command failed: " + result.CombinedOutput.Trim();
                await RefreshAsync();
            }
            catch (OperationCanceledException) { statusText.Text = "Git operation cancelled."; }
            catch (Exception ex) { statusText.Text = "Git operation failed: " + ex.Message; }
        }

        private CancellationToken BeginOperation(string status, bool cancelPrevious = true)
        {
            if (cancelPrevious) operationCancellation?.Cancel();
            operationCancellation?.Dispose();
            operationCancellation = new CancellationTokenSource();
            statusText.Text = status;
            return operationCancellation.Token;
        }

        private List<string> SelectedPaths() => filesList.SelectedItems.Cast<GitChangedFile>().Select(f => f.Path).ToList();
    }

    internal static class TextPromptWindow
    {
        public static string Ask(string title, string label)
        {
            var window = new Window
            {
                Title = title,
                Width = 420,
                Height = 165,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };
            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(WorkbenchUi.Label(label));
            var input = WorkbenchUi.TextBox();
            panel.Children.Add(input);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = WorkbenchUi.Button("OK", null, true);
            ok.IsDefault = true;
            ok.Click += delegate { window.DialogResult = true; };
            var cancel = WorkbenchUi.Button("Cancel");
            cancel.IsCancel = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            window.Content = panel;
            window.Loaded += delegate { input.Focus(); };
            return window.ShowDialog() == true ? input.Text?.Trim() : null;
        }
    }
}
