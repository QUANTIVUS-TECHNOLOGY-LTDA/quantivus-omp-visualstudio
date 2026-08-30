using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VSAgent.Models;
// EnvDTE and EnvDTE80 PIAs only expose a small subset of the COM automation
// surface at compile time; the remaining members live in the COM type library
// and are reachable through runtime binding. To keep the dispatcher portable
// across VS 17/18 without dragging in extra references, ambiguous BCL types are
// aliased explicitly so the EnvDTE and WPF/BCL names cannot collide.
using Thread = EnvDTE.Thread;
using StackFrame = EnvDTE.StackFrame;
using Expression = EnvDTE.Expression;
using Expressions = EnvDTE.Expressions;
using Breakpoint = EnvDTE.Breakpoint;
using DteWindow = EnvDTE.Window;
using DiagnosticsProcess = System.Diagnostics.Process;

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
    ///
    /// Runtime-only diagnostics and assembly inspection stay off the UI thread.
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
                switch (request.Tool)
                {
                    // Pure runtime/environment diagnostics never touch DTE and
                    // run on the thread pool to keep the UI responsive.
                    case "vs_get_environment_variables":
                    case "vs_get_system_info":
                    case "vs_get_host_runtime":
                    case "vs_analyze_assembly":
                    case "vs_dependency_graph":
                        return VisualStudioToolResponse.Ok(request.Id, ExecuteRuntime(request));

                    default:
                        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        return ExecuteOnUIThread(request);
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

        private VisualStudioToolResponse ExecuteOnUIThread(VisualStudioToolRequest request)
        {
            try
            {
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
                    case "vs_get_solution_properties": return Ok(request, GetSolutionProperties());
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
                    case "vs_debug_process_list": return Ok(request, ListProcesses());
                    case "vs_debug_thread_list": return Ok(request, ListThreads());
                    case "vs_get_call_stack": return Ok(request, GetCallStack(includeAllThreads: false));
                    case "vs_get_call_stack_all_threads": return Ok(request, GetCallStack(includeAllThreads: true));
                    case "vs_get_locals": return Ok(request, GetLocals());
                    case "vs_get_arguments": return Ok(request, GetExpressions(GetCurrentExpressions("Arguments")));
                    case "vs_evaluate": return Ok(request, Evaluate(request.Arguments));

                    // Debugger inspection
                    case "vs_list_threads": return Ok(request, ListThreads());
                    case "vs_list_processes": return Ok(request, ListProcesses());
                    case "vs_get_current_thread": return Ok(request, GetCurrentThread());
                    case "vs_list_modules": return Ok(request, ListModules());
                    case "vs_get_exception_info": return Ok(request, GetExceptionInfo());
                    case "vs_get_exception_settings": return Ok(request, GetExceptionSettings());
                    case "vs_get_process_info": return Ok(request, GetProcessInfo(request.Arguments));

                    // Breakpoints
                    case "vs_breakpoint_add": return Ok(request, AddBreakpoint(request.Arguments));
                    case "vs_breakpoint_list": return Ok(request, ListBreakpoints());
                    case "vs_breakpoint_remove": return Ok(request, RemoveBreakpoints(request.Arguments));
                    case "vs_breakpoint_remove_at": return Ok(request, RemoveBreakpointAt(request.Arguments));
                    case "vs_breakpoint_set_enabled": return Ok(request, SetBreakpointsEnabled(request.Arguments));
                    case "vs_breakpoint_enable": return Ok(request, SetBreakpointEnabled(request.Arguments, enabled: true));
                    case "vs_breakpoint_disable": return Ok(request, SetBreakpointEnabled(request.Arguments, enabled: false));
                    case "vs_breakpoint_set_condition": return Ok(request, SetBreakpointCondition(request.Arguments));
                    case "vs_breakpoint_clear_all": return Ok(request, ClearAllBreakpoints());

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

        private object ExecuteRuntime(VisualStudioToolRequest request)
        {
            switch (request.Tool)
            {
                case "vs_get_environment_variables": return GetEnvironmentVariables(request.Arguments);
                case "vs_get_system_info": return GetSystemInfo();
                case "vs_get_host_runtime": return GetHostRuntime();
                case "vs_analyze_assembly": return AnalyzeAssembly(request.Arguments);
                case "vs_dependency_graph": return GetDependencyGraph(request.Arguments);
                default: throw new InvalidOperationException("Unhandled runtime tool: " + request.Tool);
            }
        }

        // ===== Solution / build / startup ===========================================

        private DebuggerSnapshot GetStatus()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var mode = dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode
                ? "paused"
                : dte.Debugger.CurrentMode == dbgDebugMode.dbgRunMode
                    ? "running"
                    : "stopped";

            var startupProjects = dte.Solution?.SolutionBuild?.StartupProjects as Array;
            var currentProcess = dte.Debugger.CurrentProcess;
            var currentThread = dte.Debugger.CurrentThread;
            StackFrame currentFrame = null;
            if (currentThread != null)
            {
                try
                {
                    var frames = currentThread.StackFrames;
                    if (frames != null && frames.Count > 0) currentFrame = frames.Item(1);
                }
                catch { /* frames may not be enumerable when detached */ }
            }

            return new DebuggerSnapshot
            {
                Mode = mode,
                Solution = dte.Solution?.FullName ?? string.Empty,
                StartupProjects = startupProjects == null
                    ? string.Empty
                    : string.Join(", ", startupProjects.Cast<object>().Select(value => value?.ToString())),
                IsSolutionOpen = dte.Solution?.IsOpen == true,
                LastBreakReason = SafeEnumString(dte.Debugger, "LastBreakReason"),
                AllExceptionsBreakWhenThrown = SafeBool(dte.Debugger, "AllExceptionsBreakWhenThrown"),
                JustMyCode = SafeBool(dte.Debugger, "JustMyCode"),
                CurrentProcessName = SafeString(currentProcess, "Name"),
                CurrentProcessId = SafeInt(currentProcess, "ProcessID"),
                CurrentThreadId = SafeString(currentThread, "ID"),
                CurrentFrame = currentFrame == null
                    ? string.Empty
                    : (SafeString(currentFrame, "FunctionName") + " @ " + SafeString(currentFrame, "Module")),
                DebuggedProcessCount = SafeInt(dte.Debugger.DebuggedProcesses, "Count"),
                BreakpointCount = SafeInt(dte.Debugger.Breakpoints, "Count")
            };
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
            foreach (DteWindow window in dte.Windows)
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

            foreach (DteWindow window in dte.Windows)
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
                var configurationPlatform = SafeString(configuration, "PlatformName");
                configurations.Add(new
                {
                    name = configuration.Name,
                    platform = configurationPlatform,
                    isActive = active != null &&
                               string.Equals(active.Name, configuration.Name, StringComparison.OrdinalIgnoreCase) &&
                               string.Equals(SafeString(active, "PlatformName"), configurationPlatform, StringComparison.OrdinalIgnoreCase)
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
                var configurationPlatform = SafeString(configuration, "PlatformName");
                if (!string.IsNullOrWhiteSpace(platform) &&
                    !string.Equals(configurationPlatform, platform, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                configuration.Activate();
                return new { activated = true, name = configuration.Name, platform = configurationPlatform };
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
            dte.ExecuteCommand("Build.Cancel");
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

        private object GetSolutionProperties()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var build = dte.Solution?.SolutionBuild;
            var startupProjects = build?.StartupProjects as Array;
            var configurations = new List<string>();
            try
            {
                var cfgs = SafeObject(build, "SolutionConfigurations");
                if (cfgs is IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item == null) continue;
                        var name = SafeString(item, "Name");
                        if (!string.IsNullOrEmpty(name) && !configurations.Contains(name))
                            configurations.Add(name);
                    }
                }
            }
            catch { /* best-effort */ }

            return new
            {
                solutionFullName = dte.Solution?.FullName ?? string.Empty,
                startupProjects = startupProjects == null
                    ? string.Empty
                    : string.Join(", ", startupProjects.Cast<object>().Select(value => value?.ToString())),
                activeConfigurationName = SafeString(build?.ActiveConfiguration, "Name"),
                activePlatformName = SafeString(build?.ActiveConfiguration, "PlatformName"),
                buildState = SafeEnumString(build, "BuildState"),
                lastBuildInfo = SafeInt(build, "LastBuildInfo"),
                configurations
            };
        }

        // ===== Breakpoints ===========================================================

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

        private object RemoveBreakpoint(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var index = arguments?["index"]?.Value<int>() ?? -1;
            if (index <= 0) throw new ArgumentException("A 1-based 'index' is required.");
            var target = GetBreakpointAt(index);
            var location = new { file = target.File, line = target.FileLine };
            target.Delete();
            return new { removed = true, index, location };
        }

        private object RemoveBreakpointAt(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var file = arguments?["file"]?.Value<string>();
            var line = arguments?["line"]?.Value<int>() ?? 0;
            if (string.IsNullOrWhiteSpace(file) || line <= 0)
                throw new ArgumentException("file and a positive line number are required.");

            var removed = new List<object>();
            var snapshot = dte.Debugger.Breakpoints.Cast<Breakpoint>().ToList();
            foreach (var bp in snapshot)
            {
                if (bp == null) continue;
                if (string.Equals(bp.File, file, StringComparison.OrdinalIgnoreCase) && bp.FileLine == line)
                {
                    removed.Add(new { file = bp.File, line = bp.FileLine });
                    bp.Delete();
                }
            }
            return new { removed = removed.Count, locations = removed };
        }

        private object SetBreakpointEnabled(JObject arguments, bool enabled)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var index = arguments?["index"]?.Value<int>() ?? -1;
            if (index <= 0) throw new ArgumentException("A 1-based 'index' is required.");
            var bp = GetBreakpointAt(index);
            bp.Enabled = enabled;
            return new { enabled, file = bp.File, line = bp.FileLine, index };
        }

        private object SetBreakpointCondition(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var index = arguments?["index"]?.Value<int>() ?? -1;
            var condition = arguments?["condition"]?.Value<string>();
            if (index <= 0) throw new ArgumentException("A 1-based 'index' is required.");
            if (string.IsNullOrWhiteSpace(condition))
                throw new ArgumentException("A non-empty 'condition' is required.");
            var existing = GetBreakpointAt(index);
            var file = existing.File;
            var line = existing.FileLine;
            existing.Delete();
            var reAdded = dte.Debugger.Breakpoints.Add(File: file, Line: line, Condition: condition);
            return new
            {
                replaced = true,
                file,
                line,
                condition,
                count = reAdded.Count
            };
        }

        private object ClearAllBreakpoints()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snapshot = dte.Debugger.Breakpoints.Cast<Breakpoint>().ToList();
            var count = snapshot.Count;
            foreach (var bp in snapshot) bp?.Delete();
            return new { cleared = count };
        }

        private object ListBreakpoints()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new List<object>();
            var index = 0;
            foreach (Breakpoint breakpoint in dte.Debugger.Breakpoints)
            {
                if (breakpoint == null) continue;
                index++;
                result.Add(new
                {
                    index,
                    file = breakpoint.File,
                    line = breakpoint.FileLine,
                    column = breakpoint.FileColumn,
                    functionName = breakpoint.FunctionName,
                    enabled = breakpoint.Enabled,
                    condition = breakpoint.Condition,
                    conditionType = breakpoint.ConditionType.ToString(),
                    hitCountType = breakpoint.HitCountType.ToString(),
                    hitCountTarget = breakpoint.HitCountTarget,
                    currentHits = SafeInt(breakpoint, "CurrentHits")
                });
            }

            return result;
        }

        private object RemoveBreakpoints(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var requestedIndex = OptionalInt(arguments, "index", 0);
            if (requestedIndex > 0) return RemoveBreakpoint(arguments);

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
            var requestedIndex = OptionalInt(arguments, "index", 0);
            if (requestedIndex > 0) return SetBreakpointEnabled(arguments, enabled);

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

        private Breakpoint GetBreakpointAt(int index)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var current = 0;
            foreach (Breakpoint bp in dte.Debugger.Breakpoints)
            {
                if (bp == null) continue;
                current++;
                if (current == index) return bp;
            }
            throw new ArgumentOutOfRangeException(nameof(index), "Breakpoint index out of range.");
        }

        // ===== Call stack / locals / arguments =======================================

        private object GetCallStack(bool includeAllThreads)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!includeAllThreads)
            {
                var currentThread = dte.Debugger.CurrentThread;
                return currentThread == null ? new List<object>() : SnapshotCallStack(currentThread);
            }

            var result = new List<object>();
            foreach (var thread in EnumerateAllThreads())
            {
                result.Add(new
                {
                    threadId = thread.ID,
                    threadName = thread.Name,
                    frames = SnapshotCallStack(thread)
                });
            }

            return result;
        }

        private static List<object> SnapshotCallStack(Thread thread)
        {
            var frames = new List<object>();
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
                    fileName = SafeFileName(frame),
                    threadId = thread.ID,
                    threadLocation = thread.Location
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

        private Expressions GetCurrentExpressions(string property)
        {
            var thread = dte.Debugger.CurrentThread;
            if (thread == null) return null;
            StackFrame frame;
            try
            {
                var frames = thread.StackFrames;
                if (frames == null || frames.Count == 0) return null;
                frame = frames.Item(1);
            }
            catch
            {
                return null;
            }
            if (frame == null) return null;
            return SafeObject(frame, property) as Expressions;
        }

        private object GetExpressions(Expressions expressions)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (expressions == null) return new { error = "Debugger is not paused at a stack frame." };
            var result = new List<object>();
            foreach (Expression expression in expressions)
            {
                if (expression == null) continue;
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
                var configurationPlatform = SafeString(configuration, "PlatformName");
                if (!string.IsNullOrWhiteSpace(platform) &&
                    !string.Equals(configurationPlatform, platform, StringComparison.OrdinalIgnoreCase))
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

        // ===== Threads / processes ===================================================

        private object ListThreads()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var threads = new List<object>();
            var currentThread = dte.Debugger.CurrentThread;
            var process = dte.Debugger.CurrentProcess;
            if (process == null) return threads;
            var threadCollection = SafeObject(process, "Threads") as IEnumerable;
            if (threadCollection == null) return threads;
            foreach (var thread in threadCollection)
            {
                if (thread == null) continue;
                var id = SafeString(thread, "ID");
                threads.Add(new
                {
                    id,
                    name = SafeString(thread, "Name"),
                    priority = SafeInt(thread, "Priority"),
                    location = SafeString(thread, "Location"),
                    isCurrent = currentThread != null && id == SafeString(currentThread, "ID"),
                    stackDepth = SafeCount(SafeObject(thread, "StackFrames"))
                });
            }
            return threads;
        }

        private object ListProcesses()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var processes = new List<object>();
            var currentProcess = dte.Debugger.CurrentProcess;
            foreach (var process in dte.Debugger.DebuggedProcesses)
            {
                if (process == null) continue;
                var pid = SafeInt(process, "ProcessID");
                processes.Add(new
                {
                    name = SafeString(process, "Name"),
                    processId = pid,
                    userName = SafeString(process, "UserName"),
                    isCurrent = currentProcess != null && pid == SafeInt(currentProcess, "ProcessID"),
                    threadCount = SafeCount(SafeObject(process, "Threads")),
                    moduleCount = SafeCount(SafeObject(process, "Modules"))
                });
            }
            return processes;
        }

        private object GetCurrentThread()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var thread = dte.Debugger.CurrentThread;
            if (thread == null) return new { error = "No current thread." };
            var frames = new List<object>();
            foreach (StackFrame frame in thread.StackFrames)
            {
                frames.Add(new
                {
                    functionName = frame.FunctionName,
                    module = frame.Module,
                    language = frame.Language,
                    fileName = SafeFileName(frame)
                });
            }
            return new
            {
                id = thread.ID,
                name = thread.Name,
                location = thread.Location,
                frames
            };
        }

        private IEnumerable<Thread> EnumerateAllThreads()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var foundAny = false;

            foreach (EnvDTE.Process process in dte.Debugger.DebuggedProcesses)
            {
                if (!(SafeObject(process, "Threads") is IEnumerable threads)) continue;
                foreach (var candidate in threads)
                {
                    if (!(candidate is Thread thread)) continue;
                    var key = SafeString(thread, "ID") + "|" + SafeString(thread, "Name") + "|" + SafeString(thread, "Location");
                    if (!seen.Add(key)) continue;
                    foundAny = true;
                    yield return thread;
                }
            }

            if (foundAny) yield break;

            var program = dte.Debugger.CurrentProgram;
            if (program == null) yield break;
            foreach (Thread thread in program.Threads)
            {
                if (thread != null) yield return thread;
            }
        }

        // ===== Modules / exceptions ==================================================

        private object ListModules()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var modules = new List<object>();
            var process = dte.Debugger.CurrentProcess;
            if (process == null) return modules;
            var moduleCollection = SafeObject(process, "Modules") as IEnumerable;
            if (moduleCollection == null) return modules;
            foreach (var module in moduleCollection)
            {
                if (module == null) continue;
                modules.Add(new
                {
                    name = SafeString(module, "Name"),
                    path = SafeString(module, "Path"),
                    version = SafeString(module, "Version"),
                    optimized = SafeBool(module, "Optimized"),
                    address = SafeString(module, "Address")
                });
            }
            return modules;
        }

        private object GetExceptionInfo()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Debugger2.CurrentException is exposed at runtime via COM late
            // binding even when the PIA surface omits it. Fall back to an empty
            // result when no exception is in flight.
            try
            {
                dynamic debugger2 = dte.Debugger;
                dynamic exception = debugger2.CurrentException;
                if (exception == null) return new { current = (string)null };
                return new
                {
                    name = SafeString(exception, "Type"),
                    description = SafeString(exception, "Description"),
                    source = SafeString(exception, "Source"),
                    details = SafeString(exception, "Details")
                };
            }
            catch
            {
                return new { current = (string)null, note = "No active exception in the current frame." };
            }
        }

        private object GetExceptionSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // EnvDTE.Debugger exposes a subset of exception configuration
            // through dynamic-only properties. We surface what we can read.
            var allThrown = SafeBool(dte.Debugger, "AllExceptionsBreakWhenThrown");
            var justMyCode = SafeBool(dte.Debugger, "JustMyCode");
            return new
            {
                allExceptionsBreakWhenThrown = allThrown,
                justMyCode = justMyCode,
                note = "Per-category exception settings require Visual Studio Pro/Enterprise."
            };
        }

        // ===== Static environment / system info ======================================

        private object GetEnvironmentVariables(JObject arguments)
        {
            var filter = arguments?["filter"]?.Value<string>();
            var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var redacted = 0;
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                var name = entry.Key?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(name)) continue;
                if (!string.IsNullOrWhiteSpace(filter) &&
                    name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (IsSensitiveEnvironmentVariable(name))
                {
                    snapshot[name] = "[REDACTED]";
                    redacted++;
                }
                else
                {
                    snapshot[name] = entry.Value?.ToString() ?? string.Empty;
                }
            }
            return new
            {
                scope = arguments?["scope"]?.Value<string>() ?? "process",
                count = snapshot.Count,
                redacted,
                variables = snapshot
            };
        }

        private object GetSystemInfo()
        {
            return new
            {
                machineName = Environment.MachineName,
                userName = Environment.UserName,
                userDomainName = Environment.UserDomainName,
                osVersion = Environment.OSVersion.ToString(),
                osPlatform = Environment.OSVersion.Platform.ToString(),
                processorCount = Environment.ProcessorCount,
                systemPageSize = Environment.SystemPageSize,
                workingSet = Environment.WorkingSet,
                is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
                is64BitProcess = Environment.Is64BitProcess,
                clrVersion = Environment.Version.ToString(),
                commandLine = SafeCommandLine(),
                currentDirectory = Environment.CurrentDirectory
            };
        }

        // ===== Runtime analysis (process diagnostics) ================================

        private object GetProcessInfo(JObject arguments)
        {
            int pid = arguments?["pid"]?.Value<int>() ?? SafeInt(dte.Debugger.CurrentProcess, "ProcessID");
            if (pid <= 0) throw new ArgumentException("A positive 'pid' is required, or debug a process first.");

            using var probe = DiagnosticsProcess.GetProcessById(pid);
            return new
            {
                pid,
                name = probe.ProcessName,
                title = probe.MainWindowTitle,
                workingSet64 = probe.WorkingSet64,
                privateMemorySize64 = probe.PrivateMemorySize64,
                virtualMemorySize64 = probe.VirtualMemorySize64,
                pagedSystemMemorySize64 = probe.PagedSystemMemorySize64,
                pagedMemorySize64 = probe.PagedMemorySize64,
                nonpagedSystemMemorySize64 = probe.NonpagedSystemMemorySize64,
                handleCount = probe.HandleCount,
                basePriority = probe.BasePriority,
                threads = probe.Threads.Count,
                modules = SafeModuleCount(probe),
                startTime = SafeStartTime(probe),
                cpuTime = SafeTotalProcessorTime(probe),
                hasExited = probe.HasExited,
                responding = SafeResponding(probe)
            };
        }

        private object GetHostRuntime()
        {
            using var snapshot = DiagnosticsProcess.GetCurrentProcess();
            var runtime = new
            {
                clrVersion = Environment.Version.ToString(),
                serverGc = System.Runtime.GCSettings.IsServerGC,
                latencyMode = System.Runtime.GCSettings.LatencyMode.ToString(),
                totalMemory = GC.GetTotalMemory(false),
                gen0Collections = GC.CollectionCount(0),
                gen1Collections = GC.CollectionCount(1),
                gen2Collections = GC.CollectionCount(2),
                workingSet64 = snapshot.WorkingSet64,
                privateMemorySize64 = snapshot.PrivateMemorySize64,
                virtualMemorySize64 = snapshot.VirtualMemorySize64,
                handleCount = snapshot.HandleCount,
                threadCount = snapshot.Threads.Count,
                basePriority = snapshot.BasePriority,
                startTime = SafeStartTime(snapshot),
                cpuTime = SafeTotalProcessorTime(snapshot),
                uptimeSeconds = (DateTime.UtcNow - SafeStartTime(snapshot)).TotalSeconds
            };
            return new
            {
                pid = snapshot.Id,
                name = snapshot.ProcessName,
                title = snapshot.MainWindowTitle,
                runtime
            };
        }

        // ===== Safe accessors =========================================================

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

        private static string SafeWindowCaption(DteWindow window)
        {
            try { return window.Caption; }
            catch { return string.Empty; }
        }

        private static string SafeWindowKind(DteWindow window)
        {
            try { return window.Kind; }
            catch { return string.Empty; }
        }

        private static bool SafeWindowVisible(DteWindow window)
        {
            try { return window.Visible; }
            catch { return false; }
        }

        private static string SafeWindowDocument(DteWindow window)
        {
            try { return window.Document?.FullName ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeFileName(StackFrame frame)
        {
            try { return SafeString(frame, "FileName"); }
            catch { return string.Empty; }
        }

        private static string SafeCommandLine()
        {
            try { return Environment.CommandLine ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static bool IsSensitiveEnvironmentVariable(string name)
        {
            var normalized = (name ?? string.Empty).ToUpperInvariant();
            return normalized == "PWD" ||
                   normalized.Contains("PASSWORD") ||
                   normalized.Contains("PASSWD") ||
                   normalized.Contains("TOKEN") ||
                   normalized.Contains("SECRET") ||
                   normalized.Contains("API_KEY") ||
                   normalized.Contains("APIKEY") ||
                   normalized.Contains("PRIVATE_KEY") ||
                   normalized.Contains("CONNECTION_STRING") ||
                   normalized.Contains("CREDENTIAL") ||
                   normalized.Contains("COOKIE");
        }

        private static object SafeObject(object target, string property)
        {
            if (target == null) return null;
            try
            {
                var prop = target.GetType().GetProperty(property);
                if (prop != null) return prop.GetValue(target);

                return target.GetType().InvokeMember(
                    property,
                    BindingFlags.GetProperty,
                    binder: null,
                    target: target,
                    args: null);
            }
            catch
            {
                return null;
            }
        }

        private static string SafeString(object target, string property)
        {
            var value = SafeObject(target, property);
            if (value == null) return string.Empty;
            try { return value.ToString() ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static bool SafeBool(object target, string property)
        {
            var value = SafeObject(target, property);
            if (value is bool b) return b;
            if (value == null) return false;
            try { return Convert.ToBoolean(value); }
            catch { return false; }
        }

        private static int SafeInt(object target, string property)
        {
            var value = SafeObject(target, property);
            if (value == null) return 0;
            try { return Convert.ToInt32(value); }
            catch { return 0; }
        }

        private static int SafeCount(object value)
        {
            if (value is ICollection collection) return collection.Count;
            return SafeInt(value, "Count");
        }

        private static string SafeEnumString(object target, string property)
        {
            var value = SafeObject(target, property);
            return value?.ToString() ?? string.Empty;
        }

        private static int SafeModuleCount(DiagnosticsProcess process)
        {
            try { return process.Modules.Count; }
            catch { return 0; }
        }

        private static DateTime SafeStartTime(DiagnosticsProcess process)
        {
            try { return process.StartTime; }
            catch { return DateTime.UtcNow; }
        }

        private static TimeSpan SafeTotalProcessorTime(DiagnosticsProcess process)
        {
            try { return process.TotalProcessorTime; }
            catch { return TimeSpan.Zero; }
        }

        private static bool SafeResponding(DiagnosticsProcess process)
        {
            try { return process.Responding; }
            catch { return false; }
        }

        // ===== Assembly analysis (DLLSpy) =========================================

        private static readonly VSAgent.Services.Analysis.AssemblyAnalysisService AssemblyAnalyzer =
            new VSAgent.Services.Analysis.AssemblyAnalysisService();

        private static object AnalyzeAssembly(JObject arguments)
        {
            var path = arguments?["filePath"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("filePath is required.");
            if (!System.IO.File.Exists(path))
                throw new System.IO.FileNotFoundException("Assembly not found.", path);
            var analysis = AssemblyAnalyzer.Analyze(path);
            return analysis;
        }

        private static object GetDependencyGraph(JObject arguments)
        {
            var path = arguments?["filePath"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("filePath is required.");
            if (!System.IO.File.Exists(path))
                throw new System.IO.FileNotFoundException("Assembly not found.", path);
            var analysis = AssemblyAnalyzer.Analyze(path);
            return new
            {
                file = analysis.FileName,
                root = analysis.Graph.Root,
                nodes = analysis.Graph.Nodes,
                edges = analysis.Graph.Edges
            };
        }
    }
}
