# Sanitized live protocol fixtures

This directory is reserved for sanitized, server-derived protocol fixtures captured during an
explicit live verification session.

Do not manufacture a fixture and label it as live Rust+ evidence. Do not commit a fixture until it
has been checked for:

- player tokens and authentication material;
- player and server identifiers;
- player names and chat messages;
- server addresses and branding;
- coordinates that the fixture does not need to test.

`RustPlusApi` does not currently expose a public raw-frame capture hook. Until a reviewed capture
mechanism exists, the Phase 0 command writes only an aggregate normalized report and the map JPEG to
the ignored `artifacts/` directory. Binary protobuf fixtures remain a live-validation task, not a
synthetic claim.
