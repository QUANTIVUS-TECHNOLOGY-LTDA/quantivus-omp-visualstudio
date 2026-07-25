# Architecture

## Process model

Quantivus OMP uses three cooperating processes/components:

1. **VSAgent VSIX** (`net48`) runs inside Visual Studio. It owns the WPF workbench, provider settings, local persistence, ACP client and Visual Studio automation dispatcher.
2. **VSAgent.McpHost** (`net8.0`) is a separate STDIO MCP server packaged with the VSIX. It forwards tool requests over a per-Visual-Studio-process named pipe.
3. **oh-my-pi** runs as `omp acp`. The VSIX starts and supervises it and communicates through ACP over redirected standard input/output.

The debugger is not exposed through a TCP listener. The named pipe includes the Visual Studio process ID and a random identifier.

## Workbench composition

`VSAgentControl` remains the main tool-window control and is split across partial classes:

- `VSAgentControl.Designer.cs` builds the theme-aware shell, navigation, chat transcript, composer and status bar.
- `VSAgentControl.xaml.cs` contains the existing chat, ACP streaming, slash-command and queue behavior.
- `VSAgentControl.Workbench.cs` integrates persistence, navigation, prompts, agent profiles, context, diagnostics and session restoration.

Feature views are intentionally separate controls:

- `TaskCenterView`
- `AgentWorkspaceView`
- `RepositoryOverviewView`
- `GitChangesView`
- `TerminalView`
- `ContextInspectorView`
- `PromptLibraryView`
- `SessionsWorkspaceView`
- `DiagnosticsView`

`WorkbenchUi` contains reusable Visual Studio-themed control factories. Views use `VsBrushes` resource keys rather than detecting or hard-coding a theme.

## ACP lifecycle

`AgentHostService` owns the ACP lifecycle:

- locates `omp.exe` and the packaged MCP host
- starts the per-process named-pipe server
- starts `omp acp` in the active solution directory
- exposes streaming status and text events
- prepends active skill content to prompts
- drains the follow-up queue
- disposes processes and pipe resources with the package

The workbench does not replace this lifecycle. Restart and stop actions reuse the same host/client and existing resource cleanup.

## Visual Studio tools

The VSIX-side named-pipe dispatcher accesses Visual Studio through `DTE2` and EnvDTE services. The MCP host never directly loads Visual Studio assemblies. This keeps the .NET 8 MCP process isolated from the Visual Studio AppDomain and permits clear process failure boundaries.

## Persistent state

`WorkbenchStore` writes `%LOCALAPPDATA%\QuantivusOMP\workbench.json` and stores:

- chat sessions
- prompt templates
- agent profiles
- active session/profile IDs
- navigation and context preferences

Writes use a temporary file and replacement/backup strategy. Invalid JSON is copied to a timestamped `*.corrupt-*` file and defaults are reseeded. The store retains the 100 most recently updated sessions.

Credentials are not stored in the workbench file. Provider credentials continue to use `CredentialStore` and Windows DPAPI.

## Agent profiles

An activated agent profile is materialized as the reserved skill `__agent-profile`. This provides two properties:

- the exact instructions are visible in the Skills system
- the normal `ActiveSkillRegistry` and ACP permission workflow remain authoritative

A preferred model can update the existing OMP model setting. Profile confirmation policies are instructions in addition to, not a replacement for, the permission dialog.

## Context flow

`WorkspaceContextService` gathers an inspectable snapshot from:

- active selection, member, type or document
- open document paths
- explicitly selected repository text files
- staged and unstaged Git diff
- Visual Studio Error List entries

Repository enumeration is bounded and runs off the UI thread. It applies default exclusions plus `.gitignore` and `.ompignore` rules and skips common binary formats. Individual files and the final assembled context are size-bounded.

The user previews the exact context text and approximate token count. Activation stores it as the reserved skill `__workbench-context`; deactivation removes that skill from the active registry.

## Git integration

`GitCommandService` launches `git.exe` directly with `UseShellExecute=false`; no shell is involved. It provides explicit methods for status, diff, stage, unstage, discard, commit, pull, push and branch operations. Output is bounded and cancellation terminates the process.

The UI confirms discard, commit, pull, push and branch operations. Destructive command classification is covered by tests.

## Terminal integration

`TerminalView` supports PowerShell, Command Prompt and WSL Bash. Each command is user-submitted, output is redirected and bounded, and cancellation terminates the child process. A conservative classifier distinguishes read-only, state-changing and destructive command patterns; the latter two require confirmation.

The terminal is not an autonomous OMP tool. OMP tool execution continues to use ACP permissions.

## Threading

- Visual Studio DTE calls are made on the UI thread.
- repository enumeration, file reads, Git and terminal processes are asynchronous
- cancellation tokens are used for long-running work
- UI changes from process/event callbacks are marshalled through the Dispatcher
- list boxes enable recycling virtualization

## Testing boundaries

The `.NET 8` test project links platform-neutral service source files and tests persistence/recovery and Git safety logic. WPF, Visual Studio SDK and Experimental Instance tests remain Windows/Visual-Studio integration checks and are documented in the release checklist.
