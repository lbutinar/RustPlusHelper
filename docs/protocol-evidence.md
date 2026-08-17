# Rust+ protocol evidence

Last research review: **2026-08-17**

## Evidence baseline

| Source | Pinned/reviewed revision | Role |
|---|---|---|
| Facepunch Rust+ | Current public companion pages | Official product and server-hosting behavior |
| `liamcottle/rustplus.js` | [`75915f25`](https://github.com/liamcottle/rustplus.js/commit/75915f25fc7c1718f7c23c419953f4655840fade) | Established unofficial WebSocket, protobuf, pairing, request, and event reference |
| `HandyS11/RustPlusApi` | [`v2.0.0-beta.7`](https://github.com/HandyS11/RustPlusApi/releases/tag/v2.0.0-beta.7), commit `3c4037ae` | Pinned C# implementation and current typed contract |
| RustPlusPlus | Current open-source polling/event handlers | Evidence for snapshot-derived team/marker events |

Official references:

- [Rust+ companion feature page](https://rust.facepunch.com/companion)
- [Rust companion-server configuration](https://wiki.facepunch.com/rust/rust-companion-server)

Implementation references:

- [rustplus.js README](https://github.com/liamcottle/rustplus.js/blob/75915f25fc7c1718f7c23c419953f4655840fade/README.md)
- [rustplus.js protobuf](https://github.com/liamcottle/rustplus.js/blob/75915f25fc7c1718f7c23c419953f4655840fade/rustplus.proto)
- [rustplus.js pairing flow](https://github.com/liamcottle/rustplus.js/blob/75915f25fc7c1718f7c23c419953f4655840fade/docs/PairingFlow.md)
- [RustPlusApi protocol](https://github.com/HandyS11/RustPlusApi/blob/v2.0.0-beta.7/src/RustPlusApi/Protobuf/RustPlusContracts.proto)
- [RustPlusApi client guide](https://handys11.github.io/RustPlusApi/articles/rustplus-client.html)

Neither library is an official Facepunch SDK. `rustplus.js` describes its protobuf as hand-maintained;
RustPlusApi uses update tooling derived from server assemblies. Protocol claims remain versioned and
testable rather than permanent assumptions.

## Phase 0 source-verified request surface

The pinned C# adapter currently wraps only read operations needed for Phase 0:

| Application operation | RustPlusApi call | Expected payload |
|---|---|---|
| `GetServerInfoAsync` | `GetInfoAsync` | server/map/population metadata |
| `GetMapAsync` | `GetMapAsync` | dimensions, margin, JPEG, monuments |
| `GetTeamAsync` | `GetTeamInfoAsync` | leader, members, notes, death position |
| `GetTeamChatAsync` | `GetTeamChatAsync` | recent team chat |
| `GetMapMarkersAsync` | `GetMapMarkersAsync` | known marker groups and unknown raw marker types |

Connection authentication is represented by server, companion port, Steam64 player ID, signed
32-bit player token, and optional Facepunch proxy flag. RustPlusApi places these into its authenticated
request flow; the application keeps them behind `RustPlusConnectionOptions` and redacts token string
output.

The desktop connection test deliberately distinguishes socket-open from authenticated-ready. It uses
`GetInfoAsync` as the one-token read-only validation request, classifies the pinned library's verified
`AccessDenied` code as rejected pairing, and closes the test socket after validation. Secure proxy is
the default; direct `ws://` is available only through an explicit persisted user choice and there is
no proxy fallback.

## Source-verified fields retained by the adapter

- Server: map size, wipe time, population, queue, seed/salt, branding URLs, and Nexus metadata.
- Map: pixel width/height, pixel ocean margin, background ARGB, monuments, and JPEG bytes.
- Team: unsigned Steam64 ID, name, `x/y`, online/alive state, last spawn/death time, notes, and leader
  death position.
- Chat: unsigned Steam64 ID, name, message, colour, and timestamp.
- Markers: player, explosion, vending, CH47, cargo, crate, generic radius, patrol helicopter,
  travelling vendor, and unknown marker.
- Vending: item/currency IDs, quantities, price, stock, blueprint/condition values, price multiplier,
  and received-quantity multiplier.

Unknown marker numeric type and unsigned marker ID are deliberately preserved.

## Direct versus derived behavior

| Behavior | Evidence status |
|---|---|
| Team position/online/alive snapshot | Direct Rust+ response |
| Connected/disconnected/death/respawn semantic event | Derived by comparing snapshots |
| Current cargo/CH47/patrol-heli/crate/explosion marker | Direct Rust+ response |
| Marker appeared/disappeared event | Derived by comparing snapshots |
| Oil-rig activation | Community heuristic, not an explicit protocol event |
| Vending offer/stock change | Derived by comparing marker sell orders |
| WebSocket lost/restored | Transport event; restored requires a successful authenticated check |

## Live evidence status

On 2026-08-17, an explicit read-only desktop test used a locally DPAPI-protected pairing against the
selected server. The dependency graph and credential retrieval succeeded, and the TLS endpoint at
Facepunch was reached, but the proxy rejected the WebSocket upgrade with HTTP `418` instead of `101`.
No protobuf request was sent, so this is transport evidence only—not authenticated protocol success.
The test did not fall back to plaintext direct transport.

These items remain **pending, not confirmed live**:

- a successful connection through either explicitly selected transport;
- success of all five read-only operations for the user's current team/server state;
- real optional-field population;
- image/world coordinate alignment against the official app;
- sanitized raw binary fixture capture.

The deterministic fake verification and adapter-mapping tests are implementation checks, not live
protocol evidence.

RustPlusApi currently exposes typed data and raw events internally but no reviewed public raw-frame
capture hook suitable for this repository. Do not manufacture binary data and call it captured Rust+
traffic. A future fixture-capture mechanism must be reviewed for credentials before use.

## Uncertainty log

| Question | Current position | Required evidence |
|---|---|---|
| Facepunch secure proxy reliability | Selected live server returned HTTP 418 before WebSocket upgrade; current proxy viability is unconfirmed | Successful live check on another server or upstream clarification |
| Exact grid-cell formula | Community implementations differ | Golden comparison with official Rust+ app |
| Pairing-token lifetime/rejection codes | Unofficial implementations document behavior; may change | Controlled expired/re-pair test |
| Unknown marker evolution | Expected because newer travelling-vendor fields already demonstrate drift | Preserve unknown type and capture future fixture |
| Companion history endpoint | Documented by rustplus.js but unofficial | Defer; local history is authoritative for the app |
| Killer, weapon, and cause of death | Not present in verified team structures | Requires server/plugin evidence before modelling |
