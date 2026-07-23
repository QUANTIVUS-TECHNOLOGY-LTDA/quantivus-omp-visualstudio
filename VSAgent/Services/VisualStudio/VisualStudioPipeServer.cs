using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VSAgent.Models;

namespace VSAgent.Services.VisualStudio
{
    internal sealed class VisualStudioPipeServer : IDisposable
    {
        private readonly string pipeName;
        private readonly VisualStudioToolDispatcher dispatcher;
        private CancellationTokenSource lifetime;
        private Task serverTask;

        public VisualStudioPipeServer(string pipeName, VisualStudioToolDispatcher dispatcher)
        {
            this.pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public void Start(CancellationToken cancellationToken)
        {
            if (serverTask != null) return;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            serverTask = Task.Run(() => RunAsync(lifetime.Token), lifetime.Token);
        }

        public async Task StopAsync()
        {
            if (lifetime == null) return;
            lifetime.Cancel();
            try
            {
                using (var wakeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                {
                    wakeClient.Connect(100);
                }
            }
            catch { }

            try
            {
                if (serverTask != null) await serverTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            finally
            {
                serverTask = null;
                lifetime.Dispose();
                lifetime = null;
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using (var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous))
                {
                    await Task.Factory.FromAsync(
                        server.BeginWaitForConnection,
                        server.EndWaitForConnection,
                        null).ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested) break;

                    using (var reader = new StreamReader(server, Encoding.UTF8, false, 4096, true))
                    using (var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                    {
                        while (server.IsConnected && !cancellationToken.IsCancellationRequested)
                        {
                            var line = await reader.ReadLineAsync().ConfigureAwait(false);
                            if (line == null) break;

                            VisualStudioToolResponse response;
                            try
                            {
                                var request = JsonConvert.DeserializeObject<VisualStudioToolRequest>(line);
                                response = request == null || string.IsNullOrWhiteSpace(request.Id)
                                    ? VisualStudioToolResponse.Fail(string.Empty, "Invalid tool request.")
                                    : await dispatcher.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                response = VisualStudioToolResponse.Fail(string.Empty, ex.Message);
                            }

                            await writer.WriteLineAsync(JsonConvert.SerializeObject(response)).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        public void Dispose() => _ = StopAsync();
    }
}
