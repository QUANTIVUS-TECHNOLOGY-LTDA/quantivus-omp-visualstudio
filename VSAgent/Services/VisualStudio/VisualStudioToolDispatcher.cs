using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VSAgent.Models;

namespace VSAgent.Services.VisualStudio
{
    internal sealed class VisualStudioToolDispatcher
    {
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
            try
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                switch (request.Tool)
                {
                    case "vs_get_status": return VisualStudioToolResponse.Ok(request.Id, GetStatus());
                    case "vs_get_solution": return VisualStudioToolResponse.Ok(request.Id, GetSolution());
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
                    case "vs_breakpoint_list": return VisualStudioToolResponse.Ok(request.Id, ListBreakpoints());
                    case "vs_get_call_stack": return VisualStudioToolResponse.Ok(request.Id, GetCallStack());
                    case "vs_evaluate": return VisualStudioToolResponse.Ok(request.Id, Evaluate(request.Arguments));
                    default: return VisualStudioToolResponse.Fail(request.Id, "Unknown Visual Studio tool: " + request.Tool);
                }
            }
            catch (Exception ex)
            {
                return VisualStudioToolResponse.Fail(request.Id, ex.Message);
            }
        }

        private DebuggerSnapshot GetStatus()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var mode = dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode
                ? "paused"
                : dte.Debugger.CurrentMode == dbgDebugMode.dbgRunMode ? "running" : "stopped";

            var startupProjects = dte.Solution?.SolutionBuild?.StartupProjects as Array;
            return new DebuggerSnapshot
            {
                Mode = mode,
                Solution = dte.Solution?.FullName ?? string.Empty,
                StartupProjects = startupProjects == null
                    ? string.Empty
                    : string.Join(", ", startupProjects.Cast<object>().Select(value => value?.ToString())),
                IsSolutionOpen = dte.Solution?.IsOpen == true
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

        private object AddBreakpoint(JObject arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var file = arguments?["file"]?.Value<string>();
            var line = arguments?["line"]?.Value<int>() ?? 0;
            if (string.IsNullOrWhiteSpace(file) || line <= 0)
                throw new ArgumentException("file and a positive line number are required.");

            var breakpoints = dte.Debugger.Breakpoints.Add(File: file, Line: line);
            return new { added = breakpoints.Count > 0, file, line, count = breakpoints.Count };
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
                    breakpoint.FunctionName,
                    breakpoint.Enabled,
                    breakpoint.Condition,
                    breakpoint.HitCountType,
                    breakpoint.HitCountTarget
                });
            }
            return result;
        }

        private object GetCallStack()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var frames = new List<object>();
            var thread = dte.Debugger.CurrentThread;
            if (thread == null) return frames;

            foreach (StackFrame frame in thread.StackFrames)
            {
                frames.Add(new
                {
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

        private static string SafeProjectFullName(Project project)
        {
            try { return project.FullName; }
            catch { return string.Empty; }
        }
    }
}
