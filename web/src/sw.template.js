// Service worker template. The build (vite.config.ts) fills in a content-versioned cache name and the
// list of every file of dist, and writes the result to dist/sw.js.
const CACHE = '__CACHE__';
const FILES = JSON.parse('__FILES__');

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(FILES)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((names) => Promise.all(names.filter((n) => n.startsWith('fes-') && n !== CACHE).map((n) => caches.delete(n))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener('fetch', (event) => {
  if (event.request.method !== 'GET') return;
  event.respondWith(
    caches.open(CACHE).then(async (cache) => {
      const hit = await cache.match(event.request, { ignoreSearch: true });
      if (hit) return hit;
      const res = await fetch(event.request);
      if (res.ok && new URL(event.request.url).origin === self.location.origin) cache.put(event.request, res.clone());
      return res;
    }),
  );
});
