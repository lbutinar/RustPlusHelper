# Database design

SQLite is used by the desktop application. It needs no service, administrator setup, or separate
installer and remains relational and inspectable.

## Current implementation

The current database lives at `%LOCALAPPDATA%\RustPlusHelper\rustplushelper.db`. Versioned migrations
currently create:

- `schema_migrations`;
- `player_identity` for the one Steam64 ID used across saved servers;
- `servers` for non-secret connection metadata;
- `pairings` for purpose-labelled DPAPI ciphertext.
- `map_cache` for the latest server/map metadata and Rust+ JPEG per saved server;
- `map_topology` for display-ready data derived from one automatically matched or manually selected
  Rust `.map` per server.

The player identity is the application-level source of truth. A server row retains the effective
Steam64 ID snapshot associated with its current per-server pairing token so a future identity change
cannot silently reuse a token issued to a different player.

The map cache is deliberately a latest-snapshot cache rather than map history: metadata is stored as
JSON and the JPEG as a BLOB behind the server foreign key. Session normalization and history wait for
verified wipe/session behavior. Creating the entire future schema now would make unverified
assumptions. Session, team, marker,
market, device, camera, event, death, chat, and notification tables remain planned until their owning
phase has real write/read behavior.

`map_topology` stores the source basename, SHA-256 fingerprint, serialization version, opaque source
timestamp, world size, layer summaries, normalized paths, prefab count, and small RGBA overlays. It
does not store the original `.map`, its full local path, or extracted Facepunch assets. Deleting a
server cascades both map caches.

## Planned persisted data

- saved servers and encrypted pairing references;
- wipe/map sessions, cached JPEG, and monuments;
- latest team roster state;
- latest map/vending marker state;
- paired device aliases and latest state;
- user-entered camera codes and optional manual positions;
- semantic events, deaths, optional chat, and notification rules.

Do not persist every raw request, response, camera frame, or high-frequency movement sample by default.

## Initial table set

```text
Servers
Pairings
Sessions
MapAssets
MapMonuments
Players
Teams
TeamMembers
MapMarkers
VendingMachines
VendingItems
SmartDevices
Cameras
Events
Deaths
ChatMessages (optional retention)
NotificationRules (later)
```

There is deliberately no generic `Entities` table. Verified Rust+ sources are better represented by
map markers, vending machines, smart devices, and cameras. Add a generic entity model only when a real
external/server data source requires one.

## Storage rules

- Enable foreign keys and WAL mode.
- Store timestamps as UTC Unix values with a documented precision.
- Store Steam64, marker, and entity IDs as canonical decimal text where unsigned values may exceed
  SQLite's signed 64-bit range.
- Use a local session ID because Rust+ does not provide a durable application-ready team ID.
- Detect a new map session from server/wipe time plus map hash and size.
- Keep secrets encrypted through `ISecretStore`; never place plaintext tokens in a normal column.
- Apply bounded retention to events, chat, vending history, and optional sampled trails.

Phase 2 uses `Microsoft.Data.Sqlite` synchronous APIs because its documented async methods execute
synchronously. Connections explicitly enable foreign keys, the database uses WAL and `NORMAL`
synchronous mode, and mutations use parameters. The native SQLite asset is explicitly pinned to
`SQLitePCLRaw.bundle_e_sqlite3` 2.1.12 because the transitive 2.1.11 bundle is vulnerable.
