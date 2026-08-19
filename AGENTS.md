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

## Map rendering rules

- A layer/filter visibility toggle must stay an O(1) DOM operation in `mapInterop.js`
  (`rustPlusMap.setLayerVisibility` adding/removing existing Leaflet layers). It must never
  re-rasterize, re-encode, or otherwise regenerate the base map image or a raster overlay — that work
  runs synchronously on the WebView2 UI thread and freezes the app. A canvas-compositing approach was
  tried and reverted for exactly this reason; see [ui-design.md](docs/ui-design.md).
- Only a genuine data change (new map image, topology import, markers, team snapshot) may rebuild
  Leaflet layers. `MapCanvas.razor`'s `OnAfterRenderAsync` already distinguishes this via reference
  equality on `MapDashboardState`; preserve that short-circuit rather than routing visibility changes
  through the full-render path.
- Each terrain/topology raster layer renders as its own Leaflet image overlay on a dedicated
  fixed-z-index pane (`raster-<layerKey>`), so stacking order is deterministic no matter what order
  the user toggles layers in. Give any new raster layer its own pane in `rasterLayerOrder` rather than
  relying on DOM insertion order.

## Notification and tray rules

- `System.Windows.Forms` types (`NotifyIcon`, `ContextMenuStrip`, etc.) must stay isolated to their own
  file(s) using fully-qualified names, never a bare `using System.Windows.Forms;` — that namespace
  defines `Application`/`MessageBox`/etc. with the same names as WPF's `System.Windows`, which this
  project uses everywhere else, and the two collide as soon as both usings are in scope together.
- Third-party FCM/notification types (`RustPlusApi.Fcm.*`) never escape
  `RustPlusHelper.Infrastructure.RustPlus` — the same adapter boundary rule as every other Rust+
  integration. The Application layer only sees app-owned types (`AlarmTriggeredCapture`,
  `AlarmToastNotification`, etc.).
- A new companion-event-producing feature must raise it through
  `RustPlusLiveSessionManager.AddEvent`/`RecordExternalEvent` (never append to
  `ICompanionEventRepository` directly) so it's automatically picked up by `EventRecorded` and reaches
  both the Events timeline and the notification dispatcher.

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
