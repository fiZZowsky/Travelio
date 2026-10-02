/* Travelio platform bridge: persistent storage, local notifications, and the offline globe. */
(() => {
    "use strict";
    let databasePromise;
    const database = () => databasePromise ??= new Promise((resolve, reject) => {
        const request = indexedDB.open("travelio", 1);
        request.onupgradeneeded = () => request.result.createObjectStore("workspace");
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
        request.onblocked = () => reject(new Error("Pamięć jest zablokowana przez inną kartę."));
    });
    const transaction = async (mode, action) => {
        const db = await database();
        return new Promise((resolve, reject) => {
            const tx = db.transaction("workspace", mode);
            const request = action(tx.objectStore("workspace"));
            tx.oncomplete = () => resolve(request.result ?? null);
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error ?? new Error("Zapis został przerwany."));
        });
    };
    let reminderTimer;
    let reminders = [];
    const notified = new Set();
    const checkReminders = () => {
        if (!("Notification" in window) || Notification.permission !== "granted") return;
        const now = Date.now();
        for (const reminder of reminders) {
            const due = Date.parse(reminder.dueAt);
            if (due <= now && due > now - 30 * 60 * 1000 && !notified.has(reminder.stopId)) {
                notified.add(reminder.stopId);
                const options = { body: "Jeśli punkt jest już za Tobą, oznacz go w planie podróży.", icon: "/icon.svg", tag: reminder.stopId };
                if (navigator.serviceWorker?.controller)
                    navigator.serviceWorker.ready.then(registration => registration.showNotification(reminder.title, options)).catch(() => {});
                else {
                    try { new Notification(reminder.title, options); } catch { /* Browser may require a service worker. */ }
                }
            }
        }
    };
    let connectionTimer, connectionListener, visibilityListener;
    let globeState;
    let countryFeatures;
    const isoCodes = "AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO JP KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS YE YT ZA ZM ZW".split(" ");
    const countries = () => {
        const names = new Intl.DisplayNames(["pl"], { type: "region" });
        return isoCodes.map(code => ({ code, name: names.of(code) })).sort((a, b) => a.name.localeCompare(b.name, "pl"));
    };
    const drawGlobe = () => {
        const s = globeState;
        if (!s) return;
        const { context: ctx, width: w, height: h, projection, path, visited } = s;
        ctx.clearRect(0, 0, w, h);
        ctx.beginPath(); path({ type: "Sphere" });
        const gradient = ctx.createRadialGradient(w * .42, h * .35, 15, w / 2, h / 2, projection.scale());
        gradient.addColorStop(0, "#e3eee9"); gradient.addColorStop(1, "#bbd5ce");
        ctx.fillStyle = gradient; ctx.fill();
        ctx.beginPath(); path(d3.geoGraticule10()); ctx.strokeStyle = "#b9d1c7"; ctx.lineWidth = .5; ctx.stroke();
        for (const feature of countryFeatures) {
            ctx.beginPath(); path(feature);
            ctx.fillStyle = visited.has(feature.properties.code) ? "#158772" : "#faf9ef";
            ctx.strokeStyle = visited.has(feature.properties.code) ? "#e3ede2" : "#bcc9b8";
            ctx.lineWidth = .65;
            ctx.fill(); ctx.stroke();
        }
        // Small states have a real polygon plus an accessible minimum-size target.
        s.tinyTargets = [];
        for (const feature of countryFeatures.filter(f => f.properties.tiny)) {
            const center = feature.properties.center;
            if (d3.geoDistance(center, projection.invert([w / 2, h / 2])) > Math.PI / 2) continue;
            const point = projection(center);
            if (!point || point[0] < 0 || point[0] > w || point[1] < 0 || point[1] > h) continue;
            if (s.zoom < 2 && !visited.has(feature.properties.code)) continue;
            ctx.beginPath(); ctx.arc(point[0], point[1], 4, 0, 2 * Math.PI);
            ctx.fillStyle = visited.has(feature.properties.code) ? '#158772' : '#fcf8dd'; ctx.fill();
            ctx.strokeStyle = '#547d6b'; ctx.stroke();
            s.tinyTargets.push({ feature, point });
        }
        ctx.beginPath(); path({ type: "Sphere" }); ctx.strokeStyle = "#9ebfb3"; ctx.lineWidth = 1; ctx.stroke();
    };
    window.travelio = {
        storage: {
            get: key => transaction("readonly", s => s.get(key)),
            set: (key, value) => transaction("readwrite", s => s.put(value, key)),
            remove: key => transaction("readwrite", s => s.delete(key))
        },
        connection: {
            watch: reference => {
                window.travelio.connection.unwatch();
                let running = false;
                connectionListener = async event => {
                    if (running || (!event && document.visibilityState === "hidden")) return;
                    running = true;
                    try { await reference.invokeMethodAsync("NetworkChanged", navigator.onLine); } catch { /* disposed view */ }
                    finally { running = false; }
                };
                window.addEventListener("online", connectionListener);
                window.addEventListener("offline", connectionListener);
                visibilityListener = () => { if (document.visibilityState === "visible") connectionListener(); };
                document.addEventListener("visibilitychange", visibilityListener);
                connectionTimer = setInterval(connectionListener, 30000);
                return navigator.onLine;
            },
            unwatch: () => {
                clearInterval(connectionTimer);
                if (connectionListener) {
                    window.removeEventListener("online", connectionListener);
                    window.removeEventListener("offline", connectionListener);
                }
                if (visibilityListener) document.removeEventListener("visibilitychange", visibilityListener);
            }
        },
        notifications: {
            request: async () => "Notification" in window && (await Notification.requestPermission()) === "granted",
            schedule: value => {
                reminders = value;
                clearInterval(reminderTimer);
                reminderTimer = setInterval(checkReminders, 15000);
                checkReminders();
            },
            cancel: () => { reminders = []; clearInterval(reminderTimer); }
        },
        download: (name, content, type) => {
            const url = URL.createObjectURL(new Blob([content], { type }));
            const anchor = document.createElement("a");
            anchor.href = url; anchor.download = name; anchor.click();
            setTimeout(() => URL.revokeObjectURL(url), 5000);
        },
        globe: {
            countries,
            mount: async (id, visited, reference) => {
                window.travelio.globe.dispose();
                if (!countryFeatures) {
                    const response = await fetch("_content/Travelio.UI/data/countries.json");
                    if (!response.ok) throw new Error("Nie udało się pobrać mapy.");
                    countryFeatures = (await response.json()).features;
                }
                const canvas = document.getElementById(id);
                const ctx = canvas.getContext("2d");
                const projection = d3.geoOrthographic().rotate([-18, -18]).clipAngle(90).precision(.3);
                const path = d3.geoPath(projection, ctx);
                const s = globeState = { canvas, context: ctx, projection, path, visited: new Set(visited), width: 0, height: 0, zoom: 1, tinyTargets: [] };
                const resize = () => {
                    const width = canvas.clientWidth, height = canvas.clientHeight;
                    const ratio = window.devicePixelRatio || 1;
                    canvas.width = width * ratio; canvas.height = height * ratio;
                    ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
                    s.width = width; s.height = height;
                    s.baseScale = Math.min(width, height) * .445;
                    projection.translate([width / 2, height / 2]).scale(s.baseScale * s.zoom);
                    drawGlobe();
                };
                s.observer = new ResizeObserver(resize); s.observer.observe(canvas); resize();
                let down, rotation, moved, pinch;
                const pointers = new Map();
                const zoom = value => { s.zoom = Math.max(1, Math.min(12, value)); projection.scale(s.baseScale * s.zoom); canvas.setAttribute('aria-label', `Globus, przybliżenie ${s.zoom.toFixed(1)} razy. Obróć przeciągając.`); drawGlobe(); };
                s.setZoom = zoom;
                canvas.onwheel = event => { event.preventDefault(); zoom(s.zoom * Math.exp(-event.deltaY * .0015)); };
                canvas.onpointerdown = event => {
                    pointers.set(event.pointerId, [event.clientX, event.clientY]);
                    if (pointers.size === 2) { const p = [...pointers.values()]; pinch = { distance: Math.hypot(p[0][0] - p[1][0], p[0][1] - p[1][1]), zoom: s.zoom }; down = null; moved = true; }
                    else {
                    down = [event.clientX, event.clientY]; rotation = projection.rotate(); moved = false;
                    }
                    canvas.setPointerCapture(event.pointerId);
                };
                canvas.onpointermove = event => {
                    if (pointers.has(event.pointerId)) pointers.set(event.pointerId, [event.clientX, event.clientY]);
                    if (pinch && pointers.size === 2) { const p = [...pointers.values()]; zoom(pinch.zoom * Math.hypot(p[0][0] - p[1][0], p[0][1] - p[1][1]) / Math.max(1, pinch.distance)); return; }
                    if (!down) return;
                    const dx = event.clientX - down[0], dy = event.clientY - down[1];
                    if (Math.abs(dx) + Math.abs(dy) > 4) moved = true;
                    projection.rotate([rotation[0] + dx * .35 / s.zoom, Math.max(-85, Math.min(85, rotation[1] - dy * .35 / s.zoom))]);
                    drawGlobe();
                };
                canvas.onpointerup = async event => {
                    pointers.delete(event.pointerId);
                    if (pinch) { pinch = null; down = null; return; }
                    if (!down) return;
                    down = null;
                    if (moved) return;
                    const bounds = canvas.getBoundingClientRect();
                    const x = event.clientX - bounds.left, y = event.clientY - bounds.top;
                    const [cx, cy] = projection.translate();
                    if (Math.hypot(x - cx, y - cy) > projection.scale()) return;
                    const coordinate = projection.invert([x, y]);
                    const tiny = s.tinyTargets.find(t => Math.hypot(t.point[0] - x, t.point[1] - y) < 9);
                    const feature = tiny?.feature ?? countryFeatures.find(f => d3.geoContains(f, coordinate));
                    if (feature) await reference.invokeMethodAsync("Toggle", feature.properties.code);
                };
                canvas.onpointercancel = event => { pointers.delete(event.pointerId); down = null; pinch = null; };
            },
            zoom: factor => { if (globeState) globeState.setZoom(globeState.zoom * factor); },
            reset: () => { if (globeState) { globeState.projection.rotate([-18, -18]); globeState.setZoom(1); } },
            focus: code => {
                if (!globeState) return;
                const feature = countryFeatures.find(f => f.properties.code === code);
                if (!feature) return;
                const center = feature.properties.center;
                globeState.projection.rotate([-center[0], -center[1]]);
                globeState.setZoom(feature.properties.tiny ? 8 : 3);
            },
            update: visited => { if (globeState) { globeState.visited = new Set(visited); drawGlobe(); } },
            dispose: () => {
                if (!globeState) return;
                globeState.observer.disconnect();
                globeState.canvas.onpointerdown = globeState.canvas.onpointermove = globeState.canvas.onpointerup = globeState.canvas.onpointercancel = null;
                globeState.canvas.onwheel = null;
                globeState = null;
            }
        }
    };
    // Focus containment for dialogs, including return focus on close.
    let activeModal, returnFocus;
    new MutationObserver(() => {
        const modal = document.querySelector(".modal");
        if (modal === activeModal) return;
        if (modal) {
            returnFocus = document.activeElement;
            activeModal = modal;
            requestAnimationFrame(() => (modal.querySelector("[autofocus], input, button, select, textarea") || modal).focus());
        } else {
            activeModal = null;
            if (returnFocus?.isConnected) returnFocus.focus();
        }
    }).observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener("keydown", event => {
        if (event.key !== "Tab" || !activeModal) return;
        const focusable = [...activeModal.querySelectorAll("button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), a[href], [tabindex='0']")];
        const first = focusable[0], last = focusable.at(-1);
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
    });
})();

