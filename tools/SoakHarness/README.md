# WSJT Recovery Soak Harness

This harness validates recovery behavior in `WsjtxLooper` without requiring WSJT-X to run.

It does three things:
1. Starts a child process that hosts `WsjtxLooper`.
2. Sends real WSJT test datagrams (from `libs/m0lte/WsjtxUdpLibTests/*.bin`) over UDP.
3. Runs traffic phases per cycle:
- warmup traffic
- silence window (to force heartbeat timeout)
- resumed traffic (to verify reconnect)

Each cycle passes only if the host output contains:
- a heartbeat-timeout marker, then
- a later reconnect marker,
- and no socket-bind contention error (`Only one usage of each socket address`).

## Run

```powershell
dotnet run --project .\tools\SoakHarness\SoakHarness.csproj -- --cycles 3
```

Useful options:
- `--heartbeat-timeout <seconds>` (default: `6`)
- `--warmup-seconds <seconds>` (default: `10`)
- `--silence-seconds <seconds>` (default: `heartbeat-timeout + 8`)
- `--resume-seconds <seconds>` (default: `28`)
- `--cycles <count>` (default: `1`)
- `--debug`

Exit code:
- `0` all cycles pass
- `1` one or more cycles fail
- `2` harness/setup error

## Supervisor Checks

Validate process-isolation restart behavior directly:

1. Worker-exit restart check:
```powershell
dotnet run --project .\tools\SoakHarness\SoakHarness.csproj -- --supervisor-restart-test
```

2. Heartbeat-stall restart check:
```powershell
dotnet run --project .\tools\SoakHarness\SoakHarness.csproj -- --supervisor-hang-test
```

3. Run both:
```powershell
dotnet run --project .\tools\SoakHarness\SoakHarness.csproj -- --supervisor-restart-test --supervisor-hang-test
```

Optional supervisor-check args:
- `--restart-timeout-seconds <seconds>` (default: `45`)
- `--test-supervisor-heartbeat-timeout <seconds>` (default: `12`)
- `--test-stop-heartbeat-after-seconds <seconds>` (default: `3`)
- `--app-dll <full path to SOTAmatSkimmer.dll>` (optional override)

## Expected Results

Worker-exit restart check passes when all are true:
- Supervisor logs an initial worker start: `[Supervisor] Started worker PID <pid1>.`.
- Harness kills `<pid1>`.
- Supervisor logs a replacement worker with a different PID: `[Supervisor] Started worker PID <pid2>.`.
- Harness prints: `supervisor restart-by-exit check: PASS`.

Heartbeat-stall restart check passes when all are true:
- Supervisor logs an initial worker start.
- Worker test mode stops heartbeats after the injected delay.
- Supervisor logs restart reason containing: `Reason: no worker heartbeat`.
- Supervisor logs a replacement worker start with a different PID.
- Harness prints: `supervisor restart-by-heartbeat-stall check: PASS`.

Combined supervisor run passes when:
- Final summary is `passed 2/2`.
- Process exit code is `0`.

Failure indicators:
- No replacement worker PID is observed before timeout.
- Restart reason marker is not observed for heartbeat-stall test.
- Final summary reports fewer passed checks than selected checks.
