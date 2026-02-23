using System.IO.Pipes;
using System.Text.Json;
using SOTAmatSkimmer.Utilities;

namespace SOTAmatSkimmer
{
    internal sealed class WorkerIpcReporter : IDisposable
    {
        private readonly NamedPipeClientStream pipe;
        private readonly StreamWriter writer;
        private readonly SemaphoreSlim writeLock;
        private readonly CancellationTokenSource cts;
        private readonly Task heartbeatTask;
        private readonly JsonSerializerOptions serializerOptions;
        private readonly int stopHeartbeatAfterSeconds;
        private readonly DateTime startedUtc;

        private WorkerIpcReporter(NamedPipeClientStream localPipe, StreamWriter localWriter, int localStopHeartbeatAfterSeconds)
        {
            pipe = localPipe;
            writer = localWriter;
            writeLock = new SemaphoreSlim(1, 1);
            cts = new CancellationTokenSource();
            stopHeartbeatAfterSeconds = Math.Max(0, localStopHeartbeatAfterSeconds);
            startedUtc = DateTime.UtcNow;
            serializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            heartbeatTask = Task.Run(() => HeartbeatLoop(cts.Token), CancellationToken.None);
        }

        public static WorkerIpcReporter? Start(Configuration config)
        {
            if (!config.WorkerMode || string.IsNullOrWhiteSpace(config.WorkerPipeName))
            {
                return null;
            }

            NamedPipeClientStream localPipe = new(".", config.WorkerPipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            try
            {
                localPipe.Connect(10000);
            }
            catch (Exception ex)
            {
                ConsoleHelper.SafeWriteLine($"ERROR: Unable to connect worker IPC pipe '{config.WorkerPipeName}': {ex.Message}", true, ConsoleColor.Red);
                localPipe.Dispose();
                throw;
            }

            StreamWriter localWriter = new(localPipe)
            {
                AutoFlush = true
            };

#if DEBUG
            int stopHeartbeat = config.TestStopHeartbeatAfterSeconds;
#else
            int stopHeartbeat = 0;
#endif
            WorkerIpcReporter reporter = new(localPipe, localWriter, stopHeartbeat);
            reporter.PublishEvent("worker-started", $"Worker process started for mode {(config.SparkSDRmode ? "SparkSDR" : "WSJT-X")}.");
            return reporter;
        }

        public void PublishEvent(string messageType, string detail)
        {
            try
            {
                WorkerHealthSnapshot snapshot = WorkerHealthState.Snapshot();
                WorkerIpcMessage msg = new(
                    MessageType: messageType,
                    ProcessId: snapshot.ProcessId,
                    TimestampUtc: snapshot.TimestampUtc,
                    StartedUtc: snapshot.StartedUtc,
                    Mode: snapshot.Mode,
                    Connected: snapshot.Connected,
                    LastSourceMessageUtc: snapshot.LastSourceMessageUtc,
                    LastDecodeHandledUtc: snapshot.LastDecodeHandledUtc,
                    ErrorCount: snapshot.ErrorCount,
                    WorkingSetBytes: snapshot.WorkingSetBytes,
                    Detail: detail);

                string line = JsonSerializer.Serialize(msg, serializerOptions);
                WriteLineLockedAsync(line, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                ConsoleHelper.SafeWriteLine($"WARNING: Unable to write worker IPC event: {ex.Message}", true, ConsoleColor.DarkYellow);
            }
        }

        private async Task HeartbeatLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (stopHeartbeatAfterSeconds > 0 &&
                    (DateTime.UtcNow - startedUtc).TotalSeconds >= stopHeartbeatAfterSeconds)
                {
                    ConsoleHelper.SafeWriteLine($"Worker test mode: stopping heartbeat stream after {stopHeartbeatAfterSeconds} seconds.", true, ConsoleColor.DarkYellow);
                    return;
                }

                try
                {
                    WorkerHealthSnapshot snapshot = WorkerHealthState.Snapshot();
                    WorkerIpcMessage msg = new(
                        MessageType: "heartbeat",
                        ProcessId: snapshot.ProcessId,
                        TimestampUtc: snapshot.TimestampUtc,
                        StartedUtc: snapshot.StartedUtc,
                        Mode: snapshot.Mode,
                        Connected: snapshot.Connected,
                        LastSourceMessageUtc: snapshot.LastSourceMessageUtc,
                        LastDecodeHandledUtc: snapshot.LastDecodeHandledUtc,
                        ErrorCount: snapshot.ErrorCount,
                        WorkingSetBytes: snapshot.WorkingSetBytes,
                        Detail: null);

                    string line = JsonSerializer.Serialize(msg, serializerOptions);
                    await WriteLineLockedAsync(line, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ConsoleHelper.SafeWriteLine($"WARNING: Worker heartbeat pipe write failed: {ex.Message}", true, ConsoleColor.DarkYellow);
                    return;
                }

                try
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task WriteLineLockedAsync(string line, CancellationToken token)
        {
            await writeLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await writer.WriteLineAsync(line).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }
        }

        public void Dispose()
        {
            cts.Cancel();
            try
            {
                heartbeatTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // ignore shutdown issues
            }

            writer.Dispose();
            pipe.Dispose();
            writeLock.Dispose();
            cts.Dispose();
        }
    }
}
