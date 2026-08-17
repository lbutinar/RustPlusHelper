# Map-first UI design

The application opens to the map, not a generic dashboard.

```text
┌────────────────────────────────────────────────────────────┐
│ Server: EU Main        Ready · 82 ms        Search / Grid │
├─────────┬───────────────────────────────────┬──────────────┤
│ Map     │                                   │ Layers       │
│ Team    │                                   │ ☑ Team       │
│ Events  │            RUST MAP               │ ☑ Vending    │
│ Vending │                                   │ ☑ Monuments  │
│ Devices │                                   │ ☑ Events     │
│ Servers │                                   │ ☐ CCTV       │
│ Settings│                                   │ ☐ Recyclers* │
├─────────┴───────────────────────────────────┴──────────────┤
│ 14:37 Steve died near H14 · Snapshot-derived              │
└────────────────────────────────────────────────────────────┘
```

## Planned technology

- WPF process/window host;
- Blazor Hybrid for the complete primary UI surface;
- Leaflet `CRS.Simple` for the Rust JPEG and overlays;
- small JavaScript adapter receiving canonical map DTOs and batched deltas.

Keeping the full interactive content inside `BlazorWebView` avoids mixing native WPF overlays with the
browser-rendered map.

## Pages

1. Map
2. Team and Chat
3. Events
4. Vending
5. Devices and Cameras
6. Servers
7. Settings

A dashboard may be added later as a configurable summary; it is not the initial home screen.

## Layer truthfulness

Direct, derived, heuristic, and external layers must be distinguishable. An unavailable layer is
disabled with an explanation such as “requires parsed map data”; it must not show guessed locations.

World `x/y` is canonical. Pixel and grid values are projections. Marker updates should be sent to
Leaflet as deltas rather than serializing the whole map for every team movement.
