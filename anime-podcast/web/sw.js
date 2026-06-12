/* SPOKE service worker — offline-first for no-signal rides. */
const SHELL = "spoke-shell-v2";
const MEDIA = "spoke-media-v1";
const SHELL_URLS = [
  "./", "index.html", "styles.css", "app.js", "manifest.webmanifest",
  "data/episodes.json", "assets/cover.svg", "assets/icon-192.png", "assets/icon-512.png",
];

self.addEventListener("install", (e) => {
  e.waitUntil(caches.open(SHELL).then((c) => c.addAll(SHELL_URLS)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (e) => {
  e.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(keys.filter((k) => k !== SHELL && k !== MEDIA).map((k) => caches.delete(k)))
    ).then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", (e) => {
  const req = e.request;
  if (req.method !== "GET") return;
  const url = new URL(req.url);
  if (url.origin !== location.origin) return;

  // Audio: cache-first, support range requests from cache when offline.
  if (url.pathname.includes("/audio/")) {
    e.respondWith(
      caches.match(req, { ignoreSearch: true }).then((hit) => hit || fetch(req).then((res) => {
        const copy = res.clone();
        caches.open(MEDIA).then((c) => c.put(req, copy)).catch(() => {});
        return res;
      }).catch(() => caches.match(url.pathname.split("/").pop())))
    );
    return;
  }

  // Shell: cache-first, fall back to network, then to network-fill.
  e.respondWith(
    caches.match(req, { ignoreSearch: true }).then((hit) =>
      hit || fetch(req).then((res) => {
        const copy = res.clone();
        caches.open(SHELL).then((c) => c.put(req, copy)).catch(() => {});
        return res;
      }).catch(() => caches.match("index.html"))
    )
  );
});
