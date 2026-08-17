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

### Live

Live tests are explicit and never run in CI. Start read-only. Sending chat or controlling devices
requires a separate deliberate test action. Secrets must come from the OS secret provider or process
environment and must not enter test results.

## Fixture rule

Fake data tests code paths but is not protocol evidence. A file may be called a captured Rust+ fixture
only when it was obtained from a live response and manually reviewed according to
`tests/Fixtures/README.md`.
