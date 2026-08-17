# Architecture

## Decision

The planned desktop product uses C#/.NET 10, a WPF host, Blazor Hybrid UI, Leaflet `CRS.Simple`,
SQLite, and application-owned abstractions around Rust+.

Phase 0 intentionally contains only the client boundary, real adapter, fake source, verification
command, tests, and documentation. It does not contain WPF, Blazor, SQLite, pairing UI, background
monitoring, or notifications yet.

## Dependency direction

```text
RustPlusApi / WebSocket / protobuf
                ↓
Infrastructure.RustPlus adapter
                ↓
Application IRustPlusClient + canonical snapshots
                ↓
Connection, map, team, market, device, event services
                ↓
SQLite repositories and live state
                ↓
Blazor Hybrid UI
```

The key rule is that `RustPlusApi.Data.*` and protobuf contracts do not cross the infrastructure
boundary. That makes a library upgrade, fork, or eventual protocol replacement local to one project.

## Current projects

- `RustPlusHelper.Application`: application-owned contract, normalized snapshots, redaction utility,
  and deterministic fake client.
- `RustPlusHelper.Infrastructure.RustPlus`: the pinned RustPlusApi adapter and mapper.
- `RustPlusHelper.Verification`: opt-in read-only protocol verification command.
- `RustPlusHelper.Tests`: focused unit and adapter-mapping tests.

## Planned services

- `RustPlusConnectionManager` owns per-server supervisors.
- `RustPlusPollingScheduler` centrally budgets server requests.
- `RustPlusEventTranslator` and `SnapshotDiffer` emit direct, derived, or heuristic domain events.
- `ServerManager`, `TeamManager`, `MapManager`, `MarkerManager`, `MarketManager`, `DeviceManager`, and
  `CameraManager` maintain domain state.
- `NotificationManager` evaluates rules and sends messages through `INotificationChannel`.
- SQLite repositories store server/session state and bounded semantic history.

## Connection state model

```text
Unpaired → Disconnected → Connecting → SocketConnected → Validating → Ready
                                                ↓              ↓
                                          ProtocolError   AuthRejected
                                                               ↑
Ready → ConnectionLost → Reconnecting ─────────────────────────┘
```

WebSocket-open is not authenticated-ready. A low-cost authenticated call such as server information
must complete before entering `Ready`.

## Data acquisition

Rust+ supplies broadcasts for some changes but requires polling for other state. Managers must not
create independent timers. A central scheduler will account for documented request costs and use
broadcasts plus snapshot comparison.

Every emitted event will state its origin:

- `DirectBroadcast`
- `SnapshotDiff`
- `Transport`
- `Heuristic`
- `UserAction`
