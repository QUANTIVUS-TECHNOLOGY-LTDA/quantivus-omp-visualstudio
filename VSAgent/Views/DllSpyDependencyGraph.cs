using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VSAgent.Models;
using Microsoft.VisualStudio.Shell;

namespace VSAgent.Views
{
    /// <summary>
    /// Dependency graph renderer. Owns a <see cref="Canvas"/> that draws the
    /// graph nodes (assemblies) and edges (references) with a simple
    /// force-directed layout, plus a transform group for pan and zoom.
    ///
    /// The layout is intentionally lightweight — a few hundred iterations of
    /// repulsive + attractive forces are enough to keep most graphs readable.
    /// For very large graphs the layout pass runs on the UI thread because
    /// it must touch WPF visuals, but the cost is bounded by the node count.
    /// </summary>
    public sealed class DllSpyDependencyGraph : ContentControl
    {
        private readonly Canvas canvas;
        private readonly Grid root;
        private readonly TextBlock header;
        private readonly StackPanel toolbar;
        private readonly Border viewport;
        private readonly TransformGroup transformGroup;
        private readonly TranslateTransform panTransform = new TranslateTransform();
        private readonly ScaleTransform zoomTransform = new ScaleTransform(1, 1);
        private Point panAnchor;
        private bool panning;
        private const double MinZoom = 0.2;
        private const double MaxZoom = 4.0;

        public DllSpyDependencyGraph()
        {
            transformGroup = new TransformGroup();
            transformGroup.Children.Add(panTransform);
            transformGroup.Children.Add(zoomTransform);

            root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Toolbar
            toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6)
            };
            header = new TextBlock
            {
                Text = VSAgent.Services.I18n.LocalizationService.Current["dllspy.graph"],
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);
            toolbar.Children.Add(header);

            var spacer = new Border { Width = 8 };
            toolbar.Children.Add(spacer);
            toolbar.Children.Add(MakeToolbarButton(VSAgent.Services.I18n.LocalizationService.Current["dllspy.zoom.in"], "+", () => Zoom(1.25)));
            toolbar.Children.Add(MakeToolbarButton(VSAgent.Services.I18n.LocalizationService.Current["dllspy.zoom.out"], "-", () => Zoom(0.8)));
            toolbar.Children.Add(MakeToolbarButton(VSAgent.Services.I18n.LocalizationService.Current["dllspy.zoom.fit"], "Fit", FitToContent));
            toolbar.Children.Add(MakeToolbarButton(VSAgent.Services.I18n.LocalizationService.Current["dllspy.layout"], "Layout", ReLayout));

            Grid.SetRow(toolbar, 0);
            root.Children.Add(toolbar);

            viewport = new Border
            {
                BorderThickness = new Thickness(1),
                ClipToBounds = true
            };
            viewport.SetResourceReference(Border.BorderBrushProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowBorderKey);
            viewport.SetResourceReference(Border.BackgroundProperty, Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowBackgroundKey);

            canvas = new Canvas
            {
                Background = Brushes.Transparent,
                Width = 2000,
                Height = 1500
            };
            canvas.RenderTransform = transformGroup;
            canvas.RenderTransformOrigin = new Point(0, 0);

            // Mouse interactions
            canvas.MouseWheel += OnCanvasMouseWheel;
            canvas.MouseLeftButtonDown += OnCanvasMouseDown;
            canvas.MouseMove += OnCanvasMouseMove;
            canvas.MouseLeftButtonUp += OnCanvasMouseUp;

            viewport.Child = canvas;
            Grid.SetRow(viewport, 1);
            root.Children.Add(viewport);

            Content = root;
        }

        private static Button MakeToolbarButton(string tooltip, string label, Action onClick)
        {
            var btn = new Button
            {
                Content = label,
                ToolTip = tooltip,
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(2, 0, 2, 0),
                MinWidth = 28
            };
            btn.Click += (_, __) => onClick();
            return btn;
        }


        // ===== Force-directed layout ==============================================

        private static void LayoutGraph(DependencyGraph graph, double width, double height)
        {
            var nodes = graph.Nodes;
            if (nodes.Count == 0) return;
            // Initial positions on a circle so disconnected components stay apart.
            var random = new Random(0xC0FFEE);
            var cx = width / 2;
            var cy = height / 2;
            double radius = Math.Min(width, height) * 0.35;
            for (int i = 0; i < nodes.Count; i++)
            {
                var angle = Math.PI * 2 * i / Math.Max(1, nodes.Count);
                nodes[i].X = cx + radius * Math.Cos(angle) + (random.NextDouble() - 0.5) * 30;
                nodes[i].Y = cy + radius * Math.Sin(angle) + (random.NextDouble() - 0.5) * 30;
            }

            const int iterations = 240;
            const double repulsion = 9000.0;
            const double springLength = 140.0;
            const double springStiffness = 0.05;
            const double damping = 0.85;

            var velocities = new Dictionary<string, (double X, double Y)>();
            foreach (var n in nodes) velocities[n.Id] = (0, 0);

            for (int iter = 0; iter < iterations; iter++)
            {
                var force = new Dictionary<string, (double X, double Y)>();
                foreach (var n in nodes) force[n.Id] = (0, 0);

                // Repulsive forces (O(N^2); fine for our sizes).
                for (int i = 0; i < nodes.Count; i++)
                {
                    var a = nodes[i];
                    for (int j = i + 1; j < nodes.Count; j++)
                    {
                        var b = nodes[j];
                        var dx = a.X - b.X;
                        var dy = a.Y - b.Y;
                        var distSq = Math.Max(64, dx * dx + dy * dy);
                        var forceMag = repulsion / distSq;
                        var dist = Math.Sqrt(distSq);
                        var fx = forceMag * dx / dist;
                        var fy = forceMag * dy / dist;
                        force[a.Id] = (force[a.Id].X + fx, force[a.Id].Y + fy);
                        force[b.Id] = (force[b.Id].X - fx, force[b.Id].Y - fy);
                    }
                }

                // Spring forces on edges.
                foreach (var edge in graph.Edges)
                {
                    var source = nodes.FirstOrDefault(n => n.Id == edge.From);
                    var target = nodes.FirstOrDefault(n => n.Id == edge.To);
                    if (source == null || target == null) continue;
                    var dx = target.X - source.X;
                    var dy = target.Y - source.Y;
                    var dist = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                    var displacement = dist - springLength;
                    var fx = springStiffness * displacement * dx / dist;
                    var fy = springStiffness * displacement * dy / dist;
                    force[source.Id] = (force[source.Id].X + fx, force[source.Id].Y + fy);
                    force[target.Id] = (force[target.Id].X - fx, force[target.Id].Y - fy);
                }

                // Integrate velocity + apply damping. Pin root node.
                foreach (var n in nodes)
                {
                    if (n.IsRoot) continue;
                    var (fx, fy) = force[n.Id];
                    var (vx, vy) = velocities[n.Id];
                    vx = (vx + fx * 0.02) * damping;
                    vy = (vy + fy * 0.02) * damping;
                    n.X += vx;
                    n.Y += vy;
                    velocities[n.Id] = (vx, vy);
                }
            }

            // Clamp into the canvas.
            var pad = 40;
            foreach (var n in nodes)
            {
                n.X = Math.Max(pad, Math.Min(width - pad, n.X));
                n.Y = Math.Max(pad, Math.Min(height - pad, n.Y));
            }
        }

        // ===== Drawing ============================================================

        private void DrawEdges(DependencyGraph graph)
        {
            var nodeLookup = graph.Nodes.ToDictionary(n => n.Id, n => n);
            var lineBrush = TryResolveBrush(VsBrushes.ToolWindowBorderKey, Colors.Gray);
            foreach (var edge in graph.Edges)
            {
                if (!nodeLookup.TryGetValue(edge.From, out var source)) continue;
                if (!nodeLookup.TryGetValue(edge.To, out var target)) continue;
                var line = new Line
                {
                    X1 = source.X,
                    Y1 = source.Y,
                    X2 = target.X,
                    Y2 = target.Y,
                    Stroke = lineBrush,
                    StrokeThickness = 1.2,
                    IsHitTestVisible = false
                };
                // Draw arrow head via simple polygon
                canvas.Children.Add(line);
                DrawArrowHead(source, target, lineBrush);
            }
        }

        private void DrawArrowHead(GraphNode from, GraphNode to, Brush brush)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;
            var ux = dx / len;
            var uy = dy / len;
            var tipX = to.X - ux * 22;
            var tipY = to.Y - uy * 22;
            var leftX = tipX - ux * 8 + uy * 5;
            var leftY = tipY - uy * 8 - ux * 5;
            var rightX = tipX - ux * 8 - uy * 5;
            var rightY = tipY - uy * 8 + ux * 5;
            var head = new Polygon
            {
                Points = new PointCollection { new Point(tipX, tipY), new Point(leftX, leftY), new Point(rightX, rightY) },
                Fill = brush,
                Stroke = brush,
                StrokeThickness = 0.5,
                IsHitTestVisible = false
            };
            canvas.Children.Add(head);
        }

        private void DrawNodes(DependencyGraph graph)
        {
            var rootBrush = TryResolveBrush(VsBrushes.AccentMediumKey, Colors.SteelBlue);
            var depBrush = TryResolveBrush(VsBrushes.ToolWindowBackgroundKey, Colors.LightSteelBlue);
            var borderBrush = TryResolveBrush(VsBrushes.ToolWindowBorderKey, Colors.DimGray);
            var textBrush = TryResolveBrush(VsBrushes.ToolWindowTextKey, Colors.Black);

            foreach (var node in graph.Nodes)
            {
                var width = Math.Max(120, node.Label.Length * 7.0 + 24);
                var height = 36;
                var rect = new Border
                {
                    Width = width,
                    Height = height,
                    Background = node.IsRoot ? rootBrush : depBrush,
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 4, 8, 4),
                    Child = new TextBlock
                    {
                        Text = node.Label,
                        Foreground = textBrush,
                        FontSize = 11,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                };
                ToolTipService.SetToolTip(rect, node.Label);
                Canvas.SetLeft(rect, node.X - width / 2);
                Canvas.SetTop(rect, node.Y - height / 2);
                canvas.Children.Add(rect);
            }
        }
        private Brush TryResolveBrush(object key, Color fallback)
        {
            try
            {
                if (TryFindResource(key as string ?? key.ToString()) is Brush resource) return resource;
            }
            catch { }
            return new SolidColorBrush(fallback);
        }

        // ===== Pan & zoom =========================================================

        private void Zoom(double factor)
        {
            var newScale = Math.Max(MinZoom, Math.Min(MaxZoom, zoomTransform.ScaleX * factor));
            zoomTransform.ScaleX = newScale;
            zoomTransform.ScaleY = newScale;
        }

        private void FitToContent()
        {
            if (canvas.Children.Count == 0) return;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var child in canvas.Children)
            {
                if (child is FrameworkElement fe)
                {
                    var left = Canvas.GetLeft(fe);
                    var top = Canvas.GetTop(fe);
                    var right = left + fe.Width;
                    var bottom = top + fe.Height;
                    if (left < minX) minX = left;
                    if (top < minY) minY = top;
                    if (right > maxX) maxX = right;
                    if (bottom > maxY) maxY = bottom;
                }
            }
            if (minX >= maxX || minY >= maxY) return;
            var contentWidth = maxX - minX;
            var contentHeight = maxY - minY;
            var viewportWidth = viewport.ActualWidth > 0 ? viewport.ActualWidth : 600;
            var viewportHeight = viewport.ActualHeight > 0 ? viewport.ActualHeight : 400;
            var scale = Math.Min(viewportWidth / contentWidth, viewportHeight / contentHeight) * 0.85;
            scale = Math.Max(MinZoom, Math.Min(MaxZoom, scale));
            zoomTransform.ScaleX = scale;
            zoomTransform.ScaleY = scale;
            // Center
            panTransform.X = (viewportWidth - contentWidth * scale) / 2 - minX * scale;
            panTransform.Y = (viewportHeight - contentHeight * scale) / 2 - minY * scale;
        }

        private void ReLayout()
        {
            if (currentAnalysis == null) return;
            Render(currentAnalysis);
        }

        private AssemblyAnalysis? currentAnalysis;
        public new void Render(AssemblyAnalysis analysis)
        {
            currentAnalysis = analysis;
            canvas.Children.Clear();
            if (analysis?.Graph == null || analysis.Graph.Nodes.Count == 0) return;
            LayoutGraph(analysis.Graph, canvas.Width, canvas.Height);
            DrawEdges(analysis.Graph);
            DrawNodes(analysis.Graph);
            FitToContent();
        }

        private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            Zoom(factor);
        }

        private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
        {
            panning = true;
            panAnchor = e.GetPosition(viewport);
            canvas.CaptureMouse();
            Cursor = Cursors.SizeAll;
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (!panning) return;
            var current = e.GetPosition(viewport);
            panTransform.X += current.X - panAnchor.X;
            panTransform.Y += current.Y - panAnchor.Y;
            panAnchor = current;
        }

        private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
        {
            panning = false;
            canvas.ReleaseMouseCapture();
            Cursor = null;
        }
    }
}
