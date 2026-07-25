using EnvDTE;
using EnvDTE80;
using Microsoft.Win32;
using Microsoft.VisualStudio.Shell;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VSAgent.Services;
using VSAgent.Services.Omp;
using VSAgent.Ui;

namespace VSAgent.Views
{
    internal sealed class DiagnosticsView : UserControl
    {
        private readonly AgentHostService host;
        private readonly WorkbenchStore workbench;
        private readonly TextBox reportBox;
        private readonly TextBlock statusText;

        public DiagnosticsView(AgentHostService host, WorkbenchStore workbench)
        {
            this.host = host;
            this.workbench = workbench ?? throw new ArgumentNullException(nameof(workbench));
            WorkbenchUi.ApplyToolWindowTheme(this);

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(WorkbenchUi.Button("Refresh", delegate { Refresh(); }, true));
            actions.Children.Add(WorkbenchUi.Button("Restart OMP", delegate { RestartRequested?.Invoke(this, EventArgs.Empty); }));
            actions.Children.Add(WorkbenchUi.Button("Stop OMP", delegate { StopRequested?.Invoke(this, EventArgs.Empty); }));
            actions.Children.Add(WorkbenchUi.Button("Test Git", async delegate { await TestGitAsync(); }));
            actions.Children.Add(WorkbenchUi.Button("Export report", delegate { Export(); }));
            root.Children.Add(WorkbenchUi.PageHeader("Diagnostics",
                "Runtime, paths, provider state and installation checks. Exported reports redact the current user profile path.", actions));

            reportBox = WorkbenchUi.TextBox(null, true);
            reportBox.IsReadOnly = true;
            reportBox.FontFamily = new System.Windows.Media.FontFamily("Consolas");
            reportBox.FontSize = 12;
            Grid.SetRow(reportBox, 1);
            root.Children.Add(reportBox);

            statusText = WorkbenchUi.Subtitle("Ready.");
            statusText.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(statusText, 2);
            root.Children.Add(statusText);

            Content = root;
            Loaded += delegate { Refresh(); };
        }

        public event EventHandler RestartRequested;
        public event EventHandler StopRequested;

        public void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var builder = new StringBuilder();
            var assembly = Assembly.GetExecutingAssembly();
            var extensionDirectory = Path.GetDirectoryName(assembly.Location) ?? string.Empty;
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
            var solutionPath = dte?.Solution?.FullName;
            var ompPath = OmpExecutableLocator.Find(extensionDirectory);
            var mcpPath = FindMcpHost(extensionDirectory);

            Append(builder, "Quantivus OMP version", assembly.GetName().Version?.ToString());
            Append(builder, "Visual Studio", dte?.Version);
            Append(builder, ".NET runtime", Environment.Version.ToString());
            Append(builder, "Operating system", Environment.OSVersion.ToString());
            Append(builder, "Process", Process.GetCurrentProcess().ProcessName + " (PID " + Process.GetCurrentProcess().Id + ")");
            builder.AppendLine();
            Append(builder, "OMP state", host?.IsReady == true ? "connected" : "disconnected");
            Append(builder, "OMP executable", ompPath ?? "not found");
            Append(builder, "MCP host", mcpPath ?? "not found");
            Append(builder, "Named pipe", host?.PipeName ?? "not initialized");
            Append(builder, "Input characters", (host?.TotalInputChars ?? 0).ToString("N0"));
            Append(builder, "Provider", VSAgentPackage.Env?.ActiveProvider ?? "default");
            Append(builder, "Model", VSAgentPackage.Env?.ActiveModel ?? "default");
            builder.AppendLine();
            Append(builder, "Solution", solutionPath ?? "none");
            Append(builder, "Repository root", GitCommandService.FindRepositoryRoot(string.IsNullOrWhiteSpace(solutionPath) ? null : Path.GetDirectoryName(solutionPath)) ?? "none");
            builder.AppendLine();
            Append(builder, "Workbench state", workbench.FilePath);
            Append(builder, "Skills", VSAgentPackage.Skills?.FilePath);
            Append(builder, "Credentials", VSAgentPackage.Credentials?.FilePath);
            Append(builder, "Custom tools", VSAgentPackage.CustomTools?.FilePath);
            Append(builder, "Web search", VSAgentPackage.WebSearch?.FilePath);
            builder.AppendLine();
            builder.AppendLine("Checks");
            builder.AppendLine("------");
            builder.AppendLine("OMP executable: " + (File.Exists(ompPath) ? "PASS" : "FAIL"));
            builder.AppendLine("MCP host:       " + (File.Exists(mcpPath) ? "PASS" : "FAIL"));
            builder.AppendLine("Solution:       " + (!string.IsNullOrWhiteSpace(solutionPath) ? "PASS" : "INFO - no solution loaded"));
            builder.AppendLine("Git:            run 'Test Git' for an executable check");

            reportBox.Text = builder.ToString();
            statusText.Text = host?.IsReady == true ? "OMP is connected." : "OMP is not connected. Review the executable and MCP host paths.";
        }

        private async Task TestGitAsync()
        {
            statusText.Text = "Testing Git…";
            try
            {
                var service = new GitCommandService();
                var directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var result = await service.RunAsync(directory, CancellationToken.None, "--version");
                statusText.Text = result.Succeeded ? result.StandardOutput.Trim() : result.CombinedOutput.Trim();
                Refresh();
                reportBox.AppendText(Environment.NewLine + "Git executable: " + (result.Succeeded ? "PASS - " + result.StandardOutput.Trim() : "FAIL - " + result.CombinedOutput.Trim()));
            }
            catch (Exception ex)
            {
                statusText.Text = "Git test failed: " + ex.Message;
            }
        }

        private void Export()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Quantivus OMP diagnostics",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = "quantivus-omp-diagnostics.txt",
                AddExtension = true,
                DefaultExt = ".txt"
            };
            if (dialog.ShowDialog() != true) return;
            var value = Redact(reportBox.Text);
            File.WriteAllText(dialog.FileName, value, new UTF8Encoding(false));
            statusText.Text = "Sanitized report exported to " + dialog.FileName;
        }

        private static void Append(StringBuilder builder, string name, string value) =>
            builder.Append(name.PadRight(22)).Append(": ").AppendLine(value ?? "n/a");

        private static string FindMcpHost(string directory)
        {
            foreach (var path in new[]
            {
                Path.Combine(directory ?? string.Empty, "Runtime", "McpHost", "VSAgent.McpHost.exe"),
                Path.Combine(directory ?? string.Empty, "VSAgent.McpHost.exe"),
                Path.Combine(directory ?? string.Empty, "..", "VSAgent.McpHost", "VSAgent.McpHost.exe")
            })
            {
                try { if (File.Exists(Path.GetFullPath(path))) return Path.GetFullPath(path); } catch { }
            }
            return null;
        }

        private static string Redact(string value)
        {
            var output = value ?? string.Empty;
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(profile)) output = output.Replace(profile, "%USERPROFILE%");
            if (!string.IsNullOrWhiteSpace(local)) output = output.Replace(local, "%LOCALAPPDATA%");
            return output;
        }
    }
}
