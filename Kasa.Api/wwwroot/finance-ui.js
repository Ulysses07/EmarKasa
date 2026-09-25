export function createFinanceUi(c) {
  const { api, h, button, input, field, select, help, section, table, money, moneyNode, dateText, today, cents, amount, signedAmount, formDialog, closeModal, page, navigate, run, toast, summary, childValues, requestIdentity, confirmSimilar, canEdit, isCurrent, view } = c;
  const base = '/api/takip';
  const activeChannels = channels => channels.filter(channel => channel.aktif);
  const shares = rows => h('div', { class: 'allocation-tags' }, (rows || []).map(row => h('span', { class: `allocation-tag${row.kanalId == null ? ' pending' : ''}` }, `${row.kanal || 'Dağılım bekliyor'}: ${money(row.tutar)}`)));
  const act = (label, work, style = '') => button(label, event => run(event.currentTarget, work), style);
  const integer = value => { const number = Number(value); if (!Number.isInteger(number)) throw new Error('Adet ve gün alanlarına tam sayı girin.'); return number; };

  function allocationEditor(channels, initial = []) {
    const rows = initial.map(row => ({ kanalId: row.kanalId, tutar: row.tutar }));
    const list = h('div');
    const draw = () => list.replaceChildren(...rows.map((row, index) => h('div', { class: 'allocation-row' },
      field('Kanal', select(`pay-kanal-${index}`, [{ value: '', label: 'Kanal seçin' }, ...channels.filter(channel => channel.aktif || channel.id === Number(row.kanalId)).map(channel => ({ value: channel.id, label: channel.ad }))], row.kanalId, { required: true, onchange: event => { row.kanalId = Number(event.target.value); } })),
      field('Pay (₺)', input(`pay-tutar-${index}`, row.tutar, { inputmode: 'decimal', required: true, oninput: event => { row.tutar = event.target.value; } })),
      button('×', () => { rows.splice(index, 1); draw(); }, 'icon-button', { 'aria-label': `${index + 1}. kanal payını kaldır` }))));
    draw();
    return {
      node: h('fieldset', {}, h('legend', {}, 'Kanal dağılımı'), list, button('+ Kanal payı', () => { rows.push({ kanalId: '', tutar: '' }); draw(); }, 'small'), help('Kanal bilinmiyorsa boş bırakın. Bu tutar “Dağılım bekliyor” olarak izlenir. Pay girerseniz toplamı tutarın tamamına eşit olmalı.')),
      read(total) {
        const used = new Set();
        const values = rows.map(row => { const id = Number(row.kanalId); if (!id || used.has(id)) throw new Error('Her kanalı bir kez seçin.'); used.add(id); return { kanalId: id, tutar: cents(row.tutar, { allowZero: false }) / 100 }; });
        if (values.length && values.reduce((sum, row) => sum + cents(row.tutar), 0) !== Math.round(Math.abs(total) * 100)) throw new Error('Kanal paylarının toplamı tutara eşit olmalı.');
        return values;
      }
    };
  }
  function channelSelection(channels, initial = []) {
    const selected = new Set(initial);
    const node = h('fieldset', {}, h('legend', {}, 'Krediyi kullanan kanallar'), h('div', { class: 'channel-options' }, activeChannels(channels).map(channel => field(channel.ad, input(`kredi-kanal-${channel.id}`, channel.id, { type: 'checkbox', checked: selected.has(channel.id), onchange: event => { if (event.target.checked) selected.add(channel.id); else selected.delete(channel.id); } })))), help('Tek kanal seçebilirsiniz. Birden fazla kanalda kredi girişi ve her taksit, seçtiğiniz sabit kanallar arasında eşit bölünür. Sonradan eklenen kanallar bu krediye katılmaz.'));
    return { node, read() { if (!selected.size) throw new Error('Krediyi kullanan en az bir kanal seçin.'); return [...selected].sort((a, b) => a - b); } };
  }
  async function renderCards(generation, id = null) {
    if (id) {
      const card = await api(`${base}/kartlar/${id}`); if (!isCurrent(generation)) return;
      renderCard(card); return;
    }
    page('Kredi Kartları', 'Harcama, ekstre ve kaydedilen ödemeler', canEdit() ? [act('+ Kart ekle', () => cardDialog(), 'primary')] : []);
    const cards = await api(`${base}/kartlar`); if (!isCurrent(generation)) return;
    const list = cards.map(card => h('button', { type: 'button', class: 'finance-card', onclick: () => navigate('cards', card.id) }, h('div', { class: 'finance-card-head' }, h('strong', {}, card.ad), h('span', { class: 'badge' }, card.aktif ? 'Aktif' : 'Yeni kullanıma kapalı')), h('span', { class: 'summary-label' }, 'Uygulamadaki kart borcu'), moneyNode(card.borc), h('small', {}, `Ekstre borcu ${money(card.ekstreBorc)} · Limit ${money(card.limit)}`), !card.yeniTakip && h('span', { class: 'badge pending' }, 'Geçiş incelemesi gerekiyor')));
    view().replaceChildren(h('p', { class: 'plan-note' }, 'Kartın son ödeme günü kasayı değiştirmez. Yalnız kaydettiğiniz kart ödemesi, ödeme tarihinde genel kasa ve ilgili kanallardan düşer.'), cards.length ? h('div', { class: 'finance-grid' }, list) : h('div', { class: 'empty' }, h('h2', {}, 'Henüz kart eklenmedi'), help('Kart adı, limit ve ödeme günleriyle başlayın. Kart numarası veya banka şifresi istenmez.')));
  }
  function renderCard(card) {
    const editable = canEdit() && card.yeniTakip;
    const actions = editable ? [act('Kartı düzenle', () => cardDialog(card)), act('+ Harcama / iade', () => chargeDialog(card)), act('Faiz / masraf ekle', () => feeDialog(card)), act('Ödeme kaydet', () => cardPaymentDialog(card), 'primary')] : [];
    if (canEdit() && !card.yeniTakip) actions.push(act('Geçişi incele', () => cardTransition(card), 'primary'));
    page(card.ad, 'Kredi kartı', actions);
    const statementRows = card.ekstreler.map(statement => [dateText(statement.kesimTarihi), dateText(statement.sonOdemeTarihi), moneyNode(statement.borc), moneyNode(statement.odenen), moneyNode(statement.kalan), statement.asgariOdeme == null ? 'Girilmedi' : h('div', {}, moneyNode(statement.asgariOdeme), statement.asgariKalan != null && h('small', { class: `minimum-status${statement.asgariKalan > 0 ? ' pending' : ''}` }, statement.asgariKalan > 0 ? `Kayıtlı ödemelere göre asgari için ${money(statement.asgariKalan)} kaldı` : 'Kayıtlı ödemelere göre asgari tamamlandı')), editable ? button('Tarih / asgari tutar', () => statementDialog(card, statement), 'small') : '']);
    const expenseRows = card.harcamalar.map(expense => [dateText(expense.tarih), h('span', {}, expense.aciklama, expense.islemId && h('small', { class: 'table-sub' }, 'Alış / gider kaydına bağlı'), expense.iptal && h('span', { class: 'badge cancelled' }, 'İptal')), moneyNode(expense.tutar), `${expense.taksitSayisi} taksit`, shares(expense.dagilimlar), expense.ekstreKayitId ? h('div', {}, help('PDF yüklemesinden kaydedildi.'), canEdit() && button('Kaynak belgeyi aç', () => navigate('imports', { kayitId: expense.ekstreKayitId }), 'small')) : editable && !expense.iptal && !expense.islemId ? button('İptal et', () => cancelDialog('Harcama iptali', `${base}/kartlar/${card.id}/harcamalar/${expense.id}/iptal`, card, 'cards'), 'small danger') : '']);
    const payments = card.odemeler.map(payment => h('div', { class: `payment${payment.iptal ? ' cancelled' : ''}` }, h('div', {}, h('strong', {}, dateText(payment.tarih)), help(payment.not || ''), shares(payment.dagilimlar), h('small', { class: 'table-sub' }, `Kasa etkisi ${money(payment.kasaEtkisi)}`), payment.ekstreKayitId ? h('div', {}, help('PDF yüklemesinden kaydedildi.'), canEdit() && button('Kaynak belgeyi aç', () => navigate('imports', { kayitId: payment.ekstreKayitId }), 'small')) : editable && !payment.iptal && button('Ödemeyi iptal et', () => cancelDialog('Kart ödemesini iptal et', `${base}/kartlar/${card.id}/odemeler/${payment.id}/iptal`, card, 'cards'), 'small danger')), moneyNode(payment.tutar)));
    view().replaceChildren(...childValues([
      button('← Kartlara dön', () => navigate('cards'), 'back-link'),
      !card.yeniTakip && h('div', { class: 'notice' }, 'Bu kart eski kasa kuralını kullanıyor. Yeni ödeme ve harcama takibine geçmeden önce mevcut borcu ve geçmiş kasa etkisini inceleyin. Geçmiş kayıtlar silinmez.'),
      h('div', { class: 'summary-strip' }, summary(card.borc < 0 ? 'Kart alacak bakiyesi' : 'Kart borcu', money(Math.abs(card.borc))), summary('Ekstre borcu', money(card.ekstreBorc)), summary('Limit', money(card.limit))),
      card.kanalKartBorclari && section('Kanallara göre kalan kart borcu', h('div', {}, shares(card.kanalKartBorclari), help('Bu tutarlar kasa bakiyesine eklenmez veya kasadan düşülmez. Kasa yalnız ödeme kaydında değişir.'))),
      help(`Hesap kesim günü ${card.kesimGunu} · Son ödeme günü ${card.sonOdemeGunu}. Son ödeme tarihi geçse de ödeme kaydı olmadan kasa değişmez.`),
      section('Ekstreler', statementRows.length ? table(['Kesim', 'Son ödeme', 'Borç', 'Ödenen', 'Kalan', 'Asgari', ''], statementRows) : help('Henüz ekstre yok.')),
      section('Harcamalar ve iadeler', expenseRows.length ? table(['Tarih', 'Açıklama', 'Tutar', 'Plan', 'Kanallar', ''], expenseRows) : help('Henüz harcama yok.')),
      section('Kaydedilen kart ödemeleri', payments.length ? h('div', {}, payments) : help('Henüz ödeme kaydedilmedi.')),
      editable && button(card.aktif ? 'Yeni kullanıma kapat' : 'Yeniden kullanıma aç', () => stateDialog(card, 'cards'), 'small')
    ]));
  }
  async function cardDialog(card = null) {
    const channels = await api('/api/kanallar'); const allocation = allocationEditor(channels); const identity = requestIdentity();
    const controls = { ad: input('ad', card?.ad || '', { required: true, maxlength: 200 }), limit: input('limit', card?.limit ?? '', { inputmode: 'decimal', required: true }), kesimGunu: input('kesimGunu', card?.kesimGunu ?? 1, { type: 'number', min: 1, max: 31, required: true }), sonOdemeGunu: input('sonOdemeGunu', card?.sonOdemeGunu ?? 10, { type: 'number', min: 1, max: 31, required: true }), acilisTarihi: input('acilisTarihi', today(), { type: 'date', required: true }), acilisBorc: input('acilisBorc', '0', { inputmode: 'decimal', required: true }) };
    formDialog(card ? 'Kartı düzenle' : 'Kredi kartı ekle', h('div', { class: 'stack' }, field('Kart / banka adı', controls.ad), h('div', { class: 'form-grid' }, field('Limit (₺)', controls.limit), field('Hesap kesim günü', controls.kesimGunu), field('Son ödeme günü', controls.sonOdemeGunu)), !card && h('div', {}, field('Takip başlangıcı', controls.acilisTarihi), field('Mevcut açılış borcu (₺)', controls.acilisBorc), allocation.node), help(card ? 'Önceki ekstre tarihleri ve borçlar değişmez.' : 'Açılış borcu yeni harcama veya kasa çıkışı oluşturmaz. Tam kart numarası, CVV veya banka giriş bilgisi girmeyin.')), 'Kartı kaydet', async () => {
      const debt = card ? 0 : amount(controls.acilisBorc.value);
      const payload = identity({ surum: card?.surum || 0, ad: controls.ad.value.trim(), limit: amount(controls.limit.value), kesimGunu: integer(controls.kesimGunu.value), sonOdemeGunu: integer(controls.sonOdemeGunu.value), acilisTarihi: card?.takipBaslangic || controls.acilisTarihi.value, acilisBorc: debt, acilisDagilimlari: card ? [] : allocation.read(debt) });
      const result = await api(card ? `${base}/kartlar/${card.id}` : `${base}/kartlar`, { method: card ? 'PUT' : 'POST', body: payload }); closeModal(); toast('Kart kaydedildi.'); await navigate('cards', result.id);
    });
  }
  async function chargeDialog(card) {
    const channels = await api('/api/kanallar'); const allocation = allocationEditor(channels); const identity = requestIdentity();
    const date = input('tarih', today(), { type: 'date', required: true }); const description = input('aciklama', '', { required: true, maxlength: 2000 }); const total = input('tutar', '', { inputmode: 'decimal', required: true }); const installments = input('taksitSayisi', 1, { type: 'number', min: 1, max: 60, required: true }); const firstCut = input('ilkKesimTarihi', '', { type: 'date' });
    const refundable = card.harcamalar.filter(row => row.tutar > 0 && !row.iptal);
    const source = select('kaynakHarcamaId', [{ value: '', label: 'İade edilen harcamayı seçin' }, ...refundable.map(row => ({ value: row.id, label: `${dateText(row.tarih)} · ${row.aciklama} · ${money(row.tutar)}` }))]);
    const refundFields = h('div', {}, field('İadenin bağlı olduğu harcama', source), help('İade, seçtiğiniz harcamanın kanal dağılımını kullanır. Yalnız henüz ödenmemiş kısmı bu akışta iade edebilirsiniz; ödenmiş harcamanın iadesi burada desteklenmez.'));
    const updateRefund = () => {
      let refund = false; try { refund = signedAmount(total.value) < 0; } catch { /* Keep normal fields while the amount is incomplete. */ }
      refundFields.hidden = !refund; source.required = refund; source.disabled = !refund;
      allocation.node.hidden = refund; allocation.node.disabled = refund;
      installments.disabled = refund; firstCut.disabled = refund;
      if (refund) installments.value = '1';
    };
    total.addEventListener('input', updateRefund); updateRefund();
    formDialog('Kart harcaması / iade', h('div', { class: 'stack' }, h('div', { class: 'notice' }, 'Alış veya gider kaydında bu kartı seçtiyseniz aynı harcamayı burada yeniden girmeyin. O harcama otomatik izlenir.'), field('Açıklama', description), h('div', { class: 'form-grid' }, field('Tarih', date), field('Toplam tutar (₺)', total), field('Taksit sayısı', installments), field('İlk kesim tarihi (isteğe bağlı)', firstCut)), refundFields, allocation.node, help('Taksitler toplam borcu çoğaltmaz. İade için eksi tutar girip ilgili harcamayı seçin. Bankanın faiz veya masrafını “Faiz / masraf ekle” ile kaydedin. Harcama kasadan düşmez.')), 'Harcamayı kaydet', async form => {
      const value = signedAmount(total.value); if (!value) throw new Error('Sıfırdan farklı bir tutar girin.');
      const sourceId = value < 0 ? Number(source.value) : null;
      if (value < 0 && !refundable.some(row => row.id === sourceId)) throw new Error('İadenin bağlı olduğu harcamayı seçin.');
      const body = identity({ surum: card.surum, tarih: date.value, aciklama: description.value.trim(), tutar: value, taksitSayisi: value < 0 ? 1 : integer(installments.value), ilkKesimTarihi: value < 0 ? null : firstCut.value || null, dagilimlar: value < 0 ? [] : allocation.read(value), kaynakHarcamaId: sourceId });
      if (!await confirmSimilar(form, { tur: 'KartHarcama', tarih: body.tarih, tutar: value, krediKartiId: card.id, kanal: null, alisId: null }, body)) return;
      await api(`${base}/kartlar/${card.id}/harcamalar`, { method: 'POST', body }); closeModal(); toast('Kart hareketi kaydedildi.'); await navigate('cards', card.id);
    }, { wide: true });
  }
  function statementDialog(card, statement) {
    const identity = requestIdentity(); const date = input('sonOdemeTarihi', statement.sonOdemeTarihi, { type: 'date', required: true }); const minimum = input('asgariOdeme', statement.asgariOdeme ?? '', { inputmode: 'decimal' }); const reason = input('aciklama', '', { required: true, maxlength: 2000 });
    formDialog('Ekstre bilgilerini düzenle', h('div', { class: 'stack' }, help(`${dateText(statement.kesimTarihi)} kesimli ekstre. Asgari tutarı banka ekstrenizden girin; sistem oran veya tatil günü tahmin etmez.`), field('Gerçek son ödeme tarihi', date), field('Bankanın asgari ödeme tutarı (₺)', minimum), field('Değişiklik açıklaması', reason)), 'Ekstreyi kaydet', async () => { await api(`${base}/kartlar/${card.id}/ekstreler/${statement.id}`, { method: 'PUT', body: identity({ surum: card.surum, sonOdemeTarihi: date.value, asgariOdeme: minimum.value.trim() ? amount(minimum.value) : null, aciklama: reason.value.trim() }) }); closeModal(); await navigate('cards', card.id); });
  }
  function feeDialog(card) {
    if (!canEdit() || !card.yeniTakip) throw new Error('Yeni kart takibinde editör hesabı gerekir.');
    const identity = requestIdentity();
    const eligible = card.ekstreler.filter(row => row.kalan > 0 && row.kesimTarihi <= today());
    const statement = select('ekstreId', [{ value: '', label: 'Kesilmiş ekstre seçin' }, ...eligible.map(row => ({ value: row.id, label: `${dateText(row.kesimTarihi)} · ${money(row.kalan)} kalan` }))], '', { required: true });
    const date = input('tarih', today(), { type: 'date', required: true }); const total = input('tutar', '', { inputmode: 'decimal', required: true }); const description = input('aciklama', '', { required: true, maxlength: 2000 });
    formDialog('Kart faizini / masrafını ekle', h('div', { class: 'stack' }, field('İlgili ekstre', statement), !eligible.length && h('div', { class: 'notice' }, 'Kalan borcu olan kesilmiş ekstre yok. Bu akıştan masraf eklenemez.'), field('Bankanın bildirdiği tarih', date), field('Bankanın bildirdiği tutar (₺)', total), field('Faiz / masraf açıklaması', description), help('Tutarı bankadan aynen girin; faiz hesaplanmaz. Seçilen ekstre ve önceki ekstrelerin kalan borcuna göre kanallara dağıtılır. İleri taksitler bu dağılıma katılmaz. Bilinmeyen kanal payı varsa önce o dağılımı netleştirin.')), 'Kanal paylarını göster', async form => {
      if (!canEdit()) throw new Error('Editör hesabı gerekir.');
      const id = Number(statement.value); if (!eligible.some(row => row.id === id)) throw new Error('Kalan borcu olan kesilmiş bir ekstre seçin.');
      const body = identity({ surum: card.surum, ekstreId: id, tarih: date.value, tutar: cents(total.value, { allowZero: false }) / 100, aciklama: description.value.trim() });
      const preview = await api(`${base}/kartlar/${card.id}/masraf-onizleme`, { method: 'POST', body }); if (!c.isOpen(form)) return;
      const payload = { ...body, dagilimOzeti: preview.dagilimOzeti };
      formDialog('Faiz / masraf dağılımını onaylayın', h('div', { class: 'stack' }, h('div', { class: 'summary-strip' }, summary('Masraf', money(preview.tutar)), summary('Dağılıma esas kalan borç', money(preview.devredenBorc))), section('Kanal payları', shares(preview.dagilimlar)), help('Bu kayıt kart borcunu artırır. Genel kasa ve kanal kasaları yalnız kart ödemesi kaydedildiğinde değişir. Kart değiştiyse güncel kartı yükleyip yeni önizleme alın.'), button('Güncel kartı yükle', () => { closeModal(); return navigate('cards', card.id); }, 'small')), 'Masrafı kaydet', async confirmation => {
        if (!canEdit()) throw new Error('Editör hesabı gerekir.');
        if (!await confirmSimilar(confirmation, { tur: 'KartHarcama', tarih: payload.tarih, tutar: payload.tutar, krediKartiId: card.id, kanal: null, alisId: null }, payload)) return;
        await api(`${base}/kartlar/${card.id}/masraflar`, { method: 'POST', body: payload }); closeModal(); toast('Faiz / masraf kaydedildi.'); await navigate('cards', card.id);
      });
    });
  }
  function cardPaymentDialog(card) {
    const identity = requestIdentity(); const date = input('tarih', today(), { type: 'date', required: true }); const total = input('tutar', card.ekstreBorc > 0 ? card.ekstreBorc : '', { required: true, inputmode: 'decimal' }); const note = input('not', '', { maxlength: 2000 });
    const statement = select('ekstreId', [{ value: '', label: 'Önce en eski açık ekstre' }, ...card.ekstreler.filter(row => row.kalan > 0).map(row => ({ value: row.id, label: `${dateText(row.kesimTarihi)} · ${money(row.kalan)} kalan` }))]);
    formDialog('Kart ödemesi kaydet', h('div', { class: 'stack' }, field('Gerçek ödeme tarihi', date), field('Ödenen tutar (₺)', total), field('Ekstre seçimi', statement), field('Ödeme notu / dekont açıklaması', note), help('Ödeme kasayı bu tarihte etkiler. Bir sonraki adımda ekstre ve kanal paylarını inceleyin.')), 'Dağılımı göster', async () => {
      const payload = identity({ surum: card.surum, tarih: date.value, tutar: cents(total.value, { allowZero: false }) / 100, ekstreId: statement.value ? Number(statement.value) : null, not: note.value.trim() || null });
      const preview = await api(`${base}/kartlar/${card.id}/odeme-onizleme`, { method: 'POST', body: payload });
      formDialog('Kart ödemesini onaylayın', h('div', { class: 'stack' }, h('div', { class: 'summary-strip' }, summary('Ödeme', money(preview.tutar)), summary('Genel kasa çıkışı', money(preview.kasaEtkisi))), section('Kanal payları', shares(preview.dagilimlar)), section('Ekstre payları', h('div', {}, preview.ekstreler.map(row => h('p', {}, `Ekstre #${row.ekstreId}: ${money(row.tutar)}`)))), help('Kaydettiğinizde bu ödeme bir kez işlenir. Geçmişte kasada sayılmış kısım varsa kasa etkisi ödeme tutarından farklı olabilir.')), 'Ödemeyi onayla', async form => {
        if (!await confirmSimilar(form, { tur: 'KartOdeme', tarih: payload.tarih, tutar: payload.tutar, krediKartiId: card.id, kanal: null, alisId: null }, payload)) return;
        await api(`${base}/kartlar/${card.id}/odemeler`, { method: 'POST', body: payload }); closeModal(); toast('Kart ödemesi kaydedildi.'); await navigate('cards', card.id);
      });
    });
  }
  function cancelDialog(title, path, record, target) {
    const identity = requestIdentity(); const reason = input('aciklama', '', { required: true, maxlength: 2000 });
    formDialog(title, h('div', { class: 'stack' }, help('İptal borç ve kasa sonuçlarını etkileyebilir. Gerçekleşen ödemeyi yalnız yanlış yazıldığı için ikinci kez kaydetmeyin.'), field('İptal açıklaması', reason)), 'İptali onayla', async () => { await api(path, { method: 'POST', body: identity({ surum: record.surum, aciklama: reason.value.trim() }) }); closeModal(); await navigate(target, record.id); }, { danger: true });
  }
  function stateDialog(record, target) {
    const identity = requestIdentity(); const reason = input('aciklama', '', { required: true, maxlength: 2000 }); const route = target === 'cards' ? 'kartlar' : 'krediler';
    formDialog(record.aktif ? 'Yeni kullanıma kapat' : 'Yeniden kullanıma aç', h('div', { class: 'stack' }, help('Geçmiş kayıtlar ve mevcut borç takibi korunur.'), field('Açıklama', reason)), 'Durumu kaydet', async () => { await api(`${base}/${route}/${record.id}/durum`, { method: 'POST', body: identity({ surum: record.surum, aktif: !record.aktif, aciklama: reason.value.trim() }) }); closeModal(); await navigate(target, record.id); });
  }
  function transitionPreview(preview, payload, path, target, id) {
    const content = h('div', { class: 'stack' }, h('div', { class: 'summary-strip' }, summary('Genel kasa anlık farkı', money(preview.genelKasaAnlikFarki)), summary('Kanal anlık farkı', money(preview.kanalAnlikFarki)), summary('Önceden sayılmış tutar', money(preview.eskiKasadaSayilanTutar))), h('ul', {}, preview.aciklamalar.map(text => h('li', {}, text))), help('Tarih öncesindeki kayıtlar korunur. Bu özeti doğrulamadan geçişi onaylamayın.'));
    if (!preview.kabulEdilebilir) { c.openModal('Geçiş tamamlanamıyor', h('div', { class: 'stack' }, content, help('Girdi ve eşleştirmeleri düzeltip yeniden önizleyin.'), button('Kapat', closeModal))); return; }
    formDialog('Geçiş özeti', content, 'Özeti doğruladım, geçişi onayla', async () => { await api(path, { method: 'POST', body: { ...payload, onay: true } }); closeModal(); toast('Yeni takip etkinleştirildi.'); await navigate(target, id); }, { wide: true });
  }
  async function cardTransition(card) {
    const channels = await api('/api/kanallar'); const allocation = allocationEditor(channels); const identity = requestIdentity();
    const start = input('baslangic', today(), { type: 'date', min: today(), required: true }); const debt = input('kalanBorc', Math.max(0, card.borc), { inputmode: 'decimal', required: true }); const counted = input('kasadaOncedenSayilanTutar', '0', { inputmode: 'decimal', required: true }); const reason = input('aciklama', '', { required: true, maxlength: 2000 });
    formDialog('Eski kartı yeni takibe geçir', h('div', { class: 'stack' }, help('Bankanızdaki kalan borcu ve daha önce genel kasadan düşmüş kısmını kontrol edin. Bu işlem yeni harcama oluşturmaz.'), field('Geçiş tarihi', start), field('Kalan kart borcu (₺)', debt), field('Bu borcun önceden kasada sayılmış kısmı (₺)', counted), allocation.node, field('Geçiş açıklaması', reason)), 'Geçiş farkını göster', async () => { const total = amount(debt.value); const payload = identity({ surum: card.surum, baslangic: start.value, kalanBorc: total, kasadaOncedenSayilanTutar: amount(counted.value), dagilimlar: allocation.read(total), aciklama: reason.value.trim(), onay: false }); const preview = await api(`${base}/kartlar/${card.id}/gecis-onizleme`, { method: 'POST', body: payload }); transitionPreview(preview, payload, `${base}/kartlar/${card.id}/gecis`, 'cards', card.id); }, { wide: true });
  }

  async function renderLoans(generation, id = null) {
    if (id) { const loan = await api(`${base}/krediler/${id}`); if (isCurrent(generation)) renderLoan(loan); return; }
    page('Krediler', 'Çekim ve otomatik taksit planları', canEdit() ? [act('+ Kredi ekle', () => loanDialog(), 'primary')] : []);
    const loans = await api(`${base}/krediler`); if (!isCurrent(generation)) return;
    view().replaceChildren(help('Taksitler kendi tarihlerinde genel kasadan ve seçili kanal kasalarından otomatik düşer. Bu, bankadan ödeme doğrulaması değildir.'), loans.length ? h('div', { class: 'finance-grid' }, loans.map(loan => h('button', { type: 'button', class: 'finance-card', onclick: () => navigate('loans', loan.id) }, h('div', { class: 'finance-card-head' }, h('strong', {}, loan.ad), h('span', { class: 'badge' }, loan.aktif ? 'Aktif' : 'Arşivde')), h('span', { class: 'summary-label' }, 'Kalan planlı ödeme'), moneyNode(loan.kalanPlanliOdeme), h('small', {}, `Çekilen ${money(loan.cekilenTutar)} · ${dateText(loan.cekimTarihi)}`), !loan.yeniTakip && h('span', { class: 'badge pending' }, 'Geçiş incelemesi gerekiyor')))) : h('div', { class: 'empty' }, h('h2', {}, 'Henüz kredi eklenmedi'), help('Yeni çekilen veya önceden çekilmiş bir krediyi ayrı seçeneklerle kaydedebilirsiniz.')));
  }
  function renderLoan(loan) {
    const editable = canEdit() && loan.yeniTakip;
    const actions = editable ? [button('Erken kapama', () => closeLoanDialog(loan))] : canEdit() ? [act('Geçişi incele', () => loanTransition(loan), 'primary')] : [];
    page(loan.ad, 'Kredi takibi', actions);
    const statuses = { Bekliyor: 'Bekliyor', KasayaIslendi: 'Kasaya işlendi', Iptal: 'Plan değişikliğiyle iptal' };
    const rows = loan.taksitler.map(row => [String(row.no), dateText(row.tarih), moneyNode(row.tutar), h('span', {}, statuses[row.durum] || row.durum, row.not && h('small', { class: 'table-sub' }, row.not)), shares(row.dagilimlar), editable && row.durum !== 'Iptal' ? button(row.durum === 'KasayaIslendi' ? 'Not ekle' : 'Planı düzenle', () => installmentDialog(loan, row), 'small') : '']);
    view().replaceChildren(...childValues([button('← Kredilere dön', () => navigate('loans'), 'back-link'), !loan.yeniTakip && h('div', { class: 'notice' }, 'Bu kredi eski planı kullanıyor. Geçiş önizlemesi, ileri taksitlerin kanal paylarını gösterir; eski çekim yeniden gelir yazılmaz.'), h('div', { class: 'summary-strip' }, summary('Çekilen tutar', money(loan.cekilenTutar), dateText(loan.cekimTarihi)), summary('Kalan planlı ödeme', money(loan.kalanPlanliOdeme)), summary('Bekleyen taksit', String(loan.taksitler.filter(row => row.durum === 'Bekliyor').length))), section('Krediyi kullanan kanallar', shares(loan.kanalPaylari)), help('Kalan planlı ödeme, bankadaki kalan anapara değildir. Taksit gününde kasaya otomatik işlenmesi banka ödemesinin doğrulandığı anlamına gelmez.'), section('Taksit planı', rows.length ? table(['No', 'Tarih', 'Tutar', 'Durum', 'Kanallar', ''], rows) : help('Kayıtlı taksit yok.')), editable && button(loan.aktif ? 'Krediyi arşivle' : 'Arşivden çıkar', () => stateDialog(loan, 'loans'), 'small')]));
  }
  async function loanDialog() {
    const channels = await api('/api/kanallar'); const selection = channelSelection(channels); const identity = requestIdentity();
    const nextMonth = value => {
      if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return '';
      const [year, month, day] = value.split('-').map(Number);
      const next = new Date(Date.UTC(year, month, 1));
      const lastDay = new Date(Date.UTC(next.getUTCFullYear(), next.getUTCMonth() + 1, 0)).getUTCDate();
      next.setUTCDate(Math.min(day, lastDay)); return next.toISOString().slice(0, 10);
    };
    const mode = select('mevcutKredi', [{ value: 'false', label: 'Yeni çekilen kredi — kasaya giriş oluştur' }, { value: 'true', label: 'Önceden çekilmiş kredi — yeniden giriş oluşturma' }], 'false', { required: true });
    const name = input('ad', '', { required: true, maxlength: 200 }); const principal = input('cekilenTutar', '', { inputmode: 'decimal', required: true }); const drawDate = input('cekimTarihi', today(), { type: 'date', required: true }); const first = input('ilkTaksitTarihi', nextMonth(drawDate.value), { type: 'date', required: true }); const count = input('taksitSayisi', 12, { type: 'number', min: 1, max: 600, required: true }); const payment = input('aylikOdeme', '', { inputmode: 'decimal', required: true });
    let firstChanged = false;
    first.addEventListener('input', () => { firstChanged = true; });
    drawDate.addEventListener('input', () => { if (!firstChanged) first.value = nextMonth(drawDate.value); });
    formDialog('Kredi ekle', h('div', { class: 'stack' }, field('Kredi türü', mode), field('Banka / kredi adı', name), h('div', { class: 'form-grid' }, field('Çekilen tutar (₺)', principal), field('Çekim tarihi', drawDate), field('İlk taksit tarihi', first), field('Taksit sayısı', count), field('Aylık taksit (₺)', payment)), selection.node, help('Yeni kredi genel kasaya bir kez girer; aynı para seçilen kanallara dağıtılır. Önceden çekilmiş kredide yalnız kalan planı girin, eski çekim yeniden gelir olmaz.')), 'Krediyi kaydet', async () => {
      const result = await api(`${base}/krediler`, { method: 'POST', body: identity({ ad: name.value.trim(), cekilenTutar: cents(principal.value, { allowZero: false }) / 100, cekimTarihi: drawDate.value, ilkTaksitTarihi: first.value, taksitSayisi: integer(count.value), aylikOdeme: cents(payment.value, { allowZero: false }) / 100, kanalIdleri: selection.read(), mevcutKredi: mode.value === 'true' }) }); closeModal(); toast('Kredi ve taksit planı kaydedildi.'); await navigate('loans', result.id);
    }, { wide: true });
  }
  function installmentDialog(loan, installment) {
    const identity = requestIdentity(); const processed = installment.durum === 'KasayaIslendi';
    const date = input('tarih', installment.tarih, { type: 'date', required: true, disabled: processed }); const total = input('tutar', installment.tutar, { inputmode: 'decimal', required: true, disabled: processed }); const note = input('not', installment.not || '', { maxlength: 2000 }); const cancel = input('iptal', '1', { type: 'checkbox', disabled: processed }); const reason = input('aciklama', '', { required: true, maxlength: 2000 });
    formDialog(`Taksit ${installment.no}`, h('div', { class: 'stack' }, help(processed ? 'Bu taksit kasaya işlendi. Tarih/tutar değişmez; eklediğiniz not ikinci ödeme oluşturmaz.' : 'Gelecek taksidin tarih/tutar değişikliğini veya iptalini açıklayın.'), field('Tarih', date), field('Tutar (₺)', total), field('Not / dekont açıklaması', note), !processed && field('Bu taksidi plandan iptal et', cancel), field('Değişiklik açıklaması', reason)), 'Taksidi kaydet', async () => { await api(`${base}/krediler/${loan.id}/taksitler/${installment.id}`, { method: 'PUT', body: identity({ surum: loan.surum, tarih: date.value, tutar: cents(total.value, { allowZero: false }) / 100, not: note.value.trim() || null, iptal: !processed && cancel.checked, aciklama: reason.value.trim() }) }); closeModal(); await navigate('loans', loan.id); });
  }
  function closeLoanDialog(loan) {
    const identity = requestIdentity(); const date = input('tarih', today(), { type: 'date', min: today(), required: true }); const total = input('tutar', '', { inputmode: 'decimal', required: true }); const reason = input('aciklama', '', { required: true, maxlength: 2000 });
    formDialog('Krediyi erken kapat', h('div', { class: 'stack' }, h('div', { class: 'notice' }, 'Bankanın bildirdiği kapama tutarını girin. Bu tarihten sonraki kalan taksitler iptal olarak korunur, kapama tutarı bir kez kasaya işlenir. Faiz indirimi hesaplanmaz.'), field('Kapama tarihi', date), field('Bankanın kapama tutarı (₺)', total), field('Açıklama', reason)), 'Kapama tutarını kaydet', async () => { await api(`${base}/krediler/${loan.id}/erken-kapat`, { method: 'POST', body: identity({ surum: loan.surum, tarih: date.value, tutar: cents(total.value, { allowZero: false }) / 100, aciklama: reason.value.trim() }) }); closeModal(); toast('Erken kapama kaydedildi.'); await navigate('loans', loan.id); });
  }
  async function loanTransition(loan) {
    const channels = await api('/api/kanallar'); const selection = channelSelection(channels, loan.kanalPaylari.filter(row => row.kanalId != null).map(row => row.kanalId)); const identity = requestIdentity(); const date = input('baslangic', today(), { type: 'date', min: today(), required: true }); const reason = input('aciklama', '', { required: true, maxlength: 2000 });
    formDialog('Eski kredinin geçişini incele', h('div', { class: 'stack' }, help('Eski çekim genel kasaya yeniden girmez. Geçmiş kanal bakiyelerine sessiz düzeltme yapılmaz; ileri taksitler seçtiğiniz kanallara eşit bölünür.'), field('Geçiş tarihi', date), selection.node, field('Geçiş açıklaması', reason)), 'Geçiş farkını göster', async () => { const payload = identity({ surum: loan.surum, baslangic: date.value, kanalIdleri: selection.read(), aciklama: reason.value.trim(), onay: false }); const preview = await api(`${base}/krediler/${loan.id}/gecis-onizleme`, { method: 'POST', body: payload }); transitionPreview(preview, payload, `${base}/krediler/${loan.id}/gecis`, 'loans', loan.id); });
  }
  function overview(data, days, changeDays) {
    const range = select('gun', [{ value: 7, label: '7 gün' }, { value: 30, label: '30 gün' }], days, { 'aria-label': 'Yaklaşan ödeme aralığı', onchange: event => changeDays(Number(event.target.value)) });
    const rows = data.olaylar.map(event => [h('span', {}, dateText(event.tarih), event.kaynak === 'Kart' && event.tur === 'SonOdeme' && event.tutar > 0 && event.tarih < (data.tarih || today()) && h('span', { class: 'badge pending' }, 'Gecikti')), button(event.ad, () => navigate(event.kaynak === 'Kart' ? 'cards' : 'loans', event.kaynakId), 'table-link'), event.tur === 'Kesim' ? 'Hesap kesimi' : event.tur === 'SonOdeme' ? 'Son ödeme' : 'Kredi taksidi', moneyNode(event.tutar), event.otomatikKasa ? 'Taksit tarihinde otomatik düşer' : event.tur === 'Kesim' ? 'Banka ekstresi doğrulaması değildir' : 'Ödeme kaydedilince düşer']);
    return section('Yaklaşan ve geciken ödemeler', h('div', {}, h('div', { class: 'summary-strip' }, summary('Toplam kart borcu', money(data.kartBorcu)), data.kartAlacakBakiyesi > 0 && summary('Kart alacak bakiyesi', money(data.kartAlacakBakiyesi), 'Diğer kartların borcundan düşülmez.'), summary('Kalan kredi planı', money(data.kalanKrediPlani))), rows.length ? table(['Tarih', 'Kart / kredi', 'Olay', 'Tutar', 'Kasa etkisi'], rows) : help('Bu aralıkta kayıtlı ödeme yok.')), range);
  }
  return { renderCards, renderLoans, overview, cardDialog, cardPaymentDialog, feeDialog, loanDialog, installmentDialog };
}
