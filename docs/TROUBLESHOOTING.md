# Troubleshooting

## Start with Diagnostics

Open **Quantivus OMP → Diagnostics** and select **Refresh**. The report checks:

- extension and Visual Studio versions
- .NET runtime and process ID
- OMP connection state
- `omp.exe` discovery
- packaged MCP host discovery
- named-pipe name
- active provider and model
- solution and repository root
- local workbench/configuration file paths

Use **Test Git** to verify that `git.exe` is available. Diagnostic exports replace the current profile and local application-data paths with environment placeholders.

## OMP executable not found

The locator checks:

1. `VSAgent/Runtime/omp.exe` in a development build or the corresponding packaged runtime folder
2. `%LOCALAPPDATA%\Programs\oh-my-pi\omp.exe`
3. `%USERPROFILE%\.local\bin\omp.exe`
4. directories in `PATH`

Install oh-my-pi or place the executable in an approved location. Restart Visual Studio after changing `PATH`.

## MCP host not found

Build the full solution in Release configuration:

```powershell
msbuild VSAgent.sln /restore /m /p:Configuration=Release
```

The VSIX project publishes `VSAgent.McpHost` into its runtime output. Inspect the MSBuild log and confirm that `VSAgent.McpHost.exe` exists under `Runtime/McpHost` in the extension output.

## OMP starts but does not connect

- Verify the provider/model configuration in Settings.
- Verify that required credentials are present in the DPAPI-backed credential store.
- Check that antivirus or endpoint controls are not terminating `omp.exe` or `VSAgent.McpHost.exe`.
- Restart OMP from the workbench header or Diagnostics.
- Export diagnostics and inspect Visual Studio ActivityLog.xml for package errors.

## Prompts return no text

- Check the Task center status and queue.
- Cancel the active request and retry a small prompt.
- Confirm that the selected provider/model supports the configured ACP flow.
- Deactivate large context and agent-profile skills temporarily.
- Verify that the context size is below the provider limit.

## Context is too large or contains the wrong files

- Open Context and select **Deactivate**.
- Reduce the maximum character count.
- Disable Git diff or build errors.
- Filter and deselect repository files.
- Add generated or sensitive paths to `.ompignore`.
- Remember that `.gitignore` negation/order rules are applied in sequence; review both files when a path behaves unexpectedly.

## Context file is missing

The scanner skips:

- ignored paths
- common binary extensions
- files larger than the configured file-read boundary
- inaccessible files and directories

Confirm the path is not excluded by `.gitignore`, `.ompignore` or the built-in safety list.

## Git view says there is no repository

The active solution must be inside a Git work tree. Open a solution located below the repository root. Worktrees using a `.git` file are supported.

Ensure `git.exe` is on `PATH`, then use Diagnostics → Test Git.

## Diff is empty

- Untracked files do not have a normal Git diff; the view displays text content when possible.
- A file may contain only staged or only unstaged changes; the view labels both sections.
- Binary files do not produce a textual diff.
- Refresh after an external Git operation.

## Terminal command does not run

- Verify the working directory exists.
- Confirm PowerShell, `cmd.exe` or WSL is installed for the selected shell.
- State-changing and destructive patterns require confirmation.
- A cancelled command may leave child processes started by the shell; verify with Task Manager when testing complex scripts.

## Session cannot be restored

Workbenches are stored in:

```text
%LOCALAPPDATA%\QuantivusOMP\workbench.json
```

If the JSON is corrupt, the store copies it to a timestamped `workbench.json.corrupt-*` file and seeds defaults. Inspect the backup before deleting it. Sessions are limited to the 100 most recently updated entries.

## Theme or scaling problems

- Switch Visual Studio between light and dark themes and reopen the tool window.
- Test 100%, 150% and 200% Windows scaling.
- Avoid overriding Visual Studio resource dictionaries in a host extension.
- Capture the affected view, theme name, Visual Studio version and scaling factor in a bug report.

## Build fails in CI

Download the `msbuild-log` artifact. The CI job intentionally uploads the diagnostic MSBuild log even when compilation fails. When the build succeeds, also inspect `workbench-test-results`, `quantivus-omp-vsix` and `visual-studio-mcp-host` artifacts.

Run locally:

```powershell
msbuild VSAgent.sln /restore /m /p:Configuration=Release
dotnet test VSAgent.Workbench.Tests/VSAgent.Workbench.Tests.csproj -c Release
```

## Experimental Instance validation

A successful CI build does not replace interactive extension validation. Start the VSIX project with the Visual Studio Experimental Instance and verify:

- workbench navigation and narrow-window collapse
- light/dark theme changes
- ACP start, stop, restart and cancellation
- streaming Markdown and tool-call cards
- context preview and deactivation
- Git confirmations in a disposable repository
- session restore after restarting Visual Studio
