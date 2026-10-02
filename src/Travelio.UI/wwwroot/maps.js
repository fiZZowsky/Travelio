(() => {
    'use strict';
    const maps = new Map();
    window.travelio.maps = {
        show(id, stops, geometry) {
            const element = document.getElementById(id);
            if (!element) return;
            let state = maps.get(id);
            if (!state) {
                const map = L.map(element, { scrollWheelZoom: false }).setView([25, 10], 2);
                L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
                    maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">OpenStreetMap</a> contributors'
                }).addTo(map);
                const observer = new ResizeObserver(() => map.invalidateSize()); observer.observe(element);
                state = { map, observer, layer: L.featureGroup().addTo(map) }; maps.set(id, state);
            }
            state.layer.clearLayers();
            stops.forEach((stop, i) => {
                const text = document.createElement('span'); text.textContent = `${i + 1}. ${stop.title}`;
                L.marker([stop.latitude, stop.longitude], {
                    icon: L.divIcon({ className: 'route-marker', html: String(i + 1), iconSize: [30, 30], iconAnchor: [15, 15] }),
                    title: text.textContent
                }).bindPopup(text).addTo(state.layer);
            });
            if (geometry?.length > 1) L.polyline(geometry.map(p => [p.latitude, p.longitude]), { color: '#0c7d65', weight: 4, opacity: .85 }).addTo(state.layer);
            if (stops.length) state.map.fitBounds(state.layer.getBounds(), { padding: [32, 32], maxZoom: 16 });
            state.map.invalidateSize();
        },
        dispose(id) { const state = maps.get(id); if (!state) return; state.observer.disconnect(); state.map.remove(); maps.delete(id); }
    };
})();
