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
                ["version"] = "0.1.0"
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
        Tool("vs_get_status", "Get the current Visual Studio solution and debugger state, including last break reason, current process/thread, breakpoint count and debugged-process count.", EmptySchema()),
        Tool("vs_get_solution", "List the open solution and its projects.", EmptySchema()),
        Tool("vs_get_solution_properties", "Read the active solution configuration, available configurations/platforms, build state and last-build result.", EmptySchema()),
        Tool("vs_build_solution", "Build the open Visual Studio solution and wait for completion.", EmptySchema()),
        Tool("vs_rebuild_solution", "Clean and rebuild the open Visual Studio solution.", EmptySchema()),
        Tool("vs_debug_start", "Start or continue debugging the configured startup project.", EmptySchema()),
        Tool("vs_debug_stop", "Stop the current Visual Studio debugging session.", EmptySchema()),
        Tool("vs_debug_pause", "Pause the current Visual Studio debugging session.", EmptySchema()),
        Tool("vs_debug_continue", "Continue the paused Visual Studio debugging session.", EmptySchema()),
        Tool("vs_debug_step_over", "Execute Step Over in the Visual Studio debugger.", EmptySchema()),
        Tool("vs_debug_step_into", "Execute Step Into in the Visual Studio debugger.", EmptySchema()),
        Tool("vs_debug_step_out", "Execute Step Out in the Visual Studio debugger.", EmptySchema()),
        Tool("vs_breakpoint_add", "Add a source breakpoint in Visual Studio. Optionally accepts a 'condition' expression.", ObjectSchema(
            required: ["file", "line"],
            properties: new JsonObject
            {
                ["file"] = StringProperty("Absolute source file path."),
                ["line"] = IntegerProperty("One-based source line number.", 1),
                ["condition"] = StringProperty("Optional breakpoint condition expression.")
            })),
        Tool("vs_breakpoint_remove", "Remove a breakpoint by its 1-based index from vs_breakpoint_list.", ObjectSchema(
            required: ["index"],
            properties: new JsonObject
            {
                ["index"] = IntegerProperty("1-based breakpoint index.", 1)
            })),
        Tool("vs_breakpoint_remove_at", "Remove all breakpoints that match a file and line.", ObjectSchema(
            required: ["file", "line"],
            properties: new JsonObject
            {
                ["file"] = StringProperty("Absolute source file path."),
                ["line"] = IntegerProperty("One-based source line number.", 1)
            })),
        Tool("vs_breakpoint_enable", "Enable a breakpoint by index.", ObjectSchema(
            required: ["index"],
            properties: new JsonObject
            {
                ["index"] = IntegerProperty("1-based breakpoint index.", 1)
            })),
        Tool("vs_breakpoint_disable", "Disable a breakpoint by index.", ObjectSchema(
            required: ["index"],
            properties: new JsonObject
            {
                ["index"] = IntegerProperty("1-based breakpoint index.", 1)
            })),
        Tool("vs_breakpoint_set_condition", "Replace a breakpoint with a copy that has a new condition expression.", ObjectSchema(
            required: ["index", "condition"],
            properties: new JsonObject
            {
                ["index"] = IntegerProperty("1-based breakpoint index.", 1),
                ["condition"] = StringProperty("Conditional expression evaluated by the debugger.")
            })),
        Tool("vs_breakpoint_clear_all", "Remove every breakpoint in the solution.", EmptySchema()),
        Tool("vs_breakpoint_list", "List all Visual Studio breakpoints with file, line, condition, hit count and enabled state.", EmptySchema()),
        Tool("vs_get_call_stack", "Read the current thread call stack while the debugger is paused.", EmptySchema()),
        Tool("vs_get_call_stack_all_threads", "Read the call stack grouped by every debugged thread.", EmptySchema()),
        Tool("vs_get_locals", "List local variables for the current stack frame while the debugger is paused.", EmptySchema()),
        Tool("vs_get_arguments", "List method arguments for the current stack frame while the debugger is paused.", EmptySchema()),
        Tool("vs_evaluate", "Evaluate an expression in the current debugger stack frame.", ObjectSchema(
            required: ["expression"],
            properties: new JsonObject
            {
                ["expression"] = StringProperty("Expression to evaluate in the current stack frame.")
            })),
        Tool("vs_list_threads", "List threads of the current debugged process with id, name, priority, location and stack depth.", EmptySchema()),
        Tool("vs_list_processes", "List every debugged process with name, pid, user, thread count and module count.", EmptySchema()),
        Tool("vs_get_current_thread", "Inspect the current thread: id, name, location and full call stack.", EmptySchema()),
        Tool("vs_list_modules", "List loaded modules for the current debugged process with name, path, version, optimization state and address.", EmptySchema()),
        Tool("vs_get_exception_info", "Return the type, description, source and details of the active exception (if any).", EmptySchema()),
        Tool("vs_get_exception_settings", "Read the debugger's exception configuration flags.", EmptySchema()),
        Tool("vs_get_environment_variables", "Read the host process environment variables. Accepts optional 'filter' (substring) and 'scope' arguments.", ObjectSchema(
            required: [],
            properties: new JsonObject
            {
                ["filter"] = StringProperty("Optional case-insensitive substring filter."),
                ["scope"] = StringProperty("Optional scope label echoed back in the response.")
            })),
        Tool("vs_get_system_info", "Read static system information (machine, user, OS, processor count, page size, working set, CLR version).", EmptySchema()),
        Tool("vs_get_process_info", "Read runtime diagnostics for an arbitrary OS process. Defaults to the currently debugged process when no pid is supplied.", ObjectSchema(
            required: [],
            properties: new JsonObject
            {
                ["pid"] = IntegerProperty("Process id. Defaults to the debugged process when omitted.", 1)
            })),
        Tool("vs_get_host_runtime", "Read runtime diagnostics for the Visual Studio host process: GC mode, working set, private bytes, thread count, handle count, CPU time.", EmptySchema()),
        Tool("vs_analyze_assembly", "Inspect a .dll/.exe with the DLLSpy engine. Returns types, members, exports, assembly references and a force-directed dependency graph. The 'includeMembers' flag defaults to true and controls whether method/property/field bodies are returned.", ObjectSchema(
            required: ["filePath"],
            properties: new JsonObject
            {
                ["filePath"] = StringProperty("Absolute path to the .dll or .exe to inspect."),
                ["includeMembers"] = StringProperty("Boolean flag rendered as a string ('true' / 'false'). Defaults to true.")
            })),
        Tool("vs_dependency_graph", "Return only the dependency graph (nodes + edges) for the given assembly. Lighter than vs_analyze_assembly for very large binaries.", ObjectSchema(
            required: ["filePath"],
            properties: new JsonObject
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
            ["readOnlyHint"] = IsReadOnlyTool(name),
            ["destructiveHint"] = IsDestructiveTool(name)
        }
    };

    private static bool IsReadOnlyTool(string name) =>
        name.StartsWith("vs_get_", StringComparison.Ordinal) ||
        name.EndsWith("_list", StringComparison.Ordinal) ||
        name is "vs_list_threads" or "vs_list_processes" or "vs_list_modules";

    private static bool IsDestructiveTool(string name) =>
        name is "vs_debug_stop" or "vs_rebuild_solution" or "vs_breakpoint_clear_all"
            or "vs_breakpoint_remove" or "vs_breakpoint_remove_at" or "vs_breakpoint_set_condition";

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

    private static JsonObject IntegerProperty(string description, int minimum) => new()
    {
        ["type"] = "integer",
        ["description"] = description,
        ["minimum"] = minimum
    };
}
