import { cents, dateText, money, sumCents, isAbortError } from './ui-core.js';
import { $, h, button, input, field, select, help, section, table, moneyNode, allocationTags, summary, distribution } from './ui-dom.js';
import {
  state,
  api,
  formDialog,
  closeModal,
  page,
  navigate,
  run,
  act,
  toast,
  requestIdentity,
  isOpen,
  isCurrent,
  canEditCash as canEdit,
  requireEditor,
} from './ui-shell.js';

export function createStatementImportUi() {
  const view = () => $('#view');
  const base = '/api/ekstre-aktar';
  // Bankalar tek kaynaktan gelir: sunucu (GET /api/ekstre-aktar/bankalar; kod ve görünen ad). İstemcide kopya ya da yedek liste
  // yoktur; liste alınamazsa ekran ve yükleme penceresi nedenini söyleyen hatayla durur.
  let banks = [];
  async function loadBanks() {
    let list;
    try {
      list = await api(`${base}/bankalar`);
    } catch (error) {
      if (isAbortError(error)) throw error;
      throw Object.assign(new Error(`Banka listesi alınamadı: ${error.message}`), { status: error.status });
    }
    if (!Array.isArray(list) || !list.length) throw new Error('Banka listesi alınamadı: sunucu boş liste gönderdi.');
    banks = list;
    return list;
  }
  const kinds = {
    Gelir: 'Banka girişi',
    Gider: 'Banka çıkışı',
    KartHarcama: 'Kart harcaması / faiz / masraf',
    KartIade: 'Karta iade',
    KartOdemesi: 'Kart borcu ödemesi',
    Eslestir: 'Mevcut kayıtla eşleştir',
  };
  // Eşleştirme (gap-coklu-giris-cift-sayim-mutabakat-1): satır yeni kayıt üretmez, mevcut kayda bağlanır; kasa ve kart borcu değişmez.
  const matchKinds = { Gider: 'Gider', KartHarcama: 'Kart harcaması', KartTaksidi: 'Kart taksidi', KartOdeme: 'Kart ödemesi' };
  const matchName = (type, id) => `${matchKinds[type] || 'Kayıt'} #${id}`;
  const candidateLabel = item =>
    [
      matchName(item.tur, item.id),
      dateText(item.tarih),
      money(item.tutar),
      item.aciklama,
      item.kanalEtiketi,
      item.alisId && `Alış #${item.alisId}`,
      item.ekstreKayitId && 'ekstreden',
    ]
      .filter(Boolean)
      .join(' · ');
  // Kaydedilmiş satırın durumu: eşleştirme satırı bağlandığı kaydı, kaydı alış ödemesine bağlanmış satır bağlanan kaydı gösterir.
  const recordState = row =>
    row.islemTuru === 'Eslestir'
      ? row.eslesmeDurumu === 'KayitYok'
        ? `Eşleştiği kayıt (${matchName(row.eslesmeTuru, row.eslesmeId)}) silinmiş ya da iptal edilmiş`
        : `Mevcut kayıtla eşleşti (${matchName(row.eslesmeTuru, row.eslesmeId)})`
      : row.eslesmeTuru
        ? `Kaydı alış ödemesine bağlandı (${matchName(row.eslesmeTuru, row.eslesmeId)})`
        : 'Kaydedildi';
  const classes = {
    Faiz: 'Faiz',
    Komisyon: 'Komisyon',
    Vergi: 'Vergi',
    Ucret: 'Ücret',
    Transfer: 'Transfer',
    Odeme: 'Ödeme',
    Hareket: 'Hareket',
    Belirsiz: 'Kontrol edilmeli',
  };
  const editor = () => requireEditor('Ekstre yüklemek ve işlemek için editör hesabı gerekir.');
  // İptal edilen kaydın gerekçesi ve anı (sunucu anı denetim izinden okur; sürüm öncesi iptalin anı bilinmez).
  const cancelNote = row =>
    [row.iptalAciklamasi, row.iptalZamani ? new Date(row.iptalZamani).toLocaleString('tr-TR') : 'zamanı bilinmiyor']
      .filter(Boolean)
      .join(' · ');
  const bankName = bank => banks.find(item => item.kod === bank)?.ad || bank;
  const sourceName = row =>
    `${bankName(row.banka)} · ${row.kaynak === 'Kart' ? `Kart ekstresi · ${row.hesapAdi || `Kart #${row.kartId}`}` : row.hesapAdi || 'Banka hareketi'}`;
  const shares = rows => allocationTags(rows, { empty: 'Genel kasa' });
  const warningList = warnings =>
    warnings?.length
      ? h(
          'ul',
          { class: 'similar-records' },
          warnings.map(text => h('li', {}, text))
        )
      : null;
  const supportedCurrency = row => !row.paraBirimi || ['TRY', 'TL', 'Belirsiz'].includes(row.paraBirimi);
  const session = () => state.epoch;
  const stillHere = (generation, epoch) => canEdit() && isCurrent(generation) && session() === epoch;

  async function render(generation, id = null) {
    editor();
    const epoch = session();
    const recordId = Number.isSafeInteger(id?.kayitId) && id.kayitId > 0 ? id.kayitId : null;
    const beforeId = Number.isSafeInteger(id?.beforeId) && id.beforeId > 0 ? id.beforeId : null;
    const documentId = Number.isSafeInteger(id) && id > 0 ? id : null;
    if (id != null && !recordId && !beforeId && !documentId) throw new Error('Geçerli bir belge veya kaynak kayıt seçin.');
    const documentPath = recordId ? `${base}/kayitlar/${recordId}` : documentId ? `${base}/${documentId}` : null;
    page('Ekstre / Hareket Yükle', 'PDF satırlarını seç, kontrol et ve kaydet', [
      ...(documentPath ? [act('Belgeyi yenile', () => navigate('imports', id))] : []),
      act('+ PDF yükle', () => uploadDialog(generation), 'primary'),
    ]);
    if (documentPath) {
      const [document, channels, cards] = await Promise.all([
        api(documentPath),
        api('/api/kanallar'),
        api('/api/takip/kartlar'),
        loadBanks(),
      ]);
      if (!stillHere(generation, epoch)) return;
      renderDocument(document, channels, cards, generation, epoch);
      return;
    }
    const [documents] = await Promise.all([api(beforeId ? `${base}?beforeId=${beforeId}` : base), loadBanks()]);
    if (!stillHere(generation, epoch)) return;
    view().replaceChildren(
      h(
        'div',
        { class: 'notice' },
        'PDF yüklemek kayıt oluşturmaz. Okunan hareketler seçimsiz gelir; yalnız seçip onayladığın satırlar kaydedilir.'
      ),
      section(
        'PDF yükle',
        h(
          'div',
          { class: 'stack' },
          help(
            'Kart ekstresinden harcama, faiz, komisyon ve ödemeleri; banka hesap hareketinden giriş ve çıkışları inceleyebilirsin. Kanal bilgisi PDF’de bulunmaz; kaydetmeden önce sen seçersin.'
          ),
          h('p', { class: 'import-bank-hint' }, banks.map(bank => bank.ad).join(' · ')),
          act('Kart ekstresi / hesap hareketi seç', () => uploadDialog(generation), 'primary')
        )
      ),
      section(
        'Yüklenen belgeler',
        h(
          'div',
          { class: 'stack' },
          documents.length
            ? table(
                ['Belge', 'Kaynak', 'Yüklendi', 'Okunan satır', 'Kayıt', ''],
                documents.map(document => [
                  h('span', { class: 'import-source-title' }, document.dosyaAdi),
                  sourceName(document),
                  new Date(document.yuklendi).toLocaleString('tr-TR'),
                  document.satirSayisi,
                  document.kayitSayisi,
                  act('Aç ve incele', () => navigate('imports', document.id), 'small'),
                ]),
                'Yüklenen belgeler'
              )
            : help(beforeId ? 'Daha eski belge yok.' : 'Henüz PDF yüklenmedi.'),
          h(
            'div',
            { class: 'row-actions' },
            beforeId && act('En yeni belgelere dön', () => navigate('imports')),
            documents.length === 50 &&
              act('Daha eski belgeler', () => navigate('imports', { beforeId: documents[documents.length - 1].id }))
          )
        )
      )
    );
  }

  async function uploadDialog(generation) {
    editor();
    const epoch = session();
    const [cards, bankList] = await Promise.all([api('/api/takip/kartlar'), loadBanks()]);
    if (!stillHere(generation, epoch)) return;
    const file = input('dosya', '', { type: 'file', accept: '.pdf,application/pdf', required: true });
    const source = select(
      'kaynak',
      [
        { value: 'Kart', label: 'Kredi kartı ekstresi' },
        { value: 'Banka', label: 'Banka hesap hareketi' },
      ],
      'Kart'
    );
    const bankOptions = bankList.map(item => ({ value: item.kod, label: item.ad }));
    const bank = select('banka', [{ value: '', label: 'Bankayı seç' }, ...bankOptions], '', { required: true });
    const card = select(
      'kartId',
      [
        { value: '', label: 'Uygulamadaki kartı seç' },
        ...cards
          .filter(card => card.yeniTakip)
          .map(card => ({ value: card.id, label: card.ad + (card.aktif ? '' : ' · Yeni kullanıma kapalı') })),
      ],
      '',
      { required: true }
    );
    const account = input('hesapAdi', '', { maxlength: 100, placeholder: 'Örn. İşletme hesabı / son 4 hane' });
    const cardField = field('Kart', card);
    const accountField = field('Hesap kısa adı', account);
    const changeSource = () => {
      cardField.hidden = source.value !== 'Kart';
      card.disabled = source.value !== 'Kart';
      card.required = source.value === 'Kart';
      accountField.hidden = source.value !== 'Banka';
      account.disabled = source.value !== 'Banka';
      account.required = source.value === 'Banka';
    };
    source.addEventListener('change', changeSource);
    changeSource();
    formDialog(
      'Ekstre / hareket PDF’si yükle',
      h(
        'div',
        { class: 'stack' },
        h(
          'div',
          { class: 'import-source' },
          field('Belge türü', source),
          field('Banka', bank),
          cardField,
          accountField,
          field('PDF dosyası', file)
        ),
        help(
          'En fazla 10 MB ve 50 sayfa. Metin içeren, şifresiz PDF yükle. Taranmış görüntüler ve yabancı para hareketleri bu sürümde kaydedilemez.'
        ),
        help(
          'PDF uygulamanın sunucusunda tutulur. Okuma tamamlanınca tüm hareketleri kontrol ederek seçersin; yükleme kasayı veya kart borcunu değiştirmez.'
        )
      ),
      'PDF’yi oku',
      async form => {
        editor();
        if (!stillHere(generation, epoch) || !isOpen(form)) return;
        const selected = file.files?.[0];
        if (
          !selected ||
          !selected.size ||
          selected.size > 10 * 1024 * 1024 ||
          !/\.pdf$/i.test(selected.name) ||
          (selected.type && selected.type !== 'application/pdf')
        )
          throw new Error('Boş olmayan, en fazla 10 MB boyutunda bir PDF dosyası seçin.');
        if (!bankList.some(item => item.kod === bank.value)) throw new Error('Bankayı seçin.');
        if (source.value === 'Kart' && !Number(card.value)) throw new Error('Ekstrenin ait olduğu kartı seçin.');
        if (source.value === 'Banka' && !account.value.trim()) throw new Error('Hesaba kısa bir ad verin.');
        const payload = new FormData();
        payload.append('dosya', selected);
        payload.append('kaynak', source.value);
        payload.append('banka', bank.value);
        if (source.value === 'Kart') payload.append('kartId', card.value);
        else payload.append('hesapAdi', account.value.trim());
        const result = await api(`${base}/yukle`, { method: 'POST', body: payload });
        if (!stillHere(generation, epoch) || !isOpen(form)) return;
        closeModal();
        toast('PDF okundu. Kaydetmek istediğin satırları seç.');
        await navigate('imports', result.id);
      },
      { wide: true }
    );
  }

  // Ekstre satırının kanal dağılımı: ortak dağıtım düzenleyicisi (distribution) bu ekranın adları, iletileri ve seçenekleriyle.
  // Seçenekler işlem türüne göre değişir (setKind); kart ödemesi ve iadesinde dağılım otomatiktir. Paylar kanal listesi
  // sırasıyla gider, gizli liste boşaltılır, dağılım değişikliği önizlemeyi geçersiz kılar (changed).
  function allocationEditor(channels, changed) {
    const allocation = distribution(channels, null, {
      prefix: 'pay',
      choices: [],
      required: false,
      listClass: null,
      emptyHidden: true,
      sortById: false,
      onChange: changed,
      legend: 'Kanal dağılımı',
      label: 'Hangi kasa / kanallar?',
      note: null,
      messages: {
        mode: 'Seçili hareketin kasa / kanal dağılımını seçin.',
        channel: 'Seçili hareket için en az bir kanal seçin.',
        sum: 'Kanal paylarının toplamı hareket tutarına eşit olmalı.',
      },
    });
    const { mode } = allocation;
    return {
      node: allocation.node,
      setKind(kind) {
        const automatic = ['KartOdemesi', 'KartIade'].includes(kind);
        const options = automatic
          ? [{ value: 'Otomatik', label: 'İlgili kart hareketlerinden otomatik' }]
          : [
              { value: '', label: 'Dağılım seç' },
              ...(['Gelir', 'Gider'].includes(kind) ? [{ value: 'Genel', label: 'Yalnız genel kasa' }] : []),
              { value: 'Esit', label: 'Seçilen kanallara eşit' },
              { value: 'Ozel', label: 'Kanal tutarlarını gir' },
            ];
        const previous = mode.value;
        mode.replaceChildren(...options.map(option => h('option', { value: option.value }, option.label)));
        mode.value = automatic ? 'Otomatik' : options.some(option => option.value === previous) ? previous : '';
        mode.disabled = automatic;
        allocation.redraw();
      },
      read(total) {
        if (mode.value === 'Otomatik') return { dagilimTuru: 'Otomatik', dagilimlar: [] };
        return allocation.read(total);
      },
    };
  }

  function renderDocument(document, channels, cards, generation, epoch) {
    const records = document.kayitlar || [];
    const currentRows = new Map(records.filter(row => !row.iptal).map(row => [row.satirNo, row]));
    const identity = requestIdentity();
    const allowedKinds =
      document.kaynak === 'Kart' ? ['KartHarcama', 'KartIade', 'KartOdemesi', 'Eslestir'] : ['Gelir', 'Gider', 'KartOdemesi', 'Eslestir'];
    const previewHost = h('div', { class: 'import-preview', hidden: true });
    const selectedCount = h('strong', {}, '0 satır seçili');
    const list = h('div', { class: 'import-rows' });
    let preview = null;
    let previewSignature = null;
    let previewSequence = 0;
    const rows = [];
    const active = () => stillHere(generation, epoch);
    const invalidate = () => {
      preview = null;
      previewSignature = null;
      previewSequence++;
      previewHost.hidden = true;
      selectedCount.textContent = `${rows.filter(row => row.checked.checked).length} satır seçili`;
    };
    const filter = input('satir-ara', '', {
      placeholder: 'Açıklama, faiz, komisyon veya tutar ara',
      'aria-label': 'Okunan hareketlerde ara',
      type: 'search',
    });
    filter.addEventListener('input', () => {
      const term = filter.value.toLocaleLowerCase('tr-TR');
      for (const row of rows)
        row.node.hidden = !`${row.source.kaynakSatir} ${row.source.aciklama} ${row.source.tutar ?? ''} ${classes[row.source.sinif] || ''}`
          .toLocaleLowerCase('tr-TR')
          .includes(term);
    });
    for (const source of document.satirlar || []) {
      const blocked = currentRows.has(source.no);
      const foreign = !supportedCurrency(source);
      const checked = input(`sec-${source.no}`, '1', {
        type: 'checkbox',
        disabled: blocked || foreign,
        'aria-label': `${source.no}. hareketi seç`,
      });
      const date = input(`tarih-${source.no}`, source.tarih || '', { type: 'date', required: true });
      const text = input(`aciklama-${source.no}`, source.aciklama || '', { required: true, maxlength: 1000 });
      const total = input(`tutar-${source.no}`, source.tutar ?? '', { required: true, inputmode: 'decimal' });
      const kind = select(
        `tur-${source.no}`,
        [{ value: '', label: 'İşlem türünü seç' }, ...allowedKinds.map(value => ({ value, label: kinds[value] }))],
        allowedKinds.includes(source.onerilenIslem) ? source.onerilenIslem : '',
        { required: true }
      );
      const card = select(
        `kart-${source.no}`,
        [
          { value: '', label: 'Ödenen kartı seç' },
          ...cards
            .filter(card => card.yeniTakip)
            .map(card => ({ value: card.id, label: card.ad + (card.aktif ? '' : ' · Yeni kullanıma kapalı') })),
        ],
        document.kartId || ''
      );
      const cardField = field('Kredi kartı', card);
      const refund = select(`iade-kaynak-${source.no}`, [{ value: '', label: 'Önce kartı ve iade edilen harcamayı seç' }], '');
      const refundField = field('İade edilen kart harcaması', refund);
      let refundSequence = 0;
      const refundHelp = h('p', { class: 'help' });
      const match = select(`eslesme-${source.no}`, [{ value: '', label: 'Önce eşleşme adaylarını getir' }], '');
      const matchField = field('Eşleşecek mevcut kayıt', match);
      let matchSequence = 0;
      const allocation = allocationEditor(channels, invalidate);
      const fields = h(
        'div',
        { hidden: true },
        h(
          'div',
          { class: 'form-grid' },
          field('İşlem tarihi', date),
          field('Tutar (₺)', total),
          field('Açıklama', text),
          field('Kaydedilecek işlem', kind),
          cardField,
          refundField,
          matchField
        ),
        refundHelp,
        allocation.node
      );
      // Adaylar satırın (düzenlenmiş) tarih ve tutarıyla aranır: aynı tutar, en çok 3 gün farklı tarih, belge türüne uygun kayıtlar.
      const loadMatches = async () => {
        const sequence = ++matchSequence;
        match.disabled = true;
        match.value = '';
        let amountValue = null;
        try {
          amountValue = cents(total.value, { allowZero: false }) / 100;
        } catch {
          amountValue = null;
        }
        if (!/^\d{4}-\d{2}-\d{2}$/.test(date.value) || amountValue == null) {
          match.replaceChildren(h('option', { value: '' }, 'Önce tarihi ve tutarı düzelt'));
          refundHelp.textContent = 'Eşleşme adayları satırın tarihi ve tutarıyla aranır.';
          return;
        }
        match.replaceChildren(h('option', { value: '' }, 'Adaylar yükleniyor…'));
        try {
          const list = await api(`${base}/${document.id}/eslesme-adaylari`, {
            method: 'POST',
            body: { tarih: date.value, tutar: amountValue },
          });
          if (!active() || sequence !== matchSequence || kind.value !== 'Eslestir') return;
          const items = Array.isArray(list) ? list : [];
          match.replaceChildren(
            h('option', { value: '' }, items.length ? 'Eşleşecek kaydı seç' : 'Eşleşecek kayıt yok'),
            ...items.map(item => h('option', { value: `${item.tur}:${item.id}` }, candidateLabel(item)))
          );
          match.value = '';
          match.disabled = !items.length;
          refundHelp.textContent = items.length
            ? 'Bu satır yeni kayıt oluşturmaz; seçtiğin mevcut kayda bağlanır. Kasa ve kart borcu değişmez, iptali de hiçbir kaydı değiştirmez.'
            : 'Bu tutarda, en çok 3 gün farklı tarihte eşleştirilebilecek kayıt yok. Satırı seçmeden bırakabilirsin.';
        } catch (error) {
          if (active() && sequence === matchSequence) {
            refundHelp.textContent = `Eşleşme adayları yüklenemedi: ${error.message}. İşlem türünü yeniden seçerek deneyin.`;
            match.disabled = true;
          }
        }
      };
      const updateKind = async () => {
        invalidate();
        const sequence = ++refundSequence;
        cardField.hidden = document.kaynak !== 'Banka' || kind.value !== 'KartOdemesi';
        refundField.hidden = kind.value !== 'KartIade';
        matchField.hidden = kind.value !== 'Eslestir';
        allocation.setKind(kind.value);
        allocation.node.hidden = kind.value === 'Eslestir';
        refundHelp.textContent =
          kind.value === 'KartOdemesi'
            ? 'Bu satır gerçek kart ödemesi olarak kasadan düşer. Aynı ödeme daha önce girildiyse tekrar seçme; mevcut ödemeyle eşleştir.'
            : kind.value === 'KartIade'
              ? 'İade kart borcunu azaltır; nakit girişi oluşturmaz. İade edilen asıl harcamayı seç.'
              : kind.value === 'KartHarcama'
                ? 'Kart borcuna eklenir; bu satır kasadan düşmez. Faiz ve komisyonun kaynak kanallarını da sen seçersin. Harcama zaten kayıtlıysa (ör. kartlı alış ödemesi) mevcut kayıtla eşleştir.'
                : '';
        if (kind.value === 'Eslestir') {
          await loadMatches();
          return;
        }
        matchSequence++;
        if (kind.value !== 'KartIade') return;
        refund.disabled = true;
        refund.replaceChildren(h('option', { value: '' }, 'Harcamalar yükleniyor…'));
        refund.value = '';
        try {
          const detail = await api(`/api/takip/kartlar/${document.kartId}`);
          if (!active() || sequence !== refundSequence || kind.value !== 'KartIade') return;
          refund.replaceChildren(
            h('option', { value: '' }, 'İade edilen harcamayı seç'),
            ...(detail.harcamalar || [])
              .filter(charge => !charge.iptal && charge.tutar > 0)
              .map(charge =>
                h('option', { value: charge.id }, `#${charge.id} · ${dateText(charge.tarih)} · ${money(charge.tutar)} · ${charge.aciklama}`)
              )
          );
          refund.value = '';
          refund.disabled = false;
        } catch (error) {
          if (active() && sequence === refundSequence) {
            refundHelp.textContent = `İade kaynağı yüklenemedi: ${error.message}. İşlem türünü yeniden seçerek deneyin.`;
            refund.disabled = true;
          }
        }
      };
      kind.addEventListener('change', updateKind);
      card.addEventListener('change', invalidate);
      refund.addEventListener('change', invalidate);
      match.addEventListener('change', invalidate);
      for (const control of [date, text, total]) control.addEventListener('input', invalidate);
      // Tarih ya da tutar değişince eski adaylar geçersizdir: eşleştirmede yeniden aranır.
      for (const control of [date, total])
        control.addEventListener('change', () => {
          if (kind.value === 'Eslestir') run(null, loadMatches);
        });
      const status = h('p', { class: 'import-status' });
      const saved = currentRows.get(source.no);
      const updateStatus = () => {
        status.textContent = `Sayfa ${source.sayfa} · ${classes[source.sinif] || source.sinif || 'Hareket'}${blocked ? ` · ${saved.islemTuru === 'Eslestir' || saved.eslesmeTuru ? recordState(saved) : 'Zaten kaydedildi'}` : foreign ? ' · Bu para birimi kaydedilemez' : checked.checked ? ' · Seçili' : ' · Henüz seçilmedi'}`;
      };
      updateStatus();
      const node = h(
        'article',
        { class: 'import-row' },
        h(
          'div',
          { class: 'import-row-head' },
          field(`${source.no}. hareket · ${source.tarih ? dateText(source.tarih) : 'Tarih kontrol edilmeli'}`, checked),
          h(
            'span',
            { class: 'money' },
            source.tutar == null ? 'Tutar kontrol edilmeli' : `${money(source.tutar)}${foreign ? ` (${source.paraBirimi})` : ''}`
          )
        ),
        h('strong', {}, source.aciklama || 'Açıklama kontrol edilmeli'),
        status,
        h('details', {}, h('summary', {}, 'PDF’deki kaynak satır'), h('p', { class: 'import-source-text' }, source.kaynakSatir)),
        warningList(source.uyarilar),
        fields
      );
      const selectionChanged = () => {
        fields.hidden = !checked.checked;
        node.classList.toggle('selected', checked.checked);
        updateStatus();
        invalidate();
        if (checked.checked) updateKind();
      };
      checked.addEventListener('change', selectionChanged);
      allocation.setKind(kind.value);
      cardField.hidden = true;
      refundField.hidden = true;
      matchField.hidden = true;
      rows.push({ source, checked, date, text, total, kind, card, refund, match, allocation, node, selectionChanged });
      list.append(node);
    }
    const read = () => {
      const selected = rows.filter(row => row.checked.checked);
      if (!selected.length) throw new Error('Kaydetmek istediğin en az bir hareketi seç.');
      return {
        surum: document.surum,
        satirlar: selected.map(row => {
          if (currentRows.has(row.source.no) || !supportedCurrency(row.source)) throw new Error('Bu kaynak satırı yeniden kaydedilemez.');
          if (!/^\d{4}-\d{2}-\d{2}$/.test(row.date.value)) throw new Error(`${row.source.no}. hareketin tarihini kontrol edin.`);
          if (!row.text.value.trim() || row.text.value.trim().length > 1000)
            throw new Error(`${row.source.no}. hareketin açıklamasını kontrol edin.`);
          if (!allowedKinds.includes(row.kind.value)) throw new Error(`${row.source.no}. hareketin işlem türünü seçin.`);
          const total = cents(row.total.value, { allowZero: false }) / 100;
          if (row.kind.value === 'Eslestir') {
            const [matchType, matchId] = row.match.value.split(':');
            if (!matchKinds[matchType] || !Number(matchId) || row.match.disabled)
              throw new Error(`${row.source.no}. hareketin eşleşeceği mevcut kaydı seçin.`);
            return {
              satirNo: row.source.no,
              tarih: row.date.value,
              aciklama: row.text.value.trim(),
              tutar: total,
              islemTuru: 'Eslestir',
              dagilimTuru: 'Eslesme',
              dagilimlar: [],
              krediKartiId: null,
              kaynakHarcamaId: null,
              eslesenKayitTuru: matchType,
              eslesenKayitId: Number(matchId),
            };
          }
          const cardId = document.kaynak === 'Kart' ? document.kartId : row.kind.value === 'KartOdemesi' ? Number(row.card.value) : null;
          if (row.kind.value === 'KartOdemesi' && !cardId) throw new Error('Ödemenin ait olduğu kredi kartını seçin.');
          const refundId = row.kind.value === 'KartIade' ? Number(row.refund.value) : null;
          if (row.kind.value === 'KartIade' && (!refundId || row.refund.disabled))
            throw new Error('İade edilen asıl kart harcamasını seçin.');
          return {
            satirNo: row.source.no,
            tarih: row.date.value,
            aciklama: row.text.value.trim(),
            tutar: total,
            islemTuru: row.kind.value,
            ...row.allocation.read(total),
            krediKartiId: cardId || null,
            kaynakHarcamaId: refundId || null,
          };
        }),
      };
    };
    const showPreview = async () => {
      editor();
      if (!active()) return;
      const payload = read();
      const signature = JSON.stringify(payload);
      const sequence = ++previewSequence;
      preview = null;
      previewSignature = null;
      previewHost.hidden = true;
      const result = await api(`${base}/${document.id}/onizleme`, { method: 'POST', body: identity(payload) });
      if (!active() || sequence !== previewSequence || JSON.stringify(read()) !== signature) return;
      preview = result;
      previewSignature = signature;
      drawPreview();
    };
    const drawPreview = () => {
      const result = preview;
      const duplicateApproval = input('tekrarOnay', '1', { type: 'checkbox' });
      const save = act(
        'Onayla ve kaydet',
        async () => {
          editor();
          if (!active() || !preview || preview !== result) return;
          const payload = read();
          if (JSON.stringify(payload) !== previewSignature) {
            invalidate();
            throw new Error('Seçim veya bilgiler değişti. Yeniden önizleyin.');
          }
          if (result.tekrarOnayGerekli && !duplicateApproval.checked)
            throw new Error('Uyarıları ve benzer kayıtları kontrol ettiğinizi onaylayın.');
          const request = identity({
            ...payload,
            onizlemeOzeti: result.onizlemeOzeti,
            tekrarOnay: result.tekrarOnayGerekli && duplicateApproval.checked,
          });
          try {
            await api(`${base}/${document.id}/kaydet`, { method: 'POST', body: request });
            if (!active()) return;
            toast('Seçtiğin hareketler kaydedildi.');
            await navigate('imports', document.id);
          } catch (error) {
            if (error.status === 409 && active()) invalidate();
            throw error;
          }
        },
        'primary'
      );
      const cardDebt =
        sumCents(
          result.satirlar.map(row => (row.islemTuru === 'KartHarcama' ? row.tutar : row.islemTuru === 'KartIade' ? -row.tutar : 0))
        ) / 100;
      previewHost.replaceChildren(
        h(
          'div',
          { class: 'stack' },
          h('h2', {}, 'Kaydetmeden önce kontrol et'),
          h(
            'div',
            { class: 'summary-strip' },
            summary('Seçilen hareket', result.satirlar.length),
            summary('Genel kasa değişimi', money(result.kasaEtkisi)),
            summary('Harcama / iade borç etkisi', money(cardDebt), 'Kart ödemeleri ayrıca borcu azaltır.')
          ),
          warningList(result.uyarilar),
          table(
            ['Tarih / açıklama', 'İşlem', 'Tutar', 'Kasa değişimi', 'Kanal dağılımı / uyarı'],
            result.satirlar.map(row => [
              h('div', {}, dateText(row.tarih), h('small', { class: 'table-sub' }, row.aciklama)),
              kinds[row.islemTuru] || row.islemTuru,
              moneyNode(row.tutar),
              moneyNode(row.kasaEtkisi),
              h(
                'div',
                {},
                row.islemTuru === 'Eslestir' ? help('Mevcut kayıtla eşleşir; kasa ve kart borcu değişmez.') : shares(row.dagilimlar),
                warningList(row.uyarilar)
              ),
            ]),
            'Kaydedilecek hareketler'
          ),
          help(
            'Yalnız bu seçili hareketler kaydedilecek. Kart harcaması ve iadesi kasayı değiştirmez; kart ödemesi kasadan düşer; mevcut kayıtla eşleştirme hiçbir kaydı değiştirmez. PDF’nin toplam borç veya hesap bakiyesi yeni hareket değildir.'
          ),
          result.tekrarOnayGerekli &&
            h(
              'div',
              { class: 'notice' },
              field(
                'Uyarıları kontrol ettim. Benzer görünenler ayrı işlemler; belirsiz para birimli seçili tutarlar TL.',
                duplicateApproval
              )
            ),
          h(
            'div',
            { class: 'import-actions' },
            save,
            button('Seçimleri düzenle', () => {
              invalidate();
              list.scrollIntoView({ block: 'start' });
            })
          )
        )
      );
      previewHost.hidden = false;
      previewHost.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    };
    const history = records.length
      ? table(
          ['Satır / tarih', 'İşlem', 'Tutar', 'Dağılım', 'Durum', ''],
          records.map(row => [
            h('span', {}, `#${row.satirNo} · ${dateText(row.tarih)}`, h('small', { class: 'table-sub' }, row.aciklama)),
            kinds[row.islemTuru] || row.islemTuru,
            moneyNode(row.tutar),
            row.islemTuru === 'Eslestir' ? 'Kasa etkisi yok' : row.dagilimTuru === 'Genel' ? 'Yalnız genel kasa' : shares(row.dagilimlar),
            row.iptal ? h('span', {}, 'İptal', h('small', { class: 'table-sub' }, cancelNote(row))) : recordState(row),
            !row.iptal && act('Kaydı iptal et', () => cancelDialog(document, row, generation, epoch), 'small danger'),
          ]),
          'Bu belgeden kaydedilenler'
        )
      : help('Bu belgeden henüz mali kayıt oluşturulmadı.');
    view().replaceChildren(
      button('← Yüklenen belgelere dön', () => navigate('imports'), 'back-link'),
      section(
        document.dosyaAdi,
        h(
          'div',
          { class: 'stack' },
          h(
            'p',
            { class: 'import-source-title' },
            sourceName(document),
            document.kartId && ` · ${cards.find(card => card.id === document.kartId)?.ad || `Kart #${document.kartId}`}`
          ),
          warningList(document.uyarilar),
          h(
            'div',
            { class: 'notice' },
            'Okuma önerileri hata içerebilir. Tarih, tutar, işlem türü ve kanal dağılımını kaynak satırla karşılaştır. Tüm satırlar başlangıçta seçimsizdir.'
          ),
          h('a', { class: 'button', href: `${base}/${document.id}/dosya`, download: document.dosyaAdi }, 'Kaynak PDF’yi indir')
        )
      ),
      section(
        'Okunan hareketler',
        h(
          'div',
          { class: 'stack' },
          filter,
          rows.length ? list : help('Kaydedilebilir hareket bulunamadı. Belge uyarılarını kontrol edin.')
        )
      ),
      h(
        'div',
        { class: 'import-toolbar' },
        selectedCount,
        button('Seçimleri temizle', () => {
          for (const row of rows) {
            if (row.checked.checked) {
              row.checked.checked = false;
              row.selectionChanged();
            }
          }
          invalidate();
        }),
        act('Seçilenleri önizle', showPreview, 'primary')
      ),
      previewHost,
      section('Bu belgeden kaydedilenler', history)
    );
  }

  function cancelDialog(document, row, generation, epoch) {
    editor();
    const identity = requestIdentity();
    const reason = input('aciklama', '', { required: true, maxlength: 2000 });
    formDialog(
      'İçe aktarılan kaydı iptal et',
      h(
        'div',
        { class: 'stack' },
        help(
          `${dateText(row.tarih)} · ${row.aciklama} · ${money(row.tutar)}. ${row.islemTuru === 'Eslestir' ? 'Yalnız mevcut kayıtla bağı kaldırılır; kasa ve kart borcu değişmez.' : 'Bu kaydın kasa veya kart etkisi geri alınır.'} Kaynak PDF korunur, iptal gerekçesi bu belgenin kayıt listesinde görünür.`
        ),
        field('İptal açıklaması', reason)
      ),
      'Kaydı iptal et',
      async form => {
        editor();
        if (!stillHere(generation, epoch) || !isOpen(form)) return;
        if (!reason.value.trim()) throw new Error('İptal açıklamasını yazın.');
        await api(`${base}/${document.id}/kayitlar/${row.id}/iptal`, { method: 'POST', body: identity({ aciklama: reason.value.trim() }) });
        if (!stillHere(generation, epoch) || !isOpen(form)) return;
        closeModal();
        toast('Kayıt iptal edildi; kaynak ve geçmiş korundu.');
        await navigate('imports', document.id);
      },
      { danger: true }
    );
  }
  return { render, uploadDialog };
}
