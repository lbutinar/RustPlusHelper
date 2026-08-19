# Rust+ protocol evidence

Last research review: **2026-08-19**

## Evidence baseline

| Source | Pinned/reviewed revision | Role |
|---|---|---|
| Facepunch Rust+ | Current public companion pages | Official product and server-hosting behavior |
| `liamcottle/rustplus.js` | [`75915f25`](https://github.com/liamcottle/rustplus.js/commit/75915f25fc7c1718f7c23c419953f4655840fade) | Established unofficial WebSocket, protobuf, pairing, request, and event reference |
| `HandyS11/RustPlusApi` | [`v2.0.0-beta.7`](https://github.com/HandyS11/RustPlusApi/releases/tag/v2.0.0-beta.7), commit `3c4037ae` | Pinned C# implementation and current typed contract |
| `HandyS11/RustPlusApi.Camera` | Same repo/tag, `v2.0.0-beta.7` | Pinned camera session (`CameraController`) and ray-decode/render (`CameraRenderer`) implementation |
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

The terrain-slope layer is derived locally from the `.map` `height` grid. Signed 16-bit samples are
converted to Unity world metres using the independently reviewed Rust Map Parser 0.4.0 constants,
then central height differences and the map's metres-per-sample spacing produce a slope angle. The
UI bands are an application convention: flat up to 5 degrees, gentle up to 12, moderate up to 25,
and steep above 25. They are a visual planning aid, not a Rust building-placement rule; topology,
prefab blockers, objects, and server plugins can still prevent construction on apparently flat land.
Water is distinguished using serialized water height where available, with documented Ocean and
Oceanside topology used only for below-sea-level ocean fallback.

Three additional display products use the same external `.map` evidence:

- elevation tint uses world-space terrain metres, with locally chosen 25 m contour intervals and
  stronger 100 m major contours;
- water depth is `effective water height - terrain height`; serialized water is authoritative where
  present, while negative ocean water is raised to sea level only under Ocean/Oceanside topology;
- river corridors use each serialized river path's centerline, width, and outer padding. They are
  path corridors, not a claim that every rendered edge is an exact wet shoreline.

The build-planning layer is explicitly derived and conservative. It combines the slope bands,
derived water mask, Road and Building topology, serialized road/rail corridor widths, and known
external no-build polygons. Red means a known blocker or terrain steeper than 25 degrees; green
means only that none of those tested conditions rejected a sample at or below 5 degrees. Yellow
groups all remaining slopes from above 5 through 25 degrees into one caution state, and blue means
serialized or derived water. It does not evaluate live
trees, rocks, deployables, player construction, stability, terrain holes, monument child colliders,
server plugins, or all build-version-specific colliders, so it must never be presented as a build
permission check.

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

## External item-name catalogue (not Rust+)

Rust+ never supplies item names, only the numeric `ItemId`/`CurrencyId` on each vending sell order.
Friendly names shown in the Vending page and in vending offer-change events come from a bundled,
versioned catalogue (`RustPlusHelper.Application.Vending.ItemCatalog`), not from Rust+ itself.

- Source: [SzyMig/Rust-item-list-JSON](https://github.com/SzyMig/Rust-item-list-JSON), reviewed
  2026-08-19 by fetching `Rust-Items.json` directly (853,356 bytes; SHA-256 verified against the
  server's reported `Content-Length` to rule out transcription/transport corruption of the numeric
  IDs). It aggregates Rust's own bundled `items/*.json` game files.
- **The upstream repository has no declared license** (`license: null` via the GitHub API). Only
  factual `id`/`shortName`/`name` triples were extracted — not the aggregator's compiled file or its
  extra descriptive fields — consistent with this project's existing "used only to identify
  compatibility" stance toward Facepunch-originated names (see `THIRD-PARTY-NOTICES.md`).
- 1259 raw entries were deduplicated by `itemid` to 1253 unique items, preferring the dot-delimited
  shortname form (Rust's real convention, e.g. `rifle.ak`) over a stray space-delimited duplicate.
  Spot-checked against independently known stable IDs (`wood` → `-151838493`,
  `scrap` → `-932201673`, `rifle.ak` → `1545779598`).
- An `ItemId`/`CurrencyId` with no catalogue entry (a new game item, or one the source hasn't picked
  up yet) always falls back to displaying the raw numeric ID — it is never guessed or hidden.
- The catalogue can drift from the live game after a Rust update; `ItemCatalog.CatalogueVersion`
  records which reviewed snapshot is bundled so drift is at least attributable.

## Cameras (CCTV)

Verified 2026-08-19 directly against `liamcottle/rustplus.js` (`camera.js`, `rustplus.proto`) and
`HandyS11/RustPlusApi` (`RustPlusApi/Protobuf/RustPlusContracts.proto`,
`RustPlusApi/Interfaces/IRustPlus.cs`, `RustPlusApi.Camera/*`) at the pinned revisions above. Neither
Facepunch page in this document's evidence baseline describes the CCTV protocol at all — these two
community implementations are the only real sources.

- **Subscribe/unsubscribe/input, request/response shape:** the client sends an `AppRequest` with
  `cameraSubscribe: { cameraId }`; the server's `AppResponse` carries `cameraSubscribeInfo`
  (`width`, `height`, `nearPlane`, `farPlane`, `controlFlags`) before any ray broadcasts arrive.
  `cameraUnsubscribe` (an empty `AppEmpty`) ends it. `cameraInput` carries `{ buttons, mouseDelta }`.
  Both reference implementations agree on these fields exactly.
- **The subscription is not "fire and forget":** both `rustplus.js` (a 10-second `setInterval`) and
  `RustPlusApi.Camera`'s `CameraController` (`DefaultResubscribeInterval = 10s`) re-send
  `cameraSubscribe` on a timer with an explicit comment that the server stops streaming rays for a
  stale subscription. `CameraController` owns this keep-alive loop so the app never re-implements it.
- **A camera frame is not video/JPEG.** `AppCameraRays` carries `rayData` — a run-length/delta-encoded
  byte stream of quantized `(distance, alignment, material)` samples that the client must decode and
  rasterize itself. `RustPlusApi.Camera`'s `CameraRenderer`/`IndexGenerator` are an explicitly
  acknowledged, golden-fixture-validated port of `rustplus.js`'s decode (`_renderCameraFrame`,
  `IndexGenerator`, seed `1337`): a shuffled sample-position buffer is filled in progressively —
  each broadcast frame only supplies a sparse subset of pixels (a real captured 160×90 fixture
  averaged ~6 KB of `rayData` per frame, well under one byte per pixel), so the image sharpens over
  several accumulated frames rather than arriving complete in one broadcast.
- **`entityId` width discrepancy (real, unresolved, flagged rather than silently picked):**
  `rustplus.js`'s hand-maintained proto declares `AppCameraRays.Entity.entityId` as `uint32`;
  `RustPlusApi`'s proto — regenerated from the live server assembly, not hand-maintained — declares
  `entity_id` as `uint64` and also carries four extra optional fields (`time_of_day`,
  `camera_position`, `camera_rotation`, `sample_rotation`) that `rustplus.js`'s proto doesn't declare
  at all. This app doesn't map the per-frame entity list at all yet (v1 renders the PNG only), so the
  discrepancy has no code impact today, but it matters if entity overlays are ever added.
- **Capability-gated actions, not a raw button API:** `AppCameraInfo.controlFlags`
  (`None/Movement/Mouse/SprintAndDuck/Fire/Reload/Crosshair`) tells the client which inputs a camera
  accepts. `CameraController` exposes named, gated helpers (`ZoomAsync`, `ShootAsync`, `ReloadAsync`,
  `LookAsync`, `MoveAsync`) that refuse client-side (nothing sent) when the device doesn't advertise
  the required flag — documented as necessary because zoom and turret-fire share the same
  `FirePrimary` button, and because **the live server acknowledges unsupported inputs with success
  while silently ignoring them**, so an ungated zoom sent to a turret would actually fire it.
  `IsAutoTurret`/`IsDrone`/`IsPtzCamera`/`IsStaticCamera` are derived from those same flags and are
  read directly off the real controller rather than re-implemented.
- **Live-tested behavior (RustPlusApi.Camera's own doc comments, 2026-06):** PTZ zoom cycles four real
  FOV levels (65 → 43.33 → 26 → 16.25, wrapping); a drone only actually moves under a *continuous*
  input stream (`MoveAsync` streams frames for a hold duration; a single press-and-release is acked
  but does nothing); `Sprint`/`Duck` are a drone's ascend/descend, not jump/duck; a destroyed camera
  entity fails subsequent subscribes with `RustPlusErrorCode.NoPlayer`.
- **No official documentation exists.** `rust.facepunch.com/companion` and
  `wiki.facepunch.com/rust/rust-companion-server` only list "CCTV Camera"/"PTZ CCTV Camera" as in-game
  item names; neither describes the streaming protocol, subscribe/unsubscribe, or ray format.
- **No camera discovery.** Rust+ never enumerates cameras; the user must already know the in-game code
  from a computer station. This app stores user-entered codes with a nickname per server
  (`saved_cameras`) — a manual list, not a derived/direct one.

## Smart devices

Verified 2026-08-19 directly against `liamcottle/rustplus.js` (`rustplus.proto`, the FCM listener
example in its README) and `HandyS11/RustPlusApi` (`RustPlusApi.Fcm.dll`, `RustPlusApi.Fcm.Registration.dll`,
`RustPlusApi.dll`, all reflected on the pinned installed packages) at the pinned revisions above.
Neither Facepunch page describes any of this — as with cameras, the community implementations are
the only real sources.

- **No device discovery.** As with cameras, Rust+ never enumerates a player's Smart Switches, Smart
  Alarms, or Storage Monitors. The only way the app learns an entity's ID, type, and name is the FCM
  push notification sent the moment the player pairs the device in-game — the same envelope shape
  used for server pairing (`entityId`, `entityType`, `entityName`), confirmed via
  `RustPlusApi.Fcm.Data.Events.EntityEvent`.
- **Entity pairing needs a level below this app's existing pairing code.** The app's
  `RustPlusPairingManager`/`RustPlusApiPairingProvider` only talk to
  `RustPlusApi.Fcm.Registration.PairingListener`, a convenience wrapper that exclusively raises
  `OnServerPairing` and drops entity-pairing notifications on the floor. Reflecting on the installed
  `RustPlusApi.Fcm.dll` found the underlying `RustPlusApi.Fcm.RustPlusFcm` class one layer down — a
  peer of `PairingListener`, same `Credentials` type — already exposes
  `event EventHandler<Notification<EntityEvent?>>? OnEntityPairing`. No new FCM/MCS protocol work was
  needed; `RustPlusEntityPairingManager`/`RustPlusApiPairingProvider.WaitForEntityPairingAsync` just
  construct `RustPlusFcm` directly from the same already-registered/stored
  `ApplicationSecretKind.RustPlusFcmCredentials`, reusing the one registration for both server and
  entity pairing.
- **`EntityType` enum values are a verified protocol fact, not a guess:** `Switch = 1`, `Alarm = 2`,
  `StorageMonitor = 3`, mirrored exactly as the app's own `PairedEntityKind`.
- **Reading an entity's info once arms its broadcast.** Per `rustplus.js`'s documented behavior and
  confirmed against `RustPlusApi`'s own XML doc comments, calling
  `GetSmartSwitchInfoAsync`/`GetAlarmInfoAsync`/`GetStorageMonitorInfoAsync(entityId)` is what makes
  the server start sending that entity's `OnEntityChanged` broadcasts on the current connection — a
  fresh connection needs this call again per paired entity. The wire also defines
  `SetSubscription`/`CheckSubscription` requests, but neither reference implementation uses or
  documents them meaningfully, so this app does not rely on them either — noted here rather than
  guessed at.
- **The broadcast cannot tell you what kind of entity it is (real, load-bearing gotcha).**
  `EntityChangedEventArg` carries `Id`, optional `Value` (switch/alarm on-off), and optional
  `Capacity`/`HasProtection`/`Items` (storage monitor) all on the same shape — `RustPlusApi`'s own
  doc comments note it can misclassify a Storage Monitor's plain broadcast as a Smart Switch. This app
  never trusts the payload's shape: `RustPlusLiveSessionManager` always routes an incoming broadcast
  by the entity's own persisted `PairedEntityKind` (captured at pairing time), and preserves any
  known-good `Capacity`/`HasProtection`/`Items` when a broadcast for a Storage Monitor arrives with
  those fields absent (a real observed shape — see next point).
- **Storage Monitor's two-broadcast quirk:** `rustplus.js`'s README documents that a Storage Monitor
  change can arrive as two separate broadcasts — one carrying only a `Value` pulse, a second carrying
  the actual `Capacity`/`Items`. A regression test
  (`RoutesEntityBroadcastByStoredKindNotPayloadShape`) locks in that a `Value`-only broadcast for a
  known Storage Monitor never nulls out its last-known capacity/items.
- **Control surface confirmed via reflection on `RustPlusApi.dll`:** `SetSmartSwitchValueAsync(ulong,
  bool, ct)`, `ToggleSmartSwitchAsync(ulong, ct)`, `StrobeSmartSwitchAsync(ulong, TimeSpan, bool, ct)`
  all return the resulting `SmartDeviceInfo` (not just an acknowledgement bool) — the app's own
  `Set/Toggle/StrobeSmartSwitchAsync` wrappers return that resulting state directly rather than
  re-reading it. Smart Alarms have no control surface in Rust+ — the UI never offers alarm controls.
- **Smart Alarm's "triggered" push notification is out of scope here.** `RustPlusApi.Fcm` exposes a
  separate `OnAlarmTriggered`/`AlarmNotification { Title, Message }` channel — a different kind of FCM
  push from pairing. That's background push-notification handling, deliberately deferred to Phase 10
  ("notification rules/channels"); this phase reads Smart Alarm state the same way as a switch, via
  `GetAlarmInfoAsync`/`OnEntityChanged`.
- **Token cost:** `GetSmartSwitchInfoAsync`/`GetAlarmInfoAsync`/`GetStorageMonitorInfoAsync` and
  `Set/Toggle/StrobeSmartSwitchAsync` are all default-cost (1 token) requests, confirmed via the same
  rustplus.js rate-limit table already cited above.

## Notifications and background operation

Verified 2026-08-19 directly against `HandyS11/RustPlusApi`'s `RustPlusApi.Fcm` package (reflection on
the installed DLL plus source fetched from GitHub at the pinned tag `v2.0.0-beta.6`) — completing the
Smart Alarm "triggered" push explicitly deferred from the Smart devices section above.

- **`RustPlusApi.Fcm.RustPlusFcm.OnAlarmTriggered`** fires with an `AlarmNotification` (extends
  `NotificationBase { Guid ServerId, string? PersistentId }`, adds `string Title`, `string Message`) —
  confirmed by fetching `RustPlusFcm.cs`'s actual `ParseNotification`/`OnAlarmTriggered` source at the
  pinned tag, not just its XML docs.
- **`AlarmNotification.ServerId` is Rust+'s own server GUID, not this app's `ServerProfile.Id`.**
  Confirmed via `RustPlusApi.Fcm.Data.Events.ServerEvent.Id` (used in server-pairing notifications),
  doc-commented "The server's unique Rust+ ID" — the same kind of ID, a genuinely different ID space
  from this app's locally-generated server GUIDs. **This app did not previously capture it**:
  `RustPlusApiPairingProvider.WaitForServerPairingAsync` used the high-level
  `RustPlusApi.Fcm.Registration.PairingListener.WaitForServerPairingAsync`, whose `ServerPairing` result
  type only exposes `Ip/Port/PlayerId/PlayerToken/Name` — no `Id` field exists on it at all (confirmed
  via the package's own XML docs, which describe it as "exactly the four arguments needed for `new
  RustPlus(new RustPlusConnection(...))`"). Fixed by rewriting that method to bypass `PairingListener`
  and read `ServerEvent.Id` directly off `RustPlusFcm.OnServerPairing`, mirroring the same
  lower-level pattern the entity-pairing method already used. Servers paired before this fix have no
  captured ID and cannot be attributed to an alarm push until re-paired — a real, documented
  limitation, not silently glossed over.
- **`RustPlusFcm` has no built-in auto-reconnect.** Its own doc comment: "Instances are
  single-connection: after Disconnect or disposal, create a new instance." A built-in heartbeat +
  inactivity-timeout loop self-detects a silently-dead connection (raises `ErrorOccurred` and calls
  `Disconnect()` automatically), but reconnecting requires constructing a fresh `RustPlusFcm` and
  calling `ConnectAsync` again — this app's `RustPlusAlarmNotificationListener` owns that retry loop
  itself, reusing the same bounded 2/5/10/30s backoff already used for the main Rust+ connection
  (`RustPlusPollingOptions.Default.ReconnectDelays`) rather than inventing a second one.
- **De-duplication is a verified, built-in mechanism, not invented.** `RustPlusFcm`'s constructor takes
  an optional caller-owned `ICollection<string>? persistentIds` — already-processed FCM message ids,
  used to skip re-dispatching a redelivered notification. Its own doc comment states the set "is NOT
  cleared on login, so seeded ids survive reconnect" and that ids "have a server-side lifespan, so
  pruning your stored copy is your responsibility" — this app persists the set (capped at 500 entries,
  oldest evicted first, since the ids are opaque strings with no visible timestamp) via the existing
  `IApplicationSecretStore`'s single-row `secret_kind`/`protected_value` table, reused rather than
  adding a new migration for one small blob.
- **Reading the live `persistentIds`/`PersistentIds` collection off-thread is unsafe** (same doc
  block: can throw during concurrent enumeration while traffic flows). This app never touches that
  live collection after constructing `RustPlusFcm` with it — it tracks newly-received ids purely from
  the `PersistentIdReceived` event's own payload (the id string arrives directly as the event arg, no
  need to re-read the shared collection at all).
- **No official documentation exists for any of this**, as with every other FCM-based integration in
  this app — Facepunch's pages describe none of the pairing or alarm-push protocol.

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
| Vending price/stock/offer-slot change | Derived by comparing marker sell orders, keyed by item/currency/blueprint |
| Vending grid/distance | Derived locally from direct marker/team coordinates and map size |
| WebSocket lost/restored | Transport event; restored requires a successful authenticated check |
| Camera subscribe info/input ack | Direct Rust+ response |
| Camera rendered image | Client-decoded/rasterized from `AppCameraRays`, not a direct video feed |
| Camera code/nickname | User-entered; Rust+ provides no camera discovery |

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
