using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VSAgent.McpHost;

internal sealed class VisualStudioPipeClient
{
    private readonly string pipeName;

    public VisualStudioPipeClient(string pipeName)
    {
        this.pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
    }

    public async Task<PipeToolResponse> CallAsync(
        string tool,
        JsonObject? arguments,
        CancellationToken cancellationToken)
    {
        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await pipe.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true)
        {
            AutoFlush = true
        };

        var request = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("N"),
            ["tool"] = tool,
            ["arguments"] = arguments ?? new JsonObject()
        };

        await writer.WriteLineAsync(request.ToJsonString()).ConfigureAwait(false);
        var responseLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            throw new IOException("Visual Studio closed the named pipe without a response.");
        }

        return JsonSerializer.Deserialize<PipeToolResponse>(responseLine,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException("Invalid response from Visual Studio.");
    }
}

internal sealed class PipeToolResponse
{
    public string Id { get; set; } = string.Empty;
    public bool Success { get; set; }
    public JsonElement Result { get; set; }
    public string? Error { get; set; }
}
