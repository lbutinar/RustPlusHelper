# Contributing

## Before changing Rust+ behavior

Read `AGENTS.md`, `docs/protocol-evidence.md`, and `docs/security-model.md`. Protocol claims require
source or live evidence. If evidence is incomplete, retain the uncertainty in code and documentation.

## Development workflow

1. Keep changes within the approved roadmap phase.
2. Add or update focused tests.
3. Run build and test commands from `README.md`.
4. Use fake data unless live verification is explicitly required.
5. Inspect staged changes for credentials and server-derived personal data.
6. Update the relevant document or ADR when a technical decision changes.

Do not commit generated `artifacts/`, logs, local databases, credentials, pairing payloads, or live
chat/player data.
