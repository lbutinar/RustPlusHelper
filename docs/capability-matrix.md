# Rust+ capability matrix

Classifications describe the primary verified source.

| Feature | Classification | Boundary |
|---|---|---|
| Companion WebSocket and authenticated requests | **AVAILABLE DIRECTLY THROUGH RUST+** | Host/port plus player ID/token |
| Player/server pairing | **AVAILABLE THROUGH EXISTING OPEN-SOURCE LIBRARIES** | Reverse-engineered FCM/Expo/Facepunch registration |
| Server metadata, time, population, queue | **AVAILABLE DIRECTLY THROUGH RUST+** | Typed requests |
| Base map JPEG, dimensions, margin | **AVAILABLE DIRECTLY THROUGH RUST+** | Map request |
| Terrain, biome, topology masks | **REQUIRES ADDITIONAL DATA SOURCE** | Imported Rust `.map`; not in Rust+ map response |
| Static build-planning candidates | **REQUIRES ADDITIONAL DATA SOURCE** | Derived from `.map` terrain/path data plus a build-versioned no-build catalogue; not a build permission check |
| Elevation contours and water depth | **REQUIRES ADDITIONAL DATA SOURCE** | Derived from imported terrain and water heights |
| Roads, rails, rivers | **REQUIRES ADDITIONAL DATA SOURCE** | Imported Rust `.map` path data |
| Monument token/name and position | **AVAILABLE DIRECTLY THROUGH RUST+** | Friendly naming may need local mapping |
| Team roster, position, online/alive state | **AVAILABLE DIRECTLY THROUGH RUST+** | Team snapshot |
| Grid reference | **AVAILABLE DIRECTLY THROUGH RUST+** | Locally derived from direct coordinates/map size |
| Team chat read/send | **AVAILABLE DIRECTLY THROUGH RUST+** | Team chat requests/broadcast |
| Team map notes | **AVAILABLE DIRECTLY THROUGH RUST+** | Team snapshot |
| Death/respawn event | **AVAILABLE THROUGH EXISTING OPEN-SOURCE LIBRARIES** | Snapshot-derived from alive and timestamps |
| Killer, weapon, cause of death | **REQUIRES RUST SERVER ACCESS** | Plugin/log/RCON-class data source |
| Steam avatar/profile | **REQUIRES ADDITIONAL DATA SOURCE** | Steam/profile API |
| Cargo, CH47, patrol heli, crates, explosions | **AVAILABLE DIRECTLY THROUGH RUST+** | Current map markers |
| Travelling vendor | **AVAILABLE DIRECTLY THROUGH RUST+** | Newer marker type; older schemas may label unknown |
| Marker lifecycle events | **AVAILABLE THROUGH EXISTING OPEN-SOURCE LIBRARIES** | Snapshot diff |
| Oil-rig monument | **AVAILABLE DIRECTLY THROUGH RUST+** | Monument data |
| Oil-rig activation | **AVAILABLE THROUGH EXISTING OPEN-SOURCE LIBRARIES** | Heuristic, not direct event |
| Vending position, orders, price, stock | **AVAILABLE DIRECTLY THROUGH RUST+** | Vending marker sell orders |
| Vending item names/icons (names only) | **AVAILABLE VIA EXTERNAL CATALOGUE** | Versioned Rust item catalogue, not live Rust+ data |
| Vending owner Steam ID | **NOT CURRENTLY POSSIBLE** | Not in verified marker contract |
| Smart Switch read/control | **AVAILABLE DIRECTLY THROUGH RUST+** | Paired entity ID |
| Smart Alarm state/subscription | **AVAILABLE DIRECTLY THROUGH RUST+** | Paired entity ID; read-only, no in-game control surface |
| Storage Monitor | **AVAILABLE DIRECTLY THROUGH RUST+** | Paired entity ID |
| Smart device discovery and pairing | **REQUIRES ADDITIONAL DATA SOURCE** | No Rust+ enumeration; entity ID/type/name arrives only via the FCM pairing notification triggered by pairing in-game |
| Generic electrical entity discovery | **NOT CURRENTLY POSSIBLE** | No verified enumeration request |
| Dedicated light/door/lock/turret state | **NOT CURRENTLY POSSIBLE** | Smart Switch can only control a wired circuit indirectly |
| CCTV stream and supported input | **AVAILABLE DIRECTLY THROUGH RUST+** | Requires known camera code; ray stream is client-decoded via `RustPlusApi.Camera` |
| CCTV discovery and map position | **REQUIRES ADDITIONAL DATA SOURCE** | No Rust+ discovery; user enters and nicknames a known code per server |
| Exact live resource-node locations | **REQUIRES RUST SERVER ACCESS** | Dynamic spawned entities are not in Rust+ or static `.map` data |
| Resource spawn potential | **REQUIRES ADDITIONAL DATA SOURCE** | Derived from imported topology; biome/splat spawn-rule evaluation is still needed for higher fidelity |
| Exact recycler locations | **REQUIRES ADDITIONAL DATA SOURCE** | Static catalogue or parsed server map |
| Arbitrary world-entity enumeration | **NOT CURRENTLY POSSIBLE** | No verified generic entity request |
| Server-wide kill feed and raid events | **REQUIRES RUST SERVER ACCESS** | Plugin/log/server source |
| Historical positions/events | **AVAILABLE DIRECTLY THROUGH RUST+** | Application records successive direct snapshots locally |
| Existing authoritative history archive | **REQUIRES ADDITIONAL DATA SOURCE** | Unofficial history API is deferred |
