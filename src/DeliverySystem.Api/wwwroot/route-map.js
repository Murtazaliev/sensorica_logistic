window.routeMap = (function () {
    let map = null;
    let loaderPromise = null;
    let markers = [];
    let line = null;
    let dotNetRef = null;

    function loadMapgl() {
        if (window.mapgl) {
            return Promise.resolve();
        }

        if (loaderPromise) {
            return loaderPromise;
        }

        loaderPromise = new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = "https://mapgl.2gis.com/api/js/v1";
            script.async = true;
            script.onload = () => {
                if (window.mapgl) {
                    resolve();
                } else {
                    reject(new Error("MapGL 2ГИС не инициализировался."));
                }
            };
            script.onerror = () => reject(new Error("Не удалось загрузить MapGL 2ГИС."));
            document.head.appendChild(script);
        });

        return loaderPromise;
    }

    function clearObjects() {
        markers.forEach((marker) => {
            try {
                marker.destroy();
            } catch (_) {
                // ignore
            }
        });
        markers = [];

        if (line) {
            try {
                line.destroy();
            } catch (_) {
                // ignore
            }
            line = null;
        }
    }

    function render(stops, depot, roadGeometry) {
        if (!map) {
            return;
        }

        clearObjects();

        const points = (stops || []).filter(
            (s) => s.latitude !== null && s.latitude !== undefined
                && s.longitude !== null && s.longitude !== undefined);

        const hasDepot = depot
            && depot.latitude !== null && depot.latitude !== undefined
            && depot.longitude !== null && depot.longitude !== undefined;

        if (hasDepot) {
            const depotMarker = new mapgl.Marker(map, {
                coordinates: [depot.longitude, depot.latitude],
                icon: "/icons/pin-active.svg",
                label: {
                    text: depot.name || "Цех",
                    fontSize: 12,
                    color: "#15803d",
                    offset: [0, 4],
                    relativeAnchor: [0.5, 0]
                }
            });

            markers.push(depotMarker);
        }

        points.forEach((stop) => {
            const marker = new mapgl.Marker(map, {
                coordinates: [stop.longitude, stop.latitude],
                icon: "/icons/pin-draft.svg",
                label: {
                    text: stop.sequenceNumber + ". " + stop.orderNumber,
                    fontSize: 12,
                    color: "#0f172a",
                    offset: [0, 4],
                    relativeAnchor: [0.5, 0]
                }
            });

            markers.push(marker);
        });

        const road = (roadGeometry || []).filter(
            (p) => p.latitude !== null && p.latitude !== undefined
                && p.longitude !== null && p.longitude !== undefined);

        let lineCoordinates;
        if (road.length > 1) {
            lineCoordinates = road.map((p) => [p.longitude, p.latitude]);
        } else {
            lineCoordinates = points.map((stop) => [stop.longitude, stop.latitude]);
            if (hasDepot) {
                lineCoordinates.unshift([depot.longitude, depot.latitude]);
            }
        }

        if (lineCoordinates.length > 1) {
            line = new mapgl.Polyline(map, {
                coordinates: lineCoordinates,
                width: 4,
                color: "#2563eb",
                zIndex: 1
            });
        }

        const allCoordinates = lineCoordinates.slice();
        if (hasDepot) {
            allCoordinates.push([depot.longitude, depot.latitude]);
        }
        if (points.length === 0 && !hasDepot) {
            allCoordinates.length = 0;
        }

        if (allCoordinates.length > 1) {
            const lats = allCoordinates.map((c) => c[1]);
            const lngs = allCoordinates.map((c) => c[0]);
            const bounds = {
                southWest: [Math.min(...lngs), Math.min(...lats)],
                northEast: [Math.max(...lngs), Math.max(...lats)]
            };

            try {
                map.fitBounds(bounds, { padding: 80 });
            } catch (_) {
                map.setCenter(allCoordinates[0]);
            }
        } else if (allCoordinates.length === 1) {
            map.setCenter(allCoordinates[0]);
            if (typeof map.setZoom === "function") {
                map.setZoom(15);
            }
        }
    }

    return {
        init: async function (containerId, apiKey, stops, depot, roadGeometry, helper) {
            dotNetRef = helper;
            await loadMapgl();

            const container = document.getElementById(containerId);
            if (!container) {
                return;
            }

            if (map) {
                try {
                    map.destroy();
                } catch (_) {
                    // ignore
                }
                map = null;
                markers = [];
                line = null;
            }

            map = new mapgl.Map(containerId, {
                key: apiKey,
                center: [37.6173, 55.7558],
                zoom: 12,
                language: "ru",
                zoomControl: false,
                fullscreenControl: false
            });

            map.on("error", function (e) {
                if (!dotNetRef) {
                    return;
                }

                const message = e && e.error && e.error.message
                    ? e.error.message
                    : "Ошибка загрузки карты 2ГИС. Проверьте ключ и тариф Map Tiles API.";
                dotNetRef.invokeMethodAsync("OnMapError", message);
            });

            render(stops, depot, roadGeometry);
        },

        update: function (stops, depot, roadGeometry) {
            render(stops, depot, roadGeometry);
        },

        dispose: function () {
            clearObjects();

            if (map) {
                try {
                    map.destroy();
                } catch (_) {
                    // ignore
                }
                map = null;
            }

            dotNetRef = null;
        }
    };
})();
