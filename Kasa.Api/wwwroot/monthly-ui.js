export function createMonthlyUi(c) {
  const { api, h, button, input, field, select, help, section, table, money, moneyNode, dateText, today, cents, formDialog, closeModal, page, run, toast, summary, requestIdentity, canEdit, isCurrent, view } = c;
  const base = '/api/aylik-giderler';
  const kinds = { Kira: 'Kira', Maas: 'Maaş', Fatura: 'Fatura', Diger: 'Diğer' };
  const act = (label, work, style = '') => button(label, event => run(event.currentTarget, work), style);
  const editor = () => { if (!canEdit()) throw new Error('Bu işlem için editör hesabı gerekir.'); };
  const shares = row => row.dagilimTuru === 'Genel' ? h('span', {}, 'Yalnız genel kasa') : h('div', { class: 'allocation-tags' }, row.dagilimlar.map(share => h('span', { class: 'allocation-tag' }, `${share.kanal}: ${money(share.tutar)}`)));
  let currentMonth = today().slice(0, 7);
  let currentGeneration = 0;
  const refresh = () => isCurrent(currentGeneration) ? render(currentGeneration, currentMonth) : Promise.resolve();

  async function render(generation, month = currentMonth) {
    currentMonth = month; currentGeneration = generation;
    page('Aylık Giderler', 'Planlar ve elle kaydedilen gerçek ödemeler', canEdit() ? [act('+ Şablon ekle', () => templateDialog(), 'primary')] : []);
    const [year, period] = month.split('-').map(Number);
    const [data, templates] = await Promise.all([api(`${base}?yil=${year}&ay=${period}`), api(`${base}/sablonlar`)]);
    if (!isCurrent(generation) || month !== currentMonth) return;
    const monthInput = input('ay', month, { type: 'month', required: true, 'aria-label': 'Aylık gider ayı' });
    const rows = data.kayitlar.map(row => [
      h('div', {}, h('strong', {}, row.ad), h('small', { class: 'table-sub' }, kinds[row.tur] || row.tur)),
      dateText(row.planlananTarih), moneyNode(row.tutar), shares(row),
      h('span', { class: `badge ${row.durum === 'Odendi' ? 'approved' : 'pending'}` }, row.durum === 'Odendi' ? 'Ödendi' : 'Ödeme bekliyor'),
      row.odemeTarihi ? dateText(row.odemeTarihi) : '—',
      canEdit() ? row.durum === 'Odendi' ? button('Ödemeyi iptal et', () => cancelDialog(row), 'small danger') : button('Ödeme kaydet', () => paymentDialog(row, data.yil, data.ay), 'small primary') : ''
    ]);
    const templateRows = templates.map(row => [row.ad, kinds[row.tur] || row.tur, moneyNode(row.tutar), `Her ay ${row.odemeGunu}. gün`, shares(row), row.aktif ? 'Aktif' : 'Pasif', dateText(row.gecerliAy), canEdit() ? act('Düzenle', () => templateDialog(row), 'small') : '']);
    view().replaceChildren(
      h('p', { class: 'plan-note' }, 'Şablon ve plan kasa bakiyesini değiştirmez. Nakit veya havale gerçekten ödendiğinde “Ödeme kaydet” ile işleyin. Kartla ödemeyi mevcut gider veya kart ekranında kaydedin; burada ikinci kez ödeme girmeyin.'),
      h('div', { class: 'toolbar' }, monthInput, act('Ayı göster', () => monthInput.reportValidity() && render(generation, monthInput.value))),
      h('div', { class: 'summary-strip' }, summary('Planlanan', money(data.planlananToplam)), summary('Ödendi', money(data.odenenToplam)), summary('Kalan plan', money(data.planlananToplam - data.odenenToplam))),
      section('Ayın giderleri', rows.length ? table(['Gider', 'Plan tarihi', 'Tutar', 'Hangi kasa', 'Durum', 'Ödeme tarihi', ''], rows) : help('Bu ay için aylık gider planı yok.')),
      section('Gider şablonları', h('div', {}, help('Değişiklik seçtiğiniz aydan itibaren uygulanır. Eski aylar ve kaydedilmiş ödemeler korunur. Pasif şablonun geçmişi silinmez.'), templateRows.length ? table(['Şablon', 'Tür', 'Tutar', 'Ödeme günü', 'Hangi kasa', 'Durum', 'Geçerli ay', ''], templateRows) : help('Kira, maaş, fatura veya diğer düzenli giderler için şablon ekleyebilirsiniz.')))
    );
  }

  function distribution(channels, initial) {
    const selected = new Set((initial?.dagilimlar || []).map(row => row.kanalId));
    const totals = new Map((initial?.dagilimlar || []).map(row => [row.kanalId, row.tutar]));
    const list = h('div', { class: 'stack' });
    const mode = select('dagilimTuru', [{ value: '', label: 'Dağılım seçin' }, { value: 'Genel', label: 'Yalnız genel kasa' }, { value: 'Esit', label: 'Seçilen kanallara eşit' }, { value: 'Ozel', label: 'Kanal tutarlarını gir' }], initial?.dagilimTuru || '', { required: true });
    const draw = () => {
      list.hidden = !['Esit', 'Ozel'].includes(mode.value);
      list.replaceChildren(...channels.filter(row => row.aktif || selected.has(row.id)).map(channel => {
        const checked = input(`dagilim-kanal-${channel.id}`, channel.id, { type: 'checkbox', checked: selected.has(channel.id), onchange: event => { if (event.target.checked) selected.add(channel.id); else selected.delete(channel.id); total.disabled = !selected.has(channel.id); total.required = mode.value === 'Ozel' && selected.has(channel.id); } });
        const total = input(`dagilim-tutar-${channel.id}`, totals.get(channel.id) ?? '', { inputmode: 'decimal', required: mode.value === 'Ozel' && selected.has(channel.id), disabled: !selected.has(channel.id), oninput: event => totals.set(channel.id, event.target.value), 'aria-label': `${channel.ad} payı (₺)` });
        return h('div', { class: 'monthly-allocation' }, field(channel.ad, checked), mode.value === 'Ozel' && total);
      }));
    };
    mode.addEventListener('change', draw); draw();
    return {
      node: h('fieldset', {}, h('legend', {}, 'Kasa dağılımı'), field('Dağılım', mode), list, help('Yalnız genel kasa seçeneği hiçbir kanal kasasına yazılmaz. Eşit dağılımda seçtiğiniz kanallar sabittir; sonradan açılan kanallar bu plana eklenmez.')),
      read(total) {
        if (!['Genel', 'Esit', 'Ozel'].includes(mode.value)) throw new Error('Giderin hangi kasaya yazılacağını seçin.');
        if (mode.value === 'Genel') return { dagilimTuru: 'Genel', dagilimlar: [] };
        if (!selected.size) throw new Error('En az bir kanal seçin.');
        const result = [...selected].sort((a, b) => a - b).map(id => ({ kanalId: id, tutar: mode.value === 'Esit' ? 0 : cents(totals.get(id), { allowZero: false }) / 100 }));
        if (mode.value === 'Ozel' && result.reduce((sum, row) => sum + cents(row.tutar), 0) !== cents(total)) throw new Error('Kanal paylarının toplamı gider tutarına eşit olmalı.');
        return { dagilimTuru: mode.value, dagilimlar: result };
      }
    };
  }
  async function templateDialog(template = null) {
    editor();
    const channels = await api('/api/kanallar'); editor();
    const identity = requestIdentity(); const allocation = distribution(channels, template);
    const name = input('ad', template?.ad || '', { required: true, maxlength: 200 });
    const kind = select('tur', Object.entries(kinds).map(([value, label]) => ({ value, label })), template?.tur || 'Kira', { required: true });
    const total = input('tutar', template?.tutar ?? '', { inputmode: 'decimal', required: true });
    const day = input('odemeGunu', template?.odemeGunu || 1, { type: 'number', min: 1, max: 31, required: true });
    const first = input('gecerliAy', template?.gecerliAy?.slice(0, 7) > today().slice(0, 7) ? template.gecerliAy.slice(0, 7) : today().slice(0, 7), { type: 'month', min: today().slice(0, 7), required: true });
    const active = input('aktif', '1', { type: 'checkbox', checked: template?.aktif ?? true });
    formDialog(template ? 'Aylık gider şablonunu düzenle' : 'Aylık gider şablonu ekle', h('div', { class: 'stack' }, field('Gider adı', name), h('div', { class: 'form-grid' }, field('Tür', kind), field('Aylık tutar (₺)', total), field('Ödeme günü', day), field('Bu aydan itibaren', first)), allocation.node, field('Şablon aktif', active), help('Kısa aylarda ödeme günü ayın son gününe alınır. Bu kayıt ödeme değildir; kasaya işlem yazılmaz. Önceki aylar ve ödenmiş kayıtlar değişmez.')), 'Şablonu kaydet', async () => {
      editor();
      const value = cents(total.value, { allowZero: false }) / 100; const paymentDay = Number(day.value);
      if (!Number.isInteger(paymentDay) || paymentDay < 1 || paymentDay > 31) throw new Error('Ödeme günü 1 ile 31 arasında olmalı.');
      const payload = identity({ surum: template?.surum || 0, ad: name.value.trim(), tur: kind.value, tutar: value, odemeGunu: paymentDay, ...allocation.read(value), gecerliAy: `${first.value}-01`, aktif: active.checked });
      await api(template ? `${base}/sablonlar/${template.id}` : `${base}/sablonlar`, { method: template ? 'PUT' : 'POST', body: payload }); closeModal(); toast('Şablon kaydedildi; ödeme oluşturulmadı.'); await refresh();
    }, { wide: true });
  }
  function paymentDialog(row, year, month) {
    editor(); const identity = requestIdentity();
    const date = input('tarih', today(), { type: 'date', required: true }); const note = input('not', '', { maxlength: 2000 });
    formDialog('Gerçek ödemeyi kaydet', h('div', { class: 'stack' }, h('strong', {}, `${row.ad} · ${year}-${String(month).padStart(2, '0')} · ${money(row.tutar)}`), shares(row), field('Gerçek ödeme tarihi', date), field('Not / havale açıklaması', note), help('Bu nakit veya havale ödemesi genel kasadan bir kez düşer. Aynı ayın aynı gideri ikinci kez kaydedilemez. Tutar yanlışsa ödeme yapmadan önce şablonu düzenleyin.')), 'Ödemeyi kaydet', async () => {
      editor(); await api(`${base}/${row.sablonId}/ode`, { method: 'POST', body: identity({ surum: row.sablonSurum, yil: year, ay: month, tarih: date.value, not: note.value.trim() || null }) }); closeModal(); toast('Gerçek ödeme kaydedildi.'); await refresh();
    });
  }
  function cancelDialog(row) {
    editor(); const identity = requestIdentity(); const reason = input('aciklama', '', { required: true, maxlength: 2000 });
    formDialog('Aylık gider ödemesini iptal et', h('div', { class: 'stack' }, help(`${row.ad} için ${dateText(row.odemeTarihi)} tarihli ${money(row.tutar)} ödeme kasadan geri alınır. Kayıt geçmişte korunur; gerekirse doğru ödeme yeniden girilir.`), field('İptal açıklaması', reason)), 'Ödemeyi iptal et', async () => {
      editor(); await api(`${base}/odemeler/${row.odemeId}/iptal`, { method: 'POST', body: identity({ aciklama: reason.value.trim() }) }); closeModal(); toast('Ödeme iptal edildi.'); await refresh();
    }, { danger: true });
  }
  async function lockPanel(month, refreshReport) {
    const data = await api('/api/ay-kilidi');
    const locked = data.kilitliSonTarih && `${month}-01` <= data.kilitliSonTarih;
    const status = data.kilitliSonTarih ? `${dateText(data.kilitliSonTarih)} dahil geçmiş kasa kayıtları kilitli.` : 'Kilitli ay yok.';
    const action = canEdit() && (locked || month < today().slice(0, 7)) ? button(locked ? 'Bu ayı ve sonrasını aç' : 'Bu ay sonuna kadar kilitle', () => lockDialog(data, month, Boolean(locked), refreshReport), 'small') : null;
    const history = h('details', {}, h('summary', {}, 'Kilit geçmişi'), data.gecmis.length ? table(['Zaman', 'Önceki sınır', 'Yeni sınır', 'Açıklama'], data.gecmis.map(row => [new Date(row.zaman).toLocaleString('tr-TR'), row.oncekiSonTarih ? dateText(row.oncekiSonTarih) : 'Yok', row.yeniSonTarih ? dateText(row.yeniSonTarih) : 'Yok', row.aciklama])) : help('Henüz kilit değişikliği yok.'));
    return section('Ay kilidi', h('div', { class: 'stack' }, h('div', { class: 'notice' }, status), help(locked ? 'Bu aydaki mali kayıtları değiştirmek için önce kilidi açın.' : 'Yalnız tamamlanmış ay kilitlenebilir. Okumalar ve gelecek planlar çalışmaya devam eder.'), history), action);
  }
  function lockDialog(data, month, unlock, refreshReport) {
    editor(); const identity = requestIdentity(); const [year, period] = month.split('-').map(Number); const reason = input('aciklama', '', { required: true, maxlength: 2000 });
    formDialog(unlock ? 'Ay kilidini aç' : 'Tamamlanan ayı kilitle', h('div', { class: 'stack' }, h('div', { class: 'notice' }, unlock ? `${month} ayı ve sonraki bütün aylar değişikliğe açılacak. Önceki ayların kilidi korunur.` : `${month} ayının son günü dahil bütün geçmiş mali kayıtlar değişikliğe kapatılacak. Bu işlem bakiyeleri değiştirmez.`), field('Açıklama', reason)), unlock ? 'Bu ayı ve sonrasını aç' : 'Ayı kilitle', async () => {
      editor(); await api(`/api/ay-kilidi/${unlock ? 'ac' : 'kapat'}`, { method: 'POST', body: identity({ surum: data.surum, yil: year, ay: period, aciklama: reason.value.trim() }) }); closeModal(); toast(unlock ? 'Ay kilidi açıldı.' : 'Ay kilitlendi.'); await refreshReport();
    }, { danger: unlock });
  }
  return { render, templateDialog, paymentDialog, cancelDialog, lockPanel };
}
