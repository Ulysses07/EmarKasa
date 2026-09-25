export const MAX_CENTS = 99999999999999;
export async function loadRuntime(fetcher) {
  const response = await fetcher('/kasa-runtime.json', { credentials: 'same-origin', cache: 'no-store' });
  if (response.status === 404) return { saltOkunur: false, surum: null };
  if (!response.ok) throw new Error('Kasa görüntüleme ayarı yüklenemedi. Sayfayı yenileyerek tekrar deneyin.');
  const config = await response.json();
  if (!config || typeof config.saltOkunur !== 'boolean' || (config.surum != null && typeof config.surum !== 'string')) throw new Error('Kasa çalışma ayarı geçersiz. Lütfen yöneticinize bildirin.');
  return { saltOkunur: config.saltOkunur, surum: config.surum || null };
}
export function runtimeRequestAllowed(runtime, path, method = 'GET') {
  if (!runtime) return false;
  if (!runtime.saltOkunur) return true;
  const verb = method.toUpperCase();
  const route = path.split('?')[0];
  if (verb === 'POST') return ['/api/auth/login', '/api/auth/logout'].includes(route);
  return ['GET', 'HEAD'].includes(verb) && ['/api/auth/me', '/api/rapor/panel', '/api/rapor/haftalik', '/api/rapor/aylik', '/api/islemler', '/api/kanallar'].includes(route);
}
export function cashEditingAllowed(role, runtime) { return role === 'editor' && runtime?.saltOkunur === false; }
export function incomeSelection(rows, channel) {
  const channelId = Number(channel?.id);
  const channelName = typeof channel === 'string' ? channel : channel?.ad || '';
  // SQLite NOCASE folds only ASCII A-Z; Turkish dotted/dotless letters stay distinct.
  const sqliteName = value => String(value || '').replace(/[A-Z]/g, letter => letter.toLowerCase());
  const selected = rows.filter(row => row.kanalId != null
    ? Number.isInteger(channelId) && channelId > 0 && Number(row.kanalId) === channelId
    : channelName && sqliteName(row.kanal) === sqliteName(channelName));
  return {
    total: selected.reduce((sum, row) => sum + Math.round(row.tutarTl * 100), 0) / 100,
    count: selected.length,
    readOnly: selected.length > 1 || selected.some(row => row.eskiYinelenenGrup === true)
  };
}
export function childValues(values) { return values.flat(Infinity).filter(value => value != null && value !== false); }
export async function logoutAndClear(logout, clear) {
  try { await logout(); }
  catch { throw new Error('Ekrandaki bilgiler temizlendi; sunucu oturumu kapatılamadı. Bağlantı gelince yeniden giriş yapıp çıkış yapın. Bu sırada sayfayı yenilemek oturumu yeniden açabilir.'); }
  finally { clear(); }
}
export const money = value => new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY', minimumFractionDigits: 2 }).format(Number(value || 0));
export function dateText(value) {
  if (!value) return 'Belirlenmedi';
  const date = new Date(String(value).slice(0, 10) + 'T12:00:00');
  return Number.isNaN(date.valueOf()) ? String(value) : new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'short', year: 'numeric' }).format(date);
}
export function today() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}
// Inputs accept an ungrouped decimal with either separator. Never silently round money.
export function cents(value, { allowZero = true } = {}) {
  const text = String(value ?? '').trim().replace(',', '.');
  if (!/^\d+(?:\.\d{1,2})?$/.test(text)) throw new Error('Tutarı kuruş cinsinden, en çok iki ondalık basamakla girin.');
  const [whole, fraction = ''] = text.split('.');
  const result = Number(BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0')));
  if (!Number.isSafeInteger(result) || result > MAX_CENTS || (!allowZero && result === 0)) throw new Error('Tutar geçerli aralıkta ve sıfırdan büyük olmalı.');
  return result;
}
export function amount(value) { return cents(value) / 100; }
export function navigationFor(role, runtime = { saltOkunur: false }) {
  if (runtime.saltOkunur && !['editor', 'viewer'].includes(role)) return [];
  if (role === 'alici') return [['purchases', 'Alışlarım', '≡']];
  const items = [['home', 'Kasalar', '₺'], ['weekly', 'Haftalık kasa', '▤'], ['monthly', 'Aylık kasa', '▦'], ['transactions', 'İşlemler', '↕']];
  if (!runtime.saltOkunur) items.push(['monthly-expenses', 'Aylık Giderler', '▥'], ['cards', 'Kredi Kartları', '▱'], ['loans', 'Krediler', '↗']);
  if (role === 'editor' && !runtime.saltOkunur) items.push(['purchases', 'Alışlar', '≡'], ['imports', 'Ekstre / Hareket Yükle', '⇧'], ['notifications', 'Bildirimler', '◉'], ['tools', 'Ayarlar', '⌘']);
  return items;
}
export function currentPeriod(weeks, date = today()) {
  return weeks.find(w => w.donem.start <= date && date <= w.donem.end)
    || [...weeks].reverse().find(w => w.donem.start <= date) || weeks[0];
}
export function monthlyTotals(report) {
  const incoming = report.kanallar.reduce((sum, k) => sum + Math.round(k.gelen * 100), 0) + Math.round((report.genelGelir || 0) * 100);
  const expenses = report.kanallar.reduce((sum, k) => sum + Math.round(k.cariGiden * 100) + Math.round(k.sabitGider * 100) + Math.round(k.krediKarti * 100) + Math.round(k.ortakPay * 100), 0) + Math.round((report.dagilimBekleyenTutar || 0) * 100) + Math.round((report.genelGider || 0) * 100);
  return { incoming: incoming / 100, expenses: expenses / 100, result: (incoming - expenses) / 100 };
}
export function errorMessage(body, status) {
  if (body?.errors) return Object.values(body.errors).flat().join('\n');
  return body?.hata || body?.detail || (status === 401 ? 'Oturumunuz sona erdi. Yeniden giriş yapın.' : status === 403 ? 'Bu işlem için yetkiniz yok.' : status === 409 ? 'Kayıt değişti. Güncel bilgileri yükleyip tekrar deneyin.' : status === 429 ? 'Çok fazla deneme yapıldı. Biraz bekleyip tekrar deneyin.' : 'İşlem tamamlanamadı. Lütfen yeniden deneyin.');
}
export function permissions(role, purchase) {
  const editor = role === 'editor';
  const buyer = role === 'alici';
  return { finance: editor, edit: (editor || buyer) && (purchase?.durum === 'Taslak' || editor && purchase?.durum === 'Incelemede'), send: (editor || buyer) && purchase?.durum === 'Taslak', approve: editor && purchase?.durum === 'Incelemede', return: editor && ['Incelemede', 'Onaylandi'].includes(purchase?.durum), pay: editor && Number(purchase?.kalan) > 0 };
}
export const statusLabels = { Taslak: 'Taslak', Incelemede: 'İncelemede', Onaylandi: 'Onaylandı' };
export function filteredPurchases(purchases, query, status) {
  const term = (query || '').toLocaleLowerCase('tr-TR');
  return purchases.filter(p => (!status || p.durum === status) && `${p.id} ${p.tedarikci} ${p.alici} ${(p.kalemler || []).map(k => k.aciklama).join(' ')}`.toLocaleLowerCase('tr-TR').includes(term));
}
export function purchasePayload(form) {
  if (form.kalemler.length > 100) throw new Error('Bir alışta en fazla 100 kalem olabilir.');
  const kalemler = form.kalemler.map((line, index) => {
    if (line.dagilimlar.length > 100) throw new Error('Bir kalemde en fazla 100 kanal payı olabilir.');
    const total = cents(line.tutar);
    const seen = new Set();
    const dagilimlar = line.dagilimlar.filter(d => d.kanalId || String(d.tutar || '').trim()).map(d => {
      const id = Number(d.kanalId);
      if (!Number.isInteger(id) || id <= 0) throw new Error(`${index + 1}. kalem için kanal seçin; belirsiz dağılımı boş bırakabilirsiniz.`);
      if (seen.has(id)) throw new Error(`${index + 1}. kalemde aynı kanalı bir kez kullanın.`);
      seen.add(id);
      return { kanalId: id, tutar: cents(d.tutar, { allowZero: false }) / 100 };
    });
    if (!line.aciklama.trim()) throw new Error(`${index + 1}. kalemin açıklamasını girin.`);
    if (dagilimlar.reduce((sum, d) => sum + cents(d.tutar), 0) > total) throw new Error(`${index + 1}. kalemde kanal payları kalem tutarını aşıyor.`);
    return { aciklama: line.aciklama.trim(), tutar: total / 100, dagilimlar };
  });
  if (kalemler.reduce((sum, line) => sum + cents(line.tutar), 0) > MAX_CENTS) throw new Error('Alış toplamı geçerli sınırı aşıyor.');
  return { surum: form.surum || 0, tarih: form.tarih, tedarikci: form.tedarikci.trim(), not: form.not?.trim() || null, kalemler };
}
