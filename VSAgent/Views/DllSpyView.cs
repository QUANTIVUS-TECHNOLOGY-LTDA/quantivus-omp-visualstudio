using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text;
using System.Windows.Media;
using Microsoft.Win32;
using VSAgent.Models;
using VSAgent.Services;
using VSAgent.Services.Analysis;
using VSAgent.Services.I18n;
using VSAgent.Ui;

namespace VSAgent.Views
{
    /// <summary>
    /// DLLSpy workbench tab. Lets the user open a .dll/.exe and inspect its
    /// types, members, attributes, native exports and dependency graph.
    /// </summary>
    public sealed class DllSpyView : UserControl
    {
        private readonly AssemblyAnalysisService analyzer = new AssemblyAnalysisService();
        private readonly DllSpyDependencyGraph graph = new DllSpyDependencyGraph();
        private TextBox filePathBox;
        private Button browseButton;
        private Button analyzeButton;
        private ComboBox recentBox;
        private TextBox searchBox;
        private TreeView typeTree;
        private TextBox detailBox;
        private TextBlock summaryBox;
        private readonly TextBlock emptyState;
        private readonly Grid contentHost;
        private AssemblyAnalysis? current;
        private string? lastOpenedFolder;

        public DllSpyView()
        {
            var l = LocalizationService.Current;
            SetResourceReference(BackgroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowBackgroundKey);
            Padding = new Thickness(10);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Content = root;

            // Toolbar
            var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(toolbar, 0);
            root.Children.Add(toolbar);

            filePathBox = new TextBox { Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(4, 3, 4, 3) };
            filePathBox.SetResourceReference(ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);
            filePathBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Analyze(); };
            Grid.SetColumn(filePathBox, 0);
            toolbar.Children.Add(filePathBox);

            browseButton = new Button
            {
                Content = l["dllspy.open"],
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 6, 0),
                MinWidth = 120
            };
            browseButton.Click += (_, __) => BrowseFile();
            Grid.SetColumn(browseButton, 1);
            toolbar.Children.Add(browseButton);

            recentBox = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 6, 0) };
            recentBox.SetResourceReference(ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);
            recentBox.SelectionChanged += (_, __) =>
            {
                if (recentBox.SelectedItem is string recent && !string.IsNullOrEmpty(recent))
                {
                    filePathBox.Text = recent;
                    Analyze();
                }
            };
            Grid.SetColumn(recentBox, 2);
            toolbar.Children.Add(recentBox);

            analyzeButton = new Button
            {
                Content = l["dllspy.analyze"],
                Padding = new Thickness(10, 3, 10, 3),
                MinWidth = 90
            };
            analyzeButton.Click += (_, __) => Analyze();
            Grid.SetColumn(analyzeButton, 3);
            toolbar.Children.Add(analyzeButton);

            searchBox = new TextBox
            {
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(4, 3, 4, 3),
                MinWidth = 160,
                ToolTip = l["dllspy.search"]
            };
            searchBox.SetResourceReference(ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);
            searchBox.TextChanged += (_, __) => ApplyFilter();
            Grid.SetColumn(searchBox, 4);
            toolbar.Children.Add(searchBox);

            // Three column content
            contentHost = new Grid();
            contentHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            contentHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            contentHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            contentHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(contentHost, 1);
            root.Children.Add(contentHost);

            // Type tree
            var treePanel = BuildTypeTreePanel();
            Grid.SetColumn(treePanel, 0);
            contentHost.Children.Add(treePanel);

            // Splitter
            var split1 = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext, ResizeDirection = GridResizeDirection.Columns };
            Grid.SetColumn(split1, 1);
            contentHost.Children.Add(split1);

            // Center: details
            var detailPanel = BuildDetailPanel();
            Grid.SetColumn(detailPanel, 2);
            contentHost.Children.Add(detailPanel);

            // Splitter
            var split2 = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext, ResizeDirection = GridResizeDirection.Columns };
            Grid.SetColumn(split2, 3);
            contentHost.Children.Add(split2);

            // Right: graph
            graph.Margin = new Thickness(0);
            Grid.SetColumn(graph, 4);
            contentHost.Children.Add(graph);

            // Empty state overlay (shown until first analyze)
            emptyState = new TextBlock
            {
                Text = l["dllspy.empty"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(40)
            };
            emptyState.SetResourceReference(ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);
            emptyState.FontSize = 13;
            emptyState.Opacity = 0.7;
            Grid.SetColumnSpan(emptyState, 5);
            Grid.SetRowSpan(emptyState, 2);
            root.Children.Add(emptyState);
            Panel.SetZIndex(emptyState, 10);

            LocalizationService.Current.LanguageChanged += (_, __) => RefreshLocalization();
            RefreshLocalization();
        }

        private UIElement BuildTypeTreePanel()
        {
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4)
            };
            border.SetResourceReference(Border.BorderBrushProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowBorderKey);
            border.SetResourceReference(Border.BackgroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowBackgroundKey);

            var stack = new DockPanel();
            var header = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 2, 2, 4)
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);
            DockPanel.SetDock(header, Dock.Top);
            stack.Children.Add(header);

            summaryBox = new TextBlock
            {
                Margin = new Thickness(2, 0, 2, 6),
                Opacity = 0.7
            };
            summaryBox.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);
            DockPanel.SetDock(summaryBox, Dock.Top);
            stack.Children.Add(summaryBox);

            typeTree = new TreeView
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent
            };
            typeTree.SelectedItemChanged += OnTypeTreeSelectionChanged;
            stack.Children.Add(typeTree);

            border.Child = stack;
            return border;
        }

        private UIElement BuildDetailPanel()
        {
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6)
            };
            border.SetResourceReference(Border.BorderBrushProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowBorderKey);
            border.SetResourceReference(Border.BackgroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowBackgroundKey);

            var stack = new DockPanel();
            var header = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 2, 2, 6)
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);
            DockPanel.SetDock(header, Dock.Top);
            stack.Children.Add(header);

            detailBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent
            };
            detailBox.SetResourceReference(ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);
            stack.Children.Add(detailBox);

            border.Child = stack;
            return border;
        }

        private void RefreshLocalization()
        {
            var l = LocalizationService.Current;
            browseButton.Content = l["dllspy.open"];
            analyzeButton.Content = l["dllspy.analyze"];
            searchBox.ToolTip = l["dllspy.search"];
            emptyState.Text = l["dllspy.empty"];
            if (current != null) summaryBox.Text = BuildSummary(current);
        }

        private string BuildSummary(AssemblyAnalysis analysis)
        {
            var l = LocalizationService.Current;
            return l.Get("dllspy.summary", ("0", analysis.Types.Count.ToString()), ("1", analysis.References.Count.ToString()))
                   + " — " + (analysis.IsManaged ? l["dllspy.managed"] : l["dllspy.unmanaged"]);
        }

        private void BrowseFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = LocalizationService.Current["dllspy.open"],
                Filter = "Assemblies (*.dll;*.exe)|*.dll;*.exe|All files (*.*)|*.*",
                InitialDirectory = lastOpenedFolder
            };
            if (dlg.ShowDialog() == true)
            {
                filePathBox.Text = dlg.FileName;
                lastOpenedFolder = Path.GetDirectoryName(dlg.FileName);
                Analyze();
            }
        }

        private async void Analyze()
        {
            var path = filePathBox.Text?.Trim();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                detailBox.Text = path + ": not found.";
                return;
            }
            analyzeButton.IsEnabled = false;
            detailBox.Text = LocalizationService.Current["dllspy.analyze"] + "...";
            try
            {
                var analysis = await Task.Run(() => analyzer.Analyze(path));
                current = analysis;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    PopulateTree(analysis);
                    graph.Render(analysis);
                    summaryBox.Text = BuildSummary(analysis);
                    emptyState.Visibility = Visibility.Collapsed;
                    AddRecent(path);
                    PersistRecent(path);
                }));
            }
            catch (Exception ex)
            {
                detailBox.Text = "Error: " + ex.Message;
            }
            finally
            {
                analyzeButton.IsEnabled = true;
            }
        }

        private void PopulateTree(AssemblyAnalysis analysis)
        {
            typeTree.Items.Clear();
            if (!analysis.IsManaged)
            {
                var nativeRoot = new TreeViewItem { Header = analysis.Identity.Name + " (native)", IsExpanded = true };
                var exportsRoot = new TreeViewItem
                {
                    Header = LocalizationService.Current["dllspy.exports"] + " (" + analysis.Exports.Count + ")"
                };
                foreach (var export in analysis.Exports.Take(2000))
                {
                    exportsRoot.Items.Add(new TreeViewItem { Header = export.Name + "  " + export.Signature, Tag = export });
                }
                nativeRoot.Items.Add(exportsRoot);
                typeTree.Items.Add(nativeRoot);
                return;
            }

            var grouped = analysis.Types
                .GroupBy(t => string.IsNullOrEmpty(t.Namespace) ? "(global)" : t.Namespace)
                .OrderBy(g => g.Key, StringComparer.Ordinal);
            foreach (var nsGroup in grouped)
            {
                var nsNode = new TreeViewItem { Header = nsGroup.Key + "  (" + nsGroup.Count() + ")" };
                foreach (var type in nsGroup.OrderBy(t => t.Name, StringComparer.Ordinal))
                {
                    var node = new TypeTreeItem(type);
                    nsNode.Items.Add(node);
                }
                typeTree.Items.Add(nsNode);
            }
            if (typeTree.Items.Count > 0 && typeTree.Items[0] is TreeViewItem first)
                first.IsExpanded = true;
        }

        private void ApplyFilter()
        {
            var query = searchBox.Text?.Trim();
            if (string.IsNullOrEmpty(query))
            {
                RestoreTreeExpansion();
                return;
            }
            foreach (var item in typeTree.Items)
            {
                if (item is TreeViewItem ns)
                {
                    ns.Items.Clear();
                    if (current != null)
                    {
                        foreach (var type in current.Types)
                        {
                            if (type.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                type.FullName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                ns.Items.Add(new TypeTreeItem(type));
                            }
                        }
                        ns.Header = ns.Header?.ToString()?.Split('(')[0].Trim() + "  (" + ns.Items.Count + ")";
                    }
                    ns.IsExpanded = true;
                }
            }
        }

        private void RestoreTreeExpansion()
        {
            if (current == null) return;
            PopulateTree(current);
        }

        private void OnTypeTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TypeTreeItem item) detailBox.Text = FormatTypeDetails(item.Type);
            else if (e.NewValue is TreeViewItem tvi && tvi.Tag is MemberInfo member) detailBox.Text = FormatMemberDetails(member);
            else if (e.NewValue is TreeViewItem ns) detailBox.Text = ns.Header?.ToString();
        }

        private static string FormatTypeDetails(TypeInfo type)
        {
            var l = LocalizationService.Current;
            var sb = new StringBuilder();
            sb.AppendLine($"{l["dllspy.kind." + type.Kind]} {type.FullName}");
            sb.AppendLine($"  IsPublic: {type.IsPublic}  Abstract: {type.IsAbstract}  Sealed: {type.IsSealed}");
            if (!string.IsNullOrEmpty(type.BaseType)) sb.AppendLine($"  Base: {type.BaseType}");
            if (type.Interfaces.Count > 0)
                sb.AppendLine("  Implements: " + string.Join(", ", type.Interfaces));
            if (type.Members.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Members ({type.Members.Count}):");
                foreach (var m in type.Members)
                {
                    sb.AppendLine($"  [{l["dllspy.kind." + m.Kind]}] {m.Visibility} {(m.IsStatic ? "static " : "")}{m.Signature}");
                }
            }
            return sb.ToString();
        }

        private static string FormatMemberDetails(MemberInfo member)
        {
            var l = LocalizationService.Current;
            return $"[{l["dllspy.kind." + member.Kind]}] {member.Visibility} {(member.IsStatic ? "static " : "")}{member.Signature}";
        }

        private void AddRecent(string path)
        {
            var existing = recentBox.Items.Cast<string>().ToList();
            recentBox.Items.Clear();
            recentBox.Items.Add(path);
            foreach (var item in existing)
                if (!string.Equals(item, path, StringComparison.OrdinalIgnoreCase))
                    recentBox.Items.Add(item);
            recentBox.Items.Insert(0, LocalizationService.Current["dllspy.recent"]);
            recentBox.SelectedIndex = 0;
        }

        private void PersistRecent(string path)
        {
            try
            {
                var store = new WorkbenchStore();
                store.UpdatePreferences(prefs =>
                {
                    prefs.RecentAssemblies ??= new List<string>();
                    prefs.RecentAssemblies.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                    prefs.RecentAssemblies.Insert(0, path);
                    while (prefs.RecentAssemblies.Count > 12) prefs.RecentAssemblies.RemoveAt(prefs.RecentAssemblies.Count - 1);
                });
            }
            catch { /* best-effort */ }
        }

        public void LoadInitialFromPreferences()
        {
            try
            {
                var prefs = new WorkbenchStore().Preferences;
                if (prefs.RecentAssemblies == null || prefs.RecentAssemblies.Count == 0) return;
                recentBox.Items.Clear();
                recentBox.Items.Add(LocalizationService.Current["dllspy.recent"]);
                foreach (var item in prefs.RecentAssemblies) recentBox.Items.Add(item);
                recentBox.SelectedIndex = 0;
            }
            catch { }
        }

        public AssemblyAnalysis? CurrentAnalysis => current;
    }

    /// <summary>
    /// Tree view item that wraps a TypeInfo and exposes the type through its
    /// <see cref="DataContext"/> so the detail panel can render it without
    /// scanning the header text.
    /// </summary>
    internal sealed class TypeTreeItem : TreeViewItem
    {
        public TypeInfo Type { get; }
        public TypeTreeItem(TypeInfo type) : base()
        {
            Type = type;
            Header = $"{type.Name}   [{LocalizationService.Current["dllspy.kind." + type.Kind]}]";
        }
    }
}
