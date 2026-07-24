using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VSAgent.Models;
using VSAgent.Views;

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

        public bool AutoApproveReadOnly { get; set; } = true;


        public bool IsRunning => process != null && !process.HasExited && !string.IsNullOrWhiteSpace(sessionId);

        public Task StartAsync(
            string executablePath,
            string workingDirectory,
            string mcpHostPath,
            string pipeName,
            CancellationToken cancellationToken)
            => StartAsync(executablePath, workingDirectory, mcpHostPath, pipeName, null, null, cancellationToken);

        public async Task StartAsync(
            string executablePath,
            string workingDirectory,
            string mcpHostPath,
            string pipeName,
            string modelProvider,
            string modelName,
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
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            if (!string.IsNullOrWhiteSpace(modelProvider))
                startInfo.Environment["OMP_PROVIDER"] = modelProvider;
            if (!string.IsNullOrWhiteSpace(modelName))
                startInfo.Environment["OMP_MODEL"] = modelName;
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

        // Sends a follow-up message to the running session without canceling it.
        // OMP/ACP accepts multiple in-flight session/prompt requests; each is
        // matched by request id and processed by the agent as a follow-up
        // user message. We do not clear responseText, do not touch the
        // running prompt's cancellation, and do not wait for a turn-complete
        // response — the response is folded into the live stream.
        public async Task SteerAsync(string message, CancellationToken cancellationToken)
        {
            if (!IsRunning) throw new InvalidOperationException("oh-my-pi is not running.");
            if (string.IsNullOrWhiteSpace(message)) return;

            OnStatusChanged("Steering the agent...");
            try
            {
                await SendRequestAsync("session/prompt", new JObject
                {
                    ["sessionId"] = sessionId,
                    ["prompt"] = new JArray
                    {
                        new JObject { ["type"] = "text", ["text"] = message }
                    }
                }, cancellationToken).ConfigureAwait(false);
                OnStatusChanged("Steering message delivered.");
            }
            catch (Exception ex)
            {
                OnStatusChanged("Steer failed: " + ex.Message);
                throw;
            }
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
                var toolCall = message["params"]?["toolCall"];
                var selected = await RequestPermissionAsync(options, toolCall, cancellationToken).ConfigureAwait(false);
                JObject result = selected == null
                    ? new JObject { ["outcome"] = "cancelled" }
                    : new JObject { ["outcome"] = "selected", ["optionId"] = selected };
                await WriteMessageAsync(new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = result
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

        private async Task<string> RequestPermissionAsync(JArray options, JToken toolCall, CancellationToken cancellationToken)
        {
            if (options == null || options.Count == 0) return null;

            var parsed = ParseOptions(options);
            if (parsed.Count == 0) return null;

            // Heuristic: read-only requests are auto-approved when enabled.
            if (AutoApproveReadOnly && IsReadOnlyRequest(toolCall))
            {
                foreach (var opt in parsed)
                {
                    if (Contains(opt.Name, "allow") || Contains(opt.OptionId, "allow"))
                        return opt.OptionId;
                }
            }

            return await ShowPermissionDialogAsync(parsed, toolCall, cancellationToken).ConfigureAwait(false);
        }

        private static List<PermissionOption> ParseOptions(JArray options)
        {
            var result = new List<PermissionOption>(options.Count);
            foreach (var token in options)
            {
                if (token is not JObject obj) continue;
                var id = obj["optionId"]?.Value<string>() ?? obj["id"]?.Value<string>();
                if (string.IsNullOrEmpty(id)) continue;
                var name = obj["name"]?.Value<string>() ?? obj["label"]?.Value<string>() ?? id;
                var kind = obj["kind"]?.Value<string>() ?? string.Empty;
                result.Add(new PermissionOption(id, name, kind));
            }
            return result;
        }

        private static bool IsReadOnlyRequest(JToken toolCall)
        {
            var blob = toolCall?.ToString(Formatting.None) ?? string.Empty;
            return Contains(blob, "read") || Contains(blob, "list") ||
                   Contains(blob, "get") || Contains(blob, "status") ||
                   Contains(blob, "evaluate") || Contains(blob, "call_stack");
        }
        private static bool Contains(string text, string token) =>
            text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        private async Task<string> ShowPermissionDialogAsync(List<PermissionOption> options, JToken toolCall, CancellationToken cancellationToken)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return null;

            var summary = ExtractSummary(toolCall);
            var description = toolCall?.ToString(Formatting.Indented) ?? string.Empty;
            if (description.Length > 2000) description = description.Substring(0, 2000) + "\n...";

            var tcs = new TaskCompletionSource<string?>();

            await dispatcher.InvokeAsync(async () =>
            {
                var dialog = new PermissionRequestDialog(summary, description, options);
                var cancelled = false;
                using (cancellationToken.Register(() =>
                {
                    cancelled = true;
                    dialog.Dispatcher.BeginInvoke(new Action(dialog.Close));
                }))
                {
                    dialog.ShowDialog();
                }
                tcs.SetResult(cancelled ? null : (dialog.DialogResult == true ? dialog.SelectedOptionId : null));
            }).Task.ConfigureAwait(false);

            return await tcs.Task.ConfigureAwait(false);
        }

        private static string ExtractSummary(JToken toolCall)
        {
            if (toolCall is JObject obj)
            {
                var title = obj["title"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    var firstLine = title.Split(new[] { '\r', '\n' }, 2, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    if (firstLine.Length > 120) firstLine = firstLine.Substring(0, 117) + "...";
                    return firstLine;
                }
                var rawName = obj["rawInput"]?["name"]?.Value<string>() ?? obj["name"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(rawName)) return rawName;
            }
            return "oh-my-pi tool call";
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
            else if (string.Equals(type, "tool_call", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(type, "tool_call_update", StringComparison.OrdinalIgnoreCase))
            {
                var title = update?["title"]?.Value<string>() ?? update?["toolCallId"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    // omp packs the tool source into "title". Keep only the first
                    // line and cap length so the status bar stays readable.
                    var firstLine = title.Split(new[] { '\r', '\n' }, 2, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    if (firstLine.Length > 80) firstLine = firstLine.Substring(0, 77) + "...";
                    OnStatusChanged("Tool: " + firstLine);
                }
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
