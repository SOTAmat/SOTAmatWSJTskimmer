using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using SOTAmatSkimmer.Utilities;

namespace SOTAmatSkimmer
{
    internal sealed class WorkerSupervisor
    {
        private const int WorkerHeartbeatTimeoutSeconds = 60;
        private const int WorkerSourceStaleTimeoutWsjtSeconds = 120;
        private const int MaxWorkerRestarts = 10;
        private const int RestartWindowSeconds = 300;

        private readonly Configuration config;
        private readonly string[] originalArgs;
        private readonly JsonSerializerOptions serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        private readonly ConcurrentQueue<DateTime> restartTimesUtc = new();

        public WorkerSupervisor(Configuration localConfig, string[] localOriginalArgs)
        {
            config = localConfig;
            originalArgs = localOriginalArgs;
        }

        public int Run()
        {
            ConsoleHelper.SafeWriteLine("Process isolation enabled. Starting supervisor mode.", true, ConsoleColor.Cyan);

            using CancellationTokenSource shutdownCts = new();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                shutdownCts.Cancel();
            };

            int restartAttempt = 0;
            while (!shutdownCts.IsCancellationRequested)
            {
                string pipeName = $"sotamat-worker-{Environment.ProcessId}-{Guid.NewGuid():N}";
                using NamedPipeServerStream pipeServer = new(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                using Process worker = StartWorkerProcess(pipeName);

                ConsoleHelper.SafeWriteLine($"Supervisor started worker PID {worker.Id}.", true, ConsoleColor.Green);
                using CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownCts.Token);

                DateTime lastHeartbeatUtc = DateTime.UtcNow;
                DateTime? lastSourceUtc = null;
                bool workerConnected = false;
                string workerMode = config.SparkSDRmode ? "sparksdr" : "wsjt";

                Task stdoutTask = PumpProcessOutput(worker.StandardOutput, "[worker]", runCts.Token);
                Task stderrTask = PumpProcessOutput(worker.StandardError, "[worker:err]", runCts.Token);

                try
                {
                    if (!WaitForPipeConnection(pipeServer, runCts.Token))
                    {
                        if (shutdownCts.IsCancellationRequested)
                        {
                            return 0;
                        }

                        ConsoleHelper.SafeWriteLine("Supervisor restarting worker because IPC connection was not established.", true, ConsoleColor.Yellow);
                        StopWorker(worker);
                        runCts.Cancel();
                        WaitAllQuietly(stdoutTask, stderrTask);

                        restartAttempt++;
                        RegisterRestart();
                        if (ExceededRestartPolicy())
                        {
                            ConsoleHelper.SafeWriteLine($"Exceeded max worker restarts ({MaxWorkerRestarts}) within {RestartWindowSeconds} seconds. Stopping.", true, ConsoleColor.Red);
                            return 1;
                        }

                        int initialDelaySeconds = GetRestartDelaySeconds(restartAttempt);
                        ConsoleHelper.SafeWriteLine($"Supervisor backoff delay: {initialDelaySeconds} second(s) before next worker start.", true, ConsoleColor.DarkYellow);
                        SleepWithCancellation(TimeSpan.FromSeconds(initialDelaySeconds), shutdownCts.Token);
                        continue;
                    }

                    Task readTask = Task.Run(async () =>
                    {
                        using StreamReader reader = new(pipeServer);
                        while (!runCts.Token.IsCancellationRequested)
                        {
                            string? line;
                            try
                            {
                                line = await reader.ReadLineAsync().WaitAsync(runCts.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                return;
                            }

                            if (line is null)
                            {
                                return;
                            }

                            WorkerIpcMessage? msg = ParseIpcMessage(line);
                            if (msg is null)
                            {
                                continue;
                            }

                            lastHeartbeatUtc = DateTime.UtcNow;
                            workerConnected = msg.Connected;
                            lastSourceUtc = msg.LastSourceMessageUtc;
                            if (!string.IsNullOrWhiteSpace(msg.Mode))
                            {
                                workerMode = msg.Mode;
                            }

                            if (!string.Equals(msg.MessageType, "heartbeat", StringComparison.OrdinalIgnoreCase))
                            {
                                ConsoleHelper.SafeWriteLine($"Worker event: {msg.MessageType} {msg.Detail}", true, ConsoleColor.DarkCyan);
                            }
                        }
                    }, runCts.Token);

                    string? reason = MonitorWorker(worker, () => lastHeartbeatUtc, () => workerConnected, () => lastSourceUtc, () => workerMode, runCts.Token);
                    if (shutdownCts.IsCancellationRequested)
                    {
                        StopWorker(worker);
                        runCts.Cancel();
                        WaitAllQuietly(stdoutTask, stderrTask, readTask);
                        return 0;
                    }

                    ConsoleHelper.SafeWriteLine($"Supervisor restarting worker. Reason: {reason}", true, ConsoleColor.Yellow);
                    StopWorker(worker);
                    runCts.Cancel();
                    WaitAllQuietly(stdoutTask, stderrTask, readTask);

                    if (worker.ExitCode == 2)
                    {
                        ConsoleHelper.SafeWriteLine("Worker exited due to configuration/authentication failure. Stopping supervisor.", true, ConsoleColor.Red);
                        return 2;
                    }

                    restartAttempt++;
                    RegisterRestart();
                    if (ExceededRestartPolicy())
                    {
                        ConsoleHelper.SafeWriteLine($"Exceeded max worker restarts ({MaxWorkerRestarts}) within {RestartWindowSeconds} seconds. Stopping.", true, ConsoleColor.Red);
                        return 1;
                    }

                    int delaySeconds = GetRestartDelaySeconds(restartAttempt);
                    ConsoleHelper.SafeWriteLine($"Supervisor backoff delay: {delaySeconds} second(s) before next worker start.", true, ConsoleColor.DarkYellow);
                    SleepWithCancellation(TimeSpan.FromSeconds(delaySeconds), shutdownCts.Token);
                }
                finally
                {
                    runCts.Cancel();
                    WaitAllQuietly(stdoutTask, stderrTask);
                }
            }

            return 0;
        }

        private bool WaitForPipeConnection(NamedPipeServerStream pipeServer, CancellationToken token)
        {
            try
            {
                pipeServer.WaitForConnectionAsync(token).Wait(TimeSpan.FromSeconds(20));
                if (!pipeServer.IsConnected)
                {
                    ConsoleHelper.SafeWriteLine("Worker IPC pipe connection timed out.", true, ConsoleColor.Red);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ConsoleHelper.SafeWriteLine($"Worker IPC connection error: {ex.Message}", true, ConsoleColor.Red);
                return false;
            }
        }

        private string? MonitorWorker(
            Process worker,
            Func<DateTime> getLastHeartbeatUtc,
            Func<bool> getWorkerConnected,
            Func<DateTime?> getLastSourceUtc,
            Func<string> getWorkerMode,
            CancellationToken token)
        {
            int heartbeatTimeout = config.TestSupervisorHeartbeatTimeoutSeconds > 0
                ? config.TestSupervisorHeartbeatTimeoutSeconds
                : WorkerHeartbeatTimeoutSeconds;
            int sourceStaleTimeout = WorkerSourceStaleTimeoutWsjtSeconds;

            while (!token.IsCancellationRequested)
            {
                if (worker.HasExited)
                {
                    return $"worker exited with code {worker.ExitCode}";
                }

                DateTime now = DateTime.UtcNow;
                if ((now - getLastHeartbeatUtc()).TotalSeconds > heartbeatTimeout)
                {
                    return $"no worker heartbeat for > {heartbeatTimeout} sec";
                }

                if (getWorkerConnected() && string.Equals(getWorkerMode(), "wsjt", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime? sourceTs = getLastSourceUtc();
                    if (sourceTs.HasValue && (now - sourceTs.Value).TotalSeconds > sourceStaleTimeout)
                    {
                        return $"worker connected but source messages stale for > {sourceStaleTimeout} sec";
                    }
                }

                SleepWithCancellation(TimeSpan.FromSeconds(1), token);
            }

            return null;
        }

        private WorkerIpcMessage? ParseIpcMessage(string line)
        {
            try
            {
                return JsonSerializer.Deserialize<WorkerIpcMessage>(line, serializerOptions);
            }
            catch
            {
                ConsoleHelper.SafeWriteLine($"WARNING: Unable to parse worker IPC message: {line}", true, ConsoleColor.DarkYellow);
                return null;
            }
        }

        private Process StartWorkerProcess(string pipeName)
        {
            (string command, List<string> arguments) = BuildWorkerCommand(pipeName);
            ProcessStartInfo psi = new(command)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (string arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            Process process = new() { StartInfo = psi };
            if (!process.Start())
            {
                throw new InvalidOperationException("failed to start worker process");
            }

            return process;
        }

        private (string command, List<string> arguments) BuildWorkerCommand(string pipeName)
        {
            string entryLocation = Assembly.GetEntryAssembly()?.Location ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(entryLocation))
            {
                throw new InvalidOperationException("unable to determine entry assembly path for worker launch");
            }

            List<string> childArgs = BuildChildArgs(pipeName);
            if (entryLocation.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                List<string> dotnetArgs = new() { entryLocation };
                dotnetArgs.AddRange(childArgs);
                return ("dotnet", dotnetArgs);
            }

            return (entryLocation, childArgs);
        }

        private List<string> BuildChildArgs(string pipeName)
        {
            List<string> args = new();
            for (int i = 0; i < originalArgs.Length; i++)
            {
                string arg = originalArgs[i];
                if (string.Equals(arg, "--worker-mode", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < originalArgs.Length && !originalArgs[i + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        i++;
                    }
                    continue;
                }

                if (arg.StartsWith("--worker-mode=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(arg, "--worker-pipe", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    continue;
                }

                if (arg.StartsWith("--worker-pipe=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                args.Add(arg);
            }

            args.Add("--worker-mode");
            args.Add("--worker-pipe");
            args.Add(pipeName);
            return args;
        }

        private static Task PumpProcessOutput(StreamReader reader, string prefix, CancellationToken token)
        {
            return Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync().WaitAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (line is null)
                    {
                        return;
                    }

                    ConsoleHelper.SafeWriteLine($"{prefix} {line}", true, ConsoleColor.Gray);
                }
            }, token);
        }

        private void RegisterRestart()
        {
            DateTime now = DateTime.UtcNow;
            restartTimesUtc.Enqueue(now);
            while (restartTimesUtc.TryPeek(out DateTime old) &&
                   (now - old).TotalSeconds > RestartWindowSeconds)
            {
                restartTimesUtc.TryDequeue(out _);
            }
        }

        private bool ExceededRestartPolicy()
        {
            return restartTimesUtc.Count > MaxWorkerRestarts;
        }

        private static int GetRestartDelaySeconds(int attempt)
        {
            int[] schedule = { 1, 2, 5, 10, 20 };
            int idx = Math.Min(attempt - 1, schedule.Length - 1);
            return schedule[idx];
        }

        private static void StopWorker(Process worker)
        {
            try
            {
                if (!worker.HasExited)
                {
                    worker.Kill(entireProcessTree: true);
                    worker.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                ConsoleHelper.SafeWriteLine($"WARNING: Failed stopping worker process cleanly: {ex.Message}", true, ConsoleColor.DarkYellow);
            }
        }

        private static void SleepWithCancellation(TimeSpan duration, CancellationToken token)
        {
            try
            {
                Task.Delay(duration, token).Wait(token);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        }

        private static void WaitAllQuietly(params Task[] tasks)
        {
            foreach (Task task in tasks)
            {
                try
                {
                    task.Wait(TimeSpan.FromSeconds(1));
                }
                catch
                {
                    // ignore shutdown exceptions
                }
            }
        }
    }
}
