# RustPlusHelper

RustPlusHelper is a Windows-first Rust+ companion dashboard under active development. Its central
feature will be an interactive Rust map with team positions, map notes, vending machines, world-event
markers, supported smart devices, cameras, notifications, and locally recorded history.

This repository currently contains the protocol adapter, map-first desktop shell, local persistence,
automatic and manual pairing, authenticated connection testing, and the first live-map slice. A selected paired
server can now download its real Rust+ JPEG map and reopen the latest snapshot from SQLite.

RustPlusHelper is an unofficial community project. It is not affiliated with or endorsed by
Facepunch Studios. Rust and Rust+ names and brand assets belong to Facepunch Studios and are used
only to identify compatibility with its game and companion service.

## Current status

Implemented:

- .NET 10 solution and Git repository;
- application-owned `IRustPlusClient` boundary;
- adapter for `RustPlusApi` `2.0.0-beta.7`;
- deterministic `FakeRustPlusClient`;
- read-only live/fake verification command;
- aggregate-only redacted reports and map-JPEG output;
- tests for optional fields, unsigned 64-bit IDs, unknown markers, disconnect behavior, and secret
  redaction;
- project architecture, protocol, security, database, UI, and roadmap documents;
- WPF and Blazor Hybrid Windows host;
- offline Leaflet `CRS.Simple` map with a local fake development image;
- map-first navigation, independent truthful layers, team/chat, event-source, vending, device,
  server, and settings previews;
- application-owned map dashboard state and tested world-to-image projection;
- privacy-safe Debug smoke capture that validates the rendered app shell without reading desktop
  pixels;
- SQLite migrations and persistent multi-server profiles under the current Windows user's local app
  data;
- a DPAPI `CurrentUser` secret store that persists only ciphertext;
- an add/edit/select/confirm-remove Servers interface;
- user-initiated native Rust+ registration and a pairing listener that captures and saves the server
  address, Steam64 ID, and per-server player token without displaying those sensitive values;
- one application-level Steam64 identity plus masked per-server Rust+ player-token entry, pairing
  status, and DPAPI-protected token persistence;
- a read-only per-server connection test that uses the saved transport choice, validates pairing with
  server information, reports distinct failure states, and closes the test socket. Secure proxy is
  the default; plaintext direct transport requires an explicit persisted opt-in;
- live server-information and map download through the selected saved profile;
- SQLite map snapshot caching, real JPEG rendering, manual refresh, and truthful live/cached/fake
  source labels;
- live-map layers for the base map, monuments, team positions/notes, vending machines, and known
  world markers when their owning request succeeds;
- a centered Rust grid layer, player/marker grid references, and friendly monument names with
  meaningful short glyphs;
- one-connection read-only live refreshes for team members/positions, team notes, recent chat, and
  map markers, with partial failures kept independent;
- one persistent selected-server Rust+ monitor with centralized polling, reconnect backoff, and
  bounded per-server connection/team/death/respawn/marker event history that survives restart. The
  map JPEG is never polled;
- snapshot-derived team grid-crossing events with a one-minute per-member anti-spam cooldown;
- a toggleable team-death hotspot layer that groups locally recorded death-snapshot positions by
  Rust grid and scales each red hotspot by its recent death count;
- vending search over machine names, catalogue-resolved friendly item/currency names, and numeric
  item/currency IDs, with derived grid and nearest-online-teammate distance; an unresolved ID always
  falls back to the raw number rather than a guess;
- vending price/stock/offer-slot-added/removed history events, derived by comparing each machine's
  sell orders across polls;
- native import of Rust `.map` files using the documented version-10, legacy-LZ4, protobuf format;
- automatic discovery through Steam libraries, with a server-to-world match from Rust's client log
  or an exact documented procedural size+seed filename; ambiguous same-size files are never guessed;
- per-server SQLite persistence and toggleable biome, topology, terrain-slope, build-planning,
  elevation/contour, water-depth, ore-potential, road, rail, detailed-river, and no-build overlays.
  Build planning combines static evidence but does not claim that candidate terrain is guaranteed
  buildable. Each raster renders as its own Leaflet image overlay on a fixed-z-index pane, and
  visibility-only changes toggle existing layers rather than re-rasterizing or rebuilding the map.

Still requiring live verification or later phases:

- secure Facepunch proxy validation (the tested server currently returns HTTP 418 at upgrade);
- live validation of team chat for a server/team state that returns chat data;
- capture of reviewed, sanitized raw protocol fixtures;
- confirmation of real map/grid alignment against the official Rust+ app.
- live end-to-end validation of native browser registration and automatic pairing capture.

## Prerequisites

- Windows 10 or 11
- .NET SDK 10.0.303 or a compatible later patch
- Valid Rust+ pairing details only for the optional live verification

## Build and test

```powershell
dotnet restore .\RustPlusHelper.slnx
dotnet build .\RustPlusHelper.slnx --no-restore
dotnet test .\RustPlusHelper.slnx --no-build
```

Run the deterministic verification without credentials:

```powershell
dotnet run --project .\src\RustPlusHelper.Verification -- --fake
```

Launch the desktop app:

```powershell
dotnet run --project .\src\RustPlusHelper.Desktop
```

With no saved server, the app opens deterministic fake data. With a selected paired server, it opens
the cached map when available or performs the first live map download. Use **Open map** on a server
or **Refresh everything** on the map page for an explicit refresh. Team/chat/marker polling starts
automatically for the selected server.

On the Servers page, choose **Set up automatic pairing** once and complete the Steam sign-in in the
browser. Then choose **Listen for server pairing**, join the server in Rust, and use **Pair with
Server**. The received server details are saved automatically. Manual entry remains available.

The app automatically checks Steam's Rust map cache after obtaining current server information. It
uses Rust's connection log when current cache filenames hide the Rust+ seed, or exact size+seed for
the documented procedural filename. Join the selected server in Rust once if the status asks for a
connection-log match. **Choose .map manually** remains available when evidence is missing or
ambiguous. The app never selects a file only because it is newest or the same size, and the original
file and its full path are not copied into app storage.

Generated reports and map images are written below `artifacts/`, which Git ignores.

## Live verification

Credentials are deliberately not accepted as command-line options. Follow
[`docs/live-verification.md`](docs/live-verification.md) to configure .NET user-secrets or temporary
environment variables and run the read-only check.

## Documents

- [Architecture](docs/architecture.md)
- [Protocol evidence](docs/protocol-evidence.md)
- [Capability matrix](docs/capability-matrix.md)
- [Security model](docs/security-model.md)
- [Live verification procedure](docs/live-verification.md)
- [Database design](docs/database-design.md)
- [Local storage operations](docs/local-storage.md)
- [UI design](docs/ui-design.md)
- [Testing strategy](docs/testing.md)
- [Development plan](docs/development-plan.md)
- [Architecture decisions](docs/adr/README.md)
- [Third-party notices](THIRD-PARTY-NOTICES.md)

## License

RustPlusHelper is licensed under the [MIT License](LICENSE). Third-party components retain their own
licenses and terms as listed in [Third-party notices](THIRD-PARTY-NOTICES.md). Facepunch brand
assets are not covered by this project's MIT license.
