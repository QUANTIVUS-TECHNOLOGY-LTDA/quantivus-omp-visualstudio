# Quantivus OMP for Visual Studio

An open-source Visual Studio extension that runs **oh-my-pi** as an ACP agent and exposes Visual Studio build and debugger operations through a local MCP server.

> Status: alpha. The architecture and first functional implementation are present; signed releases still require an interactive Visual Studio experimental-instance validation.

## Implemented

- VSIX for Visual Studio 2022 and Visual Studio 2026.
- `omp acp` lifecycle managed by the extension.
- ACP initialization, sessions, prompts, streaming updates and permission responses.
- Separate .NET 8 MCP host communicating with the VSIX through a per-process named pipe.
- Context-menu commands routed through oh-my-pi.
- MCP tools for solution inspection, build, debugging, breakpoints, call stacks and expression evaluation.

## Architecture

```text
Visual Studio
  VSAgent VSIX (.NET Framework 4.8)
    ├─ Tool window and commands
    ├─ oh-my-pi ACP client
    ├─ DTE2 / EnvDTE debugger dispatcher
    └─ Named-pipe server
             │
             ▼
  VSAgent.McpHost (.NET 8, STDIO MCP server)
             │
             ▼
  omp acp
```

No TCP debugger listener is opened.

## Build

```powershell
msbuild VSAgent.sln /restore /p:Configuration=Release
```

The VSIX build publishes `VSAgent.McpHost` into `Runtime/McpHost`. `omp.exe` is not committed and is included only when deliberately supplied under `VSAgent/Runtime/omp.exe`.

## Licensing

Quantivus source code is Apache-2.0 licensed. oh-my-pi is a separate MIT-licensed project and is not included by default. See `THIRD_PARTY_NOTICES.md`.
