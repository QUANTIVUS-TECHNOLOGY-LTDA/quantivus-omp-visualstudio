using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VSAgent.Services.Omp;
using VSAgent.Services.VisualStudio;

namespace VSAgent.Services
{
    internal sealed class AgentHostService : IDisposable
    {
        private readonly SemaphoreSlim startLock = new SemaphoreSlim(1, 1);
        private AsyncPackage package;
        private DTE2 dte;
        private OmpAcpClient ompClient;
        private VisualStudioPipeServer pipeServer;
        private CancellationTokenSource lifetime;
        private string extensionDirectory;
        private string mcpHostPath;
        private string ompPath;

        public event EventHandler<string> StatusChanged;
        public event EventHandler<string> TextReceived;

        public string PipeName { get; private set; }
        public bool IsReady => ompClient?.IsRunning == true;

        public async Task InitializeAsync(AsyncPackage asyncPackage, CancellationToken cancellationToken)
        {
            package = asyncPackage ?? throw new ArgumentNullException(nameof(asyncPackage));
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            PipeName = "Quantivus.VSAgent." +
                       System.Diagnostics.Process.GetCurrentProcess().Id + "." +
                       Guid.NewGuid().ToString("N");

            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            dte = await package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (dte == null) throw new InvalidOperationException("Visual Studio DTE service is unavailable.");

            var dispatcher = new VisualStudioToolDispatcher(package, dte);
            pipeServer = new VisualStudioPipeServer(PipeName, dispatcher);
            pipeServer.Start(lifetime.Token);

            extensionDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            ompPath = OmpExecutableLocator.Find(extensionDirectory);
            mcpHostPath = FindMcpHost(extensionDirectory);

            if (string.IsNullOrWhiteSpace(ompPath))
            {
                OnStatusChanged("oh-my-pi is not installed. Install omp or copy omp.exe into Runtime.");
                return;
            }

            if (string.IsNullOrWhiteSpace(mcpHostPath))
            {
                OnStatusChanged("Visual Studio MCP host is not packaged. Build VSAgent.McpHost first.");
                return;
            }

            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> PromptAsync(string prompt, string editorContext, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;

            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            if (ompClient == null || !ompClient.IsRunning)
            {
                throw new InvalidOperationException(
                    "oh-my-pi could not be started. Verify the omp and VSAgent.McpHost installation paths.");
            }

            var effectivePrompt = string.IsNullOrWhiteSpace(editorContext)
                ? prompt
                : prompt + Environment.NewLine + Environment.NewLine +
                  "Active Visual Studio editor context:" + Environment.NewLine + editorContext;

            return await ompClient.PromptAsync(effectivePrompt, cancellationToken).ConfigureAwait(false);
        }

        private async Task EnsureStartedAsync(CancellationToken cancellationToken)
        {
            if (ompClient?.IsRunning == true) return;

            await startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (ompClient?.IsRunning == true) return;

                ompPath = ompPath ?? OmpExecutableLocator.Find(extensionDirectory);
                mcpHostPath = mcpHostPath ?? FindMcpHost(extensionDirectory);
                if (string.IsNullOrWhiteSpace(ompPath) || string.IsNullOrWhiteSpace(mcpHostPath)) return;

                ompClient?.Dispose();
                ompClient = new OmpAcpClient();
                ompClient.StatusChanged += (_, status) => OnStatusChanged(status);
                ompClient.TextReceived += (_, text) => TextReceived?.Invoke(this, text);

                await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var workingDirectory = GetWorkingDirectory();
                await ompClient.StartAsync(
                    ompPath,
                    workingDirectory,
                    mcpHostPath,
                    PipeName,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                startLock.Release();
            }
        }

        private string GetWorkingDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var solutionPath = dte?.Solution?.FullName;
            if (!string.IsNullOrWhiteSpace(solutionPath))
            {
                return Path.GetDirectoryName(solutionPath) ??
                       Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private static string FindMcpHost(string directory)
        {
            var candidates = new[]
            {
                Path.Combine(directory ?? string.Empty, "Runtime", "McpHost", "VSAgent.McpHost.exe"),
                Path.Combine(directory ?? string.Empty, "VSAgent.McpHost.exe"),
                Path.Combine(directory ?? string.Empty, "..", "VSAgent.McpHost", "VSAgent.McpHost.exe")
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath)) return fullPath;
            }
            return null;
        }

        private void OnStatusChanged(string status) => StatusChanged?.Invoke(this, status);

        public void Dispose()
        {
            try
            {
                lifetime?.Cancel();
                ompClient?.Dispose();
                pipeServer?.Dispose();
            }
            finally
            {
                startLock.Dispose();
                lifetime?.Dispose();
            }
        }
    }
}
