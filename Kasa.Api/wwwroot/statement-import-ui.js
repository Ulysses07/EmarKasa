export const statementBanks = [
  ['Vakifbank', 'VakıfBank'], ['Akbank', 'Akbank'], ['QNB', 'QNB'],
  ['Isbank', 'İş Bankası'], ['Garanti', 'Garanti BBVA'], ['Denizbank', 'DenizBank']
];

export function createStatementImportUi(c) {
  const { api, h, button, input, field, select, help, section, table, money, moneyNode, dateText, cents, formDialog, closeModal, page, navigate, run, toast, summary, requestIdentity, isOpen, canEdit, isCurrent, view } = c;
  const base = '/api/ekstre-aktar';
  const kinds = { Gelir: 'Banka girişi', Gider: 'Banka çıkışı', KartHarcama: 'Kart harcaması / faiz / masraf', KartIade: 'Karta iade', KartOdemesi: 'Kart borcu ödemesi' };
  const classes = { Faiz: 'Faiz', Komisyon: 'Komisyon', Vergi: 'Vergi', Ucret: 'Ücret', Transfer: 'Transfer', Odeme: 'Ödeme', Hareket: 'Hareket', Belirsiz: 'Kontrol edilmeli' };
  const editor = () => { if (!canEdit()) throw new Error('Ekstre yüklemek ve işlemek için editör hesabı gerekir.'); };
  const act = (label, work, style = '') => button(label, event => run(event.currentTarget, work), style);
  const bankName = bank => statementBanks.find(([key]) => key === bank)?.[1] || bank;
  const sourceName = row => `${bankName(row.banka)} · ${row.kaynak === 'Kart' ? `Kart ekstresi · ${row.hesapAdi || `Kart #${row.kartId}`}` : row.hesapAdi || 'Banka hareketi'}`;
  const shares = rows => h('div', { class: 'allocation-tags' }, (rows || []).map(row => h('span', { class: 'allocation-tag' }, `${row.kanal || 'Genel kasa'}: ${money(row.tutar)}`)));
  const warningList = warnings => warnings?.length ? h('ul', { class: 'similar-records' }, warnings.map(text => h('li', {}, text))) : null;
  const supportedCurrency = row => !row.paraBirimi || ['TRY', 'TL', 'Belirsiz'].includes(row.paraBirimi);
  const session = () => c.session?.();
  const stillHere = (generation, epoch) => canEdit() && isCurrent(generation) && session() === epoch;

  async function render(generation, id = null) {
    editor(); const epoch = session();
    const recordId = Number.isSafeInteger(id?.kayitId) && id.kayitId > 0 ? id.kayitId : null;
    const beforeId = Number.isSafeInteger(id?.beforeId) && id.beforeId > 0 ? id.beforeId : null;
    const documentId = Number.isSafeInteger(id) && id > 0 ? id : null;
    if (id != null && !recordId && !beforeId && !documentId) throw new Error('Geçerli bir belge veya kaynak kayıt seçin.');
    const documentPath = recordId ? `${base}/kayitlar/${recordId}` : documentId ? `${base}/${documentId}` : null;
    page('Ekstre / Hareket Yükle', 'PDF satırlarını seç, kontrol et ve kaydet', [...(documentPath ? [act('Belgeyi yenile', () => navigate('imports', id))] : []), act('+ PDF yükle', () => uploadDialog(generation), 'primary')]);
    if (documentPath) {
      const [document, channels, cards] = await Promise.all([api(documentPath), api('/api/kanallar'), api('/api/takip/kartlar')]);
      if (!stillHere(generation, epoch)) return;
      renderDocument(document, channels, cards, generation, epoch);
      return;
    }
    const documents = await api(beforeId ? `${base}?beforeId=${beforeId}` : base);
    if (!stillHere(generation, epoch)) return;
    view().replaceChildren(
      h('div', { class: 'notice' }, 'PDF yüklemek kayıt oluşturmaz. Okunan hareketler seçimsiz gelir; yalnız seçip onayladığın satırlar kaydedilir.'),
      section('PDF yükle', h('div', { class: 'stack' }, help('Kart ekstresinden harcama, faiz, komisyon ve ödemeleri; banka hesap hareketinden giriş ve çıkışları inceleyebilirsin. Kanal bilgisi PDF’de bulunmaz; kaydetmeden önce sen seçersin.'), h('p', { class: 'import-bank-hint' }, statementBanks.map(([, name]) => name).join(' · ')), act('Kart ekstresi / hesap hareketi seç', () => uploadDialog(generation), 'primary'))),
      section('Yüklenen belgeler', h('div', { class: 'stack' }, documents.length ? table(['Belge', 'Kaynak', 'Yüklendi', 'Okunan satır', 'Kayıt', ''], documents.map(document => [h('span', { class: 'import-source-title' }, document.dosyaAdi), sourceName(document), new Date(document.yuklendi).toLocaleString('tr-TR'), document.satirSayisi, document.kayitSayisi, act('Aç ve incele', () => navigate('imports', document.id), 'small')])) : help(beforeId ? 'Daha eski belge yok.' : 'Henüz PDF yüklenmedi.'), h('div', { class: 'row-actions' }, beforeId && act('En yeni belgelere dön', () => navigate('imports')), documents.length === 50 && act('Daha eski belgeler', () => navigate('imports', { beforeId: documents[documents.length - 1].id })))))
    );
  }

  async function uploadDialog(generation) {
    editor(); const epoch = session(); const cards = await api('/api/takip/kartlar');
    if (!stillHere(generation, epoch)) return;
    const file = input('dosya', '', { type: 'file', accept: '.pdf,application/pdf', required: true });
    const source = select('kaynak', [{ value: 'Kart', label: 'Kredi kartı ekstresi' }, { value: 'Banka', label: 'Banka hesap hareketi' }], 'Kart');
    const bank = select('banka', [{ value: '', label: 'Bankayı seç' }, ...statementBanks.map(([value, label]) => ({ value, label }))], '', { required: true });
    const card = select('kartId', [{ value: '', label: 'Uygulamadaki kartı seç' }, ...cards.filter(card => card.yeniTakip).map(card => ({ value: card.id, label: card.ad + (card.aktif ? '' : ' · Yeni kullanıma kapalı') }))], '', { required: true });
    const account = input('hesapAdi', '', { maxlength: 100, placeholder: 'Örn. İşletme hesabı / son 4 hane' });
    const cardField = field('Kart', card); const accountField = field('Hesap kısa adı', account);
    const changeSource = () => { cardField.hidden = source.value !== 'Kart'; card.disabled = source.value !== 'Kart'; card.required = source.value === 'Kart'; accountField.hidden = source.value !== 'Banka'; account.disabled = source.value !== 'Banka'; account.required = source.value === 'Banka'; };
    source.addEventListener('change', changeSource); changeSource();
    formDialog('Ekstre / hareket PDF’si yükle', h('div', { class: 'stack' }, h('div', { class: 'import-source' }, field('Belge türü', source), field('Banka', bank), cardField, accountField, field('PDF dosyası', file)), help('En fazla 10 MB ve 50 sayfa. Metin içeren, şifresiz PDF yükle. Taranmış görüntüler ve yabancı para hareketleri bu sürümde kaydedilemez.'), help('PDF uygulamanın sunucusunda tutulur. Okuma tamamlanınca tüm hareketleri kontrol ederek seçersin; yükleme kasayı veya kart borcunu değiştirmez.')), 'PDF’yi oku', async form => {
      editor(); if (!stillHere(generation, epoch) || !isOpen(form)) return;
      const selected = file.files?.[0];
      if (!selected || !selected.size || selected.size > 10 * 1024 * 1024 || !/\.pdf$/i.test(selected.name) || selected.type && selected.type !== 'application/pdf') throw new Error('Boş olmayan, en fazla 10 MB boyutunda bir PDF dosyası seçin.');
      if (!statementBanks.some(([key]) => key === bank.value)) throw new Error('Bankayı seçin.');
      if (source.value === 'Kart' && !Number(card.value)) throw new Error('Ekstrenin ait olduğu kartı seçin.');
      if (source.value === 'Banka' && !account.value.trim()) throw new Error('Hesaba kısa bir ad verin.');
      const payload = c.createFormData ? c.createFormData() : new FormData(); payload.append('dosya', selected); payload.append('kaynak', source.value); payload.append('banka', bank.value);
      if (source.value === 'Kart') payload.append('kartId', card.value); else payload.append('hesapAdi', account.value.trim());
      const result = await api(`${base}/yukle`, { method: 'POST', body: payload });
      if (!stillHere(generation, epoch) || !isOpen(form)) return;
      closeModal(); toast('PDF okundu. Kaydetmek istediğin satırları seç.'); await navigate('imports', result.id);
    }, { wide: true });
  }

  function allocationEditor(channels, changed) {
    const selected = new Set(); const totals = new Map(); const controls = new Map();
    const list = h('div');
    const mode = select('dagilimTuru', [], '');
    const draw = () => {
      controls.clear(); list.hidden = !['Esit', 'Ozel'].includes(mode.value);
      if (list.hidden) { list.replaceChildren(); return; }
      list.replaceChildren(...channels.filter(channel => channel.aktif).map(channel => {
        const checked = input(`pay-kanal-${channel.id}`, channel.id, { type: 'checkbox', checked: selected.has(channel.id) });
        const total = input(`pay-tutar-${channel.id}`, totals.get(channel.id) || '', { inputmode: 'decimal', disabled: !selected.has(channel.id), 'aria-label': `${channel.ad} payı (₺)` });
        checked.addEventListener('change', () => { if (checked.checked) selected.add(channel.id); else selected.delete(channel.id); total.disabled = !checked.checked; changed(); });
        total.addEventListener('input', () => { totals.set(channel.id, total.value); changed(); }); controls.set(channel.id, { checked, total });
        return h('div', { class: 'monthly-allocation' }, field(channel.ad, checked), mode.value === 'Ozel' && total);
      }));
    };
    mode.addEventListener('change', () => { draw(); changed(); });
    return {
      node: h('fieldset', {}, h('legend', {}, 'Kanal dağılımı'), field('Hangi kasa / kanallar?', mode), list),
      setKind(kind) {
        const automatic = ['KartOdemesi', 'KartIade'].includes(kind);
        const options = automatic ? [{ value: 'Otomatik', label: 'İlgili kart hareketlerinden otomatik' }] : [{ value: '', label: 'Dağılım seç' }, ...(['Gelir', 'Gider'].includes(kind) ? [{ value: 'Genel', label: 'Yalnız genel kasa' }] : []), { value: 'Esit', label: 'Seçilen kanallara eşit' }, { value: 'Ozel', label: 'Kanal tutarlarını gir' }];
        const previous = mode.value; mode.replaceChildren(...options.map(option => h('option', { value: option.value }, option.label))); mode.value = automatic ? 'Otomatik' : options.some(option => option.value === previous) ? previous : ''; mode.disabled = automatic; draw();
      },
      read(total) {
        if (mode.value === 'Otomatik' || mode.value === 'Genel') return { dagilimTuru: mode.value, dagilimlar: [] };
        if (!['Esit', 'Ozel'].includes(mode.value)) throw new Error('Seçili hareketin kasa / kanal dağılımını seçin.');
        const rows = [...controls].filter(([, controls]) => controls.checked.checked).map(([id, controls]) => ({ kanalId: id, tutar: mode.value === 'Esit' ? 0 : cents(controls.total.value, { allowZero: false }) / 100 }));
        if (!rows.length) throw new Error('Seçili hareket için en az bir kanal seçin.');
        if (mode.value === 'Ozel' && rows.reduce((sum, row) => sum + cents(row.tutar), 0) !== cents(total)) throw new Error('Kanal paylarının toplamı hareket tutarına eşit olmalı.');
        return { dagilimTuru: mode.value, dagilimlar: rows };
      }
    };
  }

  function renderDocument(document, channels, cards, generation, epoch) {
    const records = document.kayitlar || []; const currentRows = new Set(records.filter(row => !row.iptal).map(row => row.satirNo));
    const identity = requestIdentity(); const allowedKinds = document.kaynak === 'Kart' ? ['KartHarcama', 'KartIade', 'KartOdemesi'] : ['Gelir', 'Gider', 'KartOdemesi'];
    const previewHost = h('div', { class: 'import-preview', hidden: true }); const selectedCount = h('strong', {}, '0 satır seçili');
    const list = h('div', { class: 'import-rows' }); let preview = null; let previewSignature = null; let previewSequence = 0; const rows = [];
    const active = () => stillHere(generation, epoch);
    const invalidate = () => { preview = null; previewSignature = null; previewSequence++; previewHost.hidden = true; selectedCount.textContent = `${rows.filter(row => row.checked.checked).length} satır seçili`; };
    const filter = input('satir-ara', '', { placeholder: 'Açıklama, faiz, komisyon veya tutar ara', 'aria-label': 'Okunan hareketlerde ara', type: 'search' });
    filter.addEventListener('input', () => { const term = filter.value.toLocaleLowerCase('tr-TR'); for (const row of rows) row.node.hidden = !`${row.source.kaynakSatir} ${row.source.aciklama} ${row.source.tutar ?? ''} ${classes[row.source.sinif] || ''}`.toLocaleLowerCase('tr-TR').includes(term); });
    for (const source of document.satirlar || []) {
      const blocked = currentRows.has(source.no); const foreign = !supportedCurrency(source);
      const checked = input(`sec-${source.no}`, '1', { type: 'checkbox', disabled: blocked || foreign, 'aria-label': `${source.no}. hareketi seç` });
      const date = input(`tarih-${source.no}`, source.tarih || '', { type: 'date', required: true });
      const text = input(`aciklama-${source.no}`, source.aciklama || '', { required: true, maxlength: 1000 });
      const total = input(`tutar-${source.no}`, source.tutar ?? '', { required: true, inputmode: 'decimal' });
      const kind = select(`tur-${source.no}`, [{ value: '', label: 'İşlem türünü seç' }, ...allowedKinds.map(value => ({ value, label: kinds[value] }))], allowedKinds.includes(source.onerilenIslem) ? source.onerilenIslem : '', { required: true });
      const card = select(`kart-${source.no}`, [{ value: '', label: 'Ödenen kartı seç' }, ...cards.filter(card => card.yeniTakip).map(card => ({ value: card.id, label: card.ad + (card.aktif ? '' : ' · Yeni kullanıma kapalı') }))], document.kartId || '');
      const cardField = field('Kredi kartı', card);
      const refund = select(`iade-kaynak-${source.no}`, [{ value: '', label: 'Önce kartı ve iade edilen harcamayı seç' }], ''); const refundField = field('İade edilen kart harcaması', refund); let refundSequence = 0;
      const refundHelp = h('p', { class: 'help' });
      const allocation = allocationEditor(channels, invalidate);
      const fields = h('div', { hidden: true }, h('div', { class: 'form-grid' }, field('İşlem tarihi', date), field('Tutar (₺)', total), field('Açıklama', text), field('Kaydedilecek işlem', kind), cardField, refundField), refundHelp, allocation.node);
      const updateKind = async () => {
        invalidate(); const sequence = ++refundSequence; cardField.hidden = document.kaynak !== 'Banka' || kind.value !== 'KartOdemesi'; refundField.hidden = kind.value !== 'KartIade'; allocation.setKind(kind.value); refundHelp.textContent = kind.value === 'KartOdemesi' ? 'Bu satır gerçek kart ödemesi olarak kasadan düşer. Aynı ödeme daha önce girildiyse tekrar seçme.' : kind.value === 'KartIade' ? 'İade kart borcunu azaltır; nakit girişi oluşturmaz. İade edilen asıl harcamayı seç.' : kind.value === 'KartHarcama' ? 'Kart borcuna eklenir; bu satır kasadan düşmez. Faiz ve komisyonun kaynak kanallarını da sen seçersin.' : '';
        if (kind.value !== 'KartIade') return;
        refund.disabled = true; refund.replaceChildren(h('option', { value: '' }, 'Harcamalar yükleniyor…')); refund.value = '';
        try {
          const detail = await api(`/api/takip/kartlar/${document.kartId}`);
          if (!active() || sequence !== refundSequence || kind.value !== 'KartIade') return;
          refund.replaceChildren(h('option', { value: '' }, 'İade edilen harcamayı seç'), ...(detail.harcamalar || []).filter(charge => !charge.iptal && charge.tutar > 0).map(charge => h('option', { value: charge.id }, `#${charge.id} · ${dateText(charge.tarih)} · ${money(charge.tutar)} · ${charge.aciklama}`))); refund.value = ''; refund.disabled = false;
        } catch (error) { if (active() && sequence === refundSequence) { refundHelp.textContent = `İade kaynağı yüklenemedi: ${error.message}. İşlem türünü yeniden seçerek deneyin.`; refund.disabled = true; } }
      };
      kind.addEventListener('change', updateKind); card.addEventListener('change', invalidate); refund.addEventListener('change', invalidate);
      for (const control of [date, text, total]) control.addEventListener('input', invalidate);
      const status = h('p', { class: 'import-status' });
      const updateStatus = () => { status.textContent = `Sayfa ${source.sayfa} · ${classes[source.sinif] || source.sinif || 'Hareket'}${blocked ? ' · Zaten kaydedildi' : foreign ? ' · Bu para birimi kaydedilemez' : checked.checked ? ' · Seçili' : ' · Henüz seçilmedi'}`; };
      updateStatus();
      const node = h('article', { class: 'import-row' }, h('div', { class: 'import-row-head' }, field(`${source.no}. hareket · ${source.tarih ? dateText(source.tarih) : 'Tarih kontrol edilmeli'}`, checked), h('span', { class: 'money' }, source.tutar == null ? 'Tutar kontrol edilmeli' : `${money(source.tutar)}${foreign ? ` (${source.paraBirimi})` : ''}`)), h('strong', {}, source.aciklama || 'Açıklama kontrol edilmeli'), status, h('details', {}, h('summary', {}, 'PDF’deki kaynak satır'), h('p', { class: 'import-source-text' }, source.kaynakSatir)), warningList(source.uyarilar), fields);
      const selectionChanged = () => { fields.hidden = !checked.checked; node.classList.toggle('selected', checked.checked); updateStatus(); invalidate(); if (checked.checked) updateKind(); };
      checked.addEventListener('change', selectionChanged);
      allocation.setKind(kind.value); cardField.hidden = true; refundField.hidden = true;
      rows.push({ source, checked, date, text, total, kind, card, refund, allocation, node, selectionChanged }); list.append(node);
    }
    const read = () => {
      const selected = rows.filter(row => row.checked.checked);
      if (!selected.length) throw new Error('Kaydetmek istediğin en az bir hareketi seç.');
      return { surum: document.surum, satirlar: selected.map(row => {
        if (currentRows.has(row.source.no) || !supportedCurrency(row.source)) throw new Error('Bu kaynak satırı yeniden kaydedilemez.');
        if (!/^\d{4}-\d{2}-\d{2}$/.test(row.date.value)) throw new Error(`${row.source.no}. hareketin tarihini kontrol edin.`);
        if (!row.text.value.trim() || row.text.value.trim().length > 1000) throw new Error(`${row.source.no}. hareketin açıklamasını kontrol edin.`);
        if (!allowedKinds.includes(row.kind.value)) throw new Error(`${row.source.no}. hareketin işlem türünü seçin.`);
        const total = cents(row.total.value, { allowZero: false }) / 100;
        const cardId = document.kaynak === 'Kart' ? document.kartId : row.kind.value === 'KartOdemesi' ? Number(row.card.value) : null;
        if (row.kind.value === 'KartOdemesi' && !cardId) throw new Error('Ödemenin ait olduğu kredi kartını seçin.');
        const refundId = row.kind.value === 'KartIade' ? Number(row.refund.value) : null;
        if (row.kind.value === 'KartIade' && (!refundId || row.refund.disabled)) throw new Error('İade edilen asıl kart harcamasını seçin.');
        return { satirNo: row.source.no, tarih: row.date.value, aciklama: row.text.value.trim(), tutar: total, islemTuru: row.kind.value, ...row.allocation.read(total), krediKartiId: cardId || null, kaynakHarcamaId: refundId || null };
      }) };
    };
    const showPreview = async () => {
      editor(); if (!active()) return;
      const payload = read(); const signature = JSON.stringify(payload); const sequence = ++previewSequence;
      preview = null; previewSignature = null; previewHost.hidden = true;
      const result = await api(`${base}/${document.id}/onizleme`, { method: 'POST', body: identity(payload) });
      if (!active() || sequence !== previewSequence || JSON.stringify(read()) !== signature) return;
      preview = result; previewSignature = signature; drawPreview();
    };
    const drawPreview = () => {
      const result = preview; const duplicateApproval = input('tekrarOnay', '1', { type: 'checkbox' });
      const save = act('Onayla ve kaydet', async () => {
        editor(); if (!active() || !preview || preview !== result) return;
        const payload = read();
        if (JSON.stringify(payload) !== previewSignature) { invalidate(); throw new Error('Seçim veya bilgiler değişti. Yeniden önizleyin.'); }
        if (result.tekrarOnayGerekli && !duplicateApproval.checked) throw new Error('Uyarıları ve benzer kayıtları kontrol ettiğinizi onaylayın.');
        const request = identity({ ...payload, onizlemeOzeti: result.onizlemeOzeti, tekrarOnay: result.tekrarOnayGerekli && duplicateApproval.checked });
        try {
          await api(`${base}/${document.id}/kaydet`, { method: 'POST', body: request });
          if (!active()) return;
          toast('Seçtiğin hareketler kaydedildi.'); await navigate('imports', document.id);
        } catch (error) { if (error.status === 409 && active()) invalidate(); throw error; }
      }, 'primary');
      const cardDebt = result.satirlar.reduce((sum, row) => sum + (row.islemTuru === 'KartHarcama' ? row.tutar : row.islemTuru === 'KartIade' ? -row.tutar : 0), 0);
      previewHost.replaceChildren(h('div', { class: 'stack' }, h('h2', {}, 'Kaydetmeden önce kontrol et'), h('div', { class: 'summary-strip' }, summary('Seçilen hareket', result.satirlar.length), summary('Genel kasa değişimi', money(result.kasaEtkisi)), summary('Harcama / iade borç etkisi', money(cardDebt), 'Kart ödemeleri ayrıca borcu azaltır.')), warningList(result.uyarilar), table(['Tarih / açıklama', 'İşlem', 'Tutar', 'Kasa değişimi', 'Kanal dağılımı / uyarı'], result.satirlar.map(row => [h('div', {}, dateText(row.tarih), h('small', { class: 'table-sub' }, row.aciklama)), kinds[row.islemTuru] || row.islemTuru, moneyNode(row.tutar), moneyNode(row.kasaEtkisi), h('div', {}, shares(row.dagilimlar), warningList(row.uyarilar))])), help('Yalnız bu seçili hareketler kaydedilecek. Kart harcaması ve iadesi kasayı değiştirmez; kart ödemesi kasadan düşer. PDF’nin toplam borç veya hesap bakiyesi yeni hareket değildir.'), result.tekrarOnayGerekli && h('div', { class: 'notice' }, field('Uyarıları kontrol ettim. Benzer görünenler ayrı işlemler; belirsiz para birimli seçili tutarlar TL.', duplicateApproval)), h('div', { class: 'import-actions' }, save, button('Seçimleri düzenle', () => { invalidate(); list.scrollIntoView({ block: 'start' }); }))));
      previewHost.hidden = false; previewHost.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    };
    const history = records.length ? table(['Satır / tarih', 'İşlem', 'Tutar', 'Dağılım', 'Durum', ''], records.map(row => [h('span', {}, `#${row.satirNo} · ${dateText(row.tarih)}`, h('small', { class: 'table-sub' }, row.aciklama)), kinds[row.islemTuru] || row.islemTuru, moneyNode(row.tutar), row.dagilimTuru === 'Genel' ? 'Yalnız genel kasa' : shares(row.dagilimlar), row.iptal ? 'İptal' : 'Kaydedildi', !row.iptal && act('Kaydı iptal et', () => cancelDialog(document, row, generation, epoch), 'small danger')])) : help('Bu belgeden henüz mali kayıt oluşturulmadı.');
    view().replaceChildren(button('← Yüklenen belgelere dön', () => navigate('imports'), 'back-link'), section(document.dosyaAdi, h('div', { class: 'stack' }, h('p', { class: 'import-source-title' }, sourceName(document), document.kartId && ` · ${cards.find(card => card.id === document.kartId)?.ad || `Kart #${document.kartId}`}`), warningList(document.uyarilar), h('div', { class: 'notice' }, 'Okuma önerileri hata içerebilir. Tarih, tutar, işlem türü ve kanal dağılımını kaynak satırla karşılaştır. Tüm satırlar başlangıçta seçimsizdir.'), h('a', { class: 'button', href: `${base}/${document.id}/dosya`, download: document.dosyaAdi }, 'Kaynak PDF’yi indir'))), section('Okunan hareketler', h('div', { class: 'stack' }, filter, rows.length ? list : help('Kaydedilebilir hareket bulunamadı. Belge uyarılarını kontrol edin.'))), h('div', { class: 'import-toolbar' }, selectedCount, button('Seçimleri temizle', () => { for (const row of rows) { if (row.checked.checked) { row.checked.checked = false; row.selectionChanged(); } } invalidate(); }), act('Seçilenleri önizle', showPreview, 'primary')), previewHost, section('Bu belgeden kaydedilenler', history));
  }

  function cancelDialog(document, row, generation, epoch) {
    editor(); const identity = requestIdentity(); const reason = input('aciklama', '', { required: true, maxlength: 2000 });
    formDialog('İçe aktarılan kaydı iptal et', h('div', { class: 'stack' }, help(`${dateText(row.tarih)} · ${row.aciklama} · ${money(row.tutar)}. Bu kaydın kasa veya kart etkisi geri alınır; kaynak PDF ve iptal geçmişi korunur.`), field('İptal açıklaması', reason)), 'Kaydı iptal et', async form => {
      editor(); if (!stillHere(generation, epoch) || !isOpen(form)) return;
      if (!reason.value.trim()) throw new Error('İptal açıklamasını yazın.');
      await api(`${base}/${document.id}/kayitlar/${row.id}/iptal`, { method: 'POST', body: identity({ aciklama: reason.value.trim() }) });
      if (!stillHere(generation, epoch) || !isOpen(form)) return;
      closeModal(); toast('Kayıt iptal edildi; kaynak ve geçmiş korundu.'); await navigate('imports', document.id);
    }, { danger: true });
  }
  return { render, uploadDialog };
}
