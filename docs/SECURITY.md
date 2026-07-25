# Security

## Trust boundaries

Quantivus OMP connects four security domains:

- the current Visual Studio process and its loaded solution
- the local `omp.exe` process and configured model provider
- the packaged MCP host and named-pipe transport
- user-approved Git and terminal child processes

Treat repository content, generated prompts, model output and tool arguments as untrusted input. A model recommendation is never equivalent to user authorization.

## Permissions

ACP permission requests remain the authoritative control for OMP tool calls. Agent profiles and context packages are implemented as visible skills and cannot disable the permission dialog.

The workbench additionally confirms operations that can change or remove data:

- discard working-tree changes
- create commits
- pull or push
- create or switch branches when changes exist
- state-changing or destructive terminal command patterns

Force push, hard reset, clean and similar operations are not exposed as first-class Git buttons.

## Credentials

Provider API keys and tokens use the existing `CredentialStore`, encrypted with Windows DPAPI for the current user. Do not commit credentials, copy them into prompt templates or include them in diagnostic reports.

The workbench state file contains prompts, sessions and profiles but not provider credentials. Be aware that a saved chat session can still contain source snippets or other data deliberately sent in a prompt.

## Context minimization

The context inspector shows the exact text and approximate size before activation. Repository scanning applies `.gitignore`, `.ompignore` and default exclusions for common:

- keys, certificates and secrets
- environment and user-specific configuration
- databases and binary files
- build output and package caches
- Visual Studio private state

Review `.ompignore` for every sensitive repository. Ignore rules reduce accidental inclusion but are not a data-loss-prevention guarantee.

## Named pipe

The named-pipe name includes the Visual Studio process ID and a random identifier. No TCP debugger listener is opened. Production hardening should continue to verify current-user pipe ACLs and reject clients outside the expected user/session.

## Terminal

The terminal runs only commands explicitly submitted in its UI. It redirects output, bounds retained text and supports cancellation. Risk classification is conservative but pattern-based and cannot understand every script or alias. Read the command and working directory before confirming it.

The terminal must not be used to bypass ACP permissions or repository policy.

## Git

Git is invoked directly without an intermediate shell. Paths are passed after `--` for file operations. Discard, commit, pull, push and branch changes require UI actions and confirmation where risk is meaningful.

Use a disposable branch when testing workbench Git features. The extension does not create automatic commits or pushes.

## Diagnostics and logging

Diagnostic export redacts the current user profile and local application-data paths. It does not intentionally export credentials. Review every report before sharing because solution paths, provider/model names and runtime errors may still be confidential.

Avoid logging prompt bodies, API keys or full environment-variable dictionaries.

## Release requirements

Before publishing a signed VSIX:

1. validate the extension in a Visual Studio Experimental Instance
2. verify ACP permission behavior for read, edit and execute tools
3. inspect named-pipe ACLs
4. test light/dark themes and high DPI
5. test cancellation and child-process cleanup
6. run dependency and secret scanning
7. sign the VSIX and publish checksums
8. document the exact bundled or required `omp` version

Report security issues privately to the repository maintainers rather than opening a public issue containing exploit details or credentials.
