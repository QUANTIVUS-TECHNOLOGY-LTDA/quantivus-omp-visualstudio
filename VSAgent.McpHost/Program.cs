using VSAgent.McpHost;

var pipeName = GetArgument(args, "--pipe");
if (string.IsNullOrWhiteSpace(pipeName))
{
    Console.Error.WriteLine("Usage: VSAgent.McpHost --pipe <pipe-name>");
    return 2;
}

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

try
{
    var pipeClient = new VisualStudioPipeClient(pipeName);
    var server = new McpStdioServer(pipeClient, Console.In, Console.Out, Console.Error);
    await server.RunAsync(shutdown.Token);
    return 0;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

static string? GetArgument(string[] arguments, string name)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
        {
            return arguments[index + 1];
        }
    }
    return null;
}
