using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VSAgent.Services
{
    public sealed class ChatGPTService : IDisposable
    {
        public Task<string> ExplainCodeAsync(string code) => SendAsync($@"Explain the following code in detail.
Include what it does, how it works, possible defects, and concrete improvements.

```text
{code}
```", CancellationToken.None);

        public Task<string> RefactorCodeAsync(string code) => SendAsync($@"Review and refactor the following code.
Preserve behavior, explain the changes, and use the Visual Studio tools when build or debugger evidence is useful.

```text
{code}
```", CancellationToken.None);

        public Task<string> GenerateTestsAsync(string code) => SendAsync($@"Generate appropriate automated tests for the following code.
Cover normal behavior, edge cases, and failure paths. Detect the test framework used by the open solution.

```text
{code}
```", CancellationToken.None);

        public Task<string> SendCustomPromptAsync(
            string prompt,
            string? context = null,
            CancellationToken cancellationToken = default)
        {
            var builder = new StringBuilder(prompt);
            if (!string.IsNullOrWhiteSpace(context))
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine("Active editor context:");
                builder.AppendLine("```text");
                builder.AppendLine(context);
                builder.AppendLine("```");
            }
            return SendAsync(builder.ToString(), cancellationToken);
        }

        private static Task<string> SendAsync(string prompt, CancellationToken cancellationToken)
        {
            var host = VSAgentPackage.AgentHost;
            if (host == null)
                throw new InvalidOperationException("The oh-my-pi agent host has not been initialized.");
            return host.PromptAsync(prompt, null, cancellationToken);
        }

        public void Dispose() { }
    }
}
