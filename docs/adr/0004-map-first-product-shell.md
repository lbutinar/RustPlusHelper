# ADR 0004: Map-first product shell

- Status: Accepted
- Date: 2026-08-17

## Context

The useful Rust+ data—team positions, notes, vending machines, monuments, and world events—is spatial.
A generic dashboard would delay the central product value and later force map integration across
already-designed pages.

## Decision

The map is the default page from the first desktop phase. Team, events, vending, and devices link to
and select map objects. A summary dashboard is optional later.

## Consequences

- Fake map data and Leaflet interop arrive before live pairing UI.
- Layer availability must reflect the real source: direct, derived, heuristic, external, or absent.
- World coordinates remain canonical; grid and pixel coordinates are projections.
