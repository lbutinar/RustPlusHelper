# ADR 0003: SQLite local persistence

- Status: Accepted for Phase 2
- Date: 2026-08-17

## Context

The desktop app needs relational server/session/event data but must remain easy for a normal user to
install. SQL Server Express and PostgreSQL add a service, installer, credentials, and maintenance.

## Decision

Use an application-local SQLite database with WAL, foreign keys, focused repositories, and explicit
migrations.

## Consequences

- No database service or administrator setup.
- Background writes must remain short and batched.
- Retention is required for event/chat/history growth.
- Unsigned Rust identifiers use decimal text when they exceed signed 64-bit range.
- Secrets stay in an OS-backed `ISecretStore`; SQLite receives ciphertext only.
