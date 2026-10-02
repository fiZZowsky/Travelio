if ("serviceWorker" in navigator) {
    window.addEventListener("load", () => navigator.serviceWorker.register("service-worker.js", { updateViaCache: "none" }).catch(console.error));
}

