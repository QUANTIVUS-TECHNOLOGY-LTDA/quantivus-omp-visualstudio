using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VSAgent.Models;
using VSAgent.Services;
using VSAgent.Ui;

namespace VSAgent.Views
{
    internal sealed class ContextInspectorView : UserControl
    {
        private const string ContextSkillName = "__workbench-context";
        private readonly WorkbenchStore store;
        private readonly SkillStore skills;
        private readonly ActiveSkillRegistry activeSkills;
        private readonly CheckBox activeEditorCheck;
        private readonly CheckBox openDocumentsCheck;
        private readonly CheckBox gitDiffCheck;
        private readonly CheckBox buildErrorsCheck;
        private readonly TextBox maximumCharactersBox;
        private readonly TextBox filterBox;
        private readonly ListBox filesList;
        private readonly TextBox previewBox;
        private readonly TextBlock statusText;
        private WorkspaceContextService contextService;
        private WorkspaceContextSnapshot snapshot;
        private List<string> allFiles = new List<string>();
        private readonly HashSet<string> selectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource refreshCancellation;

        public ContextInspectorView(WorkbenchStore store, SkillStore skills, ActiveSkillRegistry activeSkills)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.skills = skills ?? throw new ArgumentNullException(nameof(skills));
            this.activeSkills = activeSkills ?? throw new ArgumentNullException(nameof(activeSkills));
            WorkbenchUi.ApplyToolWindowTheme(this);

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(WorkbenchUi.Button("Refresh", async delegate { await RefreshAsync(); }));
            actions.Children.Add(WorkbenchUi.Button("Preview", async delegate { await PreviewAsync(); }));
            actions.Children.Add(WorkbenchUi.Button("Apply to agent", async delegate { await ApplyAsync(); }, true));
            actions.Children.Add(WorkbenchUi.Button("Deactivate", delegate { Deactivate(); }));
            root.Children.Add(WorkbenchUi.PageHeader("Context inspector",
                "See and control exactly which editor, repository, diff and build data is prepended to OMP prompts.", actions));

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var left = new Grid();
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var sources = new StackPanel();
            sources.Children.Add(WorkbenchUi.Title("Automatic sources", 14));
            activeEditorCheck = WorkbenchUi.CheckBox("Active selection/member/document", true);
            openDocumentsCheck = WorkbenchUi.CheckBox("Open document paths", false);
            gitDiffCheck = WorkbenchUi.CheckBox("Staged and unstaged Git diff", false);
            buildErrorsCheck = WorkbenchUi.CheckBox("Visual Studio errors and warnings", false);
            sources.Children.Add(activeEditorCheck);
            sources.Children.Add(openDocumentsCheck);
            sources.Children.Add(gitDiffCheck);
            sources.Children.Add(buildErrorsCheck);
            var maxPanel = new Grid();
            maxPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            maxPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            var maxLabel = WorkbenchUi.Label("Maximum characters");
            maxLabel.VerticalAlignment = VerticalAlignment.Center;
            maxPanel.Children.Add(maxLabel);
            maximumCharactersBox = WorkbenchUi.TextBox(store.Preferences.MaximumContextCharacters.ToString());
            Grid.SetColumn(maximumCharactersBox, 1);
            maxPanel.Children.Add(maximumCharactersBox);
            sources.Children.Add(maxPanel);
            left.Children.Add(WorkbenchUi.Card(sources, new Thickness(0, 0, 0, 8), new Thickness(8)));

            filterBox = WorkbenchUi.TextBox();
            filterBox.ToolTip = "Filter repository files. Selection is preserved while filtering.";
            filterBox.TextChanged += delegate { ApplyFilter(); };
            Grid.SetRow(filterBox, 1);
            left.Children.Add(filterBox);

            filesList = WorkbenchUi.ListBox(SelectionMode.Extended);
            filesList.Margin = new Thickness(0, 8, 0, 0);
            filesList.SelectionChanged += FilesList_SelectionChanged;
            filesList.MouseDoubleClick += FilesList_MouseDoubleClick;
            Grid.SetRow(filesList, 2);
            left.Children.Add(filesList);
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

            previewBox = WorkbenchUi.TextBox(null, true);
            previewBox.IsReadOnly = true;
            previewBox.FontFamily = new FontFamily("Consolas");
            previewBox.FontSize = 12;
            previewBox.Text = "Refresh the workspace, choose context sources and preview the exact prompt section.";
            Grid.SetColumn(previewBox, 2);
            body.Children.Add(previewBox);

            statusText = WorkbenchUi.Subtitle("Context skill is inactive.");
            statusText.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(statusText, 2);
            root.Children.Add(statusText);

            Content = root;
            Loaded += async delegate { await RefreshAsync(); };
            Unloaded += delegate { refreshCancellation?.Cancel(); };
        }

        public event EventHandler<int> ContextSizeChanged;

        private async Task RefreshAsync()
        {
            refreshCancellation?.Cancel();
            refreshCancellation?.Dispose();
            refreshCancellation = new CancellationTokenSource();
            var token = refreshCancellation.Token;
            statusText.Text = "Reading Visual Studio and repository context…";
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
                if (dte == null) throw new InvalidOperationException("Visual Studio DTE is unavailable.");
                contextService = new WorkspaceContextService(dte);
                snapshot = contextService.CaptureSnapshot();
                var files = await contextService.EnumerateRepositoryFilesAsync(snapshot.RootDirectory, 5000, token);
                allFiles = files.ToList();
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplyFilter();
                    statusText.Text = "Found " + allFiles.Count + " text file(s). Active editor: " + (snapshot.ActiveDocument ?? "none");
                }));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                statusText.Text = "Context refresh failed: " + ex.Message;
            }
        }

        private async Task PreviewAsync()
        {
            if (snapshot == null || contextService == null) await RefreshAsync();
            if (snapshot == null || contextService == null) return;
            PersistVisibleSelection();
            var selection = BuildSelection();
            refreshCancellation?.Cancel();
            refreshCancellation?.Dispose();
            refreshCancellation = new CancellationTokenSource();
            try
            {
                statusText.Text = "Building context preview…";
                var text = await contextService.BuildContextAsync(snapshot, selection, refreshCancellation.Token);
                previewBox.Text = text;
                var tokens = text.Length / 4;
                statusText.Text = text.Length.ToString("N0") + " characters • approximately " + tokens.ToString("N0") + " tokens";
                ContextSizeChanged?.Invoke(this, text.Length);
                store.UpdatePreferences(p =>
                {
                    p.IncludeActiveEditorContext = selection.IncludeActiveEditor;
                    p.IncludeGitDiff = selection.IncludeGitDiff;
                    p.IncludeBuildErrors = selection.IncludeBuildErrors;
                    p.MaximumContextCharacters = selection.MaximumCharacters;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { statusText.Text = "Context preview failed: " + ex.Message; }
        }

        private async Task ApplyAsync()
        {
            await PreviewAsync();
            var content = previewBox.Text;
            if (string.IsNullOrWhiteSpace(content)) return;
            var skill = skills.FindByName(ContextSkillName);
            if (skill == null)
            {
                skill = skills.Add(new Skill
                {
                    Name = ContextSkillName,
                    Description = "User-reviewed workbench context",
                    Content = content,
                    IsEnabled = true
                });
            }
            else
            {
                skill.Description = "User-reviewed workbench context";
                skill.Content = content;
                skill.IsEnabled = true;
                skills.Update(skill);
            }
            activeSkills.Activate(ContextSkillName);
            statusText.Text += " • active for subsequent prompts";
        }

        private void Deactivate()
        {
            activeSkills.Deactivate(ContextSkillName);
            statusText.Text = "Context skill deactivated.";
            ContextSizeChanged?.Invoke(this, 0);
        }

        private ContextSelection BuildSelection()
        {
            var maximum = 120000;
            int.TryParse(maximumCharactersBox.Text, out maximum);
            maximum = Math.Max(4000, Math.Min(500000, maximum));
            maximumCharactersBox.Text = maximum.ToString();
            return new ContextSelection
            {
                IncludeActiveEditor = activeEditorCheck.IsChecked == true,
                IncludeOpenDocuments = openDocumentsCheck.IsChecked == true,
                IncludeGitDiff = gitDiffCheck.IsChecked == true,
                IncludeBuildErrors = buildErrorsCheck.IsChecked == true,
                MaximumCharacters = maximum,
                SelectedFiles = selectedFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        private void ApplyFilter()
        {
            PersistVisibleSelection();
            var query = filterBox.Text?.Trim();
            var visible = string.IsNullOrWhiteSpace(query)
                ? allFiles
                : allFiles.Where(f => f.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            filesList.ItemsSource = visible;
            foreach (var file in visible.Where(selectedFiles.Contains)) filesList.SelectedItems.Add(file);
        }

        private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (var item in e.RemovedItems.Cast<string>()) selectedFiles.Remove(item);
            foreach (var item in e.AddedItems.Cast<string>()) selectedFiles.Add(item);
            statusText.Text = selectedFiles.Count + " repository file(s) selected.";
        }

        private void PersistVisibleSelection()
        {
            if (filesList.ItemsSource == null) return;
            var visible = filesList.Items.Cast<string>().ToList();
            foreach (var item in visible) selectedFiles.Remove(item);
            foreach (var item in filesList.SelectedItems.Cast<string>()) selectedFiles.Add(item);
        }

        private void FilesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!(filesList.SelectedItem is string relative) || snapshot == null || string.IsNullOrWhiteSpace(snapshot.RootDirectory)) return;
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
                dte?.ItemOperations.OpenFile(System.IO.Path.Combine(snapshot.RootDirectory, relative));
            }
            catch (Exception ex) { statusText.Text = "Could not open file: " + ex.Message; }
        }
    }
}
