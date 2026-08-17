# Local storage operations

## Location

The desktop database is stored at:

```text
%LOCALAPPDATA%\RustPlusHelper\rustplushelper.db
```

SQLite may also create `rustplushelper.db-wal` and `rustplushelper.db-shm` while the application is
running. These are normal WAL files and must be copied together with the main database if taking a
file-level backup while the app is open. Prefer closing the application before copying.

## Current contents

- migration history;
- one application-level Steam64 player identity;
- saved server names, companion host/port, proxy choice, effective pairing-identity snapshot, and
  timestamps;
- purpose-labelled DPAPI ciphertext for pairing secrets.
- latest successful Rust+ server/map snapshot, including the JPEG and retrieval timestamp.

Team, chat, marker history, and device data are not persisted yet. A map refresh replaces the prior
snapshot for that server; deleting a server cascades its cached map.

## Security boundary

The database is not globally encrypted. Server addresses and names are local personal data but not
credentials. Secret values are individually protected with Windows DPAPI `CurrentUser`; another
Windows account or machine should not be able to decrypt copied ciphertext.

Do not send the database as a diagnostic attachment. A future diagnostics exporter must allowlist
non-sensitive fields rather than copying database rows.

## Recovery

There is no automated repair or reset command yet. If initialization fails, preserve the database and
its WAL/SHM siblings for diagnosis rather than deleting them. Migrations are forward-only and recorded
in `schema_migrations`. An older app refuses to open a database whose recorded schema is newer than it
supports.
