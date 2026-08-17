# ADR 0003: SQLite local persistence

- Status: Implemented in Phase 2
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
- Secrets use Windows DPAPI `CurrentUser` protection through `ISecretStore`; SQLite receives
  ciphertext only.
- Microsoft.Data.Sqlite's synchronous APIs are used deliberately because its async methods execute
  synchronously.
