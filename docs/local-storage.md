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
- latest successful Rust+ server/map snapshot, including the JPEG and retrieval timestamp;
- optional per-server display rasters and paths derived from an automatically matched or manually
  selected Rust `.map`;
- a bounded, per-server companion event history (connection/team/marker/vending/alarm events, 200-row
  and 30-day caps);
- saved camera codes/nicknames and paired Smart Switch/Alarm/Storage Monitor entity IDs;
- downsampled team-member movement trail points (unsigned Steam64 ID, world X/Y, and a timestamp),
  kept up to a 14-day safety cap and cascade-deleted with their server; live chat text and raw team
  snapshots themselves are not persisted.

A map refresh replaces the prior snapshot for that server; deleting a server cascades its cached map,
companion events, saved cameras, paired entities, and movement trail points.
Importing topology replaces the previous derived topology for that server. Only the source filename,
SHA-256 fingerprint, decoded metadata, normalized paths, and 384-pixel RGBA overlays are kept; the
original file and absolute path are not copied.

## Security boundary

The database is not globally encrypted. Server addresses and names are local personal data but not
credentials. Secret values are individually protected with Windows DPAPI `CurrentUser`; another
Windows account or machine should not be able to decrypt copied ciphertext.

Do not send the database as a diagnostic attachment. The diagnostics exporter
(`RustPlusHelper.Infrastructure.Storage.Diagnostics.DiagnosticsExportService`, Phase 11) follows this:
it never opens or copies the SQLite file. Its `servers.txt` entry is allowlisted to display name, port,
and transport (secure proxy vs. direct) per saved server — host/IP and player ID are deliberately
omitted, since a diagnostics zip may end up pasted into a public bug report.

## Recovery

There is no automated repair or reset command yet. If initialization fails, preserve the database and
its WAL/SHM siblings for diagnosis rather than deleting them. Migrations are forward-only and recorded
in `schema_migrations`. An older app refuses to open a database whose recorded schema is newer than it
supports.
