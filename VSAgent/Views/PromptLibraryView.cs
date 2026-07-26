using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using VSAgent.Services;
using VSAgent.Ui;

namespace VSAgent.Views
{
    internal sealed class PromptLibraryView : UserControl
    {
        private readonly WorkbenchStore store;
        private readonly TextBox searchBox;
        private readonly ListBox promptList;
        private readonly TextBox nameBox;
        private readonly TextBox categoryBox;
        private readonly TextBox tagsBox;
        private readonly TextBox descriptionBox;
        private readonly TextBox contentBox;
        private readonly TextBlock statusText;
        private PromptTemplate selected;

        public PromptLibraryView(WorkbenchStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            WorkbenchUi.ApplyToolWindowTheme(this);

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var headerActions = new StackPanel { Orientation = Orientation.Horizontal };
            headerActions.Children.Add(WorkbenchUi.Button("Import", delegate { Import(); }));
            headerActions.Children.Add(WorkbenchUi.Button("Export", delegate { Export(); }));
            root.Children.Add(WorkbenchUi.PageHeader("Prompt library",
                "Reusable, versionable prompts with workbench variables such as {{solution}}, {{file}}, {{selection}}, {{branch}} and {{diff}}.", headerActions));

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var left = new DockPanel();
            searchBox = WorkbenchUi.TextBox();
            searchBox.ToolTip = "Filter by name, category, tag or content.";
            searchBox.Margin = new Thickness(0, 0, 0, 8);
            searchBox.TextChanged += delegate { RefreshList(); };
            DockPanel.SetDock(searchBox, Dock.Top);
            left.Children.Add(searchBox);

            promptList = WorkbenchUi.ListBox();
            promptList.DisplayMemberPath = "Name";
            promptList.SelectionChanged += PromptList_SelectionChanged;
            left.Children.Add(promptList);

            var leftButtons = new StackPanel { Orientation = Orientation.Horizontal };
            leftButtons.Children.Add(WorkbenchUi.Button("New", delegate { NewPrompt(); }));
            leftButtons.Children.Add(WorkbenchUi.Button("Delete", delegate { DeletePrompt(); }));
            DockPanel.SetDock(leftButtons, Dock.Bottom);
            left.Children.Add(leftButtons);
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

            var editor = new StackPanel();
            editor.Children.Add(WorkbenchUi.Label("Name"));
            nameBox = WorkbenchUi.TextBox();
            editor.Children.Add(nameBox);
            var meta = new Grid();
            meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var categoryPanel = new StackPanel();
            categoryPanel.Children.Add(WorkbenchUi.Label("Category"));
            categoryBox = WorkbenchUi.TextBox();
            categoryPanel.Children.Add(categoryBox);
            meta.Children.Add(categoryPanel);
            var tagsPanel = new StackPanel();
            tagsPanel.Children.Add(WorkbenchUi.Label("Tags"));
            tagsBox = WorkbenchUi.TextBox();
            tagsPanel.Children.Add(tagsBox);
            Grid.SetColumn(tagsPanel, 2);
            meta.Children.Add(tagsPanel);
            editor.Children.Add(meta);
            editor.Children.Add(WorkbenchUi.Label("Description"));
            descriptionBox = WorkbenchUi.TextBox(null, true);
            descriptionBox.MinHeight = 55;
            editor.Children.Add(descriptionBox);
            editor.Children.Add(WorkbenchUi.Label("Prompt"));
            contentBox = WorkbenchUi.TextBox(null, true);
            contentBox.MinHeight = 260;
            contentBox.FontFamily = new System.Windows.Media.FontFamily("Consolas");
            editor.Children.Add(contentBox);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(WorkbenchUi.Button("Save", delegate { SavePrompt(); }));
            buttons.Children.Add(WorkbenchUi.Button("Use in chat", delegate { UsePrompt(); }, true));
            editor.Children.Add(buttons);
            statusText = WorkbenchUi.Subtitle(string.Empty);
            editor.Children.Add(statusText);

            var scroll = WorkbenchUi.PageScroll(editor);
            Grid.SetColumn(scroll, 2);
            body.Children.Add(scroll);

            Content = root;
            Loaded += delegate { RefreshList(); };
            store.Changed += delegate { if (IsLoaded) Dispatcher.BeginInvoke(new Action(RefreshList)); };
        }

        public event EventHandler<PromptTemplate> PromptSelected;

        private void RefreshList()
        {
            var query = searchBox.Text?.Trim();
            var selectedId = selected?.Id;
            var values = store.SnapshotPrompts();
            if (!string.IsNullOrWhiteSpace(query))
            {
                values = values.Where(p => Contains(p.Name, query) || Contains(p.Category, query) || Contains(p.Tags, query) ||
                                           Contains(p.Description, query) || Contains(p.Content, query)).ToList();
            }
            promptList.ItemsSource = values;
            var match = values.FirstOrDefault(p => p.Id == selectedId) ?? values.FirstOrDefault();
            if (match != null) promptList.SelectedItem = match;
        }

        private void PromptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selected = promptList.SelectedItem as PromptTemplate;
            if (selected == null) return;
            nameBox.Text = selected.Name ?? string.Empty;
            categoryBox.Text = selected.Category ?? string.Empty;
            tagsBox.Text = selected.Tags ?? string.Empty;
            descriptionBox.Text = selected.Description ?? string.Empty;
            contentBox.Text = selected.Content ?? string.Empty;
            statusText.Text = selected.IsBuiltIn ? "Built-in template. Saving creates a custom copy." : "Last updated " + selected.UpdatedAt.ToLocalTime().ToString("g");
        }

        private void NewPrompt()
        {
            selected = new PromptTemplate { Name = "Untitled prompt", Category = "General", IsBuiltIn = false };
            promptList.SelectedItem = null;
            nameBox.Text = selected.Name;
            categoryBox.Text = selected.Category;
            tagsBox.Clear();
            descriptionBox.Clear();
            contentBox.Clear();
            statusText.Text = "New prompt.";
            nameBox.Focus();
            nameBox.SelectAll();
        }

        private void SavePrompt()
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text) || string.IsNullOrWhiteSpace(contentBox.Text))
            {
                statusText.Text = "Name and prompt content are required.";
                return;
            }
            var value = selected == null || selected.IsBuiltIn ? new PromptTemplate() : selected;
            value.Name = nameBox.Text.Trim();
            value.Category = string.IsNullOrWhiteSpace(categoryBox.Text) ? "General" : categoryBox.Text.Trim();
            value.Tags = tagsBox.Text?.Trim();
            value.Description = descriptionBox.Text?.Trim();
            value.Content = contentBox.Text.Trim();
            value.IsBuiltIn = false;
            selected = store.UpsertPrompt(value);
            statusText.Text = "Prompt saved.";
            RefreshList();
        }

        private void DeletePrompt()
        {
            if (selected == null || selected.IsBuiltIn)
            {
                statusText.Text = "Built-in prompts cannot be deleted.";
                return;
            }
            if (MessageBox.Show("Delete prompt '" + selected.Name + "'?", "Quantivus OMP",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            store.DeletePrompt(selected.Id);
            selected = null;
            RefreshList();
        }

        private void UsePrompt()
        {
            if (selected == null && !string.IsNullOrWhiteSpace(contentBox.Text)) SavePrompt();
            if (selected != null) PromptSelected?.Invoke(this, selected);
        }

        private void Export()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export OMP prompt library",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = "quantivus-omp-prompts.json",
                AddExtension = true,
                DefaultExt = ".json"
            };
            if (dialog.ShowDialog() != true) return;
            var json = JsonConvert.SerializeObject(store.SnapshotPrompts(), Formatting.Indented);
            File.WriteAllText(dialog.FileName, json, new UTF8Encoding(false));
            statusText.Text = "Exported to " + dialog.FileName;
        }

        private void Import()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import OMP prompts",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var values = JsonConvert.DeserializeObject<List<PromptTemplate>>(File.ReadAllText(dialog.FileName, Encoding.UTF8));
                if (values == null) throw new InvalidDataException("No prompts were found in the file.");
                var count = 0;
                foreach (var prompt in values.Where(p => !string.IsNullOrWhiteSpace(p.Content)))
                {
                    prompt.Id = Guid.NewGuid().ToString("N");
                    prompt.IsBuiltIn = false;
                    store.UpsertPrompt(prompt);
                    count++;
                }
                statusText.Text = "Imported " + count + " prompt(s).";
            }
            catch (Exception ex)
            {
                statusText.Text = "Import failed: " + ex.Message;
            }
        }

        private static bool Contains(string value, string query) =>
            !string.IsNullOrWhiteSpace(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
