# Database design

SQLite is planned for the desktop application. It needs no service, administrator setup, or separate
installer and remains relational and inspectable.

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

The schema will be created in Phase 2, after live Phase 0 evidence and the map-first shell.
