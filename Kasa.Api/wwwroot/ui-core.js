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
// Ekrana ait rapor okumaları (ana sayfa, panel, haftalık/aylık rapor, takip özeti): ekran değişince AbortController ile iptal
// edilir, sunucu da hesabı keser. Yazma istekleri ve diyalog verisi dışındaki okumalar iptal edilmez.
export function screenBoundRead(path, method = 'GET') {
  const route = String(path).split('?')[0];
  return String(method).toUpperCase() === 'GET' && (route.startsWith('/api/rapor/') || route === '/api/takip/ozet');
}
export function abortedRequestError() { return Object.assign(new Error('İstek iptal edildi.'), { name: 'AbortError' }); }
export function isAbortError(error) { return error?.name === 'AbortError'; }
// Haftalık raporun veri sağlığı uyarısı yalnız son dönemde gelir; raporun tamamı için geçerlidir. Yoksa null.
export function dataHealthWarning(weeks) {
  return [...(weeks || [])].reverse().map(week => week?.veriSagligiUyarisi).find(text => typeof text === 'string' && text.trim()) || null;
}
export function incomeSelection(rows, channel) {
  const channelId = Number(channel?.id);
  const channelName = typeof channel === 'string' ? channel : channel?.ad || '';
  // SQLite NOCASE folds only ASCII A-Z; Turkish dotted/dotless letters stay distinct.
  const sqliteName = value => String(value || '').replace(/[A-Z]/g, letter => letter.toLowerCase());
  const selected = rows.filter(row => row.kanalId != null
    ? Number.isInteger(channelId) && channelId > 0 && Number(row.kanalId) === channelId
    : channelName && sqliteName(row.kanal) === sqliteName(channelName));
  const readOnly = selected.length > 1 || selected.some(row => row.eskiYinelenenGrup === true);
  return {
    total: selected.reduce((sum, row) => sum + Math.round(row.tutarTl * 100), 0) / 100,
    count: selected.length,
    readOnly,
    // contract-6: tek normal satırın sürümü (satır yoksa 0). Kayıtla gönderilir; satır arada başka oturumda değiştiyse ya da
    // eklendiyse sunucu 409 verir.
    surum: selected.length === 1 && !readOnly ? Number(selected[0].surum) || 0 : 0
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
// Sunucunun gönderdiği tutar (JSON sayısı) kuruşa çevrilir: eksi olabilir ve String() ile üslü yazılabilir (1e-7), kullanıcı
// girdisi ayrıştırıcısı (cents) bunları reddeder. Sunucu tutarı en çok iki ondalıklıdır (monthlyTotals deseni).
export function serverCents(value) { return Math.round(Number(value || 0) * 100); }
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
  const message = body?.hata || body?.detail || (status === 401 ? 'Oturumunuz sona erdi. Yeniden giriş yapın.' : status === 403 ? 'Bu işlem için yetkiniz yok.' : status === 409 ? 'Kayıt değişti. Güncel bilgileri yükleyip tekrar deneyin.' : status === 429 ? 'Çok fazla deneme yapıldı. Biraz bekleyip tekrar deneyin.' : 'İşlem tamamlanamadı. Lütfen yeniden deneyin.');
  // Sunucu hatasının (5xx) ProblemDetails iz kimliği kısa "Hata kodu" olarak eklenir: kullanıcı yöneticiye bildirir, yönetici
  // sunucu logundaki tam iz kimliğini bu parçayla bulur. Masaüstü TemelViewModel.HataKoduEkle ile aynı biçim.
  const code = status >= 500 ? traceCode(body?.traceId) : null;
  return code ? `${message} Hata kodu: ${code}` : message;
}
// İz kimliğinin kısa biçimi (masaüstü KasaApiException.KisaIz ile aynı kural): W3C biçiminde iz numarasının ilk 8 hanesi, diğer
// kimlikte en çok 24 karakterse kendisi, daha uzunsa ilk 12 karakteri; beklenmeyen karakter ya da boş değerde null.
export function traceCode(traceId) {
  const text = typeof traceId === 'string' ? traceId.trim() : '';
  if (!text || !/^[A-Za-z0-9:._-]+$/.test(text)) return null;
  const w3c = /^[0-9a-f]{2}-([0-9a-f]{32})-[0-9a-f]{16}-[0-9a-f]{2}$/.exec(text);
  if (w3c) return w3c[1].slice(0, 8);
  return text.length <= 24 ? text : text.slice(0, 12);
}
// ValidationProblem alanları (sunucu adlarıyla) → alan iletisi; form denetimi aynı adı taşır.
export function fieldErrors(body) {
  const result = {};
  if (!body?.errors || typeof body.errors !== 'object') return result;
  for (const [name, messages] of Object.entries(body.errors)) {
    const text = [].concat(messages).filter(message => typeof message === 'string' && message).join('\n');
    if (text) result[name] = text;
  }
  return result;
}
// Yalnız kimlik istemeyen giriş/kurtarma uçlarının 401'i oturum sonu değildir; yol tam eşleşir
// ('/api/auth/kurtarma-kodu' oturum ister). Yanlış mevcut şifre 400 alan hatasıdır, oturumu kapatmaz.
export function sessionExpired(status, path) {
  return status === 401 && !['/api/auth/login', '/api/auth/kurtar'].includes(String(path).split('?')[0]);
}
// Sunucunun benzer kayıt kuralı (BenzerKayitServisi; masaüstü BenzerKayitKontrolu.KuralMetni ile aynı metin): aynı tutar ve
// ±3 gün; kanal süzgeci yalnız kesin başka kanala düşen kaydı eler.
export const SIMILAR_RULE_TEXT = 'Aynı tutarda ve ±3 gün içindeki kayıtlar gösterilir; kartlı kayıtta aynı kartın kayıtları aranır. Kanal yalnız kesin olarak başka kanala düşen kaydı eler: kanalı belirsiz, Ortak, yalnız genel kasa ya da dağılım bekleyen kayıtlar, seçilen kanalı da içeren çok kanallı kayıtlar ve kart ödemeleri her kanalda görünür.';
// Sunucu ve masaüstü ile aynı kural ve ileti; yalnız belirlerken/değiştirirken uygulanır.
export const VIEWER_PASSWORD_MESSAGE = 'İzleyici şifresi 12–1024 karakter olmalıdır.';
// Sunucu, kayıtlı izleyici şifresinin kurala uymadığını ancak bir izleyici girişinde görür (hash uzunluk saklamaz).
export const VIEWER_PASSWORD_SHORT_MESSAGE = 'Mevcut izleyici şifresi 12 karakterden kısa (son izleyici girişinde görüldü). Kurala uygun yeni bir şifre belirleyin.';
export function viewerPasswordError(value) {
  const text = String(value ?? '');
  return !text.trim() || text.length < 12 || text.length > 1024 ? VIEWER_PASSWORD_MESSAGE : null;
}
// Şifre değişimi ve kurtarma eski oturumları ve kurtarma kodunu hemen geçersiz kılar: yazım hatalı yeni şifre tek editör
// hesabını kilitler. Tekrar uyuşmazsa istek gönderilmez (masaüstüyle aynı ileti).
export const NEW_PASSWORD_MISMATCH_MESSAGE = 'Yeni şifreler aynı olmalı.';
export function newPasswordRepeatError(password, repeat) { return String(password ?? '') === String(repeat ?? '') ? null : NEW_PASSWORD_MISMATCH_MESSAGE; }
export function permissions(role, purchase) {
  const editor = role === 'editor';
  const buyer = role === 'alici';
  return { finance: editor, edit: (editor || buyer) && (purchase?.durum === 'Taslak' || editor && purchase?.durum === 'Incelemede'), send: (editor || buyer) && purchase?.durum === 'Taslak', approve: editor && purchase?.durum === 'Incelemede', return: editor && ['Incelemede', 'Onaylandi'].includes(purchase?.durum), pay: editor && Number(purchase?.kalan) > 0 };
}
export const statusLabels = { Taslak: 'Taslak', Incelemede: 'İncelemede', Onaylandi: 'Onaylandı' };
// K3: alış ödemesinde kart yalnız yeni takipteki, yeni kullanıma açık kartlardan seçilir (sunucu kartlı yeni ödemeyi başka
// karta bağlamaz). Mevcut kaydın kendi kartı (düzeltilen ödemenin ya da bağlanan giderin eski/kapalı kartı) ayrıca listelenir:
// kayıt kendi kartıyla kalabilir. Gider formundaki (expenseDialog) kart listesiyle aynı kural ve etiket.
export function paymentCardChoices(cards, keepId = null) {
  const all = cards || [];
  const tracked = all.filter(card => card.yeniTakip && card.aktif);
  const keep = keepId == null || keepId === '' || tracked.some(card => card.id === Number(keepId)) ? null : Number(keepId);
  return [{ value: '', label: 'Nakit / havale' }, ...tracked.map(card => ({ value: card.id, label: card.ad })),
    ...(keep == null ? [] : [{ value: keep, label: `${all.find(card => card.id === keep)?.ad || `Kart #${keep}`} (eski kayıt)` }])];
}
// gap-coklu-giris-cift-sayim-mutabakat-5: yeni takipteki kartla girilmiş ödeme kart takibindedir (kart yeni kullanıma kapalı olsa da):
// tarihi, tutarı ve kartı kart harcamasıdır; yalnız başka alışa taşınır ya da alıştan ayrılır.
export function trackedCardPayment(cards, payment) {
  return payment?.krediKartiId != null && (cards || []).some(card => card.id === payment.krediKartiId && card.yeniTakip);
}
export const INSTALLMENT_RANGE_MESSAGE = 'Taksit sayısı 1 ile 60 arasında olmalı.';
// gap-coklu-giris-cift-sayim-mutabakat-6: takipli kartla yeni kart harcamasının taksit alanları. Tek taksit ve ilk kesimsiz girişte alan
// gönderilmez: istek eski biçimiyle aynıdır. İlk kesim harcamadan önce olamaz (sunucu kartın kesim gününe yakınlığını da denetler).
export function installmentFields(countText, firstCut = '', date = '') {
  const text = String(countText ?? '').trim();
  const count = text === '' ? 1 : Number(text);
  if (!Number.isInteger(count) || count < 1 || count > 60) throw Object.assign(new Error(INSTALLMENT_RANGE_MESSAGE), { fields: { taksitSayisi: INSTALLMENT_RANGE_MESSAGE } });
  if (firstCut && date && firstCut < date) { const message = 'İlk kesim tarihi harcamadan önce olamaz.'; throw Object.assign(new Error(message), { fields: { ilkKesimTarihi: message } }); }
  return { ...(count > 1 ? { taksitSayisi: count } : {}), ...(firstCut ? { ilkKesimTarihi: firstCut } : {}) };
}
// gap-coklu-giris-cift-sayim-mutabakat-5: alıştan ayrılan kart harcamasının gerçek kanal payları. Boş ya da sıfır satır atlanır; toplam
// ödeme tutarına kuruşu kuruşuna eşit olmalı.
export function detachAllocations(rows, total) {
  const shares = (rows || []).map(row => ({ kanalId: Number(row.kanalId), cents: cents(row.tutar || 0) })).filter(share => share.cents !== 0);
  if (!shares.length || shares.reduce((sum, share) => sum + share.cents, 0) !== serverCents(total))
    throw new Error(`Kart harcamasının gerçek kanal paylarını girin; toplamı ödeme tutarına (${money(total)}) eşit olmalı.`);
  return shares.map(share => ({ kanalId: share.kanalId, tutar: share.cents / 100 }));
}
export function filteredPurchases(purchases, query, status) {
  const term = (query || '').toLocaleLowerCase('tr-TR');
  return purchases.filter(p => (!status || p.durum === status) && `${p.id} ${p.tedarikci} ${p.alici} ${(p.kalemler || []).map(k => k.aciklama).join(' ')}`.toLocaleLowerCase('tr-TR').includes(term));
}
// Alış listesinin özet şeridi. Tutarlar sunucudan gelir (JSON sayısı): kayan noktayla toplanmaz, serverCents ile kuruşa
// çevrilip tamsayı olarak toplanır (0,1 + 0,2 = 0,3). Kullanıcı girdisi ayrıştırıcısı (cents) burada kullanılmaz.
export function purchaseTotals(purchases) {
  const total = purchases.reduce((sum, p) => sum + serverCents(p.toplam), 0);
  const remaining = purchases.reduce((sum, p) => sum + serverCents(p.kalan), 0);
  return { total: total / 100, remaining: remaining / 100, reviewing: purchases.filter(p => p.durum === 'Incelemede').length };
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
// Alış belgesinin tarayıcıya verilen indirme adı (purchase-1): sunucu adı zaten türden normalize eder; yine de uzantı yalnız
// içerik türünden gelir, yol parçaları, kontrol/biçim (U+202E gibi yön işaretleri) ve Windows'ta geçersiz karakterler atılır.
const DOCUMENT_EXTENSIONS = { 'application/pdf': '.pdf', 'image/png': '.png', 'image/jpeg': '.jpg' };
export function documentFileName(name, type) {
  const extension = DOCUMENT_EXTENSIONS[type] || '.bin';
  const trimEnd = text => text.replace(/[ .]+$/u, '');
  let base = trimEnd(String(name ?? '').replace(/\\/g, '/').split('/').pop().replace(/[\p{Cc}\p{Cf}\p{Zl}\p{Zp}\p{Cs}<>:"|?*]/gu, '').replace(/^ +/u, ''));
  const last = /\.([\p{L}\p{N}]{1,8})$/u.exec(base);
  if (last && /\p{L}/u.test(last[1])) base = trimEnd(base.slice(0, -last[0].length));
  const lower = base.toLowerCase();
  if (lower.endsWith(extension) || (extension === '.jpg' && lower.endsWith('.jpeg'))) base = trimEnd(base.slice(0, base.lastIndexOf('.')));
  if (base.length > 120) base = trimEnd(base.slice(0, /[\uD800-\uDBFF]/u.test(base[119]) ? 119 : 120));
  if (!base) return `belge${extension}`;
  return (/^(con|prn|aux|nul|com[1-9¹²³]|lpt[1-9¹²³])$/iu.test(base.split('.')[0].replace(/ +$/u, '')) ? `belge-${base}` : base) + extension;
}
// Alış belgeleri (gap-denetim-izi-gozlemlenebilirlik-9): kaldırma yumuşaktır; editör kaldırılanları da isteyebilir (alıcı isteyemez).
// Editör için gerekçe zorunludur; alıcı yalnız kendi yüklediği, ödemeye bağlı olmayan ve taslaktaki belgeyi kaldırabilir.
// Masaüstü (AlislarViewModel.BelgeAciklamasi / BelgeSilmeGerekcesiGerekli) ile aynı metinler.
export const DOCUMENT_REASON_REQUIRED = 'Belge silme gerekçesi girin.';
export function documentsPath(purchaseId, role, showRemoved = false) {
  return `/api/alis/${purchaseId}/belgeler${role === 'editor' && showRemoved ? '?silinenler=true' : ''}`;
}
export function documentRemovable(role, purchase, doc) {
  if (!doc || doc.silindi) return false;
  if (role === 'editor') return true;
  return purchase?.durum === 'Taslak' && !doc.odemeId && doc.yukleyenRol === 'alici';
}
export function documentDescription(doc) {
  const uploader = doc?.yukleyen ? `Yükleyen: ${doc.yukleyen}` : 'Yükleyen: bilinmiyor (eski kayıt)';
  if (!doc?.silindi) return uploader;
  const when = doc.silinmeZamani ? ` · ${new Date(doc.silinmeZamani).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }).replace(',', '')}` : '';
  return `${uploader} · Kaldırıldı: ${doc.silen || 'bilinmiyor'}${when}${doc.silmeGerekcesi ? ` · Gerekçe: ${doc.silmeGerekcesi}` : ''}`;
}
export function documentDeletePayload(role, reason) {
  const text = String(reason ?? '').trim();
  if (role === 'editor' && !text) throw new Error(DOCUMENT_REASON_REQUIRED);
  if (text.length > 2000) throw new Error('Silme gerekçesi en fazla 2000 karakter olabilir.');
  return text ? { gerekce: text } : null;
}
// Yedek disk durumu (data-3): boş alan ve toplam boyut GB olarak; eski sunucu göndermezse satır yok. Masaüstü
// (GuvenlikViewModel.DiskSatirlari) ile aynı biçim.
export function gigabytes(bytes) {
  return `${(Number(bytes) / 1073741824).toLocaleString('tr-TR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} GB`;
}
export function backupDiskLines(status) {
  const lines = [];
  if (status?.yedekDiskiBosAlanBayt != null) lines.push(`Yedek diski boş alan: ${gigabytes(status.yedekDiskiBosAlanBayt)}`);
  if (status?.veriDiskiBosAlanBayt != null) lines.push(`Veri diski boş alan: ${gigabytes(status.veriDiskiBosAlanBayt)}`);
  if (status?.toplamYedekBayt != null) lines.push(`Yedeklerin toplam boyutu: ${gigabytes(status.toplamYedekBayt)}`);
  return lines;
}
// Son geri yükleme (gap-geri-yukleme-durum-geri-sarma-1): sunucunun açılışta yaptıkları ve yapılması gerekenler (Türkçe maddeler).
// Hiç geri yükleme olmadıysa ya da eski sunucu göndermezse null. Masaüstü (GuvenlikViewModel.GeriYuklemeSatirlari) ile aynı metin.
export function restoreReport(status) {
  if (!status?.sonGeriYukleme) return null;
  const items = Array.isArray(status.geriYuklemeRaporu) ? status.geriYuklemeRaporu.filter(item => typeof item === 'string' && item.trim()) : [];
  return { title: `Son geri yükleme: ${new Date(status.sonGeriYukleme).toLocaleString('tr-TR')}`, items };
}
// Ödemeye bağlanabilir gider sorgusu (webui-6): arama metni açıklama/notta aranır; metin tutar gibi de okunuyorsa ('2024' bir
// fatura numarası da olabilir) tutar okuması aramaTutari olarak eklenir ve sunucu ikisinden birine uyan gideri döndürür.
export function linkableExpensesPath(text = '', cursor = null) {
  const params = new URLSearchParams();
  const value = String(text || '').trim();
  if (value) {
    let amountCents = null;
    try { amountCents = cents(value, { allowZero: false }); } catch { amountCents = null; }
    params.set('arama', value.slice(0, 200));
    if (amountCents != null) params.set('aramaTutari', (amountCents / 100).toFixed(2));
  }
  if (cursor) params.set('imlec', cursor);
  const query = params.toString();
  return `/api/alis/baglanabilir-giderler${query ? `?${query}` : ''}`;
}
