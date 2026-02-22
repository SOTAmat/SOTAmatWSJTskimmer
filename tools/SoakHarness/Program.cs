using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using SOTAmatSkimmer;

namespace SoakHarness;

internal static class Program
{
    private static readonly string[] ConnectedMarkers =
    {
        "Connected to WSJT-X",
        "Connected to WSJT"
    };

    private static readonly string[] HeartbeatTimeoutMarkers =
    {
        "No heartbeat from WSJT-X",
        "No heartbeat from WSJT"
    };

    private static readonly string[] SocketBindErrorMarkers =
    {
        "Only one usage of each socket address",
        "Only one usage of each socket address (protocol/network address/port)"
    };

    private static readonly Regex WorkerStartedRegex = new(@"Supervisor started worker PID (\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private enum MarkerType
    {
        Connected,
        HeartbeatTimeout,
        SocketBindError,
        WorkerStarted,
        SupervisorNoHeartbeatRestart
    }

    private readonly record struct MarkerEvent(MarkerType Type, DateTime TimestampUtc, string Line, int? WorkerPid = null);

    private sealed record HostOptions(
        int Port,
        int HeartbeatTimeoutSeconds,
        int ReconnectIntervalSeconds,
        bool Debug);

    private sealed record HarnessOptions(
        int Port,
        int HeartbeatTimeoutSeconds,
        int ReconnectIntervalSeconds,
        int WarmupSeconds,
        int SilenceSeconds,
        int ResumeSeconds,
        int Cycles,
        int SettleSeconds,
        bool Debug,
        bool SupervisorRestartTest,
        bool SupervisorHangTest,
        int RestartTimeoutSeconds,
        int SupervisorHeartbeatTimeoutSeconds,
        int StopHeartbeatAfterSeconds,
        string AppDllPath);

    private static int Main(string[] args)
    {
        try
        {
            if (args.Any(a => string.Equals(a, "--host", StringComparison.OrdinalIgnoreCase)))
            {
                return RunHost(ParseHostOptions(args));
            }

            HarnessOptions options = ParseHarnessOptions(args);
            if (options.SupervisorRestartTest || options.SupervisorHangTest)
            {
                return RunSupervisorHarness(options).GetAwaiter().GetResult();
            }

            return RunHarness(options).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[harness] fatal: {ex}");
            return 2;
        }
    }

    private static HostOptions ParseHostOptions(string[] args)
    {
        return new HostOptions(
            Port: GetIntArg(args, "--port", 2237),
            HeartbeatTimeoutSeconds: GetIntArg(args, "--heartbeat-timeout", 6),
            ReconnectIntervalSeconds: GetIntArg(args, "--reconnect-interval", 15),
            Debug: HasFlag(args, "--debug"));
    }

    private static HarnessOptions ParseHarnessOptions(string[] args)
    {
        int heartbeatTimeout = GetIntArg(args, "--heartbeat-timeout", 6);
        return new HarnessOptions(
            Port: GetIntArg(args, "--port", 2237),
            HeartbeatTimeoutSeconds: heartbeatTimeout,
            ReconnectIntervalSeconds: GetIntArg(args, "--reconnect-interval", 15),
            WarmupSeconds: GetIntArg(args, "--warmup-seconds", 10),
            SilenceSeconds: GetIntArg(args, "--silence-seconds", heartbeatTimeout + 8),
            ResumeSeconds: GetIntArg(args, "--resume-seconds", 28),
            Cycles: GetIntArg(args, "--cycles", 1),
            SettleSeconds: GetIntArg(args, "--settle-seconds", 3),
            Debug: HasFlag(args, "--debug"),
            SupervisorRestartTest: HasFlag(args, "--supervisor-restart-test"),
            SupervisorHangTest: HasFlag(args, "--supervisor-hang-test"),
            RestartTimeoutSeconds: GetIntArg(args, "--restart-timeout-seconds", 45),
            SupervisorHeartbeatTimeoutSeconds: GetIntArg(args, "--test-supervisor-heartbeat-timeout", 12),
            StopHeartbeatAfterSeconds: GetIntArg(args, "--test-stop-heartbeat-after-seconds", 3),
            AppDllPath: GetStringArg(args, "--app-dll", string.Empty));
    }

    private static int RunHost(HostOptions options)
    {
        Console.WriteLine($"[host] starting WsjtxLooper on 127.0.0.1:{options.Port}, timeout={options.HeartbeatTimeoutSeconds}s");

        Configuration config = new()
        {
            Address = "127.0.0.1",
            Callsign = "SOAK",
            Password = "SOAK",
            Gridsquare = "AA00aa",
            Port = options.Port,
            Debug = options.Debug,
            Logging = false,
            Multicast = false,
            SparkSDRmode = false,
            HeartbeatTimeoutSeconds = options.HeartbeatTimeoutSeconds,
            ReconnectIntervalSeconds = options.ReconnectIntervalSeconds
        };

        WsjtxLooper looper = new(config);
        return looper.Loop();
    }

    private static async Task<int> RunHarness(HarnessOptions options)
    {
        Console.WriteLine("[harness] WSJT reconnect soak harness starting.");
        Console.WriteLine($"[harness] config: port={options.Port}, timeout={options.HeartbeatTimeoutSeconds}s, warmup={options.WarmupSeconds}s, silence={options.SilenceSeconds}s, resume={options.ResumeSeconds}s, cycles={options.Cycles}");

        string repoRoot = FindRepoRoot();
        (byte[] statusDatagram, byte[] heartbeatDatagram) = await LoadWsjtTestDatagrams(repoRoot).ConfigureAwait(false);

        using CancellationTokenSource cts = new();
        Process hostProcess = StartHostProcess(options);
        List<Task> outputTasks = new();
        ConcurrentQueue<MarkerEvent> markerEvents = new();

        outputTasks.Add(PumpProcessOutput(hostProcess.StandardOutput, "[host]", markerEvents, cts.Token));
        outputTasks.Add(PumpProcessOutput(hostProcess.StandardError, "[host:err]", markerEvents, cts.Token));

        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token).ConfigureAwait(false);

        int passedCycles = 0;

        try
        {
            for (int cycle = 1; cycle <= options.Cycles; cycle++)
            {
                DateTime cycleStart = DateTime.UtcNow;
                Console.WriteLine($"[harness] cycle {cycle}/{options.Cycles}: warmup traffic");
                await SendDatagramsForDuration(options.Port, statusDatagram, heartbeatDatagram, options.WarmupSeconds, cts.Token).ConfigureAwait(false);

                Console.WriteLine($"[harness] cycle {cycle}/{options.Cycles}: silence window");
                await Task.Delay(TimeSpan.FromSeconds(options.SilenceSeconds), cts.Token).ConfigureAwait(false);

                Console.WriteLine($"[harness] cycle {cycle}/{options.Cycles}: resume traffic");
                await SendDatagramsForDuration(options.Port, statusDatagram, heartbeatDatagram, options.ResumeSeconds, cts.Token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(options.SettleSeconds), cts.Token).ConfigureAwait(false);

                DateTime cycleEnd = DateTime.UtcNow;
                bool cyclePassed = EvaluateCycle(markerEvents, cycleStart, cycleEnd);
                Console.WriteLine($"[harness] cycle {cycle}/{options.Cycles}: {(cyclePassed ? "PASS" : "FAIL")}");
                if (cyclePassed)
                {
                    passedCycles++;
                }
            }
        }
        finally
        {
            cts.Cancel();
            StopProcess(hostProcess);
            try
            {
                await Task.WhenAll(outputTasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected during shutdown
            }
        }

        Console.WriteLine($"[harness] completed. passed {passedCycles}/{options.Cycles} cycle(s).");
        return passedCycles == options.Cycles ? 0 : 1;
    }

    private static async Task<int> RunSupervisorHarness(HarnessOptions options)
    {
        Console.WriteLine("[harness] Supervisor restart harness starting.");

        string repoRoot = FindRepoRoot();
        string appDllPath = ResolveAppDllPath(repoRoot, options.AppDllPath);
        (byte[] statusDatagram, byte[] heartbeatDatagram) = await LoadWsjtTestDatagrams(repoRoot).ConfigureAwait(false);

        int checks = 0;
        int passed = 0;

        if (options.SupervisorRestartTest)
        {
            checks++;
            bool ok = await RunSupervisorKillRestartCheck(options, appDllPath, statusDatagram, heartbeatDatagram).ConfigureAwait(false);
            Console.WriteLine($"[harness] supervisor restart-by-exit check: {(ok ? "PASS" : "FAIL")}");
            if (ok)
            {
                passed++;
            }
        }

        if (options.SupervisorHangTest)
        {
            checks++;
            bool ok = await RunSupervisorHeartbeatHangCheck(options, appDllPath).ConfigureAwait(false);
            Console.WriteLine($"[harness] supervisor restart-by-heartbeat-stall check: {(ok ? "PASS" : "FAIL")}");
            if (ok)
            {
                passed++;
            }
        }

        if (checks == 0)
        {
            Console.WriteLine("[harness] no supervisor checks selected.");
            return 2;
        }

        Console.WriteLine($"[harness] supervisor checks complete. passed {passed}/{checks}.");
        return passed == checks ? 0 : 1;
    }

    private static async Task<bool> RunSupervisorKillRestartCheck(
        HarnessOptions options,
        string appDllPath,
        byte[] statusDatagram,
        byte[] heartbeatDatagram)
    {
        using CancellationTokenSource cts = new();
        ConcurrentQueue<MarkerEvent> markerEvents = new();

        Process appProcess = StartAppProcess(options, appDllPath, injectHeartbeatStall: false);
        List<Task> outputTasks =
        [
            PumpProcessOutput(appProcess.StandardOutput, "[app]", markerEvents, cts.Token),
            PumpProcessOutput(appProcess.StandardError, "[app:err]", markerEvents, cts.Token)
        ];

        try
        {
            MarkerEvent? firstWorker = await WaitForMarker(
                markerEvents,
                m => m.Type == MarkerType.WorkerStarted && m.WorkerPid.HasValue,
                TimeSpan.FromSeconds(options.RestartTimeoutSeconds),
                cts.Token).ConfigureAwait(false);

            if (!firstWorker.HasValue)
            {
                Console.WriteLine("[harness] did not observe initial worker start.");
                return false;
            }

            int firstPid = firstWorker.Value.WorkerPid!.Value;
            await SendDatagramsForDuration(options.Port, statusDatagram, heartbeatDatagram, 4, cts.Token).ConfigureAwait(false);

            try
            {
                Process.GetProcessById(firstPid).Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[harness] failed to kill worker pid {firstPid}: {ex.Message}");
                return false;
            }

            MarkerEvent? secondWorker = await WaitForMarker(
                markerEvents,
                m => m.Type == MarkerType.WorkerStarted && m.WorkerPid.HasValue && m.WorkerPid.Value != firstPid && m.TimestampUtc > firstWorker.Value.TimestampUtc,
                TimeSpan.FromSeconds(options.RestartTimeoutSeconds),
                cts.Token).ConfigureAwait(false);

            if (!secondWorker.HasValue)
            {
                Console.WriteLine("[harness] did not observe replacement worker start after kill.");
                return false;
            }

            return true;
        }
        finally
        {
            cts.Cancel();
            StopProcess(appProcess);
            await WaitAllQuietly(outputTasks).ConfigureAwait(false);
        }
    }

    private static async Task<bool> RunSupervisorHeartbeatHangCheck(HarnessOptions options, string appDllPath)
    {
        using CancellationTokenSource cts = new();
        ConcurrentQueue<MarkerEvent> markerEvents = new();

        Process appProcess = StartAppProcess(options, appDllPath, injectHeartbeatStall: true);
        List<Task> outputTasks =
        [
            PumpProcessOutput(appProcess.StandardOutput, "[app]", markerEvents, cts.Token),
            PumpProcessOutput(appProcess.StandardError, "[app:err]", markerEvents, cts.Token)
        ];

        try
        {
            MarkerEvent? firstWorker = await WaitForMarker(
                markerEvents,
                m => m.Type == MarkerType.WorkerStarted && m.WorkerPid.HasValue,
                TimeSpan.FromSeconds(options.RestartTimeoutSeconds),
                cts.Token).ConfigureAwait(false);

            if (!firstWorker.HasValue)
            {
                Console.WriteLine("[harness] did not observe initial worker start for heartbeat stall test.");
                return false;
            }

            int timeoutSeconds = Math.Max(5, options.SupervisorHeartbeatTimeoutSeconds);
            TimeSpan reasonWait = TimeSpan.FromSeconds(timeoutSeconds + Math.Max(1, options.StopHeartbeatAfterSeconds) + 20);

            MarkerEvent? reason = await WaitForMarker(
                markerEvents,
                m => m.Type == MarkerType.SupervisorNoHeartbeatRestart && m.TimestampUtc > firstWorker.Value.TimestampUtc,
                reasonWait,
                cts.Token).ConfigureAwait(false);

            if (!reason.HasValue)
            {
                Console.WriteLine("[harness] did not observe supervisor no-heartbeat restart reason.");
                return false;
            }

            MarkerEvent? secondWorker = await WaitForMarker(
                markerEvents,
                m => m.Type == MarkerType.WorkerStarted && m.WorkerPid.HasValue && m.WorkerPid.Value != firstWorker.Value.WorkerPid && m.TimestampUtc > reason.Value.TimestampUtc,
                TimeSpan.FromSeconds(options.RestartTimeoutSeconds),
                cts.Token).ConfigureAwait(false);

            if (!secondWorker.HasValue)
            {
                Console.WriteLine("[harness] did not observe replacement worker after heartbeat stall restart.");
                return false;
            }

            return true;
        }
        finally
        {
            cts.Cancel();
            StopProcess(appProcess);
            await WaitAllQuietly(outputTasks).ConfigureAwait(false);
        }
    }

    private static Process StartHostProcess(HarnessOptions options)
    {
        string dllPath = Assembly.GetExecutingAssembly().Location;
        ProcessStartInfo psi = new("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add(dllPath);
        psi.ArgumentList.Add("--host");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(options.Port.ToString());
        psi.ArgumentList.Add("--heartbeat-timeout");
        psi.ArgumentList.Add(options.HeartbeatTimeoutSeconds.ToString());
        psi.ArgumentList.Add("--reconnect-interval");
        psi.ArgumentList.Add(options.ReconnectIntervalSeconds.ToString());
        if (options.Debug)
        {
            psi.ArgumentList.Add("--debug");
        }

        Process process = new() { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException("failed to start host process");
        }

        return process;
    }

    private static Process StartAppProcess(HarnessOptions options, string appDllPath, bool injectHeartbeatStall)
    {
        ProcessStartInfo psi = new("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add(appDllPath);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("SOAK");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("SOAK");
        psi.ArgumentList.Add("-g");
        psi.ArgumentList.Add("AA00aa");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(options.Port.ToString());
        psi.ArgumentList.Add("--heartbeat-timeout");
        psi.ArgumentList.Add(options.HeartbeatTimeoutSeconds.ToString());
        psi.ArgumentList.Add("--reconnect-interval");
        psi.ArgumentList.Add(options.ReconnectIntervalSeconds.ToString());
        psi.ArgumentList.Add("--test-skip-auth");

        if (injectHeartbeatStall)
        {
            psi.ArgumentList.Add("--test-stop-heartbeat-after-seconds");
            psi.ArgumentList.Add(Math.Max(1, options.StopHeartbeatAfterSeconds).ToString());
            psi.ArgumentList.Add("--test-supervisor-heartbeat-timeout");
            psi.ArgumentList.Add(Math.Max(5, options.SupervisorHeartbeatTimeoutSeconds).ToString());
        }

        Process process = new() { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException("failed to start app process");
        }

        return process;
    }

    private static async Task PumpProcessOutput(
        StreamReader reader,
        string prefix,
        ConcurrentQueue<MarkerEvent> markerEvents,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync().WaitAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            Console.WriteLine($"{prefix} {line}");
            TryCaptureMarker(line, markerEvents);
        }
    }

    private static void TryCaptureMarker(string line, ConcurrentQueue<MarkerEvent> markerEvents)
    {
        DateTime now = DateTime.UtcNow;
        if (ConnectedMarkers.Any(m => line.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            markerEvents.Enqueue(new MarkerEvent(MarkerType.Connected, now, line));
        }

        if (HeartbeatTimeoutMarkers.Any(m => line.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            markerEvents.Enqueue(new MarkerEvent(MarkerType.HeartbeatTimeout, now, line));
        }

        if (SocketBindErrorMarkers.Any(m => line.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            markerEvents.Enqueue(new MarkerEvent(MarkerType.SocketBindError, now, line));
        }

        Match match = WorkerStartedRegex.Match(line);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int workerPid))
        {
            markerEvents.Enqueue(new MarkerEvent(MarkerType.WorkerStarted, now, line, workerPid));
        }

        if (line.Contains("Reason: no worker heartbeat", StringComparison.OrdinalIgnoreCase))
        {
            markerEvents.Enqueue(new MarkerEvent(MarkerType.SupervisorNoHeartbeatRestart, now, line));
        }
    }

    private static bool EvaluateCycle(
        ConcurrentQueue<MarkerEvent> markerEvents,
        DateTime cycleStartUtc,
        DateTime cycleEndUtc)
    {
        List<MarkerEvent> inWindow = markerEvents
            .Where(e => e.TimestampUtc >= cycleStartUtc && e.TimestampUtc <= cycleEndUtc)
            .OrderBy(e => e.TimestampUtc)
            .ToList();

        bool sawTimeout = false;
        bool sawReconnectAfterTimeout = false;
        bool sawBindError = false;

        foreach (MarkerEvent marker in inWindow)
        {
            if (marker.Type == MarkerType.HeartbeatTimeout)
            {
                sawTimeout = true;
            }
            else if (marker.Type == MarkerType.Connected && sawTimeout)
            {
                sawReconnectAfterTimeout = true;
            }
            else if (marker.Type == MarkerType.SocketBindError)
            {
                sawBindError = true;
            }
        }

        return sawTimeout && sawReconnectAfterTimeout && !sawBindError;
    }

    private static async Task<MarkerEvent?> WaitForMarker(
        ConcurrentQueue<MarkerEvent> markerEvents,
        Func<MarkerEvent, bool> predicate,
        TimeSpan timeout,
        CancellationToken ct)
    {
        DateTime deadlineUtc = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadlineUtc && !ct.IsCancellationRequested)
        {
            foreach (MarkerEvent marker in markerEvents.ToArray().OrderBy(m => m.TimestampUtc))
            {
                if (predicate(marker))
                {
                    return marker;
                }
            }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task SendDatagramsForDuration(
        int port,
        byte[] statusDatagram,
        byte[] heartbeatDatagram,
        int seconds,
        CancellationToken ct)
    {
        using UdpClient sender = new();
        IPEndPoint endpoint = new(IPAddress.Loopback, port);

        for (int i = 0; i < seconds; i++)
        {
            ct.ThrowIfCancellationRequested();
            await sender.SendAsync(statusDatagram, statusDatagram.Length, endpoint).ConfigureAwait(false);
            await sender.SendAsync(heartbeatDatagram, heartbeatDatagram.Length, endpoint).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }

    private static void StopProcess(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[harness] warning: failed stopping process cleanly: {ex.Message}");
        }
    }

    private static async Task WaitAllQuietly(IEnumerable<Task> tasks)
    {
        foreach (Task task in tasks)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // ignore shutdown exceptions
            }
        }
    }

    private static string ResolveAppDllPath(string repoRoot, string requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            return Path.GetFullPath(requestedPath);
        }

        string[] candidates =
        {
            Path.Combine(repoRoot, "SOTAmatSkimmer", "bin", "Debug", "net10.0", "SOTAmatSkimmer.dll"),
            Path.Combine(repoRoot, "SOTAmatSkimmer", "bin", "Release", "net10.0", "SOTAmatSkimmer.dll")
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate SOTAmatSkimmer.dll. Build the app or pass --app-dll <path>.");
    }

    private static async Task<(byte[] statusDatagram, byte[] heartbeatDatagram)> LoadWsjtTestDatagrams(string repoRoot)
    {
        string heartbeatDatagramPath = Path.Combine(repoRoot, "libs", "m0lte", "WsjtxUdpLibTests", "jtdx_HEARTBEAT_MESSAGE_TYPE.bin");
        string statusDatagramPath = Path.Combine(repoRoot, "libs", "m0lte", "WsjtxUdpLibTests", "jtdx_STATUS_MESSAGE_TYPE.bin");

        if (!File.Exists(heartbeatDatagramPath) || !File.Exists(statusDatagramPath))
        {
            throw new FileNotFoundException("required WSJT test datagram files were not found");
        }

        byte[] heartbeatDatagram = await File.ReadAllBytesAsync(heartbeatDatagramPath).ConfigureAwait(false);
        byte[] statusDatagram = await File.ReadAllBytesAsync(statusDatagramPath).ConfigureAwait(false);
        return (statusDatagram, heartbeatDatagram);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SOTAmatSkimmer.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("could not locate repository root (missing SOTAmatSkimmer.sln)");
    }

    private static int GetIntArg(string[] args, string key, int defaultValue)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"missing value for {key}");
            }

            if (!int.TryParse(args[i + 1], out int value))
            {
                throw new ArgumentException($"invalid integer value for {key}: {args[i + 1]}");
            }

            return value;
        }

        return defaultValue;
    }

    private static string GetStringArg(string[] args, string key, string defaultValue)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"missing value for {key}");
            }

            return args[i + 1];
        }

        return defaultValue;
    }

    private static bool HasFlag(string[] args, string key)
        => args.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
}
