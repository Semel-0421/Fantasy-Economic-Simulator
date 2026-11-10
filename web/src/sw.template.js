// Service worker template. The build (vite.config.ts) fills in a content-versioned cache name and the
// core precache and optional language groups, and writes the result to dist/sw.js.
// An update never takes over a running game: a new worker installs, waits, and the page offers a
// reload (src/ui/update.ts); only that reload message activates it, and only then are the old
// caches deleted. The first install does not claim open pages either: it serves from the next load.
const CACHE = '__CACHE__';
const FILES = JSON.parse('__FILES__');
const LANGUAGES = /*__OPTIONAL__*/ {};

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(FILES)));
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((names) => Promise.all(names.filter((n) => n.startsWith('fes-') && n !== CACHE).map((n) => caches.delete(n)))),
  );
});

self.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'activate') self.skipWaiting();
  if (event.data?.type === 'language') event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(LANGUAGES[event.data.code] ?? [])));
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
