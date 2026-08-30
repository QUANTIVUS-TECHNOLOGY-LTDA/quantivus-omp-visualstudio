using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VSAgent.Models;
using VSAgent.Services;
using VSAgent.Ui;

namespace VSAgent.Views
{
    /// <summary>
    /// Modal editor for a single Kanban card. Holds title, description and the
    /// selected agent profile. Returns the edited card via the
    /// <see cref="Result"/> property when the dialog is accepted.
    /// </summary>
    internal sealed class KanbanCardEditorDialog : Window
    {
        private readonly WorkbenchStore store;
        private readonly KanbanCard working;
        private readonly TextBox titleBox;
        private readonly TextBox descriptionBox;
        private readonly ComboBox profileBox;
        private readonly ComboBox statusBox;
        private readonly TextBlock statusText;

        public KanbanCard Result { get; private set; }

        public KanbanCardEditorDialog(WorkbenchStore store, KanbanCard card)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            working = card ?? new KanbanCard { Status = KanbanStatus.Backlog };
            if (string.IsNullOrWhiteSpace(working.Id))
                working.Id = Guid.NewGuid().ToString("N");

            Title = working.Title.Length > 0 ? "Edit card — " + working.Title : "New kanban card";
            Width = 520;
            Height = 460;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            WorkbenchUi.ApplyToolWindowTheme(this);

            var root = new StackPanel { Margin = new Thickness(14) };

            root.Children.Add(WorkbenchUi.Title("Kanban card", 16));
            root.Children.Add(WorkbenchUi.Subtitle("Title, prompt body, agent profile and status."));

            root.Children.Add(WorkbenchUi.Label("Title"));
            titleBox = WorkbenchUi.TextBox(working.Title);
            root.Children.Add(titleBox);

            root.Children.Add(WorkbenchUi.Label("Description / prompt"));
            descriptionBox = WorkbenchUi.TextBox(working.Description, true);
            descriptionBox.MinHeight = 140;
            descriptionBox.AcceptsReturn = true;
            root.Children.Add(descriptionBox);

            root.Children.Add(WorkbenchUi.Label("Agent profile"));
            profileBox = WorkbenchUi.ComboBox();
            var profiles = store.SnapshotAgentProfiles().ToList();
            profileBox.Items.Add(new ComboBoxItem { Content = "(no profile)", Tag = null });
            foreach (var profile in profiles)
                profileBox.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
            profileBox.SelectedIndex = SelectProfileIndex(profiles, working.AgentProfileId);
            root.Children.Add(profileBox);

            root.Children.Add(WorkbenchUi.Label("Status"));
            statusBox = WorkbenchUi.ComboBox();
            foreach (var status in new[] { KanbanStatus.Backlog, KanbanStatus.InProgress, KanbanStatus.Done, KanbanStatus.Failed })
                statusBox.Items.Add(new ComboBoxItem { Content = status.ToString(), Tag = status });
            statusBox.SelectedIndex = Math.Max(0, (int)working.Status);
            root.Children.Add(statusBox);

            statusText = WorkbenchUi.Subtitle(string.Empty);
            statusText.Margin = new Thickness(0, 6, 0, 0);
            root.Children.Add(statusText);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(WorkbenchUi.Button("Cancel", delegate { DialogResult = false; Close(); }));
            buttons.Children.Add(WorkbenchUi.Button("Save", delegate { Save(); }, true));
            root.Children.Add(buttons);

            Content = root;
        }

        private int SelectProfileIndex(System.Collections.Generic.List<AgentProfile> profiles, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return 0;
            for (int i = 0; i < profiles.Count; i++)
                if (string.Equals(profiles[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i + 1;
            return 0;
        }

        private void Save()
        {
            working.Title = (titleBox.Text ?? string.Empty).Trim();
            working.Description = descriptionBox.Text ?? string.Empty;
            working.AgentProfileId = (profileBox.SelectedItem as ComboBoxItem)?.Tag as string;
            working.Status = (statusBox.SelectedItem is ComboBoxItem si && si.Tag is KanbanStatus s) ? s : working.Status;

            if (string.IsNullOrWhiteSpace(working.Title))
            {
                statusText.Text = "Title is required.";
                return;
            }

            working.UpdatedAt = DateTime.UtcNow;
            Result = working;
            DialogResult = true;
            Close();
        }
    }
}