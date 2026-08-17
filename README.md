# RustPlusHelper

RustPlusHelper is a Windows-first Rust+ companion dashboard under active development. Its central
feature will be an interactive Rust map with team positions, map notes, vending machines, world-event
markers, supported smart devices, cameras, notifications, and locally recorded history.

This repository currently contains the approved **Phase 0 protocol verification spike**, the
**Phase 1 map-first desktop skeleton**, and the **Phase 2 local persistence foundation**. The map
still uses deterministic fake data until live protocol verification and later connection phases are
complete.

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
- an add/edit/select/confirm-remove Servers interface with no manual token field.

Still requiring a real paired server:

- live validation of the secure Facepunch proxy and the five read-only calls;
- capture of reviewed, sanitized raw protocol fixtures;
- confirmation of real map/grid alignment against the official Rust+ app.

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

Launch the Phase 1 desktop shell:

```powershell
dotnet run --project .\src\RustPlusHelper.Desktop
```

The map is intentionally marked `FAKE DATA`. The app does not contact a Rust server or ask for Rust+
credentials yet. Saved server profiles contain connection metadata only.

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

No project license has been selected yet. The pinned `RustPlusApi` dependency is MIT licensed. Do not
assume this repository itself is open source until a project license is explicitly added.
