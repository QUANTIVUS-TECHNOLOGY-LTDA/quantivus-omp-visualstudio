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
        Tool("vs_get_status", "Get the current Visual Studio solution and debugger state.", EmptySchema()),
        Tool("vs_get_solution", "List the open solution and its projects.", EmptySchema()),
        Tool("vs_build_solution", "Build the open Visual Studio solution and wait for completion.", EmptySchema()),
        Tool("vs_rebuild_solution", "Clean and rebuild the open Visual Studio solution.", EmptySchema()),
        Tool("vs_debug_start", "Start or continue debugging the configured startup project.", EmptySchema()),
        Tool("vs_debug_stop", "Stop the current Visual Studio debugging session.", EmptySchema()),
        Tool("vs_debug_pause", "Pause the current Visual Studio debugging session.", EmptySchema()),
        Tool("vs_debug_continue", "Continue the paused Visual Studio debugging session.", EmptySchema()),
        Tool("vs_debug_step_over", "Execute Step Over in the Visual Studio debugger.", EmptySchema()),
        Tool("vs_debug_step_into", "Execute Step Into in the Visual Studio debugger.", EmptySchema()),
        Tool("vs_debug_step_out", "Execute Step Out in the Visual Studio debugger.", EmptySchema()),
        Tool("vs_breakpoint_add", "Add a source breakpoint in Visual Studio.", ObjectSchema(
            required: ["file", "line"],
            properties: new JsonObject
            {
                ["file"] = StringProperty("Absolute source file path."),
                ["line"] = IntegerProperty("One-based source line number.", 1)
            })),
        Tool("vs_breakpoint_list", "List all Visual Studio breakpoints.", EmptySchema()),
        Tool("vs_get_call_stack", "Read the current thread call stack while the debugger is paused.", EmptySchema()),
        Tool("vs_evaluate", "Evaluate an expression in the current debugger stack frame.", ObjectSchema(
            required: ["expression"],
            properties: new JsonObject
            {
                ["expression"] = StringProperty("Expression to evaluate in the current stack frame.")
            }))
    );

    private static JsonObject Tool(string name, string description, JsonObject inputSchema) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = inputSchema,
        ["annotations"] = new JsonObject
        {
            ["readOnlyHint"] = name.StartsWith("vs_get_", StringComparison.Ordinal) || name.EndsWith("_list", StringComparison.Ordinal),
            ["destructiveHint"] = name is "vs_debug_stop" or "vs_rebuild_solution"
        }
    };

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
