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
        private readonly SkillStore skillStore;
        private readonly ActiveSkillRegistry activeSkills;
        private AsyncPackage package;
        private DTE2 dte;
        private OmpAcpClient ompClient;
        private VisualStudioPipeServer pipeServer;
        private CancellationTokenSource lifetime;
        private string extensionDirectory;
        private string mcpHostPath;
        private string ompPath;
        private string modelProvider;
        private string modelName;
        private double autoCompactThresholdPercent;
        private long lastCompactedChars;
        private long totalInputChars;

        public event EventHandler<string> StatusChanged;
        public event EventHandler<string> TextReceived;

        public string PipeName { get; private set; }
        public bool IsReady => ompClient?.IsRunning == true;
        public ActiveSkillRegistry ActiveSkills => activeSkills;
        public SkillStore SkillStore => skillStore;
        public long TotalInputChars => Interlocked.Read(ref totalInputChars);

        public string ModelProvider
        {
            get => modelProvider;
            set { modelProvider = value; RestartAsyncIfRunning(); }
        }

        public string ModelName
        {
            get => modelName;
            set { modelName = value; RestartAsyncIfRunning(); }
        }

        public double AutoCompactThresholdPercent
        {
            get => autoCompactThresholdPercent;
            set => autoCompactThresholdPercent = Math.Max(0, Math.Min(99, value));
        }

        public AgentHostService() : this(new SkillStore()) { }

        public AgentHostService(SkillStore store)
        {
            skillStore = store ?? new SkillStore();
            activeSkills = new ActiveSkillRegistry(skillStore);
        }

        public void AddInputChars(int chars)
        {
            if (chars > 0) Interlocked.Add(ref totalInputChars, chars);
        }

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

        public MessageQueue Queue { get; } = new MessageQueue();

        public async Task<string> PromptAsync(string prompt, string editorContext, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;

            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            if (ompClient == null || !ompClient.IsRunning)
            {
                throw new InvalidOperationException(
                    "oh-my-pi could not be started. Verify the omp and VSAgent.McpHost installation paths.");
            }

            var withContext = string.IsNullOrWhiteSpace(editorContext)
                ? prompt
                : prompt + Environment.NewLine + Environment.NewLine +
                  "Active Visual Studio editor context:" + Environment.NewLine + editorContext;

            var effectivePrompt = BuildPromptWithSkills(withContext);

            var result = await ompClient.PromptAsync(effectivePrompt, cancellationToken).ConfigureAwait(false);

            if (Queue.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                _ = DrainQueueAsync(cancellationToken);
            }

            if (!cancellationToken.IsCancellationRequested && ShouldAutoCompact(TotalInputChars))
            {
                Queue.Enqueue("/compact");
                lastCompactedChars = TotalInputChars;
            }

            return result;
        }

        public async Task SteerAsync(string message, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            if (ompClient == null || !ompClient.IsRunning)
            {
                throw new InvalidOperationException(
                    "oh-my-pi could not be started. Verify the omp and VSAgent.McpHost installation paths.");
            }

            await ompClient.SteerAsync(message, cancellationToken).ConfigureAwait(false);
        }

        public async Task DrainQueueAsync(CancellationToken cancellationToken)
        {
            OnStatusChanged($"Draining queue ({Queue.Count})...");
            while (Queue.Count > 0 && !cancellationToken.IsCancellationRequested && ompClient != null && ompClient.IsRunning)
            {
                var next = Queue.Dequeue();
                if (next == null) break;
                OnStatusChanged($"Sending queued: {Truncate(next.Text, 60)}");
                try
                {
                    await ompClient.PromptAsync(next.Text, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    OnStatusChanged("Queue drain failed: " + ex.Message);
                    break;
                }
            }
            OnStatusChanged(Queue.Count == 0 ? "Queue empty." : "Queue drain stopped.");
        }

        public string BuildPromptWithSkills(string userPrompt)
        {
            var skillSection = activeSkills.BuildPromptSection();
            if (string.IsNullOrEmpty(skillSection)) return userPrompt;
            return skillSection + Environment.NewLine + userPrompt;
        }

        public bool ShouldAutoCompact(long currentInputChars)
        {
            if (autoCompactThresholdPercent <= 0) return false;
            if (currentInputChars <= lastCompactedChars) return false;
            const long charsPerToken = 4;
            const long maxTokens = 1_000_000;
            var totalTokens = currentInputChars / charsPerToken;
            var usage = totalTokens * 100.0 / maxTokens;
            return usage >= autoCompactThresholdPercent;
        }

        private void RestartAsyncIfRunning()
        {
            if (ompClient == null || !ompClient.IsRunning) return;
            _ = EnsureStartedAsync(CancellationToken.None);
        }

        private async Task EnsureStartedAsync(CancellationToken cancellationToken)
        {
            await startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (ompClient?.IsRunning == true) return;

                ompPath = ompPath ?? OmpExecutableLocator.Find(extensionDirectory);
                mcpHostPath = mcpHostPath ?? FindMcpHost(extensionDirectory);
                if (string.IsNullOrWhiteSpace(ompPath) || string.IsNullOrWhiteSpace(mcpHostPath)) return;

                ompClient?.Dispose();
                ompClient = new OmpAcpClient();
                ompClient.AutoApproveReadOnly = ReadAutoApproveReadOnly();
                ompClient.StatusChanged += (_, status) => OnStatusChanged(status);
                ompClient.TextReceived += (_, text) => TextReceived?.Invoke(this, text);

                await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var workingDirectory = GetWorkingDirectory();
                await ompClient.StartAsync(
                    ompPath,
                    workingDirectory,
                    mcpHostPath,
                    PipeName,
                    modelProvider,
                    modelName,
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

        private bool ReadAutoApproveReadOnly()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var page = VSAgentPackage.GetOptions<OptionsProvider.GeneralOptions>()
                    as OptionsProvider.GeneralOptions;
                return page?.AutoApproveReadOnly ?? true;
            }
            catch
            {
                return true;
            }
        }

        private void OnStatusChanged(string status) => StatusChanged?.Invoke(this, status);

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }

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
