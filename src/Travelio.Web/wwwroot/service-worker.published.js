self.importScripts("./service-worker-assets.js");
const prefix = "travelio-static-";
const cacheName = prefix + self.assetsManifest.version;
const excluded = [/service-worker\.js$/, /service-worker-assets\.js$/, /service-worker\.published\.js$/];
const assets = self.assetsManifest.assets.filter(asset => !excluded.some(pattern => pattern.test(asset.url)));
self.addEventListener("install", event => event.waitUntil((async () => {
    const cache = await caches.open(cacheName);
    await cache.addAll(assets.map(asset => new Request(asset.url, { integrity: asset.hash, cache: "no-cache" })));
    // Activation waits for existing tabs to close so running WASM never mixes asset versions.
})()));
self.addEventListener("activate", event => event.waitUntil((async () => {
    const keys = await caches.keys();
    await Promise.all(keys.filter(key => key.startsWith(prefix) && key !== cacheName).map(key => caches.delete(key)));
    await self.clients.claim();
})()));
self.addEventListener("fetch", event => {
    if (event.request.method !== "GET") return;
    const url = new URL(event.request.url);
    if (url.origin !== self.location.origin || url.pathname.startsWith("/api/") || url.pathname === "/health") return;
    event.respondWith((async () => {
        const cache = await caches.open(cacheName);
        if (event.request.mode === "navigate") return (await cache.match("index.html")) || fetch(event.request);
        return (await cache.match(event.request)) || fetch(event.request);
    })());
});
self.addEventListener("notificationclick", event => {
    event.notification.close();
    event.waitUntil((async () => {
        const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
        if (windows.length) return windows[0].focus();
        return self.clients.openWindow("/trips");
    })());
});

