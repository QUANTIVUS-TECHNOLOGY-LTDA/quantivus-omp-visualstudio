using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VSAgent.Models;

namespace VSAgent.Services.VisualStudio
{
    /// <summary>
    /// Executes MCP requests against the active Visual Studio instance.
    /// All DTE/EnvDTE calls are serialized onto the Visual Studio UI thread.
    ///
    /// The dispatcher intentionally exposes both focused tools and
    /// vs_execute_command. The focused tools return structured data while the
    /// command bridge makes the complete Visual Studio command surface
    /// available to OMP, subject to the ACP permission flow.
    /// </summary>
    internal sealed class VisualStudioToolDispatcher
    {
        private const int DefaultMaximumTextCharacters = 200000;
        private const int MaximumCommandResults = 500;

        private readonly AsyncPackage package;
        private readonly DTE2 dte;

        public VisualStudioToolDispatcher(AsyncPackage package, DTE2 dte)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            this.dte = dte ?? throw new ArgumentNullException(nameof(dte));
        }

        public async Task<VisualStudioToolResponse> ExecuteAsync(
            VisualStudioToolRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                switch (request.Tool)
                {
                    // IDE and command surface
                    case "vs_get_status": return Ok(request, GetStatus());
                    case "vs_execute_command": return Ok(request, ExecuteCommand(request.Arguments));
                    case "vs_command_list": return Ok(request, ListCommands(request.Arguments));
                    case "vs_window_list": return Ok(request, ListWindows());
                    case "vs_window_activate": return Ok(request, ActivateWindow(request.Arguments));

                    // Solution and project control
                    case "vs_get_solution": return Ok(request, GetSolution());
                    case "vs_solution_open": return Ok(request, OpenSolution(request.Arguments));
                    case "vs_solution_close": return Ok(request, CloseSolution(request.Arguments));
                    case "vs_solution_configuration_list": return Ok(request, ListSolutionConfigurations());
                    case "vs_solution_configuration_activate": return Ok(request, ActivateSolutionConfiguration(request.Arguments));
                    case "vs_project_set_startup": return Ok(request, SetStartupProject(request.Arguments));
                    case "vs_build_solution": return Ok(request, BuildSolution(request.Arguments));
                    case "vs_rebuild_solution": return Ok(request, RebuildSolution(request.Arguments));
                    case "vs_clean_solution": return Ok(request, CleanSolution());
                    case "vs_build_project": return Ok(request, BuildProject(request.Arguments));
                    case "vs_build_cancel": return Ok(request, CancelBuild());
                    case "vs_get_build_errors": return Ok(request, GetBuildErrors(request.Arguments));

                    // Documents and editor
                    case "vs_document_list": return Ok(request, ListDocuments());
                    case "vs_document_get_active": return Ok(request, GetActiveDocument(request.Arguments));
                    case "vs_document_open": return Ok(request, OpenDocument(request.Arguments));
                    case "vs_document_get_text": return Ok(request, GetDocumentText(request.Arguments));
                    case "vs_document_replace_text": return Ok(request, ReplaceDocumentText(request.Arguments));
                    case "vs_document_save": return Ok(request, SaveDocument(request.Arguments));
                    case "vs_document_save_all": return Ok(request, SaveAllDocuments());
                    case "vs_document_close": return Ok(request, CloseDocument(request.Arguments));
                    case "vs_editor_get_selection": return Ok(request, GetEditorSelection());
                    case "vs_editor_replace_selection": return Ok(request, ReplaceEditorSelection(request.Arguments));
                    case "vs_editor_navigate": return Ok(request, NavigateEditor(request.Arguments));

                    // Debugger lifecycle
                    case "vs_debug_start": return Ok(request, StartDebugging(request.Arguments));
                    case "vs_debug_start_without_debugging": return Ok(request, StartWithoutDebugging(request.Arguments));
                    case "vs_debug_stop": return Ok(request, StopDebugging());
                    case "vs_debug_restart": return Ok(request, RestartDebugging());
                    case "vs_debug_pause": return Ok(request, PauseDebugging());
                    case "vs_debug_continue": return Ok(request, ContinueDebugging());
                    case "vs_debug_step_over": return Ok(request, StepOver());
                    case "vs_debug_step_into": return Ok(request, StepInto());
                    case "vs_debug_step_out": return Ok(request, StepOut());
                    case "vs_debug_run_to_cursor": return Ok(request, RunToCursor());
                    case "vs_debug_set_next_statement": return Ok(request, SetNextStatement());
                    case "vs_debug_detach_all": return Ok(request, DetachAll());
                    case "vs_debug_terminate_all": return Ok(request, TerminateAll());
                    case "vs_debug_process_list": return Ok(request, ListDebuggedProcesses());
                    case "vs_debug_thread_list": return Ok(request, ListThreads());
                    case "vs_get_call_stack": return Ok(request, GetCallStack());
                    case "vs_get_locals": return Ok(request, GetLocals());
                    case "vs_evaluate": return Ok(request, Evaluate(request.Arguments));

                    // Breakpoints
                    case "vs_breakpoint_add": return Ok(request, AddBreakpoint(request.Arguments));
                    case "vs_breakpoint_list": return Ok(request, ListBreakpoints());
                    case "vs_breakpoint_remove": return Ok(request, RemoveBreakpoints(request.Arguments));
                    case "vs_breakpoint_set_enabled": return Ok(request, SetBreakpointsEnabled(request.Arguments));

                    default:
                        return VisualStudioToolResponse.Fail(
                            request.Id,
                            "Unknown Visual Studio tool: " + request.Tool);
                }
            }
            catch (OperationCanceledException)
            {
                return VisualStudioToolResponse.Fail(request.Id, "The Visual Studio operation was cancelled.");
            }
            catch (Exception ex)
            {
                return VisualStudioToolResponse.Fail(request.Id, ex.Message);
            }
        }

        private static VisualStudioToolResponse Ok(VisualStudioToolRequest request, object result) =>
            VisualStudioToolResponse.Ok(request.Id, result);

        private DebuggerSnapshot GetStatus()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var mode = dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode
                ? "paused"
                : dte.Debugger.CurrentMode == dbgDebugMode.dbgRunMode
                    ? "running"
                    : "stopped";

            var startupProjects = dte.Solution?.SolutionBuild?.StartupProjects as Array;
            var snapshot = new DebuggerSnapshot
            {
                Mode = mode,
                Solution = dte.Solution?.FullName ?? string.Empty,
                StartupProjects = startupProjects == null
                    ? string.Empty
                    : string.Join(", ", startupProjects.Cast<object>().Select(value => value?.ToString())),
                IsSolutionOpen = dte.Solution?.IsOpen == true
            };

            return snapshot;
        }

        private object ExecuteCommand(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var name = RequiredString(arguments, "name");
            var commandArguments = OptionalString(arguments, "arguments");
            dte.ExecuteCommand(name, commandArguments ?? string.Empty);
            return new { executed = true, name, arguments = commandArguments ?? string.Empty };
        }

        private object ListCommands(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var filter = OptionalString(arguments, "filter") ?? string.Empty;
            var maximum = Math.Max(1, Math.Min(MaximumCommandResults, OptionalInt(arguments, "limit", 100)));
            var commands = new List<object>();

            foreach (Command command in dte.Commands)
            {
                if (commands.Count >= maximum) break;

                var name = SafeCommandName(command);
                var localizedName = SafeLocalizedCommandName(command);
                if (!string.IsNullOrWhiteSpace(filter) &&
                    (name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) < 0 &&
                    (localizedName?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
                {
                    continue;
                }

                commands.Add(new
                {
                    name = name ?? string.Empty,
                    localizedName = localizedName ?? string.Empty,
                    id = command.ID,
                    guid = command.Guid
                });
            }

            return new { filter, count = commands.Count, truncated = commands.Count >= maximum, commands };
        }

        private object ListWindows()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var windows = new List<object>();
            foreach (Window window in dte.Windows)
            {
                windows.Add(new
                {
                    caption = SafeWindowCaption(window),
                    kind = SafeWindowKind(window),
                    visible = SafeWindowVisible(window),
                    document = SafeWindowDocument(window)
                });
            }

            return windows;
        }

        private object ActivateWindow(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var caption = OptionalString(arguments, "caption");
            var kind = OptionalString(arguments, "kind");
            if (string.IsNullOrWhiteSpace(caption) && string.IsNullOrWhiteSpace(kind))
                throw new ArgumentException("caption or kind is required.");

            foreach (Window window in dte.Windows)
            {
                var captionMatches = string.IsNullOrWhiteSpace(caption) ||
                    string.Equals(SafeWindowCaption(window), caption, StringComparison.OrdinalIgnoreCase);
                var kindMatches = string.IsNullOrWhiteSpace(kind) ||
                    string.Equals(SafeWindowKind(window), kind, StringComparison.OrdinalIgnoreCase);

                if (!captionMatches || !kindMatches) continue;

                window.Visible = true;
                window.Activate();
                return new
                {
                    activated = true,
                    caption = SafeWindowCaption(window),
                    kind = SafeWindowKind(window)
                };
            }

            throw new InvalidOperationException("No matching Visual Studio window was found.");
        }

        private object GetSolution()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var projects = new List<object>();

            if (dte.Solution?.IsOpen == true)
            {
                foreach (Project project in EnumerateProjects())
                {
                    projects.Add(ProjectSnapshot(project));
                }
            }

            return new
            {
                isOpen = dte.Solution?.IsOpen == true,
                fullName = dte.Solution?.FullName ?? string.Empty,
                activeConfiguration = SafeActiveConfigurationName(),
                projects
            };
        }

        private object OpenSolution(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var path = RequiredExistingFile(arguments, "path");
            dte.Solution.Open(path);
            return new { opened = dte.Solution?.IsOpen == true, path };
        }

        private object CloseSolution(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var saveFirst = OptionalBool(arguments, "save", true);
            if (dte.Solution?.IsOpen != true) return new { closed = false, reason = "No solution is open." };
            var path = dte.Solution.FullName;
            dte.Solution.Close(saveFirst);
            return new { closed = true, path, saved = saveFirst };
        }

        private object ListSolutionConfigurations()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnsureSolutionOpen();

            var active = dte.Solution.SolutionBuild.ActiveConfiguration;
            var configurations = new List<object>();
            foreach (SolutionConfiguration configuration in dte.Solution.SolutionBuild.SolutionConfigurations)
            {
                configurations.Add(new
                {
                    name = configuration.Name,
                    platform = configuration.PlatformName,
                    isActive = active != null &&
                               string.Equals(active.Name, configuration.Name, StringComparison.OrdinalIgnoreCase) &&
                               string.Equals(active.PlatformName, configuration.PlatformName, StringComparison.OrdinalIgnoreCase)
                });
            }

            return configurations;
        }

        private object ActivateSolutionConfiguration(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnsureSolutionOpen();
            var name = RequiredString(arguments, "name");
            var platform = OptionalString(arguments, "platform");

            foreach (SolutionConfiguration configuration in dte.Solution.SolutionBuild.SolutionConfigurations)
            {
                if (!string.Equals(configuration.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(platform) &&
                    !string.Equals(configuration.PlatformName, platform, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                configuration.Activate();
                return new { activated = true, name = configuration.Name, platform = configuration.PlatformName };
            }

            throw new InvalidOperationException("The requested solution configuration was not found.");
        }

        private object SetStartupProject(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnsureSolutionOpen();
            var projectName = RequiredString(arguments, "project");
            var project = FindProject(projectName);
            if (project == null) throw new InvalidOperationException("Project not found: " + projectName);

            dte.Solution.SolutionBuild.StartupProjects = new object[] { project.UniqueName };
            return new { updated = true, project = project.Name, uniqueName = project.UniqueName };
        }

        private object BuildSolution(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnsureSolutionOpen();
            ActivateConfigurationIfRequested(arguments);
            dte.Solution.SolutionBuild.Build(true);
            return BuildResult("build");
        }

        private object RebuildSolution(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnsureSolutionOpen();
            ActivateConfigurationIfRequested(arguments);
            dte.Solution.SolutionBuild.Clean(true);
            dte.Solution.SolutionBuild.Build(true);
            return BuildResult("rebuild");
        }

        private object CleanSolution()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnsureSolutionOpen();
            dte.Solution.SolutionBuild.Clean(true);
            return BuildResult("clean");
        }

        private object BuildProject(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnsureSolutionOpen();
            var projectName = RequiredString(arguments, "project");
            var project = FindProject(projectName);
            if (project == null) throw new InvalidOperationException("Project not found: " + projectName);

            var configuration = OptionalString(arguments, "configuration") ?? SafeActiveConfigurationName();
            if (string.IsNullOrWhiteSpace(configuration))
                throw new InvalidOperationException("No active solution configuration is available.");

            dte.Solution.SolutionBuild.BuildProject(configuration, project.UniqueName, true);
            return new
            {
                operation = "build-project",
                project = project.Name,
                uniqueName = project.UniqueName,
                configuration,
                lastBuildInfo = dte.Solution.SolutionBuild.LastBuildInfo,
                buildState = dte.Solution.SolutionBuild.BuildState.ToString()
            };
        }

        private object CancelBuild()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnsureSolutionOpen();
            dte.Solution.SolutionBuild.Cancel();
            return new { cancelled = true };
        }

        private object GetBuildErrors(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var includeWarnings = OptionalBool(arguments, "includeWarnings", true);
            var maximum = Math.Max(1, Math.Min(5000, OptionalInt(arguments, "limit", 500)));
            var errors = new List<object>();

            var errorList = dte.ToolWindows.ErrorList;
            var items = errorList?.ErrorItems;
            if (items == null) return new { count = 0, errors };

            for (var index = 1; index <= items.Count && errors.Count < maximum; index++)
            {
                ErrorItem item;
                try { item = items.Item(index); }
                catch { continue; }

                var level = item.ErrorLevel.ToString();
                var isWarning = item.ErrorLevel == vsBuildErrorLevel.vsBuildErrorLevelMedium;
                var isMessage = item.ErrorLevel == vsBuildErrorLevel.vsBuildErrorLevelLow;
                if (!includeWarnings && (isWarning || isMessage)) continue;

                errors.Add(new
                {
                    level,
                    description = item.Description ?? string.Empty,
                    project = item.Project ?? string.Empty,
                    file = item.FileName ?? string.Empty,
                    line = item.Line,
                    column = item.Column
                });
            }

            return new
            {
                count = errors.Count,
                total = items.Count,
                truncated = errors.Count < items.Count && errors.Count >= maximum,
                errors
            };
        }

        private object ListDocuments()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var documents = new List<object>();
            foreach (Document document in dte.Documents)
            {
                documents.Add(DocumentSnapshot(document, includeText: false, DefaultMaximumTextCharacters));
            }

            return documents;
        }

        private object GetActiveDocument(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var document = dte.ActiveDocument;
            if (document == null) return new { isOpen = false };

            var includeText = OptionalBool(arguments, "includeText", false);
            var maximum = Math.Max(1, OptionalInt(arguments, "maxCharacters", DefaultMaximumTextCharacters));
            return DocumentSnapshot(document, includeText, maximum);
        }

        private object OpenDocument(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var path = RequiredExistingFile(arguments, "path");
            var line = OptionalInt(arguments, "line", 0);
            var column = OptionalInt(arguments, "column", 1);

            var window = dte.ItemOperations.OpenFile(path, Constants.vsViewKindTextView);
            window.Activate();
            if (line > 0) NavigateActiveDocument(line, Math.Max(1, column), false);

            return new
            {
                opened = true,
                path = dte.ActiveDocument?.FullName ?? path,
                line = line > 0 ? line : (int?)null,
                column = line > 0 ? Math.Max(1, column) : (int?)null
            };
        }

        private object GetDocumentText(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var document = ResolveDocument(arguments, requireTextDocument: true);
            var maximum = Math.Max(1, OptionalInt(arguments, "maxCharacters", DefaultMaximumTextCharacters));
            var completeText = ReadDocumentText(document);
            var truncated = completeText.Length > maximum;
            var text = truncated ? completeText.Substring(0, maximum) : completeText;

            return new
            {
                path = document.FullName,
                name = document.Name,
                text,
                totalCharacters = completeText.Length,
                truncated
            };
        }

        private object ReplaceDocumentText(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var document = ResolveDocument(arguments, requireTextDocument: true);
            var text = RequiredStringAllowEmpty(arguments, "text");
            var textDocument = GetTextDocument(document);
            var start = textDocument.StartPoint.CreateEditPoint();
            start.Delete(textDocument.EndPoint);
            start.Insert(text);

            return new
            {
                replaced = true,
                path = document.FullName,
                characters = text.Length,
                saved = document.Saved
            };
        }

        private object SaveDocument(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var document = ResolveDocument(arguments, requireTextDocument: false);
            document.Save();
            return new { saved = true, path = document.FullName };
        }

        private object SaveAllDocuments()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dte.ExecuteCommand("File.SaveAll");
            return new { saved = true, count = dte.Documents.Count };
        }

        private object CloseDocument(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var document = ResolveDocument(arguments, requireTextDocument: false);
            var save = OptionalBool(arguments, "save", true);
            var path = document.FullName;
            document.Close(save ? vsSaveChanges.vsSaveChangesYes : vsSaveChanges.vsSaveChangesNo);
            return new { closed = true, path, saved = save };
        }

        private object GetEditorSelection()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var document = dte.ActiveDocument;
            if (document == null) return new { hasDocument = false };

            var selection = document.Selection as TextSelection;
            if (selection == null)
            {
                return new
                {
                    hasDocument = true,
                    path = document.FullName,
                    isTextSelection = false
                };
            }

            return new
            {
                hasDocument = true,
                isTextSelection = true,
                path = document.FullName,
                text = selection.Text ?? string.Empty,
                isEmpty = selection.IsEmpty,
                startLine = selection.TopPoint.Line,
                startColumn = selection.TopPoint.LineCharOffset,
                endLine = selection.BottomPoint.Line,
                endColumn = selection.BottomPoint.LineCharOffset,
                currentLine = selection.CurrentLine
            };
        }

        private object ReplaceEditorSelection(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var text = RequiredStringAllowEmpty(arguments, "text");
            var document = dte.ActiveDocument ?? throw new InvalidOperationException("No active document.");
            var selection = document.Selection as TextSelection ??
                throw new InvalidOperationException("The active document does not expose a text selection.");

            selection.Text = text;
            return new
            {
                replaced = true,
                path = document.FullName,
                characters = text.Length
            };
        }

        private object NavigateEditor(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var path = OptionalString(arguments, "path");
            var line = Math.Max(1, OptionalInt(arguments, "line", 1));
            var column = Math.Max(1, OptionalInt(arguments, "column", 1));
            var selectLine = OptionalBool(arguments, "selectLine", false);

            if (!string.IsNullOrWhiteSpace(path))
            {
                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath)) throw new FileNotFoundException("Document not found.", fullPath);
                dte.ItemOperations.OpenFile(fullPath, Constants.vsViewKindTextView).Activate();
            }

            NavigateActiveDocument(line, column, selectLine);
            return new
            {
                navigated = true,
                path = dte.ActiveDocument?.FullName ?? string.Empty,
                line,
                column,
                selectLine
            };
        }

        private object StartDebugging(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetStartupProjectIfRequested(arguments);
            dte.Debugger.Go(false);
            return new { started = true, mode = dte.Debugger.CurrentMode.ToString() };
        }

        private object StartWithoutDebugging(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetStartupProjectIfRequested(arguments);
            dte.ExecuteCommand("Debug.StartWithoutDebugging");
            return new { started = true, debugging = false };
        }

        private object StopDebugging()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dte.Debugger.Stop(false);
            return new { stopped = true };
        }

        private object RestartDebugging()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dte.ExecuteCommand("Debug.Restart");
            return new { restarted = true };
        }

        private object PauseDebugging()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dte.Debugger.Break(false);
            return new { paused = true };
        }

        private object ContinueDebugging()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dte.Debugger.Go(false);
            return new { continued = true };
        }

        private object StepOver()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dte.Debugger.StepOver(false);
            return new { stepped = "over" };
        }

        private object StepInto()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dte.Debugger.StepInto(false);
            return new { stepped = "into" };
        }

        private object StepOut()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dte.Debugger.StepOut(false);
            return new { stepped = "out" };
        }

        private object RunToCursor()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dte.ExecuteCommand("Debug.RunToCursor");
            return new { started = true, operation = "run-to-cursor" };
        }

        private object SetNextStatement()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dte.ExecuteCommand("Debug.SetNextStatement");
            return new { updated = true };
        }

        private object DetachAll()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dte.Debugger.DetachAll();
            return new { detached = true };
        }

        private object TerminateAll()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dte.Debugger.TerminateAll();
            return new { terminated = true };
        }

        private object ListDebuggedProcesses()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var processes = new List<object>();
            foreach (EnvDTE.Process process in dte.Debugger.DebuggedProcesses)
            {
                processes.Add(new
                {
                    id = process.ProcessID,
                    name = process.Name
                });
            }

            return processes;
        }

        private object ListThreads()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var threads = new List<object>();
            var program = dte.Debugger.CurrentProgram;
            if (program == null) return threads;

            foreach (EnvDTE.Thread thread in program.Threads)
            {
                threads.Add(new
                {
                    id = thread.ID,
                    name = thread.Name,
                    state = thread.State.ToString(),
                    location = thread.Location
                });
            }

            return threads;
        }

        private object AddBreakpoint(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var file = RequiredString(arguments, "file");
            var line = OptionalInt(arguments, "line", 0);
            var column = Math.Max(1, OptionalInt(arguments, "column", 1));
            var condition = OptionalString(arguments, "condition");

            if (line <= 0) throw new ArgumentException("A positive line number is required.");

            Breakpoints breakpoints;
            if (string.IsNullOrWhiteSpace(condition))
            {
                breakpoints = dte.Debugger.Breakpoints.Add(File: file, Line: line, Column: column);
            }
            else
            {
                breakpoints = dte.Debugger.Breakpoints.Add(
                    File: file,
                    Line: line,
                    Column: column,
                    Condition: condition,
                    ConditionType: dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenTrue);
            }

            return new
            {
                added = breakpoints.Count > 0,
                file,
                line,
                column,
                condition = condition ?? string.Empty,
                count = breakpoints.Count
            };
        }

        private object ListBreakpoints()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new List<object>();
            foreach (Breakpoint breakpoint in dte.Debugger.Breakpoints)
            {
                result.Add(new
                {
                    breakpoint.File,
                    breakpoint.FileLine,
                    breakpoint.FileColumn,
                    breakpoint.FunctionName,
                    breakpoint.Enabled,
                    breakpoint.Condition,
                    conditionType = breakpoint.ConditionType.ToString(),
                    hitCountType = breakpoint.HitCountType.ToString(),
                    breakpoint.HitCountTarget
                });
            }

            return result;
        }

        private object RemoveBreakpoints(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var removeAll = OptionalBool(arguments, "all", false);
            var file = OptionalString(arguments, "file");
            var line = OptionalInt(arguments, "line", 0);

            if (!removeAll && string.IsNullOrWhiteSpace(file) && line <= 0)
                throw new ArgumentException("Set all=true or provide file and/or line.");

            var removed = 0;
            var breakpoints = dte.Debugger.Breakpoints;
            for (var index = breakpoints.Count; index >= 1; index--)
            {
                var breakpoint = breakpoints.Item(index);
                if (!removeAll && !BreakpointMatches(breakpoint, file, line)) continue;
                breakpoint.Delete();
                removed++;
            }

            return new { removed, all = removeAll, file = file ?? string.Empty, line };
        }

        private object SetBreakpointsEnabled(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var enabled = OptionalBool(arguments, "enabled", true);
            var all = OptionalBool(arguments, "all", false);
            var file = OptionalString(arguments, "file");
            var line = OptionalInt(arguments, "line", 0);

            if (!all && string.IsNullOrWhiteSpace(file) && line <= 0)
                throw new ArgumentException("Set all=true or provide file and/or line.");

            var updated = 0;
            foreach (Breakpoint breakpoint in dte.Debugger.Breakpoints)
            {
                if (!all && !BreakpointMatches(breakpoint, file, line)) continue;
                breakpoint.Enabled = enabled;
                updated++;
            }

            return new { updated, enabled, all, file = file ?? string.Empty, line };
        }

        private object GetCallStack()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var frames = new List<object>();
            var thread = dte.Debugger.CurrentThread;
            if (thread == null) return frames;

            var index = 0;
            foreach (StackFrame frame in thread.StackFrames)
            {
                frames.Add(new
                {
                    index = index++,
                    functionName = frame.FunctionName,
                    module = frame.Module,
                    language = frame.Language,
                    returnType = frame.ReturnType,
                    threadId = frame.Parent?.ID,
                    threadLocation = frame.Parent?.Location
                });
            }

            return frames;
        }

        private object GetLocals()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var frame = dte.Debugger.CurrentStackFrame;
            if (frame == null) return new { hasFrame = false, arguments = Array.Empty<object>(), locals = Array.Empty<object>() };

            return new
            {
                hasFrame = true,
                functionName = frame.FunctionName,
                arguments = SnapshotExpressions(frame.Arguments),
                locals = SnapshotExpressions(frame.Locals)
            };
        }

        private object Evaluate(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var expressionText = RequiredString(arguments, "expression");
            var timeout = Math.Max(100, Math.Min(60000, OptionalInt(arguments, "timeoutMilliseconds", 3000)));
            var treatAsStatement = OptionalBool(arguments, "treatAsStatement", true);

            var expression = dte.Debugger.GetExpression(expressionText, treatAsStatement, timeout);
            return new
            {
                expression = expressionText,
                value = expression.Value,
                type = expression.Type,
                isValid = expression.IsValidValue,
                dataMembers = expression.DataMembers?.Count ?? 0
            };
        }

        private IEnumerable<Project> EnumerateProjects()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte.Solution?.IsOpen != true) yield break;

            foreach (Project project in dte.Solution.Projects)
            {
                foreach (var nested in EnumerateProjectAndChildren(project))
                    yield return nested;
            }
        }

        private static IEnumerable<Project> EnumerateProjectAndChildren(Project project)
        {
            if (project == null) yield break;
            yield return project;

            ProjectItems items;
            try { items = project.ProjectItems; }
            catch { yield break; }
            if (items == null) yield break;

            foreach (ProjectItem item in items)
            {
                Project child;
                try { child = item.SubProject; }
                catch { child = null; }
                if (child == null) continue;

                foreach (var nested in EnumerateProjectAndChildren(child))
                    yield return nested;
            }
        }

        private Project FindProject(string nameOrPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (var project in EnumerateProjects())
            {
                if (string.Equals(project.Name, nameOrPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(project.UniqueName, nameOrPath, StringComparison.OrdinalIgnoreCase) ||
                    PathsEqual(SafeProjectFullName(project), nameOrPath))
                {
                    return project;
                }
            }

            return null;
        }

        private object ProjectSnapshot(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return new
            {
                name = project.Name,
                uniqueName = project.UniqueName,
                fullName = SafeProjectFullName(project),
                kind = project.Kind,
                saved = SafeProjectSaved(project)
            };
        }

        private object DocumentSnapshot(Document document, bool includeText, int maximum)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snapshot = new Dictionary<string, object>
            {
                ["name"] = document.Name,
                ["fullName"] = document.FullName,
                ["kind"] = document.Kind,
                ["language"] = document.Language,
                ["saved"] = document.Saved,
                ["readOnly"] = document.ReadOnly
            };

            if (includeText)
            {
                try
                {
                    var text = ReadDocumentText(document);
                    snapshot["totalCharacters"] = text.Length;
                    snapshot["truncated"] = text.Length > maximum;
                    snapshot["text"] = text.Length > maximum ? text.Substring(0, maximum) : text;
                }
                catch (Exception ex)
                {
                    snapshot["textError"] = ex.Message;
                }
            }

            return snapshot;
        }

        private Document ResolveDocument(JObject arguments, bool requireTextDocument)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var path = OptionalString(arguments, "path");
            Document document = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                document = dte.ActiveDocument;
            }
            else
            {
                foreach (Document candidate in dte.Documents)
                {
                    if (PathsEqual(candidate.FullName, path) ||
                        string.Equals(candidate.Name, path, StringComparison.OrdinalIgnoreCase))
                    {
                        document = candidate;
                        break;
                    }
                }

                if (document == null && File.Exists(path))
                {
                    dte.ItemOperations.OpenFile(Path.GetFullPath(path), Constants.vsViewKindTextView).Activate();
                    document = dte.ActiveDocument;
                }
            }

            if (document == null) throw new InvalidOperationException("No matching document is open.");
            if (requireTextDocument) _ = GetTextDocument(document);
            return document;
        }

        private static TextDocument GetTextDocument(Document document)
        {
            try
            {
                return (TextDocument)document.Object("TextDocument");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("The document is not a text document.", ex);
            }
        }

        private static string ReadDocumentText(Document document)
        {
            var textDocument = GetTextDocument(document);
            var start = textDocument.StartPoint.CreateEditPoint();
            return start.GetText(textDocument.EndPoint);
        }

        private void NavigateActiveDocument(int line, int column, bool selectLine)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var document = dte.ActiveDocument ?? throw new InvalidOperationException("No active document.");
            var selection = document.Selection as TextSelection ??
                throw new InvalidOperationException("The active document does not expose a text selection.");

            selection.MoveToLineAndOffset(line, column, false);
            if (selectLine) selection.SelectLine();
        }

        private object BuildResult(string operation)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var build = dte.Solution.SolutionBuild;
            return new
            {
                operation,
                lastBuildInfo = build.LastBuildInfo,
                succeeded = build.LastBuildInfo == 0,
                buildState = build.BuildState.ToString(),
                configuration = SafeActiveConfigurationName()
            };
        }

        private void ActivateConfigurationIfRequested(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var configurationName = OptionalString(arguments, "configuration");
            var platform = OptionalString(arguments, "platform");
            if (string.IsNullOrWhiteSpace(configurationName)) return;

            foreach (SolutionConfiguration configuration in dte.Solution.SolutionBuild.SolutionConfigurations)
            {
                if (!string.Equals(configuration.Name, configurationName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(platform) &&
                    !string.Equals(configuration.PlatformName, platform, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                configuration.Activate();
                return;
            }

            throw new InvalidOperationException("The requested solution configuration was not found.");
        }

        private void SetStartupProjectIfRequested(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var projectName = OptionalString(arguments, "project");
            if (string.IsNullOrWhiteSpace(projectName)) return;
            _ = SetStartupProject(new JObject { ["project"] = projectName });
        }

        private string SafeActiveConfigurationName()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return dte.Solution?.SolutionBuild?.ActiveConfiguration?.Name ?? string.Empty; }
            catch { return string.Empty; }
        }

        private void EnsureSolutionOpen()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte.Solution?.IsOpen != true) throw new InvalidOperationException("No Visual Studio solution is open.");
        }

        private static List<object> SnapshotExpressions(Expressions expressions)
        {
            var result = new List<object>();
            if (expressions == null) return result;

            foreach (Expression expression in expressions)
            {
                result.Add(new
                {
                    name = expression.Name,
                    value = expression.Value,
                    type = expression.Type,
                    isValid = expression.IsValidValue,
                    dataMembers = expression.DataMembers?.Count ?? 0
                });
            }

            return result;
        }

        private static bool BreakpointMatches(Breakpoint breakpoint, string file, int line)
        {
            var fileMatches = string.IsNullOrWhiteSpace(file) ||
                              PathsEqual(breakpoint.File, file) ||
                              string.Equals(Path.GetFileName(breakpoint.File), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase);
            var lineMatches = line <= 0 || breakpoint.FileLine == line;
            return fileMatches && lineMatches;
        }

        private static string RequiredString(JObject arguments, string name)
        {
            var value = OptionalString(arguments, name);
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(name + " is required.");
            return value;
        }

        private static string RequiredStringAllowEmpty(JObject arguments, string name)
        {
            if (arguments == null || arguments[name] == null)
                throw new ArgumentException(name + " is required.");
            return arguments[name].Value<string>() ?? string.Empty;
        }

        private static string RequiredExistingFile(JObject arguments, string name)
        {
            var value = RequiredString(arguments, name);
            var fullPath = Path.GetFullPath(value);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("File not found.", fullPath);
            return fullPath;
        }

        private static string OptionalString(JObject arguments, string name) =>
            arguments?[name]?.Type == JTokenType.Null ? null : arguments?[name]?.Value<string>();

        private static int OptionalInt(JObject arguments, string name, int fallback) =>
            arguments?[name]?.Value<int?>() ?? fallback;

        private static bool OptionalBool(JObject arguments, string name, bool fallback) =>
            arguments?[name]?.Value<bool?>() ?? fallback;

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string SafeProjectFullName(Project project)
        {
            try { return project.FullName; }
            catch { return string.Empty; }
        }

        private static bool SafeProjectSaved(Project project)
        {
            try { return project.Saved; }
            catch { return true; }
        }

        private static string SafeCommandName(Command command)
        {
            try { return command.Name; }
            catch { return string.Empty; }
        }

        private static string SafeLocalizedCommandName(Command command)
        {
            try { return command.LocalizedName; }
            catch { return string.Empty; }
        }

        private static string SafeWindowCaption(Window window)
        {
            try { return window.Caption; }
            catch { return string.Empty; }
        }

        private static string SafeWindowKind(Window window)
        {
            try { return window.Kind; }
            catch { return string.Empty; }
        }

        private static bool SafeWindowVisible(Window window)
        {
            try { return window.Visible; }
            catch { return false; }
        }

        private static string SafeWindowDocument(Window window)
        {
            try { return window.Document?.FullName ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
