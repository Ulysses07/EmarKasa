export function createCashControlsUi(c) {
  const { api, h, button, input, field, help, section, table, money, moneyNode, signedAmount, amount, formDialog, closeModal, run, toast, summary, requestIdentity, canEdit, isOpen, navigate } = c;
  const editor = () => { if (!canEdit()) throw new Error('Bu işlem için editör hesabı gerekir.'); };
  const act = (label, work, style = '') => button(label, event => run(event.currentTarget, work), style);
  function thresholdSettings(rows) {
    return section('Kanal alt bakiye uyarıları', h('div', { class: 'stack' }, help('İstediğiniz kanal için uyarıyı açıp alt sınır belirleyin. Uyarı bakiyeyi değiştirmez; kart borcu bu sınırdan düşülmez.'), rows.length ? table(['Kanal', 'Kasa bakiyesi', 'Alt sınır', 'Durum', ''], rows.map(row => [row.kanal, moneyNode(row.bakiye), row.etkin ? moneyNode(row.tutar) : 'Kapalı', row.etkin && row.esikAltinda ? h('span', { class: 'badge pending' }, 'Alt sınırın altında') : row.etkin ? 'Sınırın üzerinde veya eşit' : 'Uyarı kapalı', canEdit() ? button('Uyarıyı düzenle', () => thresholdDialog(row), 'small') : ''])) : help('Henüz kanal yok.')));
  }
  function thresholdDialog(row) {
    editor(); const enabled = input('etkin', '1', { type: 'checkbox', checked: row.etkin }); const total = input('tutar', row.tutar, { inputmode: 'decimal', required: true });
    formDialog(`${row.kanal} alt bakiye uyarısı`, h('div', { class: 'stack' }, field('Uyarı açık', enabled), field('Alt sınır (₺)', total), help('Sıfır girerseniz yalnız eksi bakiye için uyarı görünür. Bu ayar kasa bakiyesini değiştirmez.')), 'Uyarıyı kaydet', async () => {
      editor(); await api(`/api/kasa-esikleri/${row.kanalId}`, { method: 'PUT', body: { surum: row.surum, tutar: amount(total.value), etkin: enabled.checked } }); closeModal(); toast('Kanal uyarısı kaydedildi.'); await navigate('tools');
    });
  }
  function history(rows) {
    return section('Gerçek bakiye karşılaştırmaları', h('div', { class: 'stack' }, help('Kayıt anındaki genel kasa ile sizin bildirdiğiniz gerçek bakiye karşılaştırılır. Fark otomatik gelir veya gider yazılmaz.'), rows.length ? table(['Kayıt zamanı', 'Kayıtlı genel kasa', 'Gerçek bakiye', 'Gerçek − kayıtlı', 'Not'], rows.map(row => [new Date(row.kaydedildi).toLocaleString('tr-TR'), moneyNode(row.sistemBakiye), moneyNode(row.gercekBakiye), moneyNode(row.fark), row.not || '—'])) : help('Henüz bakiye karşılaştırması kaydedilmedi.')), canEdit() ? button('Gerçek bakiye ile karşılaştır', comparisonDialog, 'small') : null);
  }
  function comparisonDialog() {
    editor(); const identity = requestIdentity(); const total = input('gercekBakiye', '', { inputmode: 'decimal', required: true }); const note = input('not', '', { maxlength: 2000 });
    formDialog('Genel kasa bakiyesini karşılaştır', h('div', { class: 'stack' }, field('Kontrol ettiğiniz gerçek bakiye (₺)', total), field('Not', note), help('Şu an kontrol ettiğiniz tutarı girin. Önce farkı göreceksiniz; bu işlem geçmiş tarihli kayıt veya otomatik düzeltme oluşturmaz.')), 'Farkı göster', async form => {
      editor(); const body = { gercekBakiye: signedAmount(total.value), not: note.value.trim() || null };
      const preview = await api('/api/kasa-kontrol/onizleme', { method: 'POST', body }); if (!isOpen(form)) return;
      let needsPreview = false; let payload = identity({ ...body, kontrolOzeti: preview.kontrolOzeti });
      const result = h('div'); const draw = value => result.replaceChildren(h('div', { class: 'summary-strip' }, summary('Kayıtlı genel kasa', money(value.sistemBakiye)), summary('Gerçek bakiye', money(value.gercekBakiye)), summary('Gerçek − kayıtlı farkı', money(value.fark)))); draw(preview);
      const status = help('Yalnız karşılaştırma geçmişe kaydedilir. Kasa bakiyesi değişmez.');
      formDialog('Bakiye farkını inceleyin', h('div', { class: 'stack' }, result, note.value.trim() && help(note.value.trim()), status), 'Karşılaştırmayı kaydet', async confirmation => {
        editor();
        if (needsPreview) {
          const latest = await api('/api/kasa-kontrol/onizleme', { method: 'POST', body }); if (!isOpen(confirmation)) return;
          draw(latest); payload = identity({ ...body, kontrolOzeti: latest.kontrolOzeti }); needsPreview = false;
          status.textContent = 'Güncel fark gösterildi. İnceledikten sonra tekrar “Karşılaştırmayı kaydet” seçin.'; return;
        }
        try { await api('/api/kasa-kontrol', { method: 'POST', body: payload }); }
        catch (error) { if (error.status === 409) { needsPreview = true; status.textContent = 'Kasa değişti. Yeniden kaydettiğinizde önce güncel fark gösterilecek; bir sonraki onayınızla kaydedilecek.'; } throw error; }
        closeModal(); toast('Karşılaştırma kaydedildi; kasa bakiyesi değişmedi.'); await navigate('home');
      });
    });
  }
  return { thresholdSettings, thresholdDialog, history, comparisonDialog };
}
