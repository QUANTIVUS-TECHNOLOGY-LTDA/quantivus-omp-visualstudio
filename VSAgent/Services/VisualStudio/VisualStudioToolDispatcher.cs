using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VSAgent.Models;
// EnvDTE and EnvDTE80 PIAs only expose a small subset of the COM automation
// surface at compile time; the remaining members live in the COM type library
// and are reachable through runtime binding. To keep the dispatcher portable
// across VS 17/18 without dragging in extra references, ambiguous BCL types are
// aliased to their EnvDTE counterparts. BCL Process is always referenced fully
// qualified because `Process` is also a type in EnvDTE.
using Thread = EnvDTE.Thread;
using StackFrame = EnvDTE.StackFrame;
using Expression = EnvDTE.Expression;
using Expressions = EnvDTE.Expressions;
using Breakpoint = EnvDTE.Breakpoint;

namespace VSAgent.Services.VisualStudio
{
    /// <summary>
    /// Dispatches every MCP tool call that targets Visual Studio through the
    /// DTE/EnvDTE automation model. The dispatcher is intentionally exhaustive:
    /// it covers solution/build control, full debugger flow control, breakpoint
    /// lifecycle management, paused-state variable inspection (locals,
    /// arguments), thread/process/module introspection, exception analysis, the
    /// static build environment (system info, solution properties) and the live
    /// runtime environment of both the debuggee and the host process.
    ///
    /// Every DTE call is executed on the Visual Studio UI thread via
    /// <see cref="AsyncPackage.JoinableTaskFactory"/>; off-UI work uses
    /// <see cref="System.Diagnostics.Process"/> diagnostics for environment
    /// and runtime introspection so the UI thread is never blocked on
    /// process queries against the debuggee.
    /// </summary>
    internal sealed class VisualStudioToolDispatcher
    {
        private readonly AsyncPackage package;
        private readonly DTE2 dte;
        private static readonly System.Diagnostics.Process HostProcess = System.Diagnostics.Process.GetCurrentProcess();

        public VisualStudioToolDispatcher(AsyncPackage package, DTE2 dte)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            this.dte = dte ?? throw new ArgumentNullException(nameof(dte));
        }

        public async Task<VisualStudioToolResponse> ExecuteAsync(
            VisualStudioToolRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                switch (request.Tool)
                {
                    // Tool families that touch DTE must run on the UI thread.
                    case "vs_get_status":
                    case "vs_get_solution":
                    case "vs_get_solution_properties":
                    case "vs_build_solution":
                    case "vs_rebuild_solution":
                    case "vs_debug_start":
                    case "vs_debug_stop":
                    case "vs_debug_pause":
                    case "vs_debug_continue":
                    case "vs_debug_step_over":
                    case "vs_debug_step_into":
                    case "vs_debug_step_out":
                    case "vs_breakpoint_add":
                    case "vs_breakpoint_remove":
                    case "vs_breakpoint_remove_at":
                    case "vs_breakpoint_enable":
                    case "vs_breakpoint_disable":
                    case "vs_breakpoint_set_condition":
                    case "vs_breakpoint_clear_all":
                    case "vs_breakpoint_list":
                    case "vs_get_call_stack":
                    case "vs_get_call_stack_all_threads":
                    case "vs_get_locals":
                    case "vs_get_arguments":
                    case "vs_evaluate":
                    case "vs_list_threads":
                    case "vs_list_processes":
                    case "vs_get_current_thread":
                    case "vs_list_modules":
                    case "vs_get_exception_info":
                    case "vs_get_exception_settings":
                        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                        return ExecuteOnUIThread(request);

                    // Pure runtime/environment diagnostics never touch DTE and
                    // run on the thread pool to keep the UI responsive.
                    case "vs_get_environment_variables":
                    case "vs_get_system_info":
                    case "vs_get_process_info":
                    case "vs_get_host_runtime":
                        return VisualStudioToolResponse.Ok(request.Id, ExecuteRuntime(request));

                    default:
                        return VisualStudioToolResponse.Fail(request.Id, "Unknown Visual Studio tool: " + request.Tool);
                }
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
                    case "vs_get_status": return VisualStudioToolResponse.Ok(request.Id, GetStatus());
                    case "vs_get_solution": return VisualStudioToolResponse.Ok(request.Id, GetSolution());
                    case "vs_get_solution_properties": return VisualStudioToolResponse.Ok(request.Id, GetSolutionProperties());
                    case "vs_build_solution":
                        dte.Solution.SolutionBuild.Build(true);
                        return VisualStudioToolResponse.Ok(request.Id, new { started = true });
                    case "vs_rebuild_solution":
                        dte.Solution.SolutionBuild.Clean(true);
                        dte.Solution.SolutionBuild.Build(true);
                        return VisualStudioToolResponse.Ok(request.Id, new { started = true });

                    case "vs_debug_start":
                        dte.Debugger.Go(false);
                        return VisualStudioToolResponse.Ok(request.Id, new { started = true });
                    case "vs_debug_stop":
                        dte.Debugger.Stop(false);
                        return VisualStudioToolResponse.Ok(request.Id, new { stopped = true });
                    case "vs_debug_pause":
                        dte.Debugger.Break(false);
                        return VisualStudioToolResponse.Ok(request.Id, new { paused = true });
                    case "vs_debug_continue":
                        dte.Debugger.Go(false);
                        return VisualStudioToolResponse.Ok(request.Id, new { continued = true });
                    case "vs_debug_step_over":
                        dte.Debugger.StepOver(false);
                        return VisualStudioToolResponse.Ok(request.Id, new { stepped = "over" });
                    case "vs_debug_step_into":
                        dte.Debugger.StepInto(false);
                        return VisualStudioToolResponse.Ok(request.Id, new { stepped = "into" });
                    case "vs_debug_step_out":
                        dte.Debugger.StepOut(false);
                        return VisualStudioToolResponse.Ok(request.Id, new { stepped = "out" });

                    case "vs_breakpoint_add": return VisualStudioToolResponse.Ok(request.Id, AddBreakpoint(request.Arguments));
                    case "vs_breakpoint_remove": return VisualStudioToolResponse.Ok(request.Id, RemoveBreakpoint(request.Arguments));
                    case "vs_breakpoint_remove_at": return VisualStudioToolResponse.Ok(request.Id, RemoveBreakpointAt(request.Arguments));
                    case "vs_breakpoint_enable": return VisualStudioToolResponse.Ok(request.Id, SetBreakpointEnabled(request.Arguments, true));
                    case "vs_breakpoint_disable": return VisualStudioToolResponse.Ok(request.Id, SetBreakpointEnabled(request.Arguments, false));
                    case "vs_breakpoint_set_condition": return VisualStudioToolResponse.Ok(request.Id, SetBreakpointCondition(request.Arguments));
                    case "vs_breakpoint_clear_all": return VisualStudioToolResponse.Ok(request.Id, ClearAllBreakpoints());
                    case "vs_breakpoint_list": return VisualStudioToolResponse.Ok(request.Id, ListBreakpoints());

                    case "vs_get_call_stack": return VisualStudioToolResponse.Ok(request.Id, GetCallStack(false));
                    case "vs_get_call_stack_all_threads": return VisualStudioToolResponse.Ok(request.Id, GetCallStack(true));
                    case "vs_get_locals": return VisualStudioToolResponse.Ok(request.Id, GetExpressions(GetCurrentExpressions("Locals")));
                    case "vs_get_arguments": return VisualStudioToolResponse.Ok(request.Id, GetExpressions(GetCurrentExpressions("Arguments")));
                    case "vs_evaluate": return VisualStudioToolResponse.Ok(request.Id, Evaluate(request.Arguments));

                    case "vs_list_threads": return VisualStudioToolResponse.Ok(request.Id, ListThreads());
                    case "vs_list_processes": return VisualStudioToolResponse.Ok(request.Id, ListProcesses());
                    case "vs_get_current_thread": return VisualStudioToolResponse.Ok(request.Id, GetCurrentThread());

                    case "vs_list_modules": return VisualStudioToolResponse.Ok(request.Id, ListModules());
                    case "vs_get_exception_info": return VisualStudioToolResponse.Ok(request.Id, GetExceptionInfo());
                    case "vs_get_exception_settings": return VisualStudioToolResponse.Ok(request.Id, GetExceptionSettings());

                    default:
                        return VisualStudioToolResponse.Fail(request.Id, "Unknown Visual Studio tool: " + request.Tool);
                }
            }
            catch (Exception ex)
            {
                return VisualStudioToolResponse.Fail(request.Id, ex.Message);
            }
        }

        private object ExecuteRuntime(VisualStudioToolRequest request)
        {
            switch (request.Tool)
            {
                case "vs_get_environment_variables": return GetEnvironmentVariables(request.Arguments);
                case "vs_get_system_info": return GetSystemInfo();
                case "vs_get_process_info": return GetProcessInfo(request.Arguments);
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
                : dte.Debugger.CurrentMode == dbgDebugMode.dbgRunMode ? "running" : "stopped";

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

        private object GetSolution()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var projects = new List<object>();
            if (dte.Solution?.IsOpen == true)
            {
                foreach (Project project in dte.Solution.Projects)
                {
                    projects.Add(new
                    {
                        name = project.Name,
                        uniqueName = project.UniqueName,
                        fullName = SafeProjectFullName(project),
                        kind = project.Kind
                    });
                }
            }

            return new
            {
                isOpen = dte.Solution?.IsOpen == true,
                fullName = dte.Solution?.FullName ?? string.Empty,
                projects
            };
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
            var file = arguments?["file"]?.Value<string>();
            var line = arguments?["line"]?.Value<int>() ?? 0;
            var condition = arguments?["condition"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(file) || line <= 0)
                throw new ArgumentException("file and a positive line number are required.");

            Breakpoints breakpoints;
            if (!string.IsNullOrWhiteSpace(condition))
                breakpoints = dte.Debugger.Breakpoints.Add(
                    File: file,
                    Line: line,
                    Condition: condition);
            else
                breakpoints = dte.Debugger.Breakpoints.Add(File: file, Line: line);
            return new { added = breakpoints.Count > 0, file, line, condition, count = breakpoints.Count };
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
                    functionName = breakpoint.FunctionName,
                    enabled = breakpoint.Enabled,
                    condition = breakpoint.Condition,
                    hitCountTarget = breakpoint.HitCountTarget,
                    currentHits = breakpoint.CurrentHits
                });
            }
            return result;
        }

        private Breakpoint GetBreakpointAt(int index)
        {
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
            var frames = new List<object>();
            var threads = includeAllThreads ? EnumerateAllThreads() : new[] { dte.Debugger.CurrentThread };
            foreach (var thread in threads)
            {
                if (thread == null) continue;
                var threadFrames = new List<object>();
                foreach (StackFrame frame in thread.StackFrames)
                {
                    threadFrames.Add(new
                    {
                        functionName = frame.FunctionName,
                        module = frame.Module,
                        language = frame.Language,
                        returnType = frame.ReturnType,
                        fileName = SafeFileName(frame),
                        threadId = thread.ID,
                        threadLocation = thread.Location
                    });
                }
                frames.Add(new { threadId = thread.ID, threadName = thread.Name, frames = threadFrames });
            }
            return frames;
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
            var expressionText = arguments?["expression"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(expressionText))
                throw new ArgumentException("expression is required.");

            var expression = dte.Debugger.GetExpression(expressionText, true, 3000);
            return new
            {
                expression = expressionText,
                value = expression.Value,
                type = expression.Type,
                isValid = expression.IsValidValue,
                dataMembers = expression.DataMembers?.Count ?? 0
            };
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
                    stackDepth = SafeInt(thread, "StackFrames")
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
                    threadCount = SafeInt(process, "Threads"),
                    moduleCount = SafeInt(process, "Modules")
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

        private static IEnumerable<Thread> EnumerateAllThreads()
        {
            // Reserved for cross-thread enumeration when a non-current thread
            // is needed. Currently the dispatcher only enumerates the active
            // thread stack — Process2.Threads would require late-bound access.
            yield break;
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
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                var name = entry.Key?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(name)) continue;
                if (!string.IsNullOrWhiteSpace(filter) &&
                    name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                snapshot[name] = entry.Value?.ToString() ?? string.Empty;
            }
            return new
            {
                scope = arguments?["scope"]?.Value<string>() ?? "process",
                count = snapshot.Count,
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

            using var probe = Process.GetProcessById(pid);
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
            using var snapshot = Process.GetCurrentProcess();
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

        private static object SafeObject(object target, string property)
        {
            if (target == null) return null;
            try
            {
                var prop = target.GetType().GetProperty(property);
                return prop?.GetValue(target);
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

        private static string SafeEnumString(object target, string property)
        {
            var value = SafeObject(target, property);
            return value?.ToString() ?? string.Empty;
        }

        private static int SafeModuleCount(Process process)
        {
            try { return process.Modules.Count; }
            catch { return 0; }
        }

        private static DateTime SafeStartTime(Process process)
        {
            try { return process.StartTime; }
            catch { return DateTime.UtcNow; }
        }

        private static TimeSpan SafeTotalProcessorTime(Process process)
        {
            try { return process.TotalProcessorTime; }
            catch { return TimeSpan.Zero; }
        }

        private static bool SafeResponding(Process process)
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
