# Architecture

Quantivus OMP embeds an agent experience in Visual Studio without embedding the agent runtime inside `devenv.exe`. The VSIX owns Visual Studio automation and user interaction; oh-my-pi owns model access, planning, sessions and orchestration.

## Components

### VSIX

The .NET Framework 4.8 extension provides the tool window, commands, `AgentHostService`, an ACP client, a per-process named-pipe server and the DTE/debugger dispatcher.

### MCP host

The .NET 8 console program is an MCP server over STDIO. oh-my-pi starts it for the ACP session. Each MCP tool call is converted into a named-pipe request and returned as MCP content and structured content.

### oh-my-pi

`omp acp` is started outside Visual Studio. The extension initializes one ACP session for the current solution and supplies the MCP host command and unique pipe name in `session/new`.

## Startup

```text
Visual Studio loads VSAgentPackage
  -> obtain DTE2
  -> create unique pipe name
  -> start VisualStudioPipeServer
  -> locate omp.exe and MCP host
  -> start omp acp
  -> ACP initialize
  -> ACP session/new
```

## Threading and isolation

DTE and debugger COM objects are accessed only on the Visual Studio UI thread. ACP and pipe I/O stay off the UI thread. `devenv.exe`, `VSAgent.McpHost.exe` and `omp.exe` remain separate processes.

## Safety

MCP annotations are advisory. The alpha permission handler may allow read/list/get/status requests and rejects other requests by default. Production distribution requires native permission dialogs, current-user pipe ACLs, audit logging and signed releases.
