using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using VSAgent.Models;
using VSAgent.Services;
using VSAgent.Ui;

namespace VSAgent.Views
{
    internal sealed class AgentWorkspaceView : UserControl
    {
        private const string ActiveProfileSkillName = "__agent-profile";
        private readonly WorkbenchStore store;
        private readonly SkillStore skills;
        private readonly ActiveSkillRegistry activeSkills;
        private readonly ListBox profileList;
        private readonly TextBox nameBox;
        private readonly TextBox descriptionBox;
        private readonly TextBox promptBox;
        private readonly TextBox modelBox;
        private readonly CheckBox confirmWrites;
        private readonly CheckBox confirmTerminal;
        private readonly CheckBox confirmGit;
        private readonly TextBlock statusText;
        private AgentProfile selected;

        public AgentWorkspaceView(WorkbenchStore store, SkillStore skills, ActiveSkillRegistry activeSkills)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.skills = skills ?? throw new ArgumentNullException(nameof(skills));
            this.activeSkills = activeSkills ?? throw new ArgumentNullException(nameof(activeSkills));
            WorkbenchUi.ApplyToolWindowTheme(this);

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(WorkbenchUi.PageHeader("Agent workspace",
                "Select a focused role. Activation is implemented as a visible OMP skill and keeps destructive actions behind the existing permission dialog."));

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var left = new DockPanel();
            profileList = WorkbenchUi.ListBox();
            profileList.DisplayMemberPath = "Name";
            profileList.SelectionChanged += ProfileList_SelectionChanged;
            DockPanel.SetDock(profileList, Dock.Top);
            left.Children.Add(profileList);
            var leftButtons = new StackPanel { Orientation = Orientation.Horizontal };
            leftButtons.Children.Add(WorkbenchUi.Button("New", delegate { NewProfile(); }));
            leftButtons.Children.Add(WorkbenchUi.Button("Delete", delegate { DeleteProfile(); }));
            DockPanel.SetDock(leftButtons, Dock.Bottom);
            left.Children.Add(leftButtons);
            body.Children.Add(WorkbenchUi.Card(left, new Thickness(0), new Thickness(8)));

            var splitter = new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            Grid.SetColumn(splitter, 1);
            body.Children.Add(splitter);

            var editor = new StackPanel();
            editor.Children.Add(WorkbenchUi.Label("Name"));
            nameBox = WorkbenchUi.TextBox();
            editor.Children.Add(nameBox);
            editor.Children.Add(WorkbenchUi.Label("Description"));
            descriptionBox = WorkbenchUi.TextBox();
            editor.Children.Add(descriptionBox);
            editor.Children.Add(WorkbenchUi.Label("Preferred model (optional)"));
            modelBox = WorkbenchUi.TextBox();
            modelBox.ToolTip = "When set, activation updates the OMP model and restarts the agent on its next use.";
            editor.Children.Add(modelBox);
            editor.Children.Add(WorkbenchUi.Label("System instructions"));
            promptBox = WorkbenchUi.TextBox(null, true);
            promptBox.MinHeight = 220;
            editor.Children.Add(promptBox);

            var permissionPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
            permissionPanel.Children.Add(WorkbenchUi.Title("Confirmation policy", 13));
            permissionPanel.Children.Add(WorkbenchUi.Subtitle("These instructions complement, but do not bypass, the ACP permission dialog."));
            confirmWrites = WorkbenchUi.CheckBox("Require confirmation before file writes", true);
            confirmTerminal = WorkbenchUi.CheckBox("Require confirmation before terminal commands", true);
            confirmGit = WorkbenchUi.CheckBox("Require confirmation before Git write operations", true);
            permissionPanel.Children.Add(confirmWrites);
            permissionPanel.Children.Add(confirmTerminal);
            permissionPanel.Children.Add(confirmGit);
            editor.Children.Add(permissionPanel);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(WorkbenchUi.Button("Save profile", delegate { SaveProfile(); }));
            actions.Children.Add(WorkbenchUi.Button("Activate", delegate { ActivateProfile(); }, true));
            actions.Children.Add(WorkbenchUi.Button("Deactivate", delegate { DeactivateProfile(); }));
            editor.Children.Add(actions);
            statusText = WorkbenchUi.Subtitle(string.Empty);
            editor.Children.Add(statusText);

            var editorScroll = WorkbenchUi.PageScroll(editor);
            Grid.SetColumn(editorScroll, 2);
            body.Children.Add(editorScroll);

            Content = root;
            Loaded += delegate { RefreshProfiles(); };
            store.Changed += Store_Changed;
        }

        public event EventHandler<AgentProfile> ProfileActivated;

        private void Store_Changed(object sender, EventArgs e)
        {
            if (IsLoaded) Dispatcher.BeginInvoke(new Action(RefreshProfiles));
        }

        private void RefreshProfiles()
        {
            var selectedId = selected?.Id ?? store.Preferences.ActiveAgentProfileId;
            var profiles = store.SnapshotAgentProfiles().ToList();
            profileList.ItemsSource = profiles;
            var match = profiles.FirstOrDefault(p => p.Id == selectedId) ?? profiles.FirstOrDefault();
            if (match != null) profileList.SelectedItem = match;
        }

        private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selected = profileList.SelectedItem as AgentProfile;
            if (selected == null) return;
            nameBox.Text = selected.Name ?? string.Empty;
            descriptionBox.Text = selected.Description ?? string.Empty;
            promptBox.Text = selected.SystemPrompt ?? string.Empty;
            modelBox.Text = selected.PreferredModel ?? string.Empty;
            confirmWrites.IsChecked = selected.ConfirmFileWrites;
            confirmTerminal.IsChecked = selected.ConfirmTerminalCommands;
            confirmGit.IsChecked = selected.ConfirmGitWrites;
            statusText.Text = selected.IsBuiltIn ? "Built-in profile. Saving creates an editable copy." : "Custom profile.";
        }

        private void NewProfile()
        {
            selected = new AgentProfile { Name = "Custom Agent", IsBuiltIn = false };
            profileList.SelectedItem = null;
            nameBox.Text = selected.Name;
            descriptionBox.Clear();
            promptBox.Clear();
            modelBox.Clear();
            confirmWrites.IsChecked = true;
            confirmTerminal.IsChecked = true;
            confirmGit.IsChecked = true;
            statusText.Text = "New profile. Enter instructions and save it.";
            nameBox.Focus();
            nameBox.SelectAll();
        }

        private void SaveProfile()
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text) || string.IsNullOrWhiteSpace(promptBox.Text))
            {
                statusText.Text = "Name and system instructions are required.";
                return;
            }

            var profile = selected == null || selected.IsBuiltIn
                ? new AgentProfile()
                : selected;
            profile.Name = nameBox.Text.Trim();
            profile.Description = descriptionBox.Text?.Trim();
            profile.SystemPrompt = promptBox.Text.Trim();
            profile.PreferredModel = modelBox.Text?.Trim();
            profile.ConfirmFileWrites = confirmWrites.IsChecked == true;
            profile.ConfirmTerminalCommands = confirmTerminal.IsChecked == true;
            profile.ConfirmGitWrites = confirmGit.IsChecked == true;
            profile.IsBuiltIn = false;
            profile.Enabled = true;
            selected = store.UpsertAgentProfile(profile);
            statusText.Text = "Profile saved.";
            RefreshProfiles();
        }

        private void DeleteProfile()
        {
            if (selected == null || selected.IsBuiltIn)
            {
                statusText.Text = "Built-in profiles cannot be deleted.";
                return;
            }
            if (MessageBox.Show("Delete agent profile '" + selected.Name + "'?", "Quantivus OMP",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            store.DeleteAgentProfile(selected.Id);
            selected = null;
            RefreshProfiles();
        }

        private void ActivateProfile()
        {
            if (selected == null) SaveProfile();
            if (selected == null) return;

            var skill = skills.FindByName(ActiveProfileSkillName);
            var content = BuildSkillContent(selected);
            if (skill == null)
            {
                skill = skills.Add(new Skill
                {
                    Name = ActiveProfileSkillName,
                    Description = "Active workbench agent profile: " + selected.Name,
                    Content = content,
                    IsEnabled = true
                });
            }
            else
            {
                skill.Description = "Active workbench agent profile: " + selected.Name;
                skill.Content = content;
                skill.IsEnabled = true;
                skills.Update(skill);
            }
            activeSkills.Activate(ActiveProfileSkillName);
            store.UpdatePreferences(p => p.ActiveAgentProfileId = selected.Id);
            statusText.Text = "Active agent: " + selected.Name;
            ProfileActivated?.Invoke(this, selected);
        }

        private void DeactivateProfile()
        {
            activeSkills.Deactivate(ActiveProfileSkillName);
            store.UpdatePreferences(p => p.ActiveAgentProfileId = null);
            statusText.Text = "Agent profile deactivated.";
        }

        private static string BuildSkillContent(AgentProfile profile)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[Workbench agent profile: " + profile.Name + "]");
            builder.AppendLine(profile.SystemPrompt ?? string.Empty);
            builder.AppendLine();
            builder.AppendLine("Safety and confirmation policy:");
            builder.AppendLine(profile.ConfirmFileWrites
                ? "- Ask for explicit approval before file writes that are not already covered by the current user request."
                : "- File writes are permitted only within the explicit current task and repository.");
            builder.AppendLine(profile.ConfirmTerminalCommands
                ? "- Ask for explicit approval before terminal commands that change state or install software."
                : "- Terminal commands must still remain scoped to the explicit current task.");
            builder.AppendLine(profile.ConfirmGitWrites
                ? "- Ask for explicit approval before commit, branch, pull, push, reset, clean or other Git write operations."
                : "- Git writes are permitted only when directly requested; never force-push without separate confirmation.");
            return builder.ToString();
        }
    }
}
