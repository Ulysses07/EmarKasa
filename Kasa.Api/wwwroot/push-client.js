export function decodeVapidKey(value) {
  const text = value.replace(/-/g, '+').replace(/_/g, '/');
  const bytes = atob(text + '='.repeat((4 - text.length % 4) % 4));
  return Uint8Array.from(bytes, character => character.charCodeAt(0));
}

export function notificationRoute(hash, role) {
  if (!['editor', 'viewer'].includes(role)) return null;
  const match = String(hash || '').match(/^#(cards|loans)(?:\/([1-9]\d*))?$/);
  if (match) return { view: match[1], id: match[2] ? Number(match[2]) : null };
  if (hash === '#notifications' && role === 'editor') return { view: 'notifications', id: null };
  if (hash === '#home') return { view: 'home', id: null };
  return null;
}

export function createPushClient({ api, environment = globalThis, session = () => 0 }) {
  const supported = () => Boolean(environment.isSecureContext && environment.navigator?.serviceWorker && environment.PushManager && environment.Notification);
  const subscription = async () => supported() ? (await environment.navigator.serviceWorker.getRegistration('/'))?.pushManager.getSubscription() : null;
  return {
    async status() {
      if (!supported()) return { supported: false, permission: 'unsupported', subscribed: false, endpoint: null };
      const current = await subscription();
      return { supported: true, permission: environment.Notification.permission, subscribed: Boolean(current), endpoint: current?.endpoint || null };
    },
    async enable(deviceName) {
      const originalSession = session();
      const ensureSession = () => { if (originalSession == null || session() !== originalSession) throw new Error('Oturum değişti. Bildirimleri yeni oturumunuzdan yeniden açın.'); };
      ensureSession();
      if (!supported()) throw new Error('Bu tarayıcıda bildirim desteklenmiyor. Telefonda uygulamayı ana ekrana ekleyip oradan açmayı deneyin.');
      // Called directly from the user's click. Never ask for permission during startup.
      const permission = await environment.Notification.requestPermission();
      ensureSession();
      if (permission !== 'granted') throw new Error('Bildirim izni verilmedi. Tarayıcı ayarlarından bu siteye izin verebilirsiniz.');
      const key = await api('/api/bildirimler/push/anahtar');
      ensureSession();
      if (!key.etkin || !key.publicKey) throw new Error('Sunucuda cihaz bildirimleri henüz etkin değil.');
      await environment.navigator.serviceWorker.register('/service-worker.js', { scope: '/' });
      const registration = await environment.navigator.serviceWorker.ready;
      ensureSession();
      let current = await registration.pushManager.getSubscription();
      ensureSession();
      const created = !current;
      if (created) current = await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: decodeVapidKey(key.publicKey) });
      let deviceId;
      try { deviceId = environment.localStorage?.getItem('kasa-device-id'); } catch {}
      if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(deviceId || '')) {
        deviceId = environment.crypto.randomUUID();
        try { environment.localStorage?.setItem('kasa-device-id', deviceId); } catch {}
      }
      const serialized = current.toJSON();
      try {
        ensureSession();
        await api('/api/bildirimler/push/abonelik', { method: 'POST', body: { endpoint: current.endpoint, keys: serialized.keys, cihazAdi: deviceName?.trim() || 'Tarayıcım', cihazId: deviceId } });
        ensureSession();
      } catch (error) { if (created) { try { await current.unsubscribe(); } catch {} } throw error; }
      return current.endpoint;
    },
    async disable({ bestEffort = false } = {}) {
      const current = await subscription();
      if (!current) return;
      let failure;
      try { await api('/api/bildirimler/push/abonelik', { method: 'DELETE', body: { endpoint: current.endpoint } }); } catch (error) { failure = error; }
      try { await current.unsubscribe(); } catch (error) { failure ||= error; }
      try { const registration = await environment.navigator.serviceWorker.getRegistration('/'); for (const notification of await registration?.getNotifications?.() || []) notification.close(); } catch {}
      if (failure && !bestEffort) throw new Error('Bu tarayıcının bildirim kaydı tam kapatılamadı. Bağlantıyı kontrol edip yeniden deneyin.');
    },
    async test() {
      const current = await subscription();
      if (!current) throw new Error('Önce bu cihazda bildirimleri açın.');
      return api('/api/bildirimler/test', { method: 'POST', body: { endpoint: current.endpoint } });
    }
  };
}
