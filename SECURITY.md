# Security Policy

## Supported versions

The project is currently in alpha. Until the first signed release is published, only the latest commit on the default branch is expected to receive security fixes.

## Reporting a vulnerability

Do not disclose security vulnerabilities, tokens, source code, customer data, pipe names, or diagnostic dumps in a public GitHub issue.

Report vulnerabilities privately to the repository maintainers through GitHub's private vulnerability reporting feature when enabled, or through the private Quantivus security contact configured for the organization.

Include the affected commit or release, Visual Studio and Windows versions, the oh-my-pi installation method, a minimal reproduction without secrets, the security impact, and redacted logs.

## Security boundaries

- `omp.exe` runs outside `devenv.exe`.
- The MCP host communicates through a randomly named local Windows pipe.
- No network listener is created by the Visual Studio MCP bridge.
- Read-only requests may be approved automatically.
- Write and destructive permission requests default to rejection in the alpha implementation.

## Required work before production

- Restrict named-pipe access with a current-user Windows ACL.
- Add an in-product permission dialog for mutating or destructive actions.
- Sign the VSIX and release artifacts.
- Verify checksums and signatures for any bundled oh-my-pi runtime.
- Add audit logging with secret redaction and adversarial prompt-injection tests.
