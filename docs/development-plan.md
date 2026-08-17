# Development plan

## Phase 0 — Protocol verification

**Goal:** Prove the pinned C# Rust+ adapter against source and a real paired server.

**Current deliverables:** application-owned client boundary, RustPlusApi adapter, fake source,
read-only verification CLI, aggregate report, map output, tests, and protocol/security documents.

**Remaining dependency:** live team, chat, and map-marker validation plus a reviewed fixture. The
selected server's secure-proxy attempt returned HTTP 418 before WebSocket upgrade. The explicitly
selected direct transport has since authenticated and returned server and map snapshots.

**Done:** five live read-only calls succeed through the selected transport; map is saved; protocol
evidence is updated; secrets are absent from output; reviewed fixture capture is resolved.

## Phase 1 — Map-first desktop skeleton

**Goal:** Establish WPF/Blazor/Leaflet boundaries using fake data.

**Status:** Implemented on 2026-08-17. Live Phase 0 acceptance remains separately pending.

**Modules:** desktop host, Blazor components, `MapDashboardService`, map view state, Leaflet adapter,
and deterministic fake source.

**Risks/tests:** JS interop and update frequency; component tests and coordinate adapter contract.

**Done evidence:** application builds as a WPF/Blazor Hybrid executable, opens directly to an
interactive offline fake Rust map, supports independent layer toggles and navigation, and has an
architecture test confirming application contracts expose no RustPlusApi types. A privacy-safe
WebView-only smoke capture verifies that the rendered shell starts successfully.

## Phase 2 — SQLite, server registry, and secrets

**Goal:** Persist multiple servers safely.

**Status:** Implemented on 2026-08-17. Pairing itself remains Phase 3.

**Modules:** migration runner, repositories, `ServerManager`, `ISecretStore`, DPAPI implementation.

**Risks/tests:** schema upgrades, unsigned IDs, secret leaks; real SQLite and DPAPI tests.

**Done evidence:** server profiles survive a repository/process restart; unsigned player IDs round-trip
as decimal text; WAL, foreign keys, migration replay, delete cascade, and current-user DPAPI round-trip
are tested; SQLite receives only protected blobs. The desktop Servers page performs persistent
add/edit/select/confirm-remove operations and exposes no token input.

## Phase 3 — Pairing and connection supervision

**Goal:** Pair, validate, reconnect, re-pair, and remove servers.

**Status:** In progress. One application-level Steam64 identity, manual per-server player-token entry,
DPAPI persistence, an explicit secure read-only connection/authentication test, selected-server
persistent monitoring, and bounded reconnect backoff are implemented. Automated pairing and
simultaneous multi-server supervision remain.

**Modules:** pairing provider, connection manager, per-server supervisor, connection-state view.

**Risks/tests:** FCM/Expo/Steam changes, browser automation, auth classification; mock pairing and
reconnection tests.

**Current evidence:** the connection test reads the selected profile and protected token, clears the
retrieved cleartext buffer, uses exactly the explicitly saved transport, and requires a successful
`GetInfoAsync` response before reporting success. Secure proxy remains the default; direct `ws://`
requires an explicit plaintext warning and there is no automatic fallback. Unit/component tests
cover success, token-buffer clearing, `AccessDenied`, transport failure redaction, explicit direct
selection, no fallback, and the Servers UI state.

**Done:** user can pair, restart, reconnect, test, re-pair, and remove credentials safely.

## Phase 4 — Live server map

**Goal:** Render and cache the real Rust+ map.

**Status:** Core slice implemented and live-verified on 2026-08-17. The selected saved profile can
download `GetInfo` + `GetMap`, render the JPEG, cache the latest snapshot in SQLite, reopen it without
a network request, and refresh explicitly. The current centered Facepunch grid formula, layer toggle,
player-facing grid references, and monument token/name/glyph catalogue are implemented. A final live
golden alignment comparison remains.

**Modules:** map/session services, coordinate projector, grid service, map repositories.

**Risks/tests:** ocean margin, custom maps, grid formula; live golden alignment with official app.

**Done:** known positions align with the official app and cached map reopens offline. Cache reopening
is automated; live visual alignment is still pending.

## Phase 4.5 — External map topology

**Goal:** Add useful map data that Rust+ does not expose without contaminating the Rust+ adapter.

**Status:** First slice implemented on 2026-08-17. The app imports current serialization-version-10
Rust `.map` files, decodes the documented legacy-LZ4/protobuf container, validates world size,
persists display-ready data per server, and renders biome, topology, ore-potential, road, rail, and
river layers. Steam libraries are checked automatically; Rust's connection log or a unique exact
procedural size+seed match can select the cache file without user input.

**Modules:** `IMapTopologyProvider`, `IMapTopologyDiscovery`, `MapTopologyManager`, native map
infrastructure reader/cache matcher, `IMapTopologyRepository`, SQLite migration 4, Leaflet
raster/polyline rendering, Windows fallback file picker.

**Risks/tests:** Rust+ exposes no map checksum, and client log/cache formats can drift. Automatic
matching therefore never uses recency or size alone. Format drift and large/corrupt files are bounded
and tested with synthetic containers/cache layouts plus a manual read-only current-game cache smoke
test. Exact node locations remain impossible without server access. Exact prefab no-build zones and
higher-fidelity biome/splat spawn-rule evaluation remain.

**Done for this slice:** a definite size mismatch stores nothing; successful imports survive restart;
source paths are not retained; source classification remains visible in the layer panel.

## Phase 5 — Team, chat, and semantic events

**Goal:** Provide the first useful live team dashboard.

**Status:** Live monitoring slice implemented and live-verified on 2026-08-17. One persistent
connection centrally polls server info, team members/positions/notes, recent chat, and map markers.
The tested server returned a team/position snapshot and current marker; chat returned `NoTeam` and
uses a one-minute failure backoff. Online/offline, death/respawn, marker lifecycle, and connection
lost/restored events are derived in memory. Historical persistence and movement/grid events remain.

**Modules:** team/chat services, polling scheduler, snapshot differ, event bus/history.

**Risks/tests:** request budget, duplicates, movement spam; deterministic scenario transitions.

**Done:** roster/map/chat stay synchronized and each tested transition emits once.

## Phase 6 — Map notes and world-event markers

**Goal:** Render every verified direct marker and clearly labelled derived lifecycle.

**Modules:** marker manager/differ, layer renderers, marker history.

**Risks/tests:** marker reuse and schema drift; unknown-type and recorded snapshot tests.

**Done:** all known markers render and unknown markers remain visible without failure.

## Phase 7 — Vending marketplace

**Goal:** Search offers and locate machines.

**Modules:** market manager, versioned item catalogue, vending differ/repositories.

**Risks/tests:** item definition drift and multiplier semantics; known offer fixtures.

**Done:** user can search, inspect correct price/stock, calculate distance, and locate on map.

## Phase 8 — Smart devices

**Goal:** Monitor and deliberately control verified paired devices.

**Modules:** entity pairing, device manager, switch/alarm/storage adapters.

**Risks/tests:** ambiguous entity-change type and real game-side actions; mock control, manual live check.

**Done:** supported state is accurate and control always follows explicit user action.

## Phase 9 — Cameras

**Goal:** Add known-code camera viewing and supported controls.

**Modules:** camera manager, ray renderer, canvas interop.

**Risks/tests:** CPU, bandwidth, rendering semantics; golden frames and mock streams.

**Done:** subscribe/render/control/unsubscribe lifecycle is stable.

## Phase 10 — Notifications, background operation, and history

**Goal:** Remain useful while minimized.

**Modules:** tray lifetime, notification rules/channels, retention, sleep/network recovery.

**Risks/tests:** spam, duplicate delivery, resume state; rule matrix and lifecycle tests.

**Done:** desktop alerts are controlled and deduplicated while background monitoring recovers safely.

## Phase 11 — Packaging and hardening

**Goal:** Distribute a supportable Windows application.

**Modules:** installer, diagnostics export, health checks, upgrade migrations, user documentation.

**Risks/tests:** WebView2, upgrades, antivirus; clean-VM install/upgrade/uninstall.

**Done:** a normal Windows user can install, pair, use, upgrade, diagnose, and uninstall without a
development environment.
