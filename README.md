# Quantivus OMP for Visual Studio

Quantivus OMP is an open-source Visual Studio extension that runs **oh-my-pi** as an ACP agent and exposes Visual Studio build and debugger operations through a local MCP server.

> Status: alpha. The implementation builds as a VSIX and includes automated service tests. A signed release still requires interactive validation in a Visual Studio Experimental Instance.

## Workbench

The extension provides an integrated, theme-aware developer workbench instead of a detached chat window:

- **Chat** — streaming Markdown, tool-call cards, search, export, drag-and-drop files, editor selection and open-document attachments.
- **Tasks** — live OMP status, cancellation and an ordered follow-up queue.
- **Agents** — persistent software architect, C#, review, testing, security, performance, documentation, DevOps and debugging profiles. Profiles are applied as visible OMP skills.
- **Repository** — current solution, project inventory, Git root and branch, build and rebuild actions.
- **Changes** — staged and unstaged diff review, selective staging, unstage, confirmed discard, commit, pull, push and branch operations.
- **Terminal** — cancellable PowerShell, Command Prompt and WSL execution with bounded output, history and risk confirmation.
- **Context** — inspect and explicitly select editor, repository, Git diff and build-error context before it is supplied to OMP.
- **Prompts** — searchable prompt templates with import/export and variables such as `{{solution}}`, `{{file}}`, `{{selection}}`, `{{branch}}`, `{{diff}}` and `{{buildErrors}}`.
- **Sessions** — persistent conversations tied to solution, branch, provider and model, with restore and Markdown/JSON export.
- **Skills / Tools / Settings** — existing OMP skills, custom MCP tools, provider credentials, model and search configuration.
- **Diagnostics** — runtime, process, installation-path and provider checks plus sanitized report export.

The navigation can be collapsed for narrow tool windows. Visual Studio theme resources are used for light/dark compatibility, and long lists use WPF virtualization where appropriate.

## Existing ACP and MCP capabilities

- VSIX for supported Visual Studio 2022/2026 environments.
- `omp acp` lifecycle managed by the extension.
- ACP initialization, sessions, prompts, streaming updates, tool calls and permission responses.
- Separate .NET 8 MCP host communicating with the VSIX through a per-process named pipe.
- Context-menu commands routed through oh-my-pi.
- MCP tools for solution inspection, build, debugging, breakpoints, call stacks and expression evaluation.
- DPAPI-backed provider credentials.
- Slash-command completion for build, tests, review, refactoring, optimization, Git, context and debugging workflows.

## Architecture

```text
Visual Studio
  VSAgent VSIX (.NET Framework 4.8)
    ├─ Modern WPF workbench and persistent state
    ├─ oh-my-pi ACP client
    ├─ Skills, context and permission policy
    ├─ DTE2 / EnvDTE build and debugger dispatcher
    └─ Named-pipe server
             │
             ▼
  VSAgent.McpHost (.NET 8, STDIO MCP server)
             │
             ▼
  omp acp
```

No TCP debugger listener is opened. See `docs/ARCHITECTURE.md` for component and data-flow details.

## Context safety

Repository context respects `.gitignore` and an optional `.ompignore`. Default exclusions cover `.git`, `.vs`, `bin`, `obj`, package/build output, common certificates, keys, databases, environment files and user-specific Visual Studio files.

Copy `.ompignore.example` to `.ompignore` and adapt it for repository-specific generated or sensitive paths. The context inspector previews the final text and approximate token count before activation.

## Build

Prerequisites:

- Windows with a supported Visual Studio installation and the Visual Studio extension development workload
- .NET 8 SDK
- MSBuild available from Visual Studio

```powershell
msbuild VSAgent.sln /restore /m /p:Configuration=Release
```

The VSIX build publishes `VSAgent.McpHost` into `Runtime/McpHost`. `omp.exe` is not committed and is included only when deliberately supplied under `VSAgent/Runtime/omp.exe`.

## Tests

```powershell
dotnet test VSAgent.Workbench.Tests/VSAgent.Workbench.Tests.csproj -c Release
```

The service tests cover workbench persistence, built-in profile/template protection, corrupt-state recovery, Git risk classification and repository-root discovery. GitHub Actions builds the full solution, runs these tests and uploads the VSIX, MCP host, MSBuild log and test results.

## Development validation

Before release:

1. Build the Release configuration.
2. Start the extension in a Visual Studio Experimental Instance.
3. Verify light and dark themes and at least 100%, 150% and 200% DPI scaling.
4. Start, stop, restart and cancel OMP.
5. Run a prompt with streaming text and tool-call updates.
6. Preview and activate repository context.
7. Review a Git diff and exercise stage/unstage using a disposable repository.
8. Restore a session after restarting Visual Studio.
9. Verify that destructive Git and terminal actions require confirmation.

## Security

Read `docs/SECURITY.md` before enabling terminal or repository write operations. ACP permissions remain authoritative; agent profiles and context packages do not bypass the permission dialog.

## Troubleshooting

See `docs/TROUBLESHOOTING.md`. The Diagnostics workspace can verify the OMP executable, MCP host, Git installation, named pipe and active provider/model. Exported reports redact the current user profile paths.

## Licensing

Quantivus source code is Apache-2.0 licensed. oh-my-pi is a separate MIT-licensed project and is not included by default. See `THIRD_PARTY_NOTICES.md`.
