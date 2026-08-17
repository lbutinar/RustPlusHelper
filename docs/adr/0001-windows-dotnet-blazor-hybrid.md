# ADR 0001: Windows .NET and Blazor Hybrid

- Status: Accepted
- Date: 2026-08-17

## Context

The product is Windows-first, map-heavy, continuously running, and needs WebSocket/protobuf support,
native notifications, tray behavior, maintainable packaging, and good Codex ergonomics.

## Decision

Use .NET 10 with WPF as the native host and Blazor Hybrid for primary UI content. Use Leaflet inside
the Blazor surface for the non-geographical Rust map.

## Consequences

- Core, background, persistence, and Windows integration remain C#.
- Web technology is used where it materially helps interactive map/UI work.
- The application depends on WebView2 availability/packaging.
- Keep map overlays inside the Blazor surface to avoid native/browser overlay issues.
- Electron remains a fallback if C# protocol/pairing dependencies become untenable.
