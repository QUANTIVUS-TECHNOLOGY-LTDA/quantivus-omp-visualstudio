using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VSAgent.Ui;

namespace VSAgent.Views
{
    internal enum TerminalCommandRisk
    {
        ReadOnly,
        ChangesState,
        Destructive
    }

    internal sealed class TerminalView : UserControl
    {
        private readonly ComboBox shellBox;
        private readonly TextBox workingDirectoryBox;
        private readonly TextBox commandBox;
        private readonly TextBox outputBox;
        private readonly TextBlock statusText;
        private readonly List<string> history = new List<string>();
        private int historyIndex;
        private CancellationTokenSource executionCancellation;
        private Process runningProcess;

        public TerminalView()
        {
            WorkbenchUi.ApplyToolWindowTheme(this);
            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(WorkbenchUi.Button("Run", async delegate { await RunAsync(); }, true));
            actions.Children.Add(WorkbenchUi.Button("Cancel", delegate { Cancel(); }));
            actions.Children.Add(WorkbenchUi.Button("Clear", delegate { outputBox.Clear(); }));
            root.Children.Add(WorkbenchUi.PageHeader("Terminal",
                "Commands run only when explicitly submitted. State-changing and destructive patterns require confirmation.", actions));

            var options = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            shellBox = WorkbenchUi.ComboBox();
            shellBox.Items.Add("PowerShell");
            shellBox.Items.Add("Command Prompt");
            shellBox.Items.Add("WSL Bash");
            shellBox.SelectedIndex = 0;
            options.Children.Add(shellBox);
            workingDirectoryBox = WorkbenchUi.TextBox();
            workingDirectoryBox.ToolTip = "Working directory for the next command.";
            Grid.SetColumn(workingDirectoryBox, 2);
            options.Children.Add(workingDirectoryBox);
            Grid.SetRow(options, 1);
            root.Children.Add(options);

            commandBox = WorkbenchUi.TextBox(null, true);
            commandBox.MinHeight = 64;
            commandBox.FontFamily = new FontFamily("Consolas");
            commandBox.AcceptsTab = true;
            commandBox.ToolTip = "Ctrl+Enter runs the command. Up/Down navigate command history when the input is one line.";
            commandBox.PreviewKeyDown += CommandBox_PreviewKeyDown;
            Grid.SetRow(commandBox, 2);
            root.Children.Add(commandBox);

            outputBox = WorkbenchUi.TextBox(null, true);
            outputBox.IsReadOnly = true;
            outputBox.FontFamily = new FontFamily("Consolas");
            outputBox.FontSize = 12;
            outputBox.AcceptsTab = true;
            outputBox.Text = "Terminal output will appear here.\r\n";
            Grid.SetRow(outputBox, 3);
            root.Children.Add(outputBox);

            statusText = WorkbenchUi.Subtitle("Idle");
            statusText.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(statusText, 4);
            root.Children.Add(statusText);

            Content = root;
            Loaded += delegate { if (string.IsNullOrWhiteSpace(workingDirectoryBox.Text)) workingDirectoryBox.Text = GetDefaultDirectory(); };
            Unloaded += delegate { Cancel(); };
        }

        public bool IsRunning => runningProcess != null && !runningProcess.HasExited;

        private async Task RunAsync()
        {
            var command = commandBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(command) || IsRunning) return;
            var directory = workingDirectoryBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                statusText.Text = "Choose an existing working directory.";
                return;
            }

            var risk = ClassifyRisk(command);
            if (risk != TerminalCommandRisk.ReadOnly)
            {
                var description = risk == TerminalCommandRisk.Destructive
                    ? "This command contains a destructive pattern and may delete or irreversibly overwrite data."
                    : "This command may change files, packages, processes or repository state.";
                if (MessageBox.Show(description + "\r\n\r\nRun it in:\r\n" + directory + "?", "Quantivus OMP terminal",
                    MessageBoxButton.YesNo, risk == TerminalCommandRisk.Destructive ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
            }

            executionCancellation?.Cancel();
            executionCancellation?.Dispose();
            executionCancellation = new CancellationTokenSource();
            history.Remove(command);
            history.Add(command);
            historyIndex = history.Count;
            AppendOutput("\r\n> " + command + "\r\n");
            statusText.Text = "Running…";

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await ExecuteAsync(shellBox.SelectedItem as string, command, directory, executionCancellation.Token);
                stopwatch.Stop();
                statusText.Text = "Exit " + result + " • " + stopwatch.Elapsed.TotalSeconds.ToString("0.0") + "s";
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                statusText.Text = "Cancelled • " + stopwatch.Elapsed.TotalSeconds.ToString("0.0") + "s";
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                AppendOutput("ERROR: " + ex.Message + "\r\n");
                statusText.Text = "Failed • " + stopwatch.Elapsed.TotalSeconds.ToString("0.0") + "s";
            }
            finally
            {
                runningProcess?.Dispose();
                runningProcess = null;
            }
        }

        private Task<int> ExecuteAsync(string shell, string command, string directory, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<int>();
            var startInfo = CreateStartInfo(shell, command, directory);
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            runningProcess = process;
            CancellationTokenRegistration registration = default(CancellationTokenRegistration);

            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null) Dispatcher.BeginInvoke(new Action(() => AppendOutput(e.Data + "\r\n")));
            };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null) Dispatcher.BeginInvoke(new Action(() => AppendOutput(e.Data + "\r\n")));
            };
            process.Exited += delegate
            {
                try
                {
                    process.WaitForExit();
                    completion.TrySetResult(process.ExitCode);
                }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { registration.Dispose(); }
            };

            try
            {
                if (!process.Start()) throw new InvalidOperationException("The shell could not be started.");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                registration = cancellationToken.Register(delegate
                {
                    try { if (!process.HasExited) process.Kill(); } catch { }
                    completion.TrySetCanceled();
                });
            }
            catch (Exception ex)
            {
                registration.Dispose();
                process.Dispose();
                runningProcess = null;
                completion.TrySetException(ex);
            }

            return completion.Task;
        }

        private static ProcessStartInfo CreateStartInfo(string shell, string command, string directory)
        {
            string executable;
            string arguments;
            switch (shell)
            {
                case "Command Prompt":
                    executable = "cmd.exe";
                    arguments = "/d /s /c \"" + command.Replace("\"", "\\\"") + "\"";
                    break;
                case "WSL Bash":
                    executable = "wsl.exe";
                    arguments = "--exec bash -lc \"" + command.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                    break;
                default:
                    executable = "powershell.exe";
                    var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                    arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded;
                    break;
            }

            return new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
        }

        private void Cancel()
        {
            executionCancellation?.Cancel();
            try { if (runningProcess != null && !runningProcess.HasExited) runningProcess.Kill(); } catch { }
        }

        private void AppendOutput(string value)
        {
            const int maximum = 2_000_000;
            if (outputBox.Text.Length + value.Length > maximum)
                outputBox.Text = outputBox.Text.Substring(Math.Max(0, outputBox.Text.Length - maximum / 2));
            outputBox.AppendText(value);
            outputBox.ScrollToEnd();
        }

        private void CommandBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                _ = RunAsync();
                return;
            }
            if (commandBox.Text.Contains("\n") || history.Count == 0) return;
            if (e.Key == Key.Up)
            {
                historyIndex = Math.Max(0, historyIndex - 1);
                commandBox.Text = history[historyIndex];
                commandBox.CaretIndex = commandBox.Text.Length;
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                historyIndex = Math.Min(history.Count, historyIndex + 1);
                commandBox.Text = historyIndex >= history.Count ? string.Empty : history[historyIndex];
                commandBox.CaretIndex = commandBox.Text.Length;
                e.Handled = true;
            }
        }

        private string GetDefaultDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
                var solutionPath = dte?.Solution?.FullName;
                if (!string.IsNullOrWhiteSpace(solutionPath)) return Path.GetDirectoryName(solutionPath);
            }
            catch { }
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public static TerminalCommandRisk ClassifyRisk(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return TerminalCommandRisk.ReadOnly;
            var value = " " + command.ToLowerInvariant() + " ";
            var destructive = new[]
            {
                " rm -rf ", " remove-item ", " del /", " format ", " diskpart ", " git reset --hard ", " git clean ",
                " --force ", " drop database ", " truncate table ", " shutdown ", " stop-computer ", " rd /s ", " rmdir /s "
            };
            if (destructive.Any(value.Contains)) return TerminalCommandRisk.Destructive;
            var writes = new[]
            {
                " git add ", " git commit ", " git push ", " git pull ", " git switch ", " git checkout ", " dotnet add ",
                " dotnet tool install ", " npm install ", " npm update ", " pnpm install ", " yarn add ", " nuget install ",
                " set-content ", " add-content ", " copy-item ", " move-item ", " mkdir ", " new-item ", " taskkill ", " stop-process "
            };
            return writes.Any(value.Contains) ? TerminalCommandRisk.ChangesState : TerminalCommandRisk.ReadOnly;
        }
    }
}
