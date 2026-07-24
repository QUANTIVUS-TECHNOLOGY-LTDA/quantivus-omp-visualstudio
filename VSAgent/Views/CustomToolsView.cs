using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using VSAgent.Models;
using VSAgent.Services;

namespace VSAgent.Views
{
    /// <summary>
    /// Tab content for the Tools tab. Lists user-defined C# MCP tools with
    /// add/edit/remove support.
    /// </summary>
    public class CustomToolsView : UserControl
    {
        private readonly CustomToolStore store;
        private ListBox toolList;
        private TextBox nameBox;
        private TextBox descBox;
        private TextBox codeBox;
        private CheckBox enabledCheck;
        private TextBlock editHeader;
        private TextBlock storePathLabel;
        private TextBlock compileStatus;
        private CustomMcpTool currentEdit;

        public event EventHandler ToolsChanged;

        public CustomToolsView(CustomToolStore store)
        {
            this.store = store ?? new CustomToolStore();
            BuildLayout();
            RefreshList();
            ClearEdit();
        }

        private void BuildLayout()
        {
            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "Custom MCP tools",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var sub = new Grid();
            sub.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            sub.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            sub.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(sub, 1);

            var left = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            var newBtn = new Button { Content = "New", MinWidth = 50, Margin = new Thickness(0, 0, 4, 0) };
            newBtn.Click += (_, __) => ClearEdit();
            var removeBtn = new Button { Content = "Remove", MinWidth = 60 };
            removeBtn.Click += RemoveClick;
            buttons.Children.Add(newBtn);
            buttons.Children.Add(removeBtn);
            DockPanel.SetDock(buttons, Dock.Bottom);
            left.Children.Add(buttons);

            toolList = new ListBox();
            toolList.SelectionChanged += (_, __) => LoadSelected();
            left.Children.Add(toolList);
            Grid.SetColumn(left, 0);
            sub.Children.Add(left);

            var right = new StackPanel();
            editHeader = new TextBlock { Text = "New tool", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            editHeader.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            right.Children.Add(editHeader);

            right.Children.Add(Label("Name:"));
            nameBox = new TextBox();
            right.Children.Add(nameBox);

            var descLbl = Label("Description:"); descLbl.Margin = new Thickness(0, 6, 0, 0);
            right.Children.Add(descLbl);
            descBox = new TextBox();
            right.Children.Add(descBox);

            var codeLbl = Label("C# source (Roslyn-compiled at startup; signature: object Invoke(IDictionary<string,object> args)):");
            codeLbl.Margin = new Thickness(0, 6, 0, 0); codeLbl.TextWrapping = TextWrapping.Wrap;
            right.Children.Add(codeLbl);
            codeBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                MinHeight = 140,
                Margin = new Thickness(0, 0, 0, 4)
            };
            right.Children.Add(codeBox);

            enabledCheck = new CheckBox { Content = "Enabled (exposed to oh-my-pi)", Margin = new Thickness(0, 4, 0, 4) };
            right.Children.Add(enabledCheck);

            var saveBtn = new Button { Content = "Save tool", MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) };
            saveBtn.Click += SaveClick;
            right.Children.Add(saveBtn);

            compileStatus = new TextBlock { Text = "", FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            right.Children.Add(compileStatus);

            var hint = new TextBlock
            {
                Text = "Use these tools for project-specific read/edit helpers that should always be available to oh-my-pi. " +
                       "Storage: " + store.FilePath,
                FontSize = 11, Opacity = 0.65, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            right.Children.Add(hint);

            Grid.SetColumn(right, 2);
            sub.Children.Add(right);
            grid.Children.Add(sub);

            storePathLabel = new TextBlock { Text = "", FontSize = 10, Margin = new Thickness(0, 8, 0, 0) };
            storePathLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            Grid.SetRow(storePathLabel, 2);
            grid.Children.Add(storePathLabel);

            Content = grid;
        }

        private static TextBlock Label(string text)
        {
            var t = new TextBlock { Text = text, Margin = new Thickness(0, 4, 0, 2) };
            t.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            return t;
        }

        public void RefreshList()
        {
            toolList.Items.Clear();
            foreach (var t in store.Snapshot()) toolList.Items.Add(t);
        }

        private void LoadSelected()
        {
            if (toolList.SelectedItem is CustomMcpTool t)
            {
                currentEdit = t;
                editHeader.Text = "Edit tool: " + t.Name;
                nameBox.Text = t.Name;
                descBox.Text = t.Description;
                codeBox.Text = t.Code;
                enabledCheck.IsChecked = t.Enabled;
            }
        }

        private void ClearEdit()
        {
            currentEdit = null;
            toolList.SelectedIndex = -1;
            editHeader.Text = "New tool";
            nameBox.Text = string.Empty;
            descBox.Text = string.Empty;
            codeBox.Text = DefaultStarter();
            enabledCheck.IsChecked = true;
        }

        private static string DefaultStarter() =>
            "// Available: System.IO, System.Linq, System.Text, System.Collections.Generic\n" +
            "// Args: keys are MCP parameter names as JSON.\n" +
            "public static object Invoke(IDictionary<string, object> args)\n" +
            "{\n" +
            "    return new { ok = true, message = \"hello from a custom tool\" };\n" +
            "}\n";

        private void SaveClick(object sender, RoutedEventArgs e)
        {
            var name = (nameBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name)) { editHeader.Text = "Name is required"; return; }
            if (currentEdit == null)
            {
                store.Add(new CustomMcpTool
                {
                    Name = name,
                    Description = descBox.Text ?? string.Empty,
                    Code = codeBox.Text ?? string.Empty,
                    Enabled = enabledCheck.IsChecked == true
                });
            }
            else
            {
                currentEdit.Name = name;
                currentEdit.Description = descBox.Text ?? string.Empty;
                currentEdit.Code = codeBox.Text ?? string.Empty;
                currentEdit.Enabled = enabledCheck.IsChecked == true;
                store.Update(currentEdit);
            }
            compileStatus.Text = "Saved. Restart oh-my-pi to apply (close + reopen this tool window).";
            RefreshList();
            ToolsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RemoveClick(object sender, RoutedEventArgs e)
        {
            if (toolList.SelectedItem is CustomMcpTool t)
            {
                store.Remove(t.Id);
                ClearEdit();
                RefreshList();
                ToolsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
