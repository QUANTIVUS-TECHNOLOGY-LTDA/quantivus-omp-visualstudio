using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using VSAgent.Models;
using VSAgent.Services;

namespace VSAgent.Views
{
    public class SkillsView : UserControl
    {
        private readonly SkillStore skillStore;
        private readonly ActiveSkillRegistry activeSkills;

        private ListBox skillsList;
        private Button removeSkillButton;
        private TextBlock skillEditHeader;
        private TextBox skillNameBox;
        private TextBox skillDescBox;
        private TextBox skillContentBox;
        private CheckBox skillEnabledCheck;
        private Button saveSkillButton;
        private TextBlock skillStorePathLabel;
        private Skill currentEdit;

        public event EventHandler SkillsChanged;

        public SkillsView(SkillStore store, ActiveSkillRegistry activeSkills)
        {
            this.skillStore = store ?? throw new ArgumentNullException(nameof(store));
            this.activeSkills = activeSkills ?? throw new ArgumentNullException(nameof(activeSkills));
            BuildLayout();
            RefreshSkillsList();
            ClearSkillEdit();
        }

        private void BuildLayout()
        {
            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "Skills",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var subGrid = new Grid();
            subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(subGrid, 1);

            var listPanel = new DockPanel();
            var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            var newSkillButton = new Button { Content = "New", MinWidth = 50, Margin = new Thickness(0, 0, 4, 0) };
            newSkillButton.Click += (_, __) => ClearSkillEdit();
            removeSkillButton = new Button { Content = "Remove", MinWidth = 60 };
            removeSkillButton.Click += RemoveSkillClick;
            listButtons.Children.Add(newSkillButton);
            listButtons.Children.Add(removeSkillButton);
            DockPanel.SetDock(listButtons, Dock.Bottom);
            listPanel.Children.Add(listButtons);

            skillsList = new ListBox();
            skillsList.SelectionChanged += (_, __) => LoadSelectedSkillForEdit();
            listPanel.Children.Add(skillsList);
            Grid.SetColumn(listPanel, 0);
            subGrid.Children.Add(listPanel);

            var editPanel = new StackPanel();
            skillEditHeader = new TextBlock { Text = "New skill", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            skillEditHeader.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            editPanel.Children.Add(skillEditHeader);

            var nameLabel = new TextBlock { Text = "Name:", Margin = new Thickness(0, 4, 0, 2) };
            nameLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            editPanel.Children.Add(nameLabel);
            skillNameBox = new TextBox();
            editPanel.Children.Add(skillNameBox);

            var descLabel = new TextBlock { Text = "Description:", Margin = new Thickness(0, 6, 0, 2) };
            descLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            editPanel.Children.Add(descLabel);
            skillDescBox = new TextBox();
            editPanel.Children.Add(skillDescBox);

            var contentLabel = new TextBlock { Text = "Content (injected as standing instructions):", Margin = new Thickness(0, 6, 0, 2) };
            contentLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            editPanel.Children.Add(contentLabel);
            skillContentBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 120,
                Margin = new Thickness(0, 0, 0, 4)
            };
            editPanel.Children.Add(skillContentBox);

            skillEnabledCheck = new CheckBox { Content = "Enabled (content injected when skill is active)", Margin = new Thickness(0, 4, 0, 4) };
            editPanel.Children.Add(skillEnabledCheck);

            saveSkillButton = new Button { Content = "Save skill", MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            saveSkillButton.Click += SaveSkillClick;
            editPanel.Children.Add(saveSkillButton);

            var activateLabel = new TextBlock { Text = "Active in prompt:", Margin = new Thickness(0, 12, 0, 2), FontWeight = FontWeights.SemiBold };
            activateLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            editPanel.Children.Add(activateLabel);

            var activateHint = new TextBlock
            {
                Text = "Toggle a skill's Enabled checkbox above and use /skill <name>, /skill-off <name>, /skill-clear in the agent prompt.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            activateHint.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            editPanel.Children.Add(activateHint);

            Grid.SetColumn(editPanel, 2);
            subGrid.Children.Add(editPanel);
            grid.Children.Add(subGrid);

            skillStorePathLabel = new TextBlock { Text = $"Storage: {skillStore.FilePath}", FontSize = 10, Margin = new Thickness(0, 8, 0, 0) };
            skillStorePathLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            Grid.SetRow(skillStorePathLabel, 2);
            grid.Children.Add(skillStorePathLabel);

            Content = grid;
        }

        public void RefreshSkillsList()
        {
            skillsList.Items.Clear();
            foreach (var s in skillStore.Snapshot())
                skillsList.Items.Add(s);
        }

        private void LoadSelectedSkillForEdit()
        {
            if (skillsList.SelectedItem is Skill s)
            {
                currentEdit = s;
                skillEditHeader.Text = $"Edit skill: {s.Name}";
                skillNameBox.Text = s.Name;
                skillDescBox.Text = s.Description;
                skillContentBox.Text = s.Content;
                skillEnabledCheck.IsChecked = s.IsEnabled;
            }
        }

        private void ClearSkillEdit()
        {
            currentEdit = null;
            skillsList.SelectedIndex = -1;
            skillEditHeader.Text = "New skill";
            skillNameBox.Text = string.Empty;
            skillDescBox.Text = string.Empty;
            skillContentBox.Text = string.Empty;
            skillEnabledCheck.IsChecked = true;
        }

        private void SaveSkillClick(object sender, RoutedEventArgs e)
        {
            var name = (skillNameBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name)) { skillEditHeader.Text = "Name is required"; return; }
            if (currentEdit == null)
            {
                var s = new Skill
                {
                    Name = name,
                    Description = skillDescBox.Text ?? string.Empty,
                    Content = skillContentBox.Text ?? string.Empty,
                    IsEnabled = skillEnabledCheck.IsChecked == true
                };
                skillStore.Add(s);
            }
            else
            {
                currentEdit.Name = name;
                currentEdit.Description = skillDescBox.Text ?? string.Empty;
                currentEdit.Content = skillContentBox.Text ?? string.Empty;
                currentEdit.IsEnabled = skillEnabledCheck.IsChecked == true;
                skillStore.Update(currentEdit);
            }
            RefreshSkillsList();
            SkillsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RemoveSkillClick(object sender, RoutedEventArgs e)
        {
            if (skillsList.SelectedItem is Skill s)
            {
                skillStore.Remove(s.Id);
                activeSkills.Deactivate(s.Name);
                ClearSkillEdit();
                RefreshSkillsList();
                SkillsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
