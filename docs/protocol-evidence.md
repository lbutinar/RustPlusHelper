# Rust+ protocol evidence

Last research review: **2026-08-18**

## Evidence baseline

| Source | Pinned/reviewed revision | Role |
|---|---|---|
| Facepunch Rust+ | Current public companion pages | Official product and server-hosting behavior |
| `liamcottle/rustplus.js` | [`75915f25`](https://github.com/liamcottle/rustplus.js/commit/75915f25fc7c1718f7c23c419953f4655840fade) | Established unofficial WebSocket, protobuf, pairing, request, and event reference |
| `HandyS11/RustPlusApi` | [`v2.0.0-beta.7`](https://github.com/HandyS11/RustPlusApi/releases/tag/v2.0.0-beta.7), commit `3c4037ae` | Pinned C# implementation and current typed contract |
| `RustPlusApi.Fcm` / `.Fcm.Registration` | `2.0.0-beta.6` | Pinned C# FCM registration and pairing-notification implementation |
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
- [RustPlusApi registration guide](https://github.com/HandyS11/RustPlusApi/blob/develop/docs/articles/credentials.md)
- [RustPlusApi registration source](https://github.com/HandyS11/RustPlusApi/tree/develop/src/RustPlusApi.Fcm.Registration)

Neither library is an official Facepunch SDK. `rustplus.js` describes its protobuf as hand-maintained;
RustPlusApi uses update tooling derived from server assemblies. Protocol claims remain versioned and
testable rather than permanent assumptions.

## Automatic pairing evidence

Facepunch documents the companion server address and its default port relationship, but not the
consumer FCM device-registration sequence. The automatic pairing implementation is therefore an
explicitly unofficial integration based on both reviewed libraries:

- rustplus.js documents `fcm-register` followed by `fcm-listen`, and maps a server-pairing
  notification to IP, port, server name, player ID, and player token;
- RustPlusApi's registration package performs Android/FCM and Expo registration, opens a local
  browser sign-in for Steam, registers the resulting device with Rust+, and exposes the same
  server-pairing fields through `PairingListener`;
- RustPlusHelper maps those third-party fields immediately into an application-owned capture,
  defaults the saved profile to the secure proxy, and DPAPI-protects both the reusable registration
  credentials and per-server player token.

No authentication material, notification body, server address, or player identity is logged. The
browser/device registration is user-initiated and has not been run as part of automated verification.
Because the FCM, Expo, Steam, and Facepunch sequence is unofficial and externally controlled, a live
pairing remains required before this integration can be called confirmed against current services.

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

The rustplus.js rate-limit evidence lists a 25-token player bucket replenished at 3 tokens/second,
with costs of 1 for info, team info, and map markers and 5 for map; team-chat reads use the default
request cost of 1. The desktop therefore batches one explicit refresh on one connection. It does not
reconnect on a UI timer. The selected-server supervisor now keeps one persistent connection and polls
team every 5 seconds, chat/markers every 10 seconds, and info every 30 seconds (about 0.43 tokens/sec).
The five-token map is never polled. `NoTeam` backs chat off to one minute, and reconnects use bounded
2/5/10/30-second delays.

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

Grid references are local projections, not Rust+ response fields. The current Facepunch map rule
uses `floor(mapSize * 7 / 1024)` cells per axis and divides the playable map size evenly by that
count. The desktop projects those cells into the Rust+ JPEG inside its supplied ocean margin; rows
run north-to-south and columns use `A` through `Z`, then `AA`, `AB`, and so on.

Unknown marker numeric type and unsigned marker ID are deliberately preserved.

## External `.map` evidence (not Rust+)

Rust+ `GetMap` does not expose terrain masks, biome channels, topology masks, paths, or the original
world file. The optional topology importer is therefore a separate external source and never a
protocol capability.

- [Facepunch Map Data](https://wiki.facepunch.com/rust/Map_Data) documents the 12-byte header,
  legacy LZ4 stream, protobuf `WorldData`, named byte maps, prefabs, and paths.
- [Facepunch Topology](https://wiki.facepunch.com/rust/Topology) documents topology semantics and
  explicitly marks several spawn descriptions as unconfirmed.
- [Cooperkit Rust Map Parser](https://github.com/Cooperkit/Rustmap-Parser) 0.4.0 was reviewed as the
  current independent implementation reference for layer encoding and image orientation.

The importer supports world serialization version 10, rejects definite Rust+/file size mismatches,
and keeps only display-ready derivatives. Automatic discovery first correlates the saved server host
and world name in Rust's local `output_log.txt`; documented `MapType.Size.Seed.map` matching is a
secondary path. Current client cache names may append build/hash/checksum segments and may not expose
the Rust+ seed. Rust+ exposes no map checksum, so same-size-only candidates are never auto-selected.
The ore overlay is topology potential, not exact live node state.

The no-build layer is also external, not Rust+. Facepunch documents that Road and Rail topology
prevent player construction, but Monument topology alone does not prove a monument's blocked
volume. The imported `.map` supplies placed prefab IDs and transforms; a compact catalogue derived
from [Rust Map Parser 0.4.0](https://github.com/Cooperkit/Rustmap-Parser) supplies the named/tagged
prevent-building circle and box colliders published for Rust build `24181174`. The app transforms
those shapes into map coordinates and labels the result `EXTERNAL RUST BUILD 24181174`.

This is exact snapshot geometry only when the server content matches that build. The `.map` format
contains no Rust build ID, so the UI always presents the mismatch warning. Unrecognized prefabs,
unsupported collider shapes, server plugins, and custom server restrictions are omitted rather than
guessed. The bundled catalogue contains no live server data, authenticated traffic, game meshes, or
textures.

## Direct versus derived behavior

| Behavior | Evidence status |
|---|---|
| Team position/online/alive snapshot | Direct Rust+ response |
| Connected/disconnected/death/respawn semantic event | Derived by comparing snapshots |
| Team death position history | Position from the team snapshot where alive changes to dead; persisted locally |
| Team death hotspot intensity | Derived locally by grouping retained death positions by Rust grid |
| Current cargo/CH47/patrol-heli/crate/explosion marker | Direct Rust+ response |
| Marker appeared/disappeared event | Derived by comparing snapshots |
| Oil-rig activation | Community heuristic, not an explicit protocol event |
| Vending offer/stock change | Derived by comparing marker sell orders |
| Vending grid/distance | Derived locally from direct marker/team coordinates and map size |
| WebSocket lost/restored | Transport event; restored requires a successful authenticated check |

## Live evidence status

On 2026-08-17, an explicit read-only desktop test used a locally DPAPI-protected pairing against the
selected server. The TLS endpoint at Facepunch was reached, but the proxy rejected the WebSocket
upgrade with HTTP `418` instead of `101`; the test did not fall back to plaintext.

The profile was then explicitly changed to direct `ws://` transport after the UI plaintext warning.
That selected transport authenticated successfully: `GetInfoAsync` returned server information and
`GetMapAsync` returned dimensions, ocean margin, monuments, and a non-empty JPEG. The desktop cached
that snapshot locally. No endpoint, player identity, token, server name, map contents, or precise
coordinates were copied into repository evidence.

A later read-only cycle on the same explicitly selected transport authenticated once and requested
team, recent team chat, and map markers. Team info returned one member with live status/position data,
and map markers returned one current marker. Team chat returned the protocol error code `NoTeam`.
The application retained the successful team and marker snapshots and surfaced chat as unavailable;
no live name, Steam ID, chat body, marker identity, or coordinate was retained in repository output.

The same selected server was then held on one persistent connection through multiple scheduled poll
intervals. Team and marker state continued updating, no map request was made by the supervisor, and
the initial transport event established the diff baseline. The private UI verification capture was
deleted after review.

These items remain **pending, not confirmed live**:

- end-to-end native browser registration and automatic server-pairing capture;
- a successful team-chat response for a server/team state with available chat history;
- real optional-field population outside the confirmed server/map subset;
- image/world coordinate alignment against the official app;
- sanitized raw binary fixture capture.

The deterministic fake verification and adapter-mapping tests are implementation checks, not live
protocol evidence.

The team-death hotspot layer does not claim that Rust+ provides danger zones. For each detected
alive-to-dead transition, the app stores the member coordinates returned in that same team snapshot.
It excludes records older than the server's reported wipe time when available, groups the remaining
positions by the locally calculated Rust grid, and scales the visual hotspot by count. This can
indicate repeated team losses, but it cannot identify the killer, weapon, cause,
enemy activity, or current danger. Existing history created before positional storage remains valid
event history but cannot appear on the map.

RustPlusApi currently exposes typed data and raw events internally but no reviewed public raw-frame
capture hook suitable for this repository. Do not manufacture binary data and call it captured Rust+
traffic. A future fixture-capture mechanism must be reviewed for credentials before use.

## Uncertainty log

| Question | Current position | Required evidence |
|---|---|---|
| Facepunch secure proxy reliability | Selected live server returned HTTP 418 before WebSocket upgrade; current proxy viability is unconfirmed | Successful live check on another server or upstream clarification |
| Grid visual alignment | Current Facepunch formula is implemented; exact JPEG alignment still needs a live golden comparison | Compare several positions with the official Rust+ app |
| Pairing-token lifetime/rejection codes | Unofficial implementations document behavior; may change | Controlled expired/re-pair test |
| Unknown marker evolution | Expected because newer travelling-vendor fields already demonstrate drift | Preserve unknown type and capture future fixture |
| Companion history endpoint | Documented by rustplus.js but unofficial | Defer; local history is authoritative for the app |
| `NoTeam` from chat while team info succeeds | Observed live on the selected server; requests are independent | Re-test while in a multi-member team with chat history |
| Killer, weapon, and cause of death | Not present in verified team structures | Requires server/plugin evidence before modelling |
| Same-size `.map` wipe identity | Rust+ exposes no map checksum | User confirmation or a future authoritative external fingerprint source |
| No-build catalogue/server build match | `.map` files contain no Rust build ID; bundled geometry is from build 24181174 | Build-identifying map metadata or a matching locally installed Rust build |
