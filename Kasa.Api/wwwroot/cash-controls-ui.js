// Kasa hareket dökümü satır türleri (GET /api/kasa-hareketleri; gap-denetim-izi-gozlemlenebilirlik-3).
export const MOVEMENT_LABELS = {
  Gelir: 'Dönem geliri', EkstreGeliri: 'Ekstre geliri', EkGelir: 'Ek gelir', KrediCekimi: 'Kredi çekimi', Gider: 'Gider', SabitGider: 'Sabit gider',
  AylikGider: 'Aylık gider', KartOdemesi: 'Kart ödemesi', KartIadesi: 'Kart borcu iadesi', KrediTaksidi: 'Kredi taksidi', KartAySonu: 'Eski kart ay sonu düşümü',
};
// "Kontrolden beri değişenler"deki denetim olayları: kasayı etkileyen kayıtların adları (değişiklik geçmişi ekranının tam listesi denetim-ui.js'tedir).
const CHANGE_ENTITIES = {
  Islem: 'Gider', Gelen: 'Gelir', Kanal: 'Kanal', Ayar: 'Kasa ayarı', Alis: 'Alış', AlisKalem: 'Alış kalemi', AlisDagilim: 'Alış kanal payı', AlisOdeme: 'Alış ödemesi',
  TakipKart: 'Kredi kartı', TakipHarcama: 'Kart harcaması', TakipKartTaksit: 'Kart taksidi', TakipKartOdeme: 'Kart ödemesi', TakipKredi: 'Kredi', TakipKrediTaksit: 'Kredi taksidi',
  Kredi: 'Kredi kaydı', KrediKarti: 'Kart kaydı', KartOdeme: 'Eski kart ödemesi', AylikGiderOdeme: 'Aylık gider ödemesi', EkstreKayit: 'Ekstre satırı', HesapHareket: 'Hesap hareketi',
};
const CHANGE_TYPES = { Ekle: 'Eklendi', Degistir: 'Değiştirildi', Sil: 'Silindi', BagKoptu: 'Bağ koptu', GecmisAyEtkisi: 'Geçmiş ay payı değişti', KilitAc: 'Ay kilidi açıldı', KilitKapat: 'Ay kapatıldı' };
// Olay ayrıntısında gösterilen mali alanlar (bütün alanlar değişiklik geçmişi ekranında).
const CHANGE_FIELDS = ['Tarih', 'TutarTl', 'Tutar', 'Iptal', 'Kanal', 'Cari', 'Aciklama', 'Durum'];

export const NOTE_REQUIRED_MESSAGE = 'Fark varsa açıklama girin.';
/** Sunucu kuralı: fark sıfırdan farklıyken açıklama zorunludur. */
export function noteRequired(fark, note) { return Number(fark || 0) !== 0 && !String(note ?? '').trim(); }

/** Kontrol satırının güncel durumu: sonradan değiştiyse güncel sistem bakiyesi ve güncel fark; filigransız eski kayıt belirtilir. */
export function controlStatus(row, money) {
  const parts = [];
  if (row.sonradanDegisti) parts.push(`Sonradan değişti: güncel sistem ${money(row.guncelSistemBakiye)}, güncel fark ${money(row.guncelFark)}`);
  else if (row.guncelSistemBakiye != null) parts.push('Kayıttan sonra değişmedi');
  if (!row.hesapTarihi) parts.push('Eski kayıt, filigran yok');
  return parts.join(' · ') || '—';
}

/** Döküm sorgu yolu: yalnız dolu süzgeçler. */
export function movementsPath({ baslangic = '', bitis = '', kanalId = '' } = {}) {
  const query = new URLSearchParams();
  if (baslangic) query.set('baslangic', baslangic);
  if (bitis) query.set('bitis', bitis);
  if (kanalId !== '' && kanalId != null) query.set('kanalId', String(kanalId));
  const text = query.toString();
  return text ? `/api/kasa-hareketleri?${text}` : '/api/kasa-hareketleri';
}

function parse(json) { if (!json) return null; try { const value = JSON.parse(json); return value && typeof value === 'object' ? value : null; } catch { return null; } }
/** Denetim olayının kısa ayrıntısı: gerekçe ve önemli mali alanlar (değişiklikte önceki → yeni). */
export function changeDetail(event) {
  const before = parse(event.oncekiJson); const after = parse(event.yeniJson);
  const text = value => value == null ? '—' : typeof value === 'boolean' ? (value ? 'Evet' : 'Hayır') : String(value);
  const lines = CHANGE_FIELDS.filter(name => (before && name in before) || (after && name in after)).map(name => before && after && name in before && name in after
    ? `${name}: ${text(before[name])} → ${text(after[name])}` : `${name}: ${text((after && name in after ? after : before)[name])}`);
  return [event.gerekce, ...lines].filter(Boolean).join(' · ') || '—';
}
export function changeRecord(event) { return `${CHANGE_ENTITIES[event.varlik] || event.varlik}${event.varlikId ? ` #${event.varlikId}` : ''}`; }
export function changeType(event) { return CHANGE_TYPES[event.tur] || event.tur; }

export function createCashControlsUi(c) {
  const { api, h, button, input, field, select, help, section, table, money, moneyNode, signedAmountField, amount, formDialog, openModal, closeModal, run, toast, summary, requestIdentity, canEdit, isOpen, navigate, dateText, today } = c;
  const editor = () => { if (!canEdit()) throw new Error('Bu işlem için editör hesabı gerekir.'); };
  function thresholdSettings(rows) {
    return section('Kanal alt bakiye uyarıları', h('div', { class: 'stack' }, help('İstediğiniz kanal için uyarıyı açıp alt sınır belirleyin. Uyarı bakiyeyi değiştirmez; kart borcu bu sınırdan düşülmez.'), rows.length ? table(['Kanal', 'Kasa bakiyesi', 'Alt sınır', 'Durum', ''], rows.map(row => [row.kanal, moneyNode(row.bakiye), row.etkin ? moneyNode(row.tutar) : 'Kapalı', row.etkin && row.esikAltinda ? h('span', { class: 'badge pending' }, 'Alt sınırın altında') : row.etkin ? 'Sınırın üzerinde veya eşit' : 'Uyarı kapalı', canEdit() ? button('Uyarıyı düzenle', () => thresholdDialog(row), 'small') : ''])) : help('Henüz kanal yok.')));
  }
  function thresholdDialog(row) {
    editor(); const enabled = input('etkin', '1', { type: 'checkbox', checked: row.etkin }); const total = input('tutar', row.tutar, { inputmode: 'decimal', required: true });
    formDialog(`${row.kanal} alt bakiye uyarısı`, h('div', { class: 'stack' }, field('Uyarı açık', enabled), field('Alt sınır (₺)', total), help('Sıfır girerseniz yalnız eksi bakiye için uyarı görünür. Bu ayar kasa bakiyesini değiştirmez.')), 'Uyarıyı kaydet', async () => {
      editor(); await api(`/api/kasa-esikleri/${row.kanalId}`, { method: 'PUT', body: { surum: row.surum, tutar: amount(total.value), etkin: enabled.checked } }); closeModal(); toast('Kanal uyarısı kaydedildi.'); await navigate('tools');
    });
  }
  // Kontrol listesi: kayıtlı değerler değişmez; güncel durum kaydın günü için bugünkü veriyle yeniden hesaplanır (gap-coklu-giris-cift-sayim-mutabakat-17).
  function history(rows) {
    const note = row => [row.not, row.farkAciklamasi && `Fark açıklaması: ${row.farkAciklamasi}`].filter(Boolean).join(' · ') || '—';
    const actions = row => canEdit() ? h('div', { class: 'row-actions' }, button('Değişenleri göster', () => run(null, () => sinceDialog(row)), 'small'),
      (Number(row.fark) !== 0 || row.sonradanDegisti) && button('Farkı açıkla', () => explainDialog(row), 'small')) : '';
    return section('Gerçek bakiye karşılaştırmaları', h('div', { class: 'stack' },
      help('Kayıt anındaki genel kasa ile sizin bildirdiğiniz gerçek bakiye karşılaştırılır. Fark otomatik gelir veya gider yazılmaz. Kayıt gününe ya da öncesine sonradan girilen, silinen veya düzeltilen kayıtlar güncel durumu değiştirir; kayıtlı değerler değişmez.'),
      rows.length ? table(['Kayıt zamanı', 'Kayıtlı genel kasa', 'Gerçek bakiye', 'Gerçek − kayıtlı', 'Güncel durum', 'Not', ''], rows.map(row => [new Date(row.kaydedildi).toLocaleString('tr-TR'), moneyNode(row.sistemBakiye), moneyNode(row.gercekBakiye), moneyNode(row.fark),
        row.sonradanDegisti ? h('span', { class: 'badge pending' }, controlStatus(row, money)) : controlStatus(row, money), note(row), actions(row)])) : help('Henüz bakiye karşılaştırması kaydedilmedi.'),
      h('div', { class: 'row-actions' }, canEdit() && button('Gerçek bakiye ile karşılaştır', comparisonDialog, 'small'), button('Kasa hareket dökümü', () => run(null, movementsDialog), 'small'))));
  }
  const channelTable = channels => channels?.length ? table(['Kanal', 'Kanal kasası'], channels.map(k => [k.kanal, moneyNode(k.bakiye)])) : null;
  function comparisonDialog() {
    editor(); const identity = requestIdentity(); const total = signedAmountField('gercekBakiye', '', 'Kontrol ettiğiniz gerçek bakiye (₺)'); const note = input('not', '', { maxlength: 2000 });
    formDialog('Genel kasa bakiyesini karşılaştır', h('div', { class: 'stack' }, total.node, field('Açıklama', note), help('Şu an kontrol ettiğiniz tutarı girin. Önce farkı göreceksiniz; fark varsa açıklama zorunludur. Bu işlem geçmiş tarihli kayıt veya otomatik düzeltme oluşturmaz.')), 'Farkı göster', async form => {
      editor(); const actual = total.read();
      // Açıklama önizleme özetine girmez: fark görüldükten sonra yazılabilir ya da düzeltilebilir.
      const finalNote = input('not', note.value, { maxlength: 2000 });
      const body = () => ({ gercekBakiye: actual, not: finalNote.value.trim() || null });
      let preview = await api('/api/kasa-kontrol/onizleme', { method: 'POST', body: body() }); if (!isOpen(form)) return;
      let needsPreview = false;
      const result = h('div', { class: 'stack' }); const draw = value => result.replaceChildren(...[h('div', { class: 'summary-strip' }, summary('Kayıtlı genel kasa', money(value.sistemBakiye)), summary('Gerçek bakiye', money(value.gercekBakiye)), summary('Gerçek − kayıtlı farkı', money(value.fark))), channelTable(value.kanalBakiyeleri)].filter(Boolean)); draw(preview);
      const status = help('Yalnız karşılaştırma geçmişe kaydedilir. Kasa bakiyesi değişmez. Fark varsa açıklama zorunludur.');
      formDialog('Bakiye farkını inceleyin', h('div', { class: 'stack' }, result, field('Açıklama', finalNote), status), 'Karşılaştırmayı kaydet', async confirmation => {
        editor();
        if (needsPreview) {
          const latest = await api('/api/kasa-kontrol/onizleme', { method: 'POST', body: body() }); if (!isOpen(confirmation)) return;
          preview = latest; draw(latest); needsPreview = false;
          status.textContent = 'Güncel fark gösterildi. İnceledikten sonra tekrar “Karşılaştırmayı kaydet” seçin.'; return;
        }
        if (noteRequired(preview.fark, finalNote.value)) throw Object.assign(new Error(NOTE_REQUIRED_MESSAGE), { fields: { not: NOTE_REQUIRED_MESSAGE } });
        try { await api('/api/kasa-kontrol', { method: 'POST', body: identity({ ...body(), kontrolOzeti: preview.kontrolOzeti }) }); }
        catch (error) { if (error.status === 409) { needsPreview = true; status.textContent = 'Kasa değişti. Yeniden kaydettiğinizde önce güncel fark gösterilecek; bir sonraki onayınızla kaydedilecek.'; } throw error; }
        closeModal(); toast('Karşılaştırma kaydedildi; kasa bakiyesi değişmedi.'); await navigate('home');
      });
    });
  }
  // Farkın sonradan açıklanması: tutarlar değişmez; kayıt arada başka bir açıklamayla değiştiyse sunucu 409 verir (yenileyin).
  function explainDialog(row) {
    editor(); const identity = requestIdentity(); const text = input('aciklama', row.farkAciklamasi || '', { maxlength: 2000, required: true });
    formDialog('Farkı açıkla', h('div', { class: 'stack' }, h('div', { class: 'summary-strip' }, summary('Kayıtlı fark', money(row.fark)), row.sonradanDegisti && summary('Güncel fark', money(row.guncelFark))), field('Açıklama', text), help('Açıklama kontrol kaydına eklenir; kayıtlı bakiyeler ve fark değişmez.')), 'Açıklamayı kaydet', async () => {
      editor(); const aciklama = text.value.trim();
      if (!aciklama) throw Object.assign(new Error('Açıklama girin.'), { fields: { aciklama: 'Açıklama girin.' } });
      await api(`/api/kasa-kontrol/${row.id}/aciklama`, { method: 'PUT', body: identity({ surum: row.surum, aciklama }) });
      closeModal(); toast('Fark açıklaması kaydedildi.'); await navigate('home');
    });
  }
  const movementType = m => `${MOVEMENT_LABELS[m.tur] || m.tur}${m.otomatik ? ' (kendiliğinden)' : ''}${m.etkiTarihi !== m.kayitTarihi ? ` · kayıt ${dateText(m.kayitTarihi)}` : ''}`;
  const movementTable = (rows, effect = m => m.genelKasaEtkisi, label = 'Genel kasa etkisi') => table(['Etki tarihi', 'Tür', 'Açıklama', 'Kanal', label],
    rows.map(m => [dateText(m.etkiTarihi), movementType(m), m.aciklama, m.kanal, moneyNode(effect(m), effect(m) < 0 ? 'negative' : '')]));
  // "Bu kontrolden beri değişenler" (gap-denetim-izi-gozlemlenebilirlik-3): denetim olayları, mali istekler ve kontrol gününden bugüne hareketler.
  async function sinceDialog(row) {
    editor(); const data = await api(`/api/kasa-kontrol/${row.id}/sonrasi`);
    const before = data.hareketler.filter(m => m.etkiTarihi <= data.esasTarih); const after = data.hareketler.filter(m => m.etkiTarihi > data.esasTarih);
    openModal('Bu kontrolden beri değişenler', h('div', { class: 'stack' },
      h('div', { class: 'summary-strip' }, summary('Kontroldeki sistem bakiyesi', money(data.sistemBakiye), dateText(data.esasTarih)),
        summary('Aynı gün, bugünkü veriyle', money(data.guncelSistemBakiye), `Geriye dönük değişim ${money(data.guncelSistemBakiye - data.sistemBakiye)}`),
        summary('Bugünkü genel kasa', money(data.bugunkuSistemBakiye), `Kontrol gününden sonra ${money(data.bugunkuSistemBakiye - data.guncelSistemBakiye)}`)),
      !data.filigranVar && h('div', { class: 'notice', role: 'status' }, 'Eski kayıt, filigran yok: değişiklikler kayıt anından sonraki zamana göre listelenir; mali istek sırası bilinmez.'),
      data.kirpildi && h('div', { class: 'notice', role: 'status' }, 'Listeler en eski 500 öğeyle kırpıldı; ayrıntı için değişiklik geçmişini kullanın.'),
      section('Kontrolden sonra yapılan değişiklikler', data.degisiklikler.length ? table(['Zaman', 'İşlem', 'Kayıt', 'Ayrıntı'], data.degisiklikler.map(e => [new Date(e.zaman).toLocaleString('tr-TR'), changeType(e), changeRecord(e), changeDetail(e)])) : help('Kontrolden sonra kasayı etkileyen değişiklik yok.')),
      data.istekler.length > 0 && help(`Mali istekler: ${data.istekler.map(r => `${r.tur} #${r.sonucId}`).join(', ')}`),
      before.length > 0 && section('Kontrol gününe ya da öncesine sonradan girilen giderler', movementTable(before)),
      section('Kontrol gününden bugüne kasaya işleyen hareketler', after.length ? movementTable(after) : help('Kontrol gününden sonra kasaya işleyen hareket yok.'))), true);
  }
  // Kasa hareket dökümü: genel kasayı ya da seçilen kanalın kasasını oluşturan bütün hareketler kaynağıyla (en çok 366 gün, bitiş en geç bugün).
  async function movementsDialog() {
    const channels = await api('/api/kanallar');
    const end = today(); const from = input('baslangic', end.slice(0, 8) + '01', { type: 'date', required: true }); const to = input('bitis', end, { type: 'date', required: true });
    const channel = select('kanalId', [{ value: '', label: 'Genel kasa' }, ...channels.map(k => ({ value: String(k.id), label: k.ad }))], '');
    const results = h('div', { class: 'stack' }, help('Aralığı seçip “Dökümü göster”e basın.'));
    formDialog('Kasa hareket dökümü', h('div', { class: 'stack' }, h('div', { class: 'filter-period' }, field('Başlangıç', from), field('Bitiş', to), field('Kasa', channel)), results,
      help('Kredi taksitleri ve eski kartın ay sonu düşümü tarihinde kendiliğinden işler. Kanal seçilince kanal kasası kuralı uygulanır: sabit, ortak ve dağılım bekleyen giderler kanal kasasını değiştirmez.')), 'Dökümü göster', async () => {
      if (from.value > to.value) throw new Error('Bitiş tarihi başlangıçtan önce olamaz.');
      const data = await api(movementsPath({ baslangic: from.value, bitis: to.value, kanalId: channel.value }));
      const kanal = data.kanalId != null;
      results.replaceChildren(h('div', { class: 'summary-strip' }, summary('Açılış', money(data.acilisBakiyesi), dateText(data.baslangic)), summary('Kapanış', money(data.kapanisBakiyesi), dateText(data.bitis)), summary('Hareket sayısı', String(data.hareketler.length))),
        data.hareketler.length ? movementTable(data.hareketler, kanal ? m => m.kanalEtkisi : m => m.genelKasaEtkisi, kanal ? 'Kanal kasası etkisi' : 'Genel kasa etkisi') : help('Bu aralıkta hareket yok.'));
    }, { wide: true });
  }
  return { thresholdSettings, thresholdDialog, history, comparisonDialog, explainDialog, sinceDialog, movementsDialog };
}
