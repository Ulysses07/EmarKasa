// No fetch handler or private-data cache: reports always come from the authenticated API.
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
function destination(value) {
  try {
    const url = new URL(value || '/#notifications', self.location.origin);
    if (url.origin === self.location.origin && url.pathname === '/' && !url.search && /^#(?:home|notifications|(?:cards|loans)(?:\/[1-9]\d*)?)$/.test(url.hash)) return url.href;
  } catch {}
  return self.location.origin + '/#notifications';
}
self.addEventListener('push', event => {
  let payload = {};
  try { payload = event.data?.json() || {}; } catch {}
  event.waitUntil(self.registration.showNotification(String(payload.baslik || 'Kasa'), {
    body: String(payload.mesaj || 'Yeni bir ödeme hatırlatmanız var.'),
    icon: '/icon.svg', badge: '/icon.svg', tag: String(payload.tag || `kasa-${payload.id || 'bildirim'}`),
    data: { url: destination(payload.url) }
  }));
});
self.addEventListener('notificationclick', event => {
  event.notification.close();
  const target = destination(event.notification.data?.url);
  event.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(async clients => {
    const existing = clients.find(client => new URL(client.url).origin === self.location.origin);
    if (existing) { await existing.navigate(target); return existing.focus(); }
    return self.clients.openWindow(target);
  }));
});
