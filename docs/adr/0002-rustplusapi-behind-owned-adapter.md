# ADR 0002: RustPlusApi behind an owned adapter

- Status: Accepted with Phase 0 live validation pending
- Date: 2026-08-17

## Context

`rustplus.js` is the established unofficial reference, but using it from Tauri/.NET requires Node or
IPC. RustPlusApi provides an active typed C# implementation, current protobuf coverage, native
WebSocket support, pairing packages, camera functionality, and tests. Its 2.0 release is beta and the
entire Rust+ protocol remains unofficial.

## Decision

Pin `RustPlusApi` `2.0.0-beta.7`. Wrap it in `RustPlusApiClient`, implementing the application-owned
`IRustPlusClient`. Map every third-party response to canonical snapshots before returning.

## Consequences

- A package upgrade or fork is local to one infrastructure project.
- Unknown marker type and raw numeric value must survive mapping.
- No application/UI project references RustPlusApi directly.
- Live Phase 0 evidence is required before building pairing and desktop features.
- A custom protobuf implementation requires a separately approved, evidence-backed ADR.
