using System.Diagnostics;

namespace SOTAmatSkimmer
{
    internal static class WorkerHealthState
    {
        private static readonly object Sync = new();
        private static DateTime startedUtc = DateTime.UtcNow;
        private static DateTime? lastSourceMessageUtc;
        private static DateTime? lastDecodeHandledUtc;
        private static bool connected;
        private static int errorCount;
        private static string mode = "unknown";

        public static void Reset(string workerMode)
        {
            lock (Sync)
            {
                startedUtc = DateTime.UtcNow;
                lastSourceMessageUtc = null;
                lastDecodeHandledUtc = null;
                connected = false;
                errorCount = 0;
                mode = string.IsNullOrWhiteSpace(workerMode) ? "unknown" : workerMode;
            }
        }

        public static void SetConnected(bool isConnected)
        {
            lock (Sync)
            {
                connected = isConnected;
            }
        }

        public static void RecordSourceMessage()
        {
            lock (Sync)
            {
                lastSourceMessageUtc = DateTime.UtcNow;
            }
        }

        public static void RecordDecodeHandled()
        {
            lock (Sync)
            {
                lastDecodeHandledUtc = DateTime.UtcNow;
            }
        }

        public static void RecordError()
        {
            lock (Sync)
            {
                errorCount++;
            }
        }

        public static WorkerHealthSnapshot Snapshot()
        {
            lock (Sync)
            {
                return new WorkerHealthSnapshot(
                    ProcessId: Environment.ProcessId,
                    TimestampUtc: DateTime.UtcNow,
                    StartedUtc: startedUtc,
                    Mode: mode,
                    Connected: connected,
                    LastSourceMessageUtc: lastSourceMessageUtc,
                    LastDecodeHandledUtc: lastDecodeHandledUtc,
                    ErrorCount: errorCount,
                    WorkingSetBytes: Process.GetCurrentProcess().WorkingSet64);
            }
        }
    }

    internal sealed record WorkerHealthSnapshot(
        int ProcessId,
        DateTime TimestampUtc,
        DateTime StartedUtc,
        string Mode,
        bool Connected,
        DateTime? LastSourceMessageUtc,
        DateTime? LastDecodeHandledUtc,
        int ErrorCount,
        long WorkingSetBytes);

    internal sealed record WorkerIpcMessage(
        string MessageType,
        int ProcessId,
        DateTime TimestampUtc,
        DateTime StartedUtc,
        string Mode,
        bool Connected,
        DateTime? LastSourceMessageUtc,
        DateTime? LastDecodeHandledUtc,
        int ErrorCount,
        long WorkingSetBytes,
        string? Detail);
}
