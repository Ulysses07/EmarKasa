// Değişiklik geçmişi (merkezi denetim izi): editörün salt okunur görünümü. Kasayı değiştiren her kaydın önceki/yeni değeri,
// yapan, zaman, gerekçe, ay kilidi açılışı ve güvenlik olayları GET /api/denetim'den okunur (yeniden eskiye, sayfalı).
// app.js modülü yalnız Ayarlar'daki düğmeyle yükler. Bütün metinler textContent ile yazılır (sunucu verisi HTML sayılmaz).
import { h, button, input, field, select, help, table } from './ui-dom.js';
import { api, openModal, run } from './ui-shell.js';

export const ENTITY_LABELS = {
  Islem: 'Gider',
  Gelen: 'Gelir',
  Kanal: 'Kanal',
  Ayar: 'Kasa ayarı',
  Cari: 'Cari',
  Alis: 'Alış',
  AlisKalem: 'Alış kalemi',
  AlisDagilim: 'Alış kanal payı',
  AlisOdeme: 'Alış ödemesi',
  Belge: 'Belge',
  Alici: 'Alıcı hesabı',
  TakipKart: 'Kredi kartı',
  TakipEkstre: 'Kart ekstresi',
  TakipHarcama: 'Kart harcaması',
  TakipKartTaksit: 'Kart taksidi',
  TakipKartOdeme: 'Kart ödemesi',
  TakipKredi: 'Kredi',
  TakipKrediTaksit: 'Kredi taksidi',
  KrediKarti: 'Kart kaydı',
  KartOdeme: 'Eski kart ödemesi',
  Kredi: 'Kredi kaydı',
  AylikGiderSablon: 'Aylık gider şablonu',
  AylikGiderRevizyon: 'Aylık gider sürümü',
  AylikGiderOdeme: 'Aylık gider ödemesi',
  KasaKontrol: 'Kasa kontrolü',
  KasaEsik: 'Kanal alt sınırı',
  AyKilidi: 'Ay kilidi',
  EkstreBelge: 'Ekstre belgesi',
  EkstreKayit: 'Ekstre satırı',
  Hesap: 'Hesap',
  HesapHareket: 'Hesap hareketi',
  HesapTransfer: 'Hesap transferi',
  KrediTaksitOdeme: 'Kredi taksit ödemesi',
  Oturum: 'Oturum ve güvenlik',
};
export const TYPE_LABELS = {
  Ekle: 'Eklendi',
  Degistir: 'Değiştirildi',
  Sil: 'Silindi',
  KilitAc: 'Ay kilidi açıldı',
  KilitKapat: 'Ay kapatıldı',
  GecmisKayit: 'Sürüm öncesi kayıt (zamanı bilinmiyor)',
  BagKoptu: 'Bağ koptu (bağlı kayıt silindi)',
  GecmisAyEtkisi: 'Geçmiş ayın kanal payı değişti',
  GirisBasarili: 'Giriş yapıldı',
  GirisBasarisiz: 'Başarısız giriş',
  HizSiniri: 'Hız sınırı reddi',
  GirisYogun: 'Sunucu yoğun: giriş ertelendi',
  SifreDegisti: 'Editör şifresi değişti',
  SifreDegistirmeBasarisiz: 'Şifre değiştirme reddedildi',
  KurtarmaKoduUretildi: 'Kurtarma kodu oluşturuldu',
  KurtarmaKoduUretimiBasarisiz: 'Kurtarma kodu reddedildi',
  KurtarmaKullanildi: 'Kurtarma kodu kullanıldı',
  KurtarmaBasarisiz: 'Başarısız kurtarma',
  IzleyiciSifresiDegisti: 'İzleyici şifresi değişti',
  AliciSifresiDegisti: 'Alıcı şifresi değişti',
  AliciOturumlariKapatildi: 'Alıcı oturumları kapatıldı',
};
export const ACTOR_LABELS = { editor: 'Editör', viewer: 'İzleyici', alici: 'Alıcı', anonim: 'Kimliksiz istek', sistem: 'Sistem' };
export const PAGE_SIZE = 50;

// Süzgeçten sorgu yolu: yalnız dolu alanlar; kayıt kimliği varlık adıyla birlikte anlamlıdır (sunucu da böyle ister).
export function historyQuery({ varlik = '', varlikId = '', kilitAcmaOlayiId = '', oncekiId = null, adet = PAGE_SIZE } = {}) {
  const query = new URLSearchParams();
  const entity = String(varlik ?? '').trim();
  const id = String(varlikId ?? '').trim();
  const lock = String(kilitAcmaOlayiId ?? '').trim();
  if (id && !entity) throw new Error('Kayıt numarasıyla süzmek için kayıt türünü de seçin.');
  if (lock && !/^[1-9]\d*$/.test(lock)) throw new Error('Kilit açılışı numarası pozitif bir tam sayı olmalı.');
  if (entity) query.set('varlik', entity);
  if (id) query.set('varlikId', id);
  if (lock) query.set('kilitAcmaOlayiId', lock);
  if (oncekiId != null) query.set('oncekiId', String(oncekiId));
  query.set('adet', String(adet));
  return `/api/denetim?${query}`;
}

export function formatValue(value) {
  if (value == null) return '—';
  if (typeof value === 'boolean') return value ? 'Evet' : 'Hayır';
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}

function parse(json) {
  if (!json) return null;
  try {
    const value = JSON.parse(json);
    return value && typeof value === 'object' && !Array.isArray(value) ? value : { deger: value };
  } catch {
    return { deger: json };
  }
}

// Olayın alan farkları: değişiklikte iki taraf, eklemede yalnız yeni, silmede yalnız önceki değer.
export function changeLines(event) {
  const before = parse(event.oncekiJson);
  const after = parse(event.yeniJson);
  const fields = [...new Set([...Object.keys(before || {}), ...Object.keys(after || {})])];
  return fields.map(name => {
    const hasBefore = before != null && Object.hasOwn(before, name);
    const hasAfter = after != null && Object.hasOwn(after, name);
    if (hasBefore && hasAfter) return `${name}: ${formatValue(before[name])} → ${formatValue(after[name])}`;
    return hasAfter ? `${name}: ${formatValue(after[name])}` : `${name}: ${formatValue(before[name])} (önceki)`;
  });
}

export function actorText(event) {
  const actor = ACTOR_LABELS[event.aktorRol] || event.aktorRol;
  return [event.aktorId != null ? `${actor} #${event.aktorId}` : actor, event.istemciIp].filter(Boolean).join(' · ');
}

export function recordText(event) {
  const name = ENTITY_LABELS[event.varlik] || event.varlik;
  return [
    event.varlikId ? `${name} #${event.varlikId}` : name,
    event.kilitAcmaOlayiId != null ? `ay kilidi açılışı #${event.kilitAcmaOlayiId}` : null,
  ]
    .filter(Boolean)
    .join(' · ');
}

export function createDenetimUi() {
  function open(initial = {}) {
    const entity = select(
      'varlik',
      [{ value: '', label: 'Bütün kayıtlar' }, ...Object.entries(ENTITY_LABELS).map(([value, label]) => ({ value, label }))],
      initial.varlik || ''
    );
    const id = input('varlikId', initial.varlikId || '', { inputmode: 'numeric', maxlength: 100, placeholder: 'Örn. 812' });
    const lock = input('kilitAcmaOlayiId', initial.kilitAcmaOlayiId || '', { inputmode: 'numeric', maxlength: 12 });
    const results = h('div', { class: 'stack' }, help('Süzgeç seçip “Geçmişi göster”e basın.'));
    const errors = h('p', { class: 'form-error', role: 'alert', hidden: true });
    let filters = null;
    let lastId = null;
    let rows = [];
    const more = button('Daha eski kayıtlar', event => run(event.currentTarget, () => load(true)), 'small');
    const draw = done => {
      const body = rows.length
        ? table(
            ['Zaman', 'Yapan', 'İşlem', 'Kayıt', 'Değişiklik', 'Gerekçe'],
            rows.map(event => [
              new Date(event.zaman).toLocaleString('tr-TR'),
              actorText(event),
              TYPE_LABELS[event.tur] || event.tur,
              recordText(event),
              h('div', {}, ...changeLines(event).map(line => h('div', { class: 'table-sub' }, line))),
              event.gerekce || '—',
            ]),
            'Değişiklik kayıtları'
          )
        : help('Bu süzgeçte kayıt yok.');
      results.replaceChildren(body, done ? help('Daha eski kayıt yok.') : more);
    };
    async function load(append) {
      errors.hidden = true;
      const page = await api(historyQuery({ ...filters, oncekiId: append ? lastId : null }));
      rows = append ? rows.concat(page) : page;
      if (page.length) lastId = page[page.length - 1].id;
      draw(page.length < PAGE_SIZE);
    }
    const form = h(
      'form',
      {
        class: 'stack',
        onsubmit: event => {
          event.preventDefault();
          return run(form.querySelector('button[type="submit"]'), async () => {
            try {
              filters = { varlik: entity.value, varlikId: id.value, kilitAcmaOlayiId: lock.value };
              historyQuery(filters);
              lastId = null;
            } catch (error) {
              errors.textContent = error.message;
              errors.hidden = false;
              return;
            }
            await load(false);
          });
        },
      },
      h('div', { class: 'form-grid' }, field('Kayıt türü', entity), field('Kayıt numarası', id), field('Ay kilidi açılışı numarası', lock)),
      errors,
      h('div', { class: 'modal-actions' }, h('button', { type: 'submit', class: 'button primary' }, 'Geçmişi göster'))
    );
    openModal(
      'Değişiklik geçmişi',
      h(
        'div',
        { class: 'stack' },
        help(
          'Kasayı değiştiren kayıtların önceki ve yeni değerleri, yapan, zaman ve gerekçesiyle; ay kilidi açılışları ve güvenlik olayları. Kayıtlar değiştirilemez ve silinemez. Bu sürümden önce saklanan iptal gerekçeleri ve alış ödemesi düzeltmeleri “Sürüm öncesi kayıt” olarak aktarıldı; zamanları aktarım anıdır.'
        ),
        form,
        results
      ),
      true
    );
  }
  return { open };
}
