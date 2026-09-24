// Emar Kasa · telefon görünümü service worker'ı.
// YALNIZ uygulama kabuğunu (HTML/CSS/JS/simge) önbelleğe alır; /api ASLA önbelleğe alınmaz ve
// bu worker'dan geçirilmez (istek doğrudan ağa gider). Kabuk ağdan önce denenir: güncelleme hemen
// gelir, çevrimdışıyken son kabuk açılır (veri ekranları "bağlantı yok" der).
'use strict';

const SURUM = 'emar-kasa-m-v1';
const KABUK = [
  '/m/',
  '/m/index.html',
  '/m/app.css',
  '/m/app.js',
  '/m/manifest.webmanifest',
  '/m/icons/icon-192.png',
  '/m/icons/icon-512.png',
  '/m/icons/apple-touch-icon.png',
];

self.addEventListener('install', (e) => {
  e.waitUntil(caches.open(SURUM).then((c) => c.addAll(KABUK)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (e) => {
  e.waitUntil(caches.keys()
    .then((anahtarlar) => Promise.all(anahtarlar.filter((k) => k !== SURUM).map((k) => caches.delete(k))))
    .then(() => self.clients.claim()));
});

self.addEventListener('fetch', (e) => {
  const istek = e.request;
  if (istek.method !== 'GET') return;
  const url = new URL(istek.url);
  if (url.origin !== self.location.origin) return;
  // Veri: asla önbellek yok, worker dokunmaz.
  if (url.pathname === '/api' || url.pathname.startsWith('/api/')) return;
  // Yalnız /m/ kabuğu.
  if (!url.pathname.startsWith('/m/')) return;

  e.respondWith((async () => {
    try {
      const yanit = await fetch(istek);
      if (yanit.ok && yanit.type === 'basic' && KABUK.includes(url.pathname)) {
        const kopya = yanit.clone();
        e.waitUntil(caches.open(SURUM).then((c) => c.put(url.pathname, kopya)));
      }
      return yanit;
    } catch (hata) {
      const onbellek = await caches.match(url.pathname)
        || (istek.mode === 'navigate' ? await caches.match('/m/index.html') : undefined);
      if (onbellek) return onbellek;
      throw hata;
    }
  })());
});
