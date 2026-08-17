# RustPlusHelper repository instructions

## Product scope

RustPlusHelper is a Windows-first personal Rust companion dashboard. The map is the primary product
surface. Work should focus on verified Rust+ companion data: connections, servers, teams, chat,
maps, markers, events, vending machines, supported smart devices, cameras, notifications, and local
history.

Do not add raid, crafting, recycling, electricity, or other generic Rust calculators.

## Protocol evidence rule

Never invent a Rust+ endpoint, protobuf field, authentication step, event, or entity capability.
Before changing protocol behavior:

1. Check official Facepunch companion documentation.
2. Check the pinned `RustPlusApi` version and current upstream changes.
3. Compare with `liamcottle/rustplus.js`.
4. Record confirmed behavior or uncertainty in `docs/protocol-evidence.md`.
5. Add a sanitized fixture or focused test when practical.

Direct, snapshot-derived, heuristic, external-source, and server-access data must remain visibly
distinct.

## Architecture rules

- Third-party Rust+ types stay inside `RustPlusHelper.Infrastructure.RustPlus`.
- Application and UI code depend on `IRustPlusClient` and application-owned snapshots.
- Do not write a custom Rust+ protobuf implementation until the adapter has a demonstrated gap and
  the change is approved.
- Keep WebSocket lifetime, polling, and reconnection out of UI components.
- Store unsigned 64-bit Rust identifiers safely; SQLite persistence should use canonical decimal
  text where signed integer range is insufficient.

## Security rules

- Never commit or log player tokens, FCM credentials, Expo tokens, Facepunch auth tokens, pairing
  payloads, or raw authenticated requests.
- Live credentials may come only from the secret provider or process environment, never command-line
  arguments or committed configuration.
- Treat direct `ws://` as insecure. It requires an explicit opt-in and must never be a silent fallback.
- Verification output belongs under ignored `artifacts/` unless it has been manually sanitized.
- Never commit live chat text, player names/IDs, server addresses, or precise positions as fixtures.

## Build and test

The intended toolchain is Windows .NET SDK 10, pinned by `global.json`.

```powershell
dotnet restore .\RustPlusHelper.slnx
dotnet build .\RustPlusHelper.slnx --no-restore
dotnet test .\RustPlusHelper.slnx --no-build
dotnet run --project .\src\RustPlusHelper.Verification -- --fake
```

Run focused tests during development and the complete suite before handoff. A live Rust+ smoke test is
manual and opt-in; it must never run automatically in CI.
