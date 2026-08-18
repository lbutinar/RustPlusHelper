(() => {
    "use strict";

    const entries = new Map();
    let activeElementId = null;

    const layerKeys = [
        "baseMap",
        "grid",
        "biomes",
        "topology",
        "terrainSlope",
        "resourcePotential",
        "roads",
        "railways",
        "rivers",
        "noBuildZones",
        "team",
        "teamNotes",
        "vendingMachines",
        "monuments",
        "events",
        "deathHistory",
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

    const externalRasterLayerKeys = new Set(["biomes", "topology", "terrainSlope", "resourcePotential"]);
    const externalPathLayerKeys = new Set(["roads", "railways", "rivers"]);
    const externalPolygonLayerKeys = new Set(["noBuildZones"]);

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

        const entry = {
            map,
            bounds,
            layers,
            baseImage: image,
            width: model.width,
            height: model.height,
            imageUrl,
            rasterUrls: new Map(),
            rasters: [],
            imagePromises: new Map(),
            compositeUrls: new Map(),
            compositeGeneration: 0,
            activeCompositeKey: "base|",
            markers: new Map()
        };
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

    function rasterDataUrl(entry, raster) {
        const cached = entry.rasterUrls.get(raster.id);
        if (cached) {
            return cached;
        }

        const binary = atob(raster.rgba);
        const bytes = new Uint8ClampedArray(binary.length);
        for (let index = 0; index < binary.length; index++) {
            bytes[index] = binary.charCodeAt(index);
        }

        const canvas = document.createElement("canvas");
        canvas.width = raster.width;
        canvas.height = raster.height;
        const context = canvas.getContext("2d", { alpha: true });
        context.putImageData(new ImageData(bytes, raster.width, raster.height), 0, 0);
        const dataUrl = canvas.toDataURL("image/png");
        entry.rasterUrls.set(raster.id, dataUrl);
        return dataUrl;
    }

    function renderRaster(entry, raster) {
        entry.rasters.push({
            ...raster,
            dataUrl: rasterDataUrl(entry, raster)
        });
    }

    function renderPolyline(entry, polyline) {
        const layer = entry.layers[polyline.layer];
        if (!layer || !polyline.points || polyline.points.length < 2) {
            return;
        }

        const styles = {
            roads: { color: "#d7a66b", weight: 2.2, opacity: 0.82 },
            railways: { color: "#202426", weight: 2.5, opacity: 0.9, dashArray: "5 4" },
            rivers: { color: "#4ab6df", weight: 2.8, opacity: 0.9 }
        };
        const points = polyline.points.map(point => [entry.height - point.pixelY, point.pixelX]);
        const line = L.polyline(points, {
            ...(styles[polyline.layer] ?? { color: "#ffffff", weight: 2, opacity: 0.8 }),
            interactive: true
        });
        line.bindTooltip(escapeHtml(polyline.label), { className: "rust-map-tooltip" });
        layer.addLayer(line);
    }

    function renderPolygon(entry, polygon) {
        const layer = entry.layers[polygon.layer];
        if (!layer || !polygon.points || polygon.points.length < 3) {
            return;
        }

        const points = polygon.points.map(point => [entry.height - point.pixelY, point.pixelX]);
        const shape = L.polygon(points, {
            color: "#ff5b4d",
            weight: 1.5,
            opacity: 0.9,
            fillColor: "#e83d32",
            fillOpacity: 0.2,
            interactive: true
        });
        shape.bindTooltip(escapeHtml(polygon.label), { className: "rust-map-tooltip" });
        layer.addLayer(shape);
    }

    function renderHeatSpot(entry, spot) {
        const layer = entry.layers[spot.layer];
        if (!layer) {
            return;
        }

        const count = Math.max(1, Number(spot.count) || 1);
        const intensity = Math.min(1, 0.28 + Math.log2(count + 1) * 0.18);
        const radius = Math.min(34, 12 + Math.log2(count + 1) * 6);
        const circle = L.circleMarker([entry.height - spot.pixelY, spot.pixelX], {
            radius,
            color: "#ff6b52",
            weight: 1.5,
            opacity: Math.min(1, intensity + 0.2),
            fillColor: count >= 5 ? "#ef2d2d" : count >= 3 ? "#ff5a36" : "#ff9a4a",
            fillOpacity: intensity,
            interactive: true,
            className: "rust-death-hotspot"
        });
        const latest = spot.latestAtUtc
            ? new Date(spot.latestAtUtc).toLocaleString()
            : "Unknown";
        circle.bindTooltip([
            `<strong>${escapeHtml(spot.label)}</strong>`,
            `<span>Grid ${escapeHtml(spot.gridReference)}</span>`,
            `<span>Latest: ${escapeHtml(latest)}</span>`,
            "<span>Derived from locally recorded team deaths</span>"
        ].join(""), { className: "rust-map-tooltip" });
        layer.addLayer(circle);
    }

    function applyVisibility(entry, visibility) {
        const hasVisibleRaster = entry.rasters.some(raster => visibility[raster.layer] === true);
        for (const key of layerKeys) {
            const layer = entry.layers[key];
            const isRasterLayer = externalRasterLayerKeys.has(key);
            const shouldShow = isRasterLayer
                ? false
                : key === "baseMap"
                    ? visibility.baseMap === true || hasVisibleRaster
                    : visibility[key] === true;
            const isShown = entry.map.hasLayer(layer);
            if (shouldShow && !isShown) {
                layer.addTo(entry.map);
            } else if (!shouldShow && isShown) {
                entry.map.removeLayer(layer);
            }
        }
    }

    function loadImage(entry, url) {
        const cached = entry.imagePromises.get(url);
        if (cached) {
            return cached;
        }

        const promise = new Promise((resolve, reject) => {
            const image = new Image();
            image.onload = () => resolve(image);
            image.onerror = () => reject(new Error("A local map image could not be decoded."));
            image.src = url;
        });
        entry.imagePromises.set(url, promise);
        return promise;
    }

    async function updateCompositeMap(entry, visibility) {
        const visibleRasters = entry.rasters.filter(raster => visibility[raster.layer] === true);
        const includeBaseMap = visibility.baseMap === true;
        const cacheKey = `${includeBaseMap ? "base" : "background"}|${visibleRasters.map(raster => raster.id).join("|")}`;
        if (entry.activeCompositeKey === cacheKey) {
            return;
        }

        const generation = ++entry.compositeGeneration;
        if (visibleRasters.length === 0 && includeBaseMap) {
            entry.baseImage.setUrl(entry.imageUrl);
            entry.activeCompositeKey = cacheKey;
            return;
        }

        const cached = entry.compositeUrls.get(cacheKey);
        if (cached) {
            entry.baseImage.setUrl(cached);
            entry.activeCompositeKey = cacheKey;
            return;
        }

        const canvas = document.createElement("canvas");
        canvas.width = entry.width;
        canvas.height = entry.height;
        const context = canvas.getContext("2d", { alpha: false });
        if (includeBaseMap) {
            const baseImage = await loadImage(entry, entry.imageUrl);
            context.drawImage(baseImage, 0, 0, entry.width, entry.height);
        } else {
            context.fillStyle = "#182522";
            context.fillRect(0, 0, entry.width, entry.height);
        }

        for (const raster of visibleRasters) {
            const image = await loadImage(entry, raster.dataUrl);
            context.drawImage(
                image,
                raster.pixelLeft,
                raster.pixelTop,
                raster.pixelRight - raster.pixelLeft,
                raster.pixelBottom - raster.pixelTop);
        }

        const dataUrl = canvas.toDataURL("image/png");
        entry.compositeUrls.set(cacheKey, dataUrl);
        if (generation === entry.compositeGeneration) {
            entry.baseImage.setUrl(dataUrl);
            entry.activeCompositeKey = cacheKey;
        }
    }

    async function setLayerVisibility(elementId, visibility) {
        const entry = entries.get(elementId);
        if (!entry) {
            return;
        }

        applyVisibility(entry, visibility ?? {});
        await updateCompositeMap(entry, visibility ?? {});
    }

    async function render(elementId, imageUrl, model) {
        if (!window.L) {
            throw new Error("Leaflet was not loaded from the local application assets.");
        }

        const entry = ensureEntry(elementId, imageUrl, model);
        entry.markers.clear();
        for (const key of layerKeys) {
            if (key !== "baseMap") {
                if (model.rasters == null && externalRasterLayerKeys.has(key)) {
                    continue;
                }
                if (model.polylines == null && externalPathLayerKeys.has(key)) {
                    continue;
                }
                if (model.polygons == null && externalPolygonLayerKeys.has(key)) {
                    continue;
                }
                entry.layers[key].clearLayers();
            }
        }

        renderGrid(entry, model.grid);

        if (model.rasters != null) {
            entry.rasters = [];
            entry.rasterUrls.clear();
            entry.imagePromises.clear();
            entry.compositeUrls.clear();
            entry.activeCompositeKey = null;
            for (const raster of model.rasters) {
                renderRaster(entry, raster);
            }
        }

        if (model.polylines != null) {
            for (const polyline of model.polylines) {
                renderPolyline(entry, polyline);
            }
        }


        if (model.polygons != null) {
            for (const polygon of model.polygons) {
                renderPolygon(entry, polygon);
            }
        }

        for (const item of model.items ?? []) {
            const layer = entry.layers[item.layer] ?? entry.layers.events;
            const marker = makeMarker(item, model.height);
            layer.addLayer(marker);
            entry.markers.set(item.id, marker);
        }


        for (const spot of model.heatSpots ?? []) {
            renderHeatSpot(entry, spot);
        }

        applyVisibility(entry, model.layerVisibility ?? {});
        await updateCompositeMap(entry, model.layerVisibility ?? {});
        window.setTimeout(() => entry.map.invalidateSize({ pan: false }), 0);
    }

    function fitActive() {
        if (!activeElementId) {
            return;
        }

        const entry = entries.get(activeElementId);
        entry?.map.fitBounds(entry.bounds, { animate: true, padding: [18, 18] });
    }

    function focusItem(elementId, itemId) {
        const entry = entries.get(elementId);
        const marker = entry?.markers.get(itemId);
        if (!entry || !marker) {
            return;
        }

        entry.map.setView(marker.getLatLng(), Math.max(entry.map.getZoom(), 1.5), { animate: true });
        marker.openTooltip();
    }

    window.rustPlusMap = {
        render,
        setLayerVisibility,
        focusItem,
        destroy: destroyEntry,
        fitActive
    };
})();
