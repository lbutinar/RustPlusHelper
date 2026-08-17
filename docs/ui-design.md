# Map-first UI design

## Current implementation status

Implemented:

- WPF window and Blazor Hybrid root;
- locally vendored Leaflet 1.9.4 using `CRS.Simple`;
- offline fake SVG map with world-to-image projected markers;
- Map, Team, Events, Vending, Devices, Servers, and Settings navigation;
- independent layers and disabled explanations for data Rust+ does not position;
- responsive dark Rust-inspired styling;
- bUnit navigation and interaction coverage.
- real Rust+ JPEG rendering for the selected paired server;
- cache-first startup, explicit live refresh, and live/cached/fake source badges;
- base-map, monument, team, note, vending, and world-marker layers enabled only when their verified
  snapshots are available;
- explicit lightweight team/chat/marker refresh and full map+data refresh actions;
- team positions, team notes, vending/world markers, and partial request errors rendered directly
  from the latest successful Rust+ snapshot.
- live connection status plus a bounded event feed for transport, online/offline, death/respawn, and
  marker lifecycle transitions;
- explicit `.map` import plus disabled-by-default biome, terrain-topology, ore-potential, road, rail,
  and river layers. Exact no-build geometry remains disabled with its missing-source explanation.

The map grid is a toggleable derived layer based on Facepunch's current centered-grid formula. Grid
references also appear in marker tooltips and the team roster. A future search box may navigate to a
typed grid without changing the projection.

## Server registry

The Servers page uses the real local SQLite registry. It provides add, edit, select, test, open-map,
and two-step removal actions. The form defaults to the Facepunch secure proxy, accepts a masked
per-server player token, and persists only DPAPI-protected ciphertext.

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

Phase 1 currently sends normalized complete overlay snapshots to a persistent Leaflet map and clears
individual layer groups. Fine-grained add/update/remove deltas are deferred until live polling exists.

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

The ore-potential layer is labelled `DERIVED · NOT LIVE NODES`. It currently visualizes the
documented Cliffside topology and lower-confidence Decor/Clutter topology; it does not claim that an
ore node is presently spawned at a pixel.

World `x/y` is canonical. Pixel and grid values are projections. Marker updates should be sent to
Leaflet as deltas rather than serializing the whole map for every team movement.
