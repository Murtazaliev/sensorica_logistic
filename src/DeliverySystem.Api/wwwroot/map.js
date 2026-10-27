window.deliveryMap = (function () {
    let map = null;
    let dotNetRef = null;
    let loaderPromise = null;
    let pointMarkers = [];
    let draftMarker = null;

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

    function iconFor(kind) {
        return "/icons/pin-" + kind + ".svg";
    }

    function markerOptions(pt, kind) {
        return {
            coordinates: [pt.longitude, pt.latitude],
            icon: iconFor(kind),
            label: {
                text: pt.name || "",
                fontSize: 12,
                color: "#0f172a",
                offset: [0, 4],
                relativeAnchor: [0.5, 0]
            }
        };
    }

    function clearPointMarkers() {
        pointMarkers.forEach((marker) => {
            try {
                marker.destroy();
            } catch (_) {
                // ignore
            }
        });
        pointMarkers = [];
    }

    function clearDraft() {
        if (draftMarker) {
            try {
                draftMarker.destroy();
            } catch (_) {
                // ignore
            }
            draftMarker = null;
        }
    }

    function setDraft(lat, lng) {
        if (!map) {
            return;
        }

        if (lat === null || lat === undefined || lng === null || lng === undefined) {
            clearDraft();
            return;
        }

        const options = {
            coordinates: [lng, lat],
            icon: iconFor("draft"),
            label: {
                text: "Выбрано",
                fontSize: 12,
                color: "#1d4ed8",
                offset: [0, 4],
                relativeAnchor: [0.5, 0]
            }
        };

        if (draftMarker) {
            draftMarker.setCoordinates(options.coordinates);
        } else {
            draftMarker = new mapgl.Marker(map, options);
        }
    }

    function renderPoints(points, draftLat, draftLng) {
        if (!map) {
            return;
        }

        clearPointMarkers();

        (points || []).forEach((pt) => {
            if (pt.latitude === null || pt.latitude === undefined
                || pt.longitude === null || pt.longitude === undefined) {
                return;
            }

            const kind = pt.approvalStatus === "Active"
                ? "active"
                : pt.approvalStatus === "PendingApproval"
                    ? "pending"
                    : "rejected";

            const marker = new mapgl.Marker(map, markerOptions(pt, kind));

            marker.on("click", function () {
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync("OnPointMarkerClicked", pt.id);
                }
            });

            pointMarkers.push(marker);
        });

        setDraft(draftLat, draftLng);
    }

    return {
        init: async function (containerId, dotNetHelper, points, centerLat, centerLng, apiKey) {
            dotNetRef = dotNetHelper;
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
                pointMarkers = [];
                draftMarker = null;
            }

            const lat = centerLat || 55.7558;
            const lng = centerLng || 37.6173;

            map = new mapgl.Map(containerId, {
                key: apiKey,
                center: [lng, lat],
                zoom: 13,
                language: "ru",
                zoomControl: false,
                fullscreenControl: false
            });

            map.on("click", function (e) {
                if (!e || !e.lngLat) {
                    return;
                }

                const clickedLat = Number(e.lngLat[1].toFixed(6));
                const clickedLng = Number(e.lngLat[0].toFixed(6));
                setDraft(clickedLat, clickedLng);

                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync("OnMapClicked", clickedLat, clickedLng);
                }
            });

            map.on("error", function (e) {
                if (dotNetRef) {
                    const message = e && e.error && e.error.message
                        ? e.error.message
                        : "Ошибка загрузки карты 2ГИС. Проверьте ключ и тариф Map Tiles API.";
                    dotNetRef.invokeMethodAsync("OnMapError", message);
                }
            });

            renderPoints(points, null, null);
        },

        updatePoints: function (points, draftLat, draftLng) {
            if (!map) {
                return;
            }
            renderPoints(points, draftLat, draftLng);
        },

        focus: function (lat, lng, zoom) {
            if (!map || lat === null || lat === undefined || lng === null || lng === undefined) {
                return;
            }

            map.setCenter([lng, lat]);
            if (typeof map.setZoom === "function") {
                map.setZoom(zoom || 15);
            }
            setDraft(lat, lng);
        },

        dispose: function () {
            clearPointMarkers();
            clearDraft();

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
