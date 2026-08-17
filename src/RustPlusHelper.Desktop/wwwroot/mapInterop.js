(() => {
    "use strict";

    const entries = new Map();
    let activeElementId = null;

    const layerKeys = [
        "baseMap",
        "grid",
        "team",
        "teamNotes",
        "vendingMachines",
        "monuments",
        "events",
        "smartDevices",
        "cameras"
    ];

    const markerGlyphs = {
        "team": "T",
        "team-note": "+",
        "vending": "V",
        "monument": "?",
        "cargo": "C",
        "ch47": "47",
        "patrol-heli": "H",
        "crate": "□",
        "explosion": "!",
        "death": "†",
        "radius": "○",
        "travelling-vendor": "TV",
        "unknown": "?"
    };

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll("\"", "&quot;")
            .replaceAll("'", "&#039;");
    }

    function createEntry(elementId, imageUrl, model) {
        const map = L.map(elementId, {
            crs: L.CRS.Simple,
            minZoom: -1.5,
            maxZoom: 3,
            zoomSnap: 0.25,
            zoomControl: false,
            preferCanvas: true
        });

        L.control.zoom({ position: "bottomleft" }).addTo(map);

        const bounds = L.latLngBounds([0, 0], [model.height, model.width]);
        const layers = Object.fromEntries(layerKeys.map(key => [key, L.layerGroup()]));
        const image = L.imageOverlay(imageUrl, bounds, {
            interactive: false,
            className: "rust-base-map"
        });
        layers.baseMap.addLayer(image);

        const entry = { map, bounds, layers, width: model.width, height: model.height, imageUrl };
        entries.set(elementId, entry);
        activeElementId = elementId;
        map.fitBounds(bounds, { animate: false, padding: [18, 18] });
        map.setMaxBounds(bounds.pad(0.15));
        return entry;
    }

    function destroyEntry(elementId) {
        const entry = entries.get(elementId);
        if (!entry) {
            return;
        }

        entry.map.remove();
        entries.delete(elementId);
        if (activeElementId === elementId) {
            activeElementId = null;
        }
    }

    function ensureEntry(elementId, imageUrl, model) {
        const existing = entries.get(elementId);
        if (existing
            && existing.width === model.width
            && existing.height === model.height
            && existing.imageUrl === imageUrl) {
            activeElementId = elementId;
            return existing;
        }

        destroyEntry(elementId);
        return createEntry(elementId, imageUrl, model);
    }

    function makeMarker(item, imageHeight) {
        const glyph = item.glyph || markerGlyphs[item.kind] || "?";
        const stateClasses = [
            item.kind === "team" && !item.isOnline ? "offline" : "",
            item.kind === "team" && !item.isAlive ? "dead" : ""
        ].filter(Boolean).join(" ");
        const safeKind = Object.hasOwn(markerGlyphs, item.kind) ? item.kind : "unknown";
        const icon = L.divIcon({
            className: `rust-marker-wrap ${stateClasses}`,
            html: `<span class="rust-marker marker-${safeKind}">${escapeHtml(glyph)}</span>`,
            iconSize: [32, 32],
            iconAnchor: [16, 16]
        });
        const marker = L.marker([imageHeight - item.pixelY, item.pixelX], {
            icon,
            keyboard: true,
            title: String(item.label ?? item.kind)
        });
        const tooltip = [
            `<strong>${escapeHtml(item.label ?? item.kind)}</strong>`,
            item.gridReference ? `<span>Grid ${escapeHtml(item.gridReference)}</span>` : "",
            `<span>X ${Number(item.worldX).toFixed(0)} · Y ${Number(item.worldY).toFixed(0)}</span>`
        ].filter(Boolean).join("");
        marker.bindTooltip(tooltip, {
            direction: "top",
            offset: [0, -13],
            className: "rust-map-tooltip"
        });
        return marker;
    }

    function gridLabel(entry, text, pixelX, pixelY) {
        const icon = L.divIcon({
            className: "rust-grid-label-wrap",
            html: `<span class="rust-grid-label">${escapeHtml(text)}</span>`,
            iconSize: [30, 18],
            iconAnchor: [15, 9]
        });
        return L.marker([entry.height - pixelY, pixelX], {
            icon,
            interactive: false,
            keyboard: false,
            zIndexOffset: -1000
        });
    }

    function renderGrid(entry, grid) {
        if (!grid || !Number.isInteger(grid.cellCount) || grid.cellCount <= 0) {
            return;
        }

        const layer = entry.layers.grid;
        const width = grid.right - grid.left;
        const height = grid.bottom - grid.top;
        const lineOptions = index => ({
            color: "#d6ddd8",
            weight: index % 5 === 0 ? 1.15 : 0.65,
            opacity: index % 5 === 0 ? 0.34 : 0.19,
            interactive: false,
            className: index % 5 === 0 ? "rust-grid-line major" : "rust-grid-line"
        });

        for (let index = 0; index <= grid.cellCount; index++) {
            const x = grid.left + (width * index / grid.cellCount);
            const y = grid.top + (height * index / grid.cellCount);
            layer.addLayer(L.polyline(
                [[entry.height - grid.top, x], [entry.height - grid.bottom, x]],
                lineOptions(index)));
            layer.addLayer(L.polyline(
                [[entry.height - y, grid.left], [entry.height - y, grid.right]],
                lineOptions(index)));
        }

        for (let index = 0; index < grid.cellCount; index++) {
            const centerX = grid.left + (width * (index + 0.5) / grid.cellCount);
            const centerY = grid.top + (height * (index + 0.5) / grid.cellCount);
            const labelInset = height * (grid.cellCount > 2 ? 1.5 : 0.5) / grid.cellCount;
            const rowLabelX = grid.left >= 18 ? grid.left - 10 : grid.left + 10;
            layer.addLayer(gridLabel(entry, grid.columnLabels[index], centerX, grid.top + labelInset));
            if (grid.cellCount > 3) {
                layer.addLayer(gridLabel(entry, grid.columnLabels[index], centerX, grid.bottom - labelInset));
            }
            layer.addLayer(gridLabel(entry, grid.rowLabels[index], rowLabelX, centerY));
        }
    }

    function applyVisibility(entry, visibility) {
        for (const key of layerKeys) {
            const layer = entry.layers[key];
            const shouldShow = visibility[key] === true;
            const isShown = entry.map.hasLayer(layer);
            if (shouldShow && !isShown) {
                layer.addTo(entry.map);
            } else if (!shouldShow && isShown) {
                entry.map.removeLayer(layer);
            }
        }
    }

    function render(elementId, imageUrl, model) {
        if (!window.L) {
            throw new Error("Leaflet was not loaded from the local application assets.");
        }

        const entry = ensureEntry(elementId, imageUrl, model);
        for (const key of layerKeys) {
            if (key !== "baseMap") {
                entry.layers[key].clearLayers();
            }
        }

        renderGrid(entry, model.grid);

        for (const item of model.items ?? []) {
            const layer = entry.layers[item.layer] ?? entry.layers.events;
            layer.addLayer(makeMarker(item, model.height));
        }

        applyVisibility(entry, model.layerVisibility ?? {});
        window.setTimeout(() => entry.map.invalidateSize({ pan: false }), 0);
    }

    function fitActive() {
        if (!activeElementId) {
            return;
        }

        const entry = entries.get(activeElementId);
        entry?.map.fitBounds(entry.bounds, { animate: true, padding: [18, 18] });
    }

    window.rustPlusMap = {
        render,
        destroy: destroyEntry,
        fitActive
    };
})();
