using System.Text.Json;
using System.Text.Json.Nodes;

namespace VSAgent.McpHost;

internal sealed class McpStdioServer
{
    private readonly VisualStudioPipeClient pipeClient;
    private readonly TextReader input;
    private readonly TextWriter output;
    private readonly TextWriter error;
    private readonly SemaphoreSlim outputLock = new(1, 1);

    public McpStdioServer(
        VisualStudioPipeClient pipeClient,
        TextReader input,
        TextWriter output,
        TextWriter error)
    {
        this.pipeClient = pipeClient;
        this.input = input;
        this.output = output;
        this.error = error;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) return;

            JsonObject? message;
            try
            {
                message = JsonNode.Parse(line) as JsonObject;
            }
            catch (JsonException ex)
            {
                await error.WriteLineAsync("Invalid MCP JSON: " + ex.Message).ConfigureAwait(false);
                continue;
            }

            if (message is null) continue;
            var method = message["method"]?.GetValue<string>();
            var id = message["id"]?.DeepClone();
            if (string.IsNullOrWhiteSpace(method)) continue;

            try
            {
                switch (method)
                {
                    case "initialize":
                        await RespondAsync(id, Initialize(message["params"] as JsonObject), cancellationToken).ConfigureAwait(false);
                        break;
                    case "notifications/initialized":
                        break;
                    case "ping":
                        await RespondAsync(id, new JsonObject(), cancellationToken).ConfigureAwait(false);
                        break;
                    case "tools/list":
                        await RespondAsync(id, new JsonObject { ["tools"] = CreateTools() }, cancellationToken).ConfigureAwait(false);
                        break;
                    case "tools/call":
                        await RespondAsync(id, await CallToolAsync(message["params"] as JsonObject, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        if (id is not null)
                            await ErrorAsync(id, -32601, "Method not found: " + method, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                await error.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
                if (id is not null)
                    await ErrorAsync(id, -32000, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static JsonObject Initialize(JsonObject? parameters)
    {
        var requestedVersion = parameters?["protocolVersion"]?.GetValue<string>();
        return new JsonObject
        {
            ["protocolVersion"] = requestedVersion ?? "2025-03-26",
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "quantivus-visual-studio",
                ["version"] = "0.2.0"
            }
        };
    }

    private async Task<JsonObject> CallToolAsync(JsonObject? parameters, CancellationToken cancellationToken)
    {
        var name = parameters?["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name)) return ToolError("Tool name is required.");

        var arguments = parameters?["arguments"] as JsonObject ?? new JsonObject();
        var response = await pipeClient.CallAsync(name, arguments, cancellationToken).ConfigureAwait(false);
        if (!response.Success) return ToolError(response.Error ?? "Visual Studio tool failed.");

        var text = response.Result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? "null"
            : response.Result.GetRawText();

        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = text }
            },
            ["structuredContent"] = JsonNode.Parse(text),
            ["isError"] = false
        };
    }

    private static JsonObject ToolError(string message) => new()
    {
        ["content"] = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = message }
        },
        ["isError"] = true
    };

    private async Task RespondAsync(JsonNode? id, JsonNode result, CancellationToken cancellationToken)
    {
        if (id is null) return;
        await WriteAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task ErrorAsync(JsonNode id, int code, string message, CancellationToken cancellationToken) =>
        WriteAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        }, cancellationToken);

    private async Task WriteAsync(JsonObject message, CancellationToken cancellationToken)
    {
        await outputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await output.WriteLineAsync(message.ToJsonString(new JsonSerializerOptions { WriteIndented = false })).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            outputLock.Release();
        }
    }

    private static JsonArray CreateTools() => new(
        Tool("vs_get_status", "Get solution, build, active-document and debugger status.", EmptySchema()),
        Tool("vs_execute_command", "Execute any registered Visual Studio command by canonical command name. This exposes the full IDE command surface and may change files or IDE state.", ObjectSchema(
            ["name"],
            new JsonObject
            {
                ["name"] = StringProperty("Canonical Visual Studio command, for example File.SaveAll, Debug.Restart or TestExplorer.RunAllTests."),
                ["arguments"] = StringProperty("Optional command arguments.")
            })),
        Tool("vs_command_list", "List registered Visual Studio commands, optionally filtered by name.", ObjectSchema(
            [],
            new JsonObject
            {
                ["filter"] = StringProperty("Optional case-insensitive command-name filter."),
                ["limit"] = IntegerProperty("Maximum commands to return.", 1, 500)
            })),
        Tool("vs_window_list", "List Visual Studio document and tool windows.", EmptySchema()),
        Tool("vs_window_activate", "Show and activate a Visual Studio window by caption and/or kind.", ObjectSchema(
            [],
            new JsonObject
            {
                ["caption"] = StringProperty("Exact window caption."),
                ["kind"] = StringProperty("Exact Visual Studio window kind GUID.")
            })),

        Tool("vs_get_solution", "Inspect the open solution, active configuration and nested projects.", EmptySchema()),
        Tool("vs_get_solution_properties", "Read active and available solution configurations, build state and the last build result.", EmptySchema()),
        Tool("vs_solution_open", "Open a solution or supported workspace file in Visual Studio.", ObjectSchema(
            ["path"], new JsonObject { ["path"] = StringProperty("Absolute path to the solution or workspace file.") })),
        Tool("vs_solution_close", "Close the current solution.", ObjectSchema(
            [], new JsonObject { ["save"] = BooleanProperty("Save pending changes before closing. Defaults to true.") })),
        Tool("vs_solution_configuration_list", "List solution configurations and platforms.", EmptySchema()),
        Tool("vs_solution_configuration_activate", "Activate a solution configuration and optional platform.", ObjectSchema(
            ["name"],
            new JsonObject
            {
                ["name"] = StringProperty("Configuration name, for example Debug or Release."),
                ["platform"] = StringProperty("Optional platform, for example Any CPU or x64.")
            })),
        Tool("vs_project_set_startup", "Set the startup project by project name, unique name or path.", ObjectSchema(
            ["project"], new JsonObject { ["project"] = StringProperty("Project name, unique name or project-file path.") })),
        Tool("vs_build_solution", "Build the open solution and wait for completion.", BuildSchema()),
        Tool("vs_rebuild_solution", "Clean and rebuild the open solution and wait for completion.", BuildSchema()),
        Tool("vs_clean_solution", "Clean the open solution and wait for completion.", EmptySchema()),
        Tool("vs_build_project", "Build one project and wait for completion.", ObjectSchema(
            ["project"],
            new JsonObject
            {
                ["project"] = StringProperty("Project name, unique name or project-file path."),
                ["configuration"] = StringProperty("Optional solution configuration; defaults to the active configuration.")
            })),
        Tool("vs_build_cancel", "Cancel the current Visual Studio build.", EmptySchema()),
        Tool("vs_get_build_errors", "Read Error List entries with file, line, column and project metadata.", ObjectSchema(
            [],
            new JsonObject
            {
                ["includeWarnings"] = BooleanProperty("Include warnings and messages. Defaults to true."),
                ["limit"] = IntegerProperty("Maximum entries to return.", 1, 5000)
            })),

        Tool("vs_document_list", "List all open Visual Studio documents.", EmptySchema()),
        Tool("vs_document_get_active", "Get active-document metadata and optionally its text.", ObjectSchema(
            [],
            new JsonObject
            {
                ["includeText"] = BooleanProperty("Include document text. Defaults to false."),
                ["maxCharacters"] = IntegerProperty("Maximum text characters returned.", 1, 2000000)
            })),
        Tool("vs_document_open", "Open a file in the Visual Studio editor and optionally navigate to a position.", ObjectSchema(
            ["path"],
            new JsonObject
            {
                ["path"] = StringProperty("Absolute file path."),
                ["line"] = IntegerProperty("Optional one-based line.", 1, null),
                ["column"] = IntegerProperty("Optional one-based column.", 1, null)
            })),
        Tool("vs_document_get_text", "Read text from an open document or the active document.", ObjectSchema(
            [],
            new JsonObject
            {
                ["path"] = StringProperty("Optional open-document path or name."),
                ["maxCharacters"] = IntegerProperty("Maximum text characters returned.", 1, 2000000)
            })),
        Tool("vs_document_replace_text", "Replace the complete contents of an open text document.", ObjectSchema(
            ["text"],
            new JsonObject
            {
                ["path"] = StringProperty("Optional open-document path or name; defaults to the active document."),
                ["text"] = StringProperty("Complete replacement text.")
            })),
        Tool("vs_document_save", "Save an open document or the active document.", ObjectSchema(
            [], new JsonObject { ["path"] = StringProperty("Optional open-document path or name.") })),
        Tool("vs_document_save_all", "Save all open Visual Studio documents.", EmptySchema()),
        Tool("vs_document_close", "Close an open document or the active document.", ObjectSchema(
            [],
            new JsonObject
            {
                ["path"] = StringProperty("Optional open-document path or name."),
                ["save"] = BooleanProperty("Save before closing. Defaults to true.")
            })),
        Tool("vs_editor_get_selection", "Read the active editor selection and caret position.", EmptySchema()),
        Tool("vs_editor_replace_selection", "Replace the active text selection or insert at the caret.", ObjectSchema(
            ["text"], new JsonObject { ["text"] = StringProperty("Replacement or inserted text.") })),
        Tool("vs_editor_navigate", "Navigate the active editor, optionally opening a file first.", ObjectSchema(
            [],
            new JsonObject
            {
                ["path"] = StringProperty("Optional file path."),
                ["line"] = IntegerProperty("One-based line. Defaults to 1.", 1, null),
                ["column"] = IntegerProperty("One-based column. Defaults to 1.", 1, null),
                ["selectLine"] = BooleanProperty("Select the destination line.")
            })),

        Tool("vs_debug_start", "Start or continue debugging the configured startup project.", ProjectOptionalSchema()),
        Tool("vs_debug_start_without_debugging", "Run the startup project without attaching the debugger.", ProjectOptionalSchema()),
        Tool("vs_debug_stop", "Stop the current debugging session.", EmptySchema()),
        Tool("vs_debug_restart", "Restart the current debugging session.", EmptySchema()),
        Tool("vs_debug_pause", "Break all debugged processes.", EmptySchema()),
        Tool("vs_debug_continue", "Continue a paused debugging session.", EmptySchema()),
        Tool("vs_debug_step_over", "Execute Step Over.", EmptySchema()),
        Tool("vs_debug_step_into", "Execute Step Into.", EmptySchema()),
        Tool("vs_debug_step_out", "Execute Step Out.", EmptySchema()),
        Tool("vs_debug_run_to_cursor", "Run the debugger to the current editor caret.", EmptySchema()),
        Tool("vs_debug_set_next_statement", "Set the next statement to the current editor caret.", EmptySchema()),
        Tool("vs_debug_detach_all", "Detach the debugger from all debugged processes.", EmptySchema()),
        Tool("vs_debug_terminate_all", "Terminate all debugged processes.", EmptySchema()),
        Tool("vs_debug_process_list", "List processes currently debugged by Visual Studio.", EmptySchema()),
        Tool("vs_debug_thread_list", "List threads in the current debug program.", EmptySchema()),
        Tool("vs_get_call_stack", "Read the current thread call stack while paused.", EmptySchema()),
        Tool("vs_get_locals", "Read arguments and locals from the current stack frame.", EmptySchema()),
        Tool("vs_evaluate", "Evaluate an expression in the current debugger stack frame.", ObjectSchema(
            ["expression"],
            new JsonObject
            {
                ["expression"] = StringProperty("Expression to evaluate."),
                ["timeoutMilliseconds"] = IntegerProperty("Evaluation timeout.", 100, 60000),
                ["treatAsStatement"] = BooleanProperty("Treat the expression as a statement. Defaults to true.")
            })),

        Tool("vs_breakpoint_add", "Add a source breakpoint, optionally with a condition.", ObjectSchema(
            ["file", "line"],
            new JsonObject
            {
                ["file"] = StringProperty("Absolute source file path."),
                ["line"] = IntegerProperty("One-based source line.", 1, null),
                ["column"] = IntegerProperty("One-based source column. Defaults to 1.", 1, null),
                ["condition"] = StringProperty("Optional break condition evaluated when true.")
            })),
        Tool("vs_breakpoint_list", "List Visual Studio breakpoints with stable 1-based indexes and metadata.", EmptySchema()),
        Tool("vs_breakpoint_remove", "Remove a breakpoint by index, matching breakpoints, or all breakpoints.", BreakpointSelectorSchema(includeEnabled: false)),
        Tool("vs_breakpoint_remove_at", "Remove all breakpoints that match a file and line.", ObjectSchema(
            ["file", "line"],
            new JsonObject
            {
                ["file"] = StringProperty("Absolute source file path."),
                ["line"] = IntegerProperty("One-based source line.", 1, null)
            })),
        Tool("vs_breakpoint_set_enabled", "Enable or disable breakpoints by index, selector, or all=true.", BreakpointSelectorSchema(includeEnabled: true)),
        Tool("vs_breakpoint_enable", "Enable a breakpoint by index.", ObjectSchema(
            ["index"],
            new JsonObject
            {
                ["index"] = IntegerProperty("1-based breakpoint index.", 1, null)
            })),
        Tool("vs_breakpoint_disable", "Disable a breakpoint by index.", ObjectSchema(
            ["index"],
            new JsonObject
            {
                ["index"] = IntegerProperty("1-based breakpoint index.", 1, null)
            })),
        Tool("vs_breakpoint_set_condition", "Replace a breakpoint with a copy that has a new condition expression.", ObjectSchema(
            ["index", "condition"],
            new JsonObject
            {
                ["index"] = IntegerProperty("1-based breakpoint index.", 1, null),
                ["condition"] = StringProperty("Conditional expression evaluated by the debugger.")
            })),
        Tool("vs_breakpoint_clear_all", "Remove every breakpoint in the solution.", EmptySchema()),
        Tool("vs_get_call_stack_all_threads", "Read the call stack grouped by every debugged thread.", EmptySchema()),
        Tool("vs_get_arguments", "List method arguments for the current stack frame while the debugger is paused.", EmptySchema()),
        Tool("vs_list_threads", "List threads of the current debugged process with id, name, priority, location and stack depth.", EmptySchema()),
        Tool("vs_list_processes", "List every debugged process with name, pid, user, thread count and module count.", EmptySchema()),
        Tool("vs_get_current_thread", "Inspect the current thread: id, name, location and full call stack.", EmptySchema()),
        Tool("vs_list_modules", "List loaded modules for the current debugged process with name, path, version, optimization state and address.", EmptySchema()),
        Tool("vs_get_exception_info", "Return the type, description, source and details of the active exception (if any).", EmptySchema()),
        Tool("vs_get_exception_settings", "Read the debugger's exception configuration flags.", EmptySchema()),
        Tool("vs_get_environment_variables", "Read host process environment variables with sensitive values redacted.", ObjectSchema(
            [],
            new JsonObject
            {
                ["filter"] = StringProperty("Optional case-insensitive substring filter."),
                ["scope"] = StringProperty("Optional scope label echoed back in the response.")
            })),
        Tool("vs_get_system_info", "Read static system information (machine, user, OS, processor count, page size, working set, CLR version).", EmptySchema()),
        Tool("vs_get_process_info", "Read runtime diagnostics for an arbitrary OS process. Defaults to the currently debugged process when no pid is supplied.", ObjectSchema(
            [],
            new JsonObject
            {
                ["pid"] = IntegerProperty("Process id. Defaults to the debugged process when omitted.", 1, null)
            })),
        Tool("vs_get_host_runtime", "Read runtime diagnostics for the Visual Studio host process: GC mode, working set, private bytes, thread count, handle count, CPU time.", EmptySchema()),
        Tool("vs_analyze_assembly", "Inspect a .dll/.exe with the DLLSpy engine and return types, members, exports, references and a dependency graph.", ObjectSchema(
            ["filePath"],
            new JsonObject
            {
                ["filePath"] = StringProperty("Absolute path to the .dll or .exe to inspect.")
            })),
        Tool("vs_dependency_graph", "Return only the dependency graph (nodes + edges) for the given assembly. Lighter than vs_analyze_assembly for very large binaries.", ObjectSchema(
            ["filePath"],
            new JsonObject
            {
                ["filePath"] = StringProperty("Absolute path to the .dll or .exe to inspect.")
            }))
    );

    private static JsonObject Tool(string name, string description, JsonObject inputSchema) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = inputSchema,
        ["annotations"] = new JsonObject
        {
            ["readOnlyHint"] = IsReadOnly(name),
            ["destructiveHint"] = IsDestructive(name),
            ["idempotentHint"] = IsReadOnly(name)
        }
    };

    private static bool IsReadOnly(string name) =>
        name.StartsWith("vs_get_", StringComparison.Ordinal) ||
        name.EndsWith("_list", StringComparison.Ordinal) ||
        name is "vs_document_get_active"
            or "vs_document_get_text"
            or "vs_editor_get_selection"
            or "vs_evaluate"
            or "vs_list_threads"
            or "vs_list_processes"
            or "vs_list_modules"
            or "vs_analyze_assembly"
            or "vs_dependency_graph";

    private static bool IsDestructive(string name) =>
        name is "vs_execute_command"
            or "vs_solution_close"
            or "vs_solution_open"
            or "vs_rebuild_solution"
            or "vs_clean_solution"
            or "vs_build_cancel"
            or "vs_document_replace_text"
            or "vs_document_close"
            or "vs_editor_replace_selection"
            or "vs_debug_stop"
            or "vs_debug_restart"
            or "vs_debug_set_next_statement"
            or "vs_debug_detach_all"
            or "vs_debug_terminate_all"
            or "vs_breakpoint_remove"
            or "vs_breakpoint_remove_at"
            or "vs_breakpoint_clear_all"
            or "vs_breakpoint_set_condition";

    private static JsonObject BuildSchema() => ObjectSchema(
        [],
        new JsonObject
        {
            ["configuration"] = StringProperty("Optional configuration name."),
            ["platform"] = StringProperty("Optional platform name.")
        });

    private static JsonObject ProjectOptionalSchema() => ObjectSchema(
        [],
        new JsonObject { ["project"] = StringProperty("Optional startup project name, unique name or project-file path.") });

    private static JsonObject BreakpointSelectorSchema(bool includeEnabled)
    {
        var properties = new JsonObject
        {
            ["index"] = IntegerProperty("Optional 1-based breakpoint index.", 1, null),
            ["all"] = BooleanProperty("Apply to all breakpoints."),
            ["file"] = StringProperty("Optional source file path or file name."),
            ["line"] = IntegerProperty("Optional one-based source line.", 1, null)
        };
        if (includeEnabled)
            properties["enabled"] = BooleanProperty("Desired enabled state. Defaults to true.");

        return ObjectSchema([], properties);
    }

    private static JsonObject EmptySchema() => ObjectSchema([], new JsonObject());

    private static JsonObject ObjectSchema(string[] required, JsonObject properties) => new()
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["required"] = new JsonArray(required.Select(value => JsonValue.Create(value)).ToArray()),
        ["additionalProperties"] = false
    };

    private static JsonObject StringProperty(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description
    };

    private static JsonObject BooleanProperty(string description) => new()
    {
        ["type"] = "boolean",
        ["description"] = description
    };

    private static JsonObject IntegerProperty(string description, int? minimum, int? maximum)
    {
        var property = new JsonObject
        {
            ["type"] = "integer",
            ["description"] = description
        };
        if (minimum.HasValue) property["minimum"] = minimum.Value;
        if (maximum.HasValue) property["maximum"] = maximum.Value;
        return property;
    }
}
