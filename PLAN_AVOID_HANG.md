# PLAN: Avoid Hang-Like Failures Without Modifying 3rd-Party Code

## Objective
Prevent permanent hang-like behavior when WSJT-X/SparkSDR adapters or 3rd-party loops stall, while keeping 3rd-party source unmodified.

## Key Constraint
If a 3rd-party library hangs inside an endless loop in the same process/thread context, your code cannot safely preempt or abort that thread in modern .NET.
Reliable recovery requires process-level isolation so a supervisor can terminate and replace a stuck worker process.

## Final Runtime Architecture (Implemented)
1. Supervisor process (default app entry mode)
- Owns lifecycle, watchdogs, restart policy, and operator-visible logging.
- Launches one worker child process.
- Reads worker health via IPC.
- Kills/restarts worker on hang or unhealthy state.

2. Worker process (internal mode)
- Runs WSJT-X or SparkSDR adapter code, including 3rd-party library loops.
- Emits heartbeat and health snapshots to supervisor.

3. IPC
- Named pipe between supervisor and worker.
- Worker sends heartbeat/event messages (mode, connected state, last source message timestamp, errors).

## Agreed Hard-Coded Defaults
These are fixed defaults in code (not exposed as user-facing runtime switches):
- Process isolation: always enabled in normal app mode.
- Worker heartbeat timeout: 60 seconds.
- Worker source stale timeout (WSJT mode): 120 seconds.
- Worker source stale timeout (SparkSDR mode): disabled.
- Max worker restarts: 10.
- Restart policy window: 300 seconds.
- Restart backoff: 1s, 2s, 5s, 10s, 20s.

## Hang Detection and Recovery Policy
1. Worker heartbeat stale (>60s)
- Supervisor treats worker as hung.
- Supervisor terminates worker process tree and starts a new worker.

2. Worker alive but WSJT source messages stale (>120s while connected)
- Supervisor restarts worker.

3. Worker exits unexpectedly
- Supervisor restarts worker with backoff.

4. Restart storm (>10 restarts within 300s)
- Supervisor stops and returns failure instead of endless thrashing.

## User-Facing Simplicity
- No public process-isolation tuning flags are required for normal use.
- Internal-only flags remain hidden for worker IPC wiring and test harness injection.

## Current Implementation Locations
- `SOTAmatSkimmer/Program.cs`
- `SOTAmatSkimmer/WorkerSupervisor.cs`
- `SOTAmatSkimmer/WorkerIpcReporter.cs`
- `SOTAmatSkimmer/WorkerHealthState.cs`
- `SOTAmatSkimmer/WSJTloop.cs`
- `SOTAmatSkimmer/SparkSDRloop.cs`
- `SOTAmatSkimmer/SOTAmatClient.cs`

## Verification Strategy
1. Build and baseline reconnect soak:
- `dotnet build .\SOTAmatSkimmer.sln -c Debug`
- `dotnet run --project .\tools\SoakHarness\SoakHarness.csproj -- --cycles 3`

2. Supervisor recovery checks:
- Restart on forced worker exit.
- Restart on worker heartbeat stall.

## Result
With supervisor/worker process isolation and hard-coded watchdog defaults, a hung 3rd-party endless loop is recoverable without patching upstream library code.
