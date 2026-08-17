# ADR 0005: DPAPI current-user secret protection

- Status: Implemented in Phase 2
- Date: 2026-08-17

## Context

Rust+ player tokens and future notification credentials must survive application restarts without
being logged or stored as plaintext. Normal users should not install or manage a separate vault.

## Decision

Protect secret bytes with Windows DPAPI using `DataProtectionScope.CurrentUser`. Bind ciphertext to
the application format version, server ID, and secret purpose through additional entropy. Store the
resulting blob in the SQLite `pairings` table behind the application-owned `ISecretStore` interface.

## Consequences

- Only the same Windows user profile can normally decrypt the value.
- Copying the database is not a portable credential backup.
- A future export feature needs explicit password-based re-encryption.
- Retrieved cleartext buffers are caller-owned and must be zeroed after use.
- Pairing removal is enforced by the server foreign-key delete cascade.
