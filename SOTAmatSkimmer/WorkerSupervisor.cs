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
        private const int UnixDomainSocketPathMaxLength = 104;
        private const string UnixPipePrefix = "CoreFxPipe_";

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
            SupervisorWriteLine("Process isolation enabled. Starting supervisor mode.", true, ConsoleColor.Cyan);

            using CancellationTokenSource shutdownCts = new();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                shutdownCts.Cancel();
            };

            int restartAttempt = 0;
            while (!shutdownCts.IsCancellationRequested)
            {
                string pipeName = BuildWorkerPipeName();
                using NamedPipeServerStream pipeServer = new(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                using Process worker = StartWorkerProcess(pipeName);

                SupervisorWriteLine($"Started worker PID {worker.Id}.", true, ConsoleColor.Green);
                using CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownCts.Token);

                DateTime lastHeartbeatUtc = DateTime.UtcNow;
                DateTime? lastSourceUtc = null;
                bool workerConnected = false;
                string workerMode = config.SparkSDRmode ? "sparksdr" : "wsjt";

                Task? readTask = null;
                Task? outputTask = PumpWorkerStdoutAsync(worker.StandardOutput, runCts.Token);
                Task? errorTask = PumpWorkerStderrAsync(worker.StandardError, runCts.Token);

                try
                {
                    if (!WaitForPipeConnection(pipeServer, runCts.Token))
                    {
                        if (shutdownCts.IsCancellationRequested)
                        {
                            return 0;
                        }

                        SupervisorWriteLine("Restarting worker because IPC connection was not established.", true, ConsoleColor.Yellow);
                        StopWorker(worker);
                        runCts.Cancel();
                        WaitAllQuietly(outputTask, errorTask);

                        restartAttempt++;
                        RegisterRestart();
                        if (ExceededRestartPolicy())
                        {
                            SupervisorWriteLine($"Exceeded max worker restarts ({MaxWorkerRestarts}) within {RestartWindowSeconds} seconds. Stopping.", true, ConsoleColor.Red);
                            return 1;
                        }

                        int initialDelaySeconds = GetRestartDelaySeconds(restartAttempt);
                        SupervisorWriteLine($"Backoff delay: {initialDelaySeconds} second(s) before next worker start.", true, ConsoleColor.DarkYellow);
                        SleepWithCancellation(TimeSpan.FromSeconds(initialDelaySeconds), shutdownCts.Token);
                        continue;
                    }

                    readTask = Task.Run(async () =>
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
                                SupervisorWriteLine($"Worker event: {msg.MessageType} {msg.Detail}", true, ConsoleColor.DarkCyan);
                            }
                        }
                    }, runCts.Token);

                    string? reason = MonitorWorker(worker, () => lastHeartbeatUtc, () => workerConnected, () => lastSourceUtc, () => workerMode, runCts.Token);
                    if (shutdownCts.IsCancellationRequested)
                    {
                        StopWorker(worker);
                        runCts.Cancel();
                        WaitAllQuietly(readTask, outputTask, errorTask);
                        return 0;
                    }

                    SupervisorWriteLine($"Restarting worker. Reason: {reason}", true, ConsoleColor.Yellow);
                    StopWorker(worker);
                    runCts.Cancel();
                    WaitAllQuietly(readTask, outputTask, errorTask);

                    if (worker.ExitCode == 2)
                    {
                        SupervisorWriteLine("Worker exited due to configuration/authentication failure. Stopping supervisor.", true, ConsoleColor.Red);
                        return 2;
                    }

                    restartAttempt++;
                    RegisterRestart();
                    if (ExceededRestartPolicy())
                    {
                        SupervisorWriteLine($"Exceeded max worker restarts ({MaxWorkerRestarts}) within {RestartWindowSeconds} seconds. Stopping.", true, ConsoleColor.Red);
                        return 1;
                    }

                    int delaySeconds = GetRestartDelaySeconds(restartAttempt);
                    SupervisorWriteLine($"Backoff delay: {delaySeconds} second(s) before next worker start.", true, ConsoleColor.DarkYellow);
                    SleepWithCancellation(TimeSpan.FromSeconds(delaySeconds), shutdownCts.Token);
                }
                finally
                {
                    runCts.Cancel();
                    WaitAllQuietly(readTask, outputTask, errorTask);
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
                    SupervisorWriteLine("Worker IPC pipe connection timed out.", true, ConsoleColor.Red);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                SupervisorWriteLine($"Worker IPC connection error: {ex.Message}", true, ConsoleColor.Red);
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
#if DEBUG
            int heartbeatTimeout = config.TestSupervisorHeartbeatTimeoutSeconds > 0
                ? config.TestSupervisorHeartbeatTimeoutSeconds
                : WorkerHeartbeatTimeoutSeconds;
#else
            int heartbeatTimeout = WorkerHeartbeatTimeoutSeconds;
#endif
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
                SupervisorWriteLine($"WARNING: Unable to parse worker IPC message: {line}", true, ConsoleColor.DarkYellow);
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

        private static Task PumpWorkerStdoutAsync(StreamReader reader, CancellationToken token)
        {
            return Task.Run(async () =>
            {
                char[] buffer = new char[256];
                while (!token.IsCancellationRequested)
                {
                    int charsRead;
                    try
                    {
                        charsRead = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    if (charsRead == 0)
                    {
                        return;
                    }

                    ConsoleHelper.SafeWriteRaw(new string(buffer, 0, charsRead));
                }
            }, token);
        }

        private static Task PumpWorkerStderrAsync(StreamReader reader, CancellationToken token)
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
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    if (line is null)
                    {
                        return;
                    }

                    ConsoleHelper.SafeWriteLine($"[Worker stderr] {line}", false, ConsoleColor.DarkYellow);
                }
            }, token);
        }

        private (string command, List<string> arguments) BuildWorkerCommand(string pipeName)
        {
            List<string> childArgs = BuildChildArgs(pipeName);
            string? processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                processPath = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (!string.IsNullOrWhiteSpace(processPath) &&
                !processPath.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase) &&
                !processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                return (processPath, childArgs);
            }

            string? entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
            if (!string.IsNullOrWhiteSpace(entryAssemblyName))
            {
                string entryDllPath = Path.Combine(AppContext.BaseDirectory, $"{entryAssemblyName}.dll");
                if (File.Exists(entryDllPath))
                {
                    List<string> dotnetArgs = new() { entryDllPath };
                    dotnetArgs.AddRange(childArgs);
                    return (string.IsNullOrWhiteSpace(processPath) ? "dotnet" : processPath, dotnetArgs);
                }
            }

            throw new InvalidOperationException("unable to determine entry assembly path for worker launch");
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
            return restartTimesUtc.Count >= MaxWorkerRestarts;
        }

        private static int GetRestartDelaySeconds(int attempt)
        {
            int[] schedule = { 1, 2, 5, 10, 20 };
            int idx = Math.Min(attempt - 1, schedule.Length - 1);
            return schedule[idx];
        }

        private static string BuildWorkerPipeName()
        {
            // Keep the identifier short enough for Unix-domain socket path limits.
            string randomSuffix = Guid.NewGuid().ToString("N")[..8];
            string pipeName = $"sm-{Environment.ProcessId:x}-{randomSuffix}";

            if (OperatingSystem.IsWindows())
            {
                return pipeName;
            }

            int maxPipeNameLength = GetUnixPipeNameLengthLimit();
            if (pipeName.Length <= maxPipeNameLength)
            {
                return pipeName;
            }

            string compactPipeName = $"s{Environment.ProcessId:x}{randomSuffix}";
            if (compactPipeName.Length <= maxPipeNameLength)
            {
                return compactPipeName;
            }

            return compactPipeName[..Math.Max(1, maxPipeNameLength)];
        }

        private static int GetUnixPipeNameLengthLimit()
        {
            string tempPath = Path.GetTempPath();
            bool needsSeparator = !tempPath.EndsWith(Path.DirectorySeparatorChar) &&
                                  !tempPath.EndsWith(Path.AltDirectorySeparatorChar);
            int separatorLength = needsSeparator ? 1 : 0;
            int maxPipeNameLength = UnixDomainSocketPathMaxLength - tempPath.Length - separatorLength - UnixPipePrefix.Length;
            return Math.Max(1, maxPipeNameLength);
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
                SupervisorWriteLine($"WARNING: Failed stopping worker process cleanly: {ex.Message}", true, ConsoleColor.DarkYellow);
            }
        }

        private static void SupervisorWriteLine(string message = "", bool dateStamp = true, ConsoleColor? color = null)
        {
            ConsoleHelper.SafeWriteLine($"[Supervisor] {message}", dateStamp, color);
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

        private static void WaitAllQuietly(params Task?[] tasks)
        {
            foreach (Task? task in tasks)
            {
                if (task is null)
                {
                    continue;
                }

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
