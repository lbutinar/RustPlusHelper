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

**Status:** In progress. One application-level Steam64 identity, automatic and manual per-server
pairing, DPAPI persistence, an explicit secure read-only connection/authentication test,
selected-server persistent monitoring, and bounded reconnect backoff are implemented. Live
validation of automatic registration/pairing and simultaneous multi-server supervision remain.

**Modules:** pairing provider, connection manager, per-server supervisor, connection-state view.

**Risks/tests:** FCM/Expo/Steam changes, browser automation, auth classification; mock pairing and
reconnection tests.

**Current evidence:** the connection test reads the selected profile and protected token, clears the
retrieved cleartext buffer, uses exactly the explicitly saved transport, and requires a successful
`GetInfoAsync` response before reporting success. Secure proxy remains the default; direct `ws://`
requires an explicit plaintext warning and there is no automatic fallback. Unit/component tests
cover success, token-buffer clearing, `AccessDenied`, transport failure redaction, explicit direct
selection, no fallback, and the Servers UI state. Focused pairing-manager tests cover registration
credential cleanup, first capture, safe same-address re-pairing, and identity mismatch rejection.

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
test. Exact node locations remain impossible without server access. Build-snapshot prefab no-build
zones are rendered with explicit source/mismatch warnings; higher-fidelity biome/splat spawn-rule
evaluation remains.

**Done for this slice:** a definite size mismatch stores nothing; successful imports survive restart;
source paths are not retained; source classification remains visible in the layer panel; derived
build-planning, elevation/contour, water-depth, and width-aware river displays remain separately
toggleable and carry their limitations in the UI.

## Phase 5 — Team, chat, and semantic events

**Goal:** Provide the first useful live team dashboard.

**Status:** Live monitoring slice implemented and live-verified on 2026-08-17. One persistent
connection centrally polls server info, team members/positions/notes, recent chat, and map markers.
The tested server returned a team/position snapshot and current marker; chat returned `NoTeam` and
uses a one-minute failure backoff. Online/offline, death/respawn, marker lifecycle, and connection
lost/restored events are snapshot-derived, retained in a bounded per-server SQLite history, and
reloaded after restart. Online, alive team-member grid crossings emit at most once per member per
minute. Alive-to-dead transitions retain the detecting snapshot's position, and the map groups those
bounded local records by Rust grid into a derived team-death hotspot layer. It does not infer cause
or current enemy presence. Sampled movement trails remain deferred.

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

**Status:** Implemented on 2026-08-19. Machine names, friendly item/currency names, and numeric
Rust+ item/currency IDs are all searchable; each result shows direct price/stock, its derived grid,
and distance from the nearest online teammate; map focus/navigation works. Friendly names come from
a bundled, versioned external catalogue (`ItemCatalog`, see `docs/protocol-evidence.md`) — an
unresolved ID always falls back to the raw number rather than a guess. Offer-change history is
derived by keying each vending offer on `(ItemId, CurrencyId, IsItemBlueprint, IsCurrencyBlueprint)`
and comparing that "slot signature" across consecutive polls of the same marker, emitting
`VendingPriceChanged`/`VendingStockChanged`/`VendingOfferAdded`/`VendingOfferRemoved` events; a
marker that itself just appeared or disappeared only gets that marker-level event, never a flood of
offer-level ones.

**Modules:** `VendingMarketplace` (market manager + search), `ItemCatalog` (versioned item
catalogue), and an inline offer differ inside `RustPlusLiveSessionManager.AddMarkerEvents` — matching
the same inline-differ convention Phase 5's team/marker events already use rather than a separate
differ class. No new SQLite table was needed; offer-change events reuse the existing
`companion_events` history.

**Risks/tests:** item definition drift (catalogue can lag a Rust update; always labelled external,
never silently guessed) and multiplier semantics (covered by `RustPlusApiMapperTests`); known offer
fixtures for price/stock/add/remove diffing and the appear-suppression rule are covered in
`RustPlusLiveSessionManagerTests`.

**Done:** user can search by name or numeric ID, inspect correct price/stock with friendly names,
calculate distance, locate on map, and see vending price/stock/offer changes as history events.

## Phase 8 — Smart devices

**Goal:** Monitor and deliberately control verified paired devices.

**Modules:** entity pairing, device manager, switch/alarm/storage adapters.

**Risks/tests:** ambiguous entity-change type and real game-side actions; mock control, manual live check.

**Done:** supported state is accurate and control always follows explicit user action.

**Status:** Implemented on 2026-08-19. Rust+ has no device discovery; Smart Switch, Smart Alarm, and
Storage Monitor entities are paired in-game and delivered to the app as an FCM push notification
(`entityId`, `entityType`, `entityName`) — the same envelope shape as server pairing, but a level
below the app's existing `PairingListener`-based flow, which only surfaces server pairing. A new
`RustPlusEntityPairingManager` talks to `RustPlusApi.Fcm.RustPlusFcm` directly to receive it. Reading
an entity's info once (`GetSmartSwitchInfoAsync`/`GetAlarmInfoAsync`/`GetStorageMonitorInfoAsync`) is
what arms that entity's live broadcast for the current connection; the broadcast itself
(`OnEntityChanged`) carries no entity type of its own, so the live session manager always routes it
by the app's own persisted `PairedEntityKind`, never by the payload's shape (see
`docs/protocol-evidence.md`). Switches support Toggle/Strobe; Storage Monitor rows show capacity and
resolved item names via the existing `ItemCatalog`; Alarms are read-only. See
`docs/protocol-evidence.md` for the full evidence trail.

## Phase 9 — Cameras

**Goal:** Add known-code camera viewing and supported controls.

**Status:** Implemented on 2026-08-19. Rust+ CCTV has no discovery and no video/JPEG stream — it
sends a compact custom-encoded ray stream that the client decodes and rasterizes itself, and the
subscription silently stops after ~10 seconds unless renewed. Both facts, plus the exact
subscribe/input/ray message shapes, were verified against `liamcottle/rustplus.js` and the app's
already-pinned `RustPlusApi` package (see `docs/protocol-evidence.md`). Rather than hand-rolling that
decoder, the app takes a new dependency on `RustPlusApi`'s own optional `RustPlusApi.Camera` package
(same pinned version, same repository, an explicitly validated port of rustplus.js's decode/render
with golden fixtures from four device types) and reuses its `CameraController` (resubscribe keep-alive,
capability-gated `ZoomAsync`/`ShootAsync`/`ReloadAsync`/`LookAsync`/`MoveAsync`) and `CameraRenderer`
(ray frame → PNG bytes) — both stay fully inside `RustPlusHelper.Infrastructure.RustPlus`, matching the
architecture rule that third-party protocol types never escape the adapter.

A per-server list of user-entered camera codes (`saved_cameras`, migration 10) lets the user name and
recall known codes; there is still no automatic camera discovery. Viewing shares the app's single
persistent connection via new methods on `RustPlusLiveSessionManager` (see `docs/architecture.md`)
rather than a separate `CameraManager` or a second connection. Controls are shown only when the
subscribed camera's `ControlFlags` actually support them (Zoom for PTZ, Shoot/Reload for auto-turrets,
look/move nudges otherwise) — never a fabricated control. Continuous mouse-drag look and held-key
drone movement are out of scope for this slice; discrete tap/nudge buttons cover the same capability
with much less UI complexity, and remain the natural next increment.

**Modules:** camera subscribe/input/frame methods on `IRustPlusClient`/`RustPlusApiClient`
(`RustPlusApi.Camera`-backed), `ISavedCameraRepository`/`SqliteSavedCameraRepository`, the Devices &
Cameras page.

**Risks/tests:** CPU cost is negligible in practice (accumulate-and-render one modest-resolution frame
per broadcast); the UI-visible publish rate is throttled independent of broadcast frequency to avoid
the same freeze class fixed earlier for map layer toggles (see `AGENTS.md`'s "Map rendering rules").
Mapping fidelity is covered indirectly (the adapter's camera code compiles directly against
`RustPlusApi.Camera`'s real types); direct adapter-level testing isn't practical without stubbing the
33-member third-party `IRustPlus` interface, so — consistent with this repo's existing precedent that
`RustPlusApiClient` itself stays untested in favor of manual, opt-in live verification — that surface
is instead covered end-to-end via `RustPlusLiveSessionManagerTests` (subscribe/throttle/stop/keep-alive
failure, including a regression test for a frame arriving mid-subscribe) and a full bUnit flow
(add/list/view/gated-controls/stop) in `MainComponentTests`.

**Done:** user can save/nickname a camera code, view its live feed, and use only the controls that
camera actually supports; stopping and switching cameras tears down the previous subscription cleanly.

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
