# Architecture

## Decision

The planned desktop product uses C#/.NET 10, a WPF host, Blazor Hybrid UI, Leaflet `CRS.Simple`,
SQLite, and application-owned abstractions around Rust+.

Phase 0 contains the client boundary, real adapter, fake source, verification command, tests, and
documentation. Phase 1 adds the WPF/Blazor/Leaflet shell against fake data. Phase 2 adds SQLite
migrations, the server registry, and DPAPI-protected secret persistence. Phase 3 now includes a
single application-level Steam64 identity, manual per-server token entry, and an explicit read-only
connection/authentication test. Automated server pairing and entity (Smart Switch/Alarm/Storage
Monitor) pairing are both implemented via FCM push notifications; persistent selected-server
monitoring is implemented. Phase 4 uses the connection manager for
explicit `GetInfo` + `GetMap` operations, persists the latest successful snapshot, and renders the
real JPEG without exposing credentials to UI components. A persistent read-only session reuses one
authenticated connection for info, team, chat, and marker requests. Team/chat/marker results are
independent so a `NoTeam` chat response does not discard valid team positions or markers.

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

External Rust world files follow a separate boundary and never pass through the Rust+ adapter:

```text
Steam cache/log → IMapTopologyDiscovery ─┐
User-selected .map ──────────────────────┴→ IMapTopologyProvider → MapTopologyManager
                                                                  ↓
                                                     IMapTopologyRepository → map layers
```

The key rule is that `RustPlusApi.Data.*` and protobuf contracts do not cross the infrastructure
boundary. That makes a library upgrade, fork, or eventual protocol replacement local to one project.

## Current projects

- `RustPlusHelper.Application`: application-owned contract, normalized snapshots, redaction utility,
  and deterministic fake client.
- `RustPlusHelper.Infrastructure.RustPlus`: the pinned RustPlusApi adapter and mapper.
- `RustPlusHelper.Infrastructure.Map`: bounded version-10 Rust `.map` reader plus conservative Steam
  cache/log discovery; outputs only application-owned matches and display data.
- `RustPlusHelper.Infrastructure.Storage`: SQLite migrations/repositories, Windows DPAPI secret
  protection, a dependency-free rolling file logger (`Logging.FileLoggerProvider`), and diagnostics
  export/health checks (`Diagnostics.DiagnosticsExportService`/`DatabaseHealthCheck`) built on top of
  the `IHealthCheck` contract defined in `RustPlusHelper.Application`.
- `RustPlusHelper.Verification`: opt-in read-only protocol verification command.
- `RustPlusHelper.Desktop`: WPF host, Blazor map-first shell, and local Leaflet assets.
- `RustPlusHelper.Tests`: focused unit and adapter-mapping tests.
- `RustPlusHelper.Desktop.Tests`: bUnit component and interaction tests.
- `RustPlusHelper.Infrastructure.Storage.Tests`: real temporary-SQLite and current-user DPAPI tests.
- `installer/RustPlusHelper.Setup`: a WiX Toolset v5 project producing the per-machine MSI installer.
  Deliberately outside `RustPlusHelper.slnx` (release-time build, not part of the everyday build/test
  loop) — see `docs/development-plan.md` Phase 11 and `README.md`.

The desktop composition root references the application boundary plus the Rust+ and storage
infrastructure projects. Third-party types remain contained inside the Rust+ adapter. It retains a
`FakeRustPlusClient` for development/no-server mode and uses `RustPlusApiClientFactory` for saved-
server connection tests and live map downloads; replacing the adapter does not change UI components.

## Planned services

- `RustPlusConnectionManager` currently owns serialized, short-lived saved-server connection tests
  and full map downloads.
- `RustPlusLiveSessionManager` owns the selected server's persistent WebSocket, all low-cost polling,
  reconnect backoff, and bounded snapshot-diff/transport events; UI components own no timers. It also
  owns user-initiated camera viewing (`ViewCameraAsync`/`StopViewingCameraAsync` and the gated
  zoom/shoot/reload/look/move actions) on that same connection rather than a separate `CameraManager`
  service — a camera subscription is a single-connection-only resource (the server tracks one
  subscription per client), so it lives with the connection that already owns that constraint. Camera
  frames are pushed rather than polled: `IRustPlusClient.CameraFrameReceived` is the app's first
  event-based (not request/response) adapter surface, wrapping `RustPlusApi.Camera`'s
  `CameraController`/`CameraRenderer`. The frame→UI publish rate is throttled independent of how often
  the server actually broadcasts.
- `MapDashboardService` selects live, cached, or fake sources and exposes one canonical map state.
- live overlays and semantic events are memory-only. Polling and reconnection are centralized in the
  supervisor rather than UI timers.
- `IMapCacheRepository` stores the latest map/server snapshot per saved server for offline reopening.
- `MapTopologyManager` validates imported world size and `IMapTopologyRepository` persists only
  derived rasters/path metadata per server. `IMapTopologyDiscovery` only auto-selects a cache file
  when Rust's connection log names it or documented procedural size+seed is unique; same-size-only
  candidates stay manual.
- `RustPlusPollingScheduler` centrally budgets server requests.
- `RustPlusEventTranslator` and `SnapshotDiffer` emit direct, derived, or heuristic domain events.
- `ServerManager`, `TeamManager`, `MapManager`, `MarkerManager`, `MarketManager`, and `DeviceManager`
  maintain domain state. Camera viewing is implemented on `RustPlusLiveSessionManager` above rather
  than a separate `CameraManager`.
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

The current selected-server schedule is team every 5 seconds, chat and markers every 10 seconds, and
server info every 30 seconds. That averages about 0.43 request tokens/second versus the documented
3-token/second player refill. `NoTeam` chat failures back off to one minute; other non-transport
failures retry no faster than 30 seconds. Map data costs five tokens and is wipe/manual only.

Every emitted event will state its origin:

- `DirectBroadcast`
- `SnapshotDiff`
- `Transport`
- `Heuristic`
- `UserAction`
