const CACHE = 'qatfarm-mobile-shell-v2-2';
const STATIC = [
  '/css/app.css',
  '/css/offline.css',
  '/js/app.js',
  '/favicon.svg',
  '/icons/icon-192.png',
  '/icons/icon-512.png',
  '/offline.html'
];
self.addEventListener('install', event => {
  event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(STATIC)).then(() => self.skipWaiting()));
});
self.addEventListener('activate', event => {
  event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))).then(() => self.clients.claim()));
});
self.addEventListener('fetch', event => {
  const req = event.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return;
  // Blazor Server and authenticated pages always prefer the network; cached shell is only a fallback.
  if (req.mode === 'navigate') {
    event.respondWith(fetch(req).catch(() => caches.match('/offline.html')));
    return;
  }
  if (STATIC.includes(url.pathname)) {
    event.respondWith(caches.match(req).then(cached => cached || fetch(req)));
  }
});
