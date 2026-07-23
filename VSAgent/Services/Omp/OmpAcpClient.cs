using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VSAgent.Services.Omp
{
    internal sealed class OmpAcpClient : IDisposable
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JToken>> pending =
            new ConcurrentDictionary<string, TaskCompletionSource<JToken>>();
        private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
        private readonly StringBuilder responseText = new StringBuilder();
        private Process process;
        private CancellationTokenSource lifetime;
        private Task readerTask;
        private Task stderrTask;
        private long nextId;
        private string sessionId;

        public event EventHandler<string> StatusChanged;
        public event EventHandler<string> TextReceived;

        public bool IsRunning => process != null && !process.HasExited && !string.IsNullOrWhiteSpace(sessionId);

        public async Task StartAsync(
            string executablePath,
            string workingDirectory,
            string mcpHostPath,
            string pipeName,
            CancellationToken cancellationToken)
        {
            if (IsRunning) return;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                throw new FileNotFoundException("oh-my-pi was not found. Install omp or place omp.exe in the extension Runtime directory.", executablePath);
            if (string.IsNullOrWhiteSpace(mcpHostPath) || !File.Exists(mcpHostPath))
                throw new FileNotFoundException("The Visual Studio MCP host was not found.", mcpHostPath);

            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "acp",
                WorkingDirectory = Directory.Exists(workingDirectory)
                    ? workingDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.Exited += (_, __) => OnStatusChanged("oh-my-pi stopped.");
            if (!process.Start()) throw new InvalidOperationException("Could not start oh-my-pi.");

            readerTask = Task.Run(() => ReadLoopAsync(lifetime.Token), lifetime.Token);
            stderrTask = Task.Run(() => ReadErrorLoopAsync(lifetime.Token), lifetime.Token);
            OnStatusChanged("Initializing oh-my-pi ACP connection...");

            await SendRequestAsync("initialize", new JObject
            {
                ["protocolVersion"] = 1,
                ["clientCapabilities"] = new JObject(),
                ["clientInfo"] = new JObject
                {
                    ["name"] = "Quantivus OMP for Visual Studio",
                    ["version"] = "0.1.0"
                }
            }, cancellationToken).ConfigureAwait(false);

            var sessionResponse = await SendRequestAsync("session/new", new JObject
            {
                ["cwd"] = startInfo.WorkingDirectory,
                ["mcpServers"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "visual-studio",
                        ["command"] = mcpHostPath,
                        ["args"] = new JArray("--pipe", pipeName)
                    }
                }
            }, cancellationToken).ConfigureAwait(false);

            sessionId = sessionResponse?["sessionId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new InvalidDataException("oh-my-pi did not return an ACP session ID.");

            OnStatusChanged("oh-my-pi is connected to Visual Studio.");
        }

        public async Task<string> PromptAsync(string prompt, CancellationToken cancellationToken)
        {
            if (!IsRunning) throw new InvalidOperationException("oh-my-pi is not running.");
            lock (responseText) responseText.Clear();

            OnStatusChanged("oh-my-pi is working...");
            await SendRequestAsync("session/prompt", new JObject
            {
                ["sessionId"] = sessionId,
                ["prompt"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = prompt }
                }
            }, cancellationToken).ConfigureAwait(false);

            OnStatusChanged("oh-my-pi completed the request.");
            lock (responseText) return responseText.ToString();
        }

        public async Task StopAsync()
        {
            var localProcess = process;
            process = null;
            sessionId = null;
            lifetime?.Cancel();

            if (localProcess != null)
            {
                try
                {
                    if (!localProcess.HasExited)
                    {
                        localProcess.StandardInput.Close();
                        if (!localProcess.WaitForExit(1500)) localProcess.Kill();
                    }
                }
                catch { }
                finally { localProcess.Dispose(); }
            }

            if (readerTask != null) await IgnoreCancellationAsync(readerTask).ConfigureAwait(false);
            if (stderrTask != null) await IgnoreCancellationAsync(stderrTask).ConfigureAwait(false);
        }

        private async Task<JToken> SendRequestAsync(string method, JObject parameters, CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref nextId).ToString();
            var completion = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pending.TryAdd(id, completion)) throw new InvalidOperationException("Could not register ACP request.");

            using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
            {
                await WriteMessageAsync(new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = long.Parse(id),
                    ["method"] = method,
                    ["params"] = parameters
                }, cancellationToken).ConfigureAwait(false);

                try { return await completion.Task.ConfigureAwait(false); }
                finally { pending.TryRemove(id, out _); }
            }
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && process != null && !process.HasExited)
            {
                var line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line == null) break;

                JObject message;
                try { message = JObject.Parse(line); }
                catch (JsonException)
                {
                    OnStatusChanged("Ignored invalid ACP output from oh-my-pi.");
                    continue;
                }

                var idToken = message["id"];
                var method = message["method"]?.Value<string>();
                if (idToken != null && method == null)
                {
                    var key = idToken.ToString(Formatting.None).Trim('"');
                    if (pending.TryGetValue(key, out var completion))
                    {
                        if (message["error"] != null)
                            completion.TrySetException(new InvalidOperationException(message["error"].ToString(Formatting.None)));
                        else
                            completion.TrySetResult(message["result"] ?? JValue.CreateNull());
                    }
                    continue;
                }

                if (string.Equals(method, "session/update", StringComparison.Ordinal))
                {
                    HandleSessionUpdate(message["params"] as JObject);
                    continue;
                }

                if (idToken != null && !string.IsNullOrWhiteSpace(method))
                    await HandleAgentRequestAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task HandleAgentRequestAsync(JObject message, CancellationToken cancellationToken)
        {
            var method = message["method"]?.Value<string>();
            var id = message["id"];
            if (string.Equals(method, "session/request_permission", StringComparison.Ordinal))
            {
                var options = message["params"]?["options"] as JArray;
                var safeOption = SelectPermissionOption(options, message["params"]);
                await WriteMessageAsync(new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = safeOption == null
                        ? new JObject { ["outcome"] = "cancelled" }
                        : new JObject { ["outcome"] = "selected", ["optionId"] = safeOption }
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await WriteMessageAsync(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new JObject
                {
                    ["code"] = -32601,
                    ["message"] = "ACP client method is not implemented by this Visual Studio extension."
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        private static string SelectPermissionOption(JArray options, JToken request)
        {
            if (options == null) return null;
            var description = request?["toolCall"]?.ToString(Formatting.None) ?? string.Empty;
            var readOnly = description.IndexOf("read", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           description.IndexOf("list", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           description.IndexOf("get", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           description.IndexOf("status", StringComparison.OrdinalIgnoreCase) >= 0;

            foreach (var option in options)
            {
                var optionId = option?["optionId"]?.Value<string>() ?? option?["id"]?.Value<string>();
                var name = option?["name"]?.Value<string>() ?? option?["label"]?.Value<string>() ?? string.Empty;
                if (readOnly && (name.IndexOf("allow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 optionId?.IndexOf("allow", StringComparison.OrdinalIgnoreCase) >= 0))
                    return optionId;
            }

            foreach (var option in options)
            {
                var optionId = option?["optionId"]?.Value<string>() ?? option?["id"]?.Value<string>();
                var name = option?["name"]?.Value<string>() ?? option?["label"]?.Value<string>() ?? string.Empty;
                if (name.IndexOf("reject", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("deny", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    optionId?.IndexOf("reject", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    optionId?.IndexOf("deny", StringComparison.OrdinalIgnoreCase) >= 0)
                    return optionId;
            }
            return null;
        }

        private void HandleSessionUpdate(JObject parameters)
        {
            var update = parameters?["update"] as JObject;
            var type = update?["sessionUpdate"]?.Value<string>() ?? update?["type"]?.Value<string>();
            if (string.Equals(type, "agent_message_chunk", StringComparison.Ordinal))
            {
                var text = update?["content"]?["text"]?.Value<string>() ?? update?["text"]?.Value<string>();
                if (!string.IsNullOrEmpty(text))
                {
                    lock (responseText) responseText.Append(text);
                    TextReceived?.Invoke(this, text);
                }
            }
            else if (string.Equals(type, "tool_call", StringComparison.Ordinal) ||
                     string.Equals(type, "tool_call_update", StringComparison.Ordinal))
            {
                var title = update?["title"]?.Value<string>() ?? update?["toolCallId"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(title)) OnStatusChanged("Tool: " + title);
            }
        }

        private async Task ReadErrorLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && process != null && !process.HasExited)
            {
                var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line == null) break;
                if (!string.IsNullOrWhiteSpace(line)) OnStatusChanged(line);
            }
        }

        private async Task WriteMessageAsync(JObject message, CancellationToken cancellationToken)
        {
            if (process == null || process.HasExited)
                throw new InvalidOperationException("oh-my-pi ACP process is not available.");

            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await process.StandardInput.WriteLineAsync(message.ToString(Formatting.None)).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            finally { writeLock.Release(); }
        }

        private void OnStatusChanged(string status) => StatusChanged?.Invoke(this, status);

        private static async Task IgnoreCancellationAsync(Task task)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        public void Dispose()
        {
            _ = StopAsync();
            writeLock.Dispose();
            lifetime?.Dispose();
        }
    }
}
