# Architecture

## Decision

The planned desktop product uses C#/.NET 10, a WPF host, Blazor Hybrid UI, Leaflet `CRS.Simple`,
SQLite, and application-owned abstractions around Rust+.

Phase 0 contains the client boundary, real adapter, fake source, verification command, tests, and
documentation. Phase 1 adds the WPF/Blazor/Leaflet shell against fake data. Phase 2 adds SQLite
migrations, the server registry, and DPAPI-protected secret persistence. Phase 3 now includes a
single application-level Steam64 identity, manual per-server token entry, and an explicit read-only
connection/authentication test. Automated pairing, persistent connection supervision, background
monitoring, and notifications are not implemented yet.

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
- `RustPlusHelper.Infrastructure.Storage`: SQLite migrations/repositories and Windows DPAPI secret
  protection.
- `RustPlusHelper.Verification`: opt-in read-only protocol verification command.
- `RustPlusHelper.Desktop`: WPF host, Blazor map-first shell, and local Leaflet assets.
- `RustPlusHelper.Tests`: focused unit and adapter-mapping tests.
- `RustPlusHelper.Desktop.Tests`: bUnit component and interaction tests.
- `RustPlusHelper.Infrastructure.Storage.Tests`: real temporary-SQLite and current-user DPAPI tests.

The desktop composition root references the application boundary plus the Rust+ and storage
infrastructure projects. Third-party types remain contained inside the Rust+ adapter. It injects
`FakeRustPlusClient` for the still-demonstration map and `RustPlusApiClientFactory` only for explicit
saved-server connection tests; replacing the adapter does not change UI components.

## Planned services

- `RustPlusConnectionManager` currently owns serialized, short-lived saved-server connection tests;
  it will grow into per-server supervisors without moving socket lifetime into UI components.
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

Each attempt uses exactly the transport stored on the profile. Secure proxy is the default. Direct
`ws://` requires an explicit UI choice with a plaintext-credential warning, and a failed proxy
attempt is never retried through direct transport.

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
