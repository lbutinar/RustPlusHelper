# Testing strategy

## Current Phase 0 tests

- application connection options redact their token string representation;
- explicit and key-shaped secrets are redacted;
- fake client provides deterministic server/map/team/chat/marker data;
- fake disconnect makes later requests fail predictably;
- unsigned IDs above `long.MaxValue` survive application models;
- missing optional protocol fields remain optional;
- unknown marker raw type and ID survive the adapter;
- current vending multiplier fields survive the adapter;
- normalized verification reports exclude player IDs, names, chat bodies, positions, and tokens;
- credentials are rejected as command-line options.

## Current Phase 1 tests

- canonical world coordinates project into top-left image pixels with the documented Y flip;
- invalid map dimensions and margins are rejected;
- `MapDashboardService` loads all fake snapshots only through `IRustPlusClient`;
- render models separate monuments, team, notes, vending, events, and unknown markers;
- unavailable CCTV/device layers cannot be enabled;
- the actual Blazor root component opens on the map and invokes local map interop;
- navigation switches to the Team surface;
- toggling a layer updates application state;
- application public contracts do not expose RustPlusApi types.

## Current Phase 2 tests

- server profiles save, select, reload, validate, and remove through `ServerManager`;
- real temporary SQLite databases apply migration 1 idempotently;
- WAL and foreign keys are enabled;
- profiles survive reopening and `ulong.MaxValue` remains canonical decimal text;
- DPAPI `CurrentUser` data round-trips and fails with the wrong context;
- SQLite stores a protected blob rather than the supplied cleartext;
- deleting a server cascades its pairing ciphertext;
- the actual Servers component saves through `ServerManager`;
- the privacy-safe smoke runner can navigate to and validate the Servers page.

## Current Phase 3 tests

- a successful connection test opens a client, validates with read-only server information, closes
  the socket, and clears the retrieved token buffer;
- `AccessDenied` is reported as rejected pairing rather than a generic transport failure;
- direct `ws://` is used only when explicitly persisted on the profile;
- a failed secure-proxy attempt is never retried through direct transport;
- transport exceptions cannot surface a numeric player token in connection state;
- the Servers page exercises the saved-pairing **Test connection** flow through a fake client factory.

## Current Phase 4 tests

- a live-map operation authenticates, requests server info then map, closes the socket, and clears the
  retrieved token buffer;
- selected saved servers populate the map dashboard through a production-client factory;
- a second dashboard session reopens the cached map without another Rust+ connection;
- real temporary SQLite tests round-trip map metadata/JPEG and verify delete cascade;
- live map state disables team, vending, and event layers until their polling phases;
- a manual local run confirmed direct `GetInfo` + `GetMap` and wrote a non-empty JPEG cache entry.

## Current Phase 5 snapshot tests

- one authenticated refresh requests info, optional map, team, chat, and markers before closing;
- optional `NoTeam`/other request failures remain separate after successful authentication;
- cached maps remain visible while team/chat/marker data refreshes;
- live layers become available only when their owning snapshot succeeds;
- a data-only refresh leaves the cached map untouched;
- a private local capture confirmed a real team position and current marker rendered on the map, then
  the capture was deleted without entering Git.
- deterministic supervisor tests prove one client is reused across poll intervals and the map method
  is never called;
- scripted snapshots derive online/offline, death/respawn, and marker appeared/disappeared events;
- a forced disconnect proves connection-lost, backoff, reconnection, and connection-restored events;
- a live selected-server run remained connected across scheduled intervals; its private capture was
  deleted after review.

## Test layers planned

### Unit

- world-to-image projection and Y flip;
- grid conversion against official-app golden examples;
- team online/death/respawn/movement snapshot differences;
- marker and vending differences;
- event deduplication and notification cooldowns;
- reconnect state machine and polling budget;
- log/diagnostic redaction.

### Protocol adapter

- optional and newly added fields;
- signed player token and unsigned 64-bit identifiers;
- unknown marker enum values;
- request error-code mapping;
- disconnect during pending request;
- sanitized recorded fixtures where available.

### Fake and recorded sources

`IRustPlusClient` implementations will include:

- `RustPlusApiClient` for real connections;
- `FakeRustPlusClient` for deterministic UI/application development;
- `RecordedRustPlusClient` later for time-sequenced sanitized scenarios.

Scenarios should include wipe, movement, grid change, death, respawn, online transition, marker spawn,
vending stock change, alarm change, connection loss/recovery, and auth rejection.

### Database

Use a real temporary SQLite database for migrations, constraints, session separation, transactional
snapshot replacement, retention, and background-writer/foreground-reader behavior.

### UI

Use bUnit for Blazor components and a narrow browser contract test for Leaflet interop. Add Playwright
only for important end-to-end flows once the desktop shell exists.

For a local Windows visual smoke check after a Debug build:

```powershell
.\scripts\Capture-DesktopSmoke.ps1
.\scripts\Capture-DesktopSmoke.ps1 -Section Servers -OutputPath .\artifacts\ui\phase2-servers.png
```

The script launches only the built Debug executable with a one-process environment flag. The app
captures its own WebView content through WebView2, stores the PNG under ignored `artifacts/ui/`, and
then closes. It never reads desktop pixels, so other applications and notifications cannot appear in
the image. The capture-only hook is excluded from Release builds.

For an explicit local live diagnostic, set `RUSTPLUSHELPER_UI_CAPTURE_LIVE_TEST=1` for a Servers
capture. That makes the Debug-only hook click the first saved server's **Test connection** button and
wait for its terminal status before capturing. This opt-in path must never be enabled in CI, and its
ignored screenshot can contain private server/profile data.

### Live

Live tests are explicit and never run in CI. Start read-only. Sending chat or controlling devices
requires a separate deliberate test action. Secrets must come from the OS secret provider or process
environment and must not enter test results.

## Fixture rule

Fake data tests code paths but is not protocol evidence. A file may be called a captured Rust+ fixture
only when it was obtained from a live response and manually reviewed according to
`tests/Fixtures/README.md`.
