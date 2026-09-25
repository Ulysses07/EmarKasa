export function createNotificationUi(c) {
  const { api, h, button, input, field, help, section, page, dateText, formDialog, closeModal, run, toast, navigate, view, isCurrent, push, notificationRoute, role } = c;
  const act = (label, work, style = '', props = {}) => button(label, event => run(event.currentTarget, work), style, props);
  async function render(generation) {
    page('Bildirimler', 'Kart ve kredi hatırlatmaları', [act('Bildirim ayarları', () => settings())]);
    const records = await api('/api/bildirimler'); if (!isCurrent(generation)) return;
    const rows = records.map(record => h('article', { class: `notification-item${record.okundu ? ' read' : ''}` },
      h('div', {}, h('div', { class: 'notification-meta' }, dateText(record.tarih), !record.okundu && h('span', { class: 'badge review' }, 'Yeni')), h('h2', {}, record.baslik), h('p', {}, record.mesaj)),
      h('div', { class: 'row-actions' }, button('Kaydı aç', () => {
        let target; try { target = notificationRoute(new URL(record.hedef, 'https://kasa.invalid').hash, role()); } catch {}
        if (target) navigate(target.view, target.id); else toast('Bu bildirimin ilgili kaydı bulunamadı.', true);
      }, 'small'), !record.okundu && act('Okundu olarak işaretle', async () => { await api(`/api/bildirimler/${record.id}/okundu`, { method: 'POST' }); await navigate('notifications'); }, 'small'))));
    view().replaceChildren(help('Bir bildirimi okumak borcu ödemez ve kasa hareketi oluşturmaz. Tarihler İstanbul saatine göre izlenir.'), records.length ? h('div', { class: 'notification-list' }, rows) : h('div', { class: 'empty' }, h('h2', {}, 'Henüz bildirim yok'), help('Kartın kesim günü, son ödemeden 3 gün önce ve son ödeme günü; kredinin taksitinden 3 gün önce ve taksit günü hatırlatma oluşturulur.')));
  }
  async function settings() {
    const [config, key, devices, browser] = await Promise.all([api('/api/bildirimler/ayarlar'), api('/api/bildirimler/push/anahtar'), api('/api/bildirimler/push/abonelikler'), push.status()]);
    const enabled = input('etkin', '1', { type: 'checkbox', checked: config.etkin });
    const hour = input('saat', config.saat, { type: 'number', min: 0, max: 23, required: true }); const minute = input('dakika', config.dakika, { type: 'number', min: 0, max: 59, required: true });
    const deviceName = input('cihazAdi', '', { maxlength: 100, placeholder: 'Örn. Telefonum veya Ofis bilgisayarı' });
    const status = !browser.supported ? 'Bu tarayıcıda cihaz bildirimi desteklenmiyor.' : browser.subscribed ? 'Bu tarayıcı bildirimlere kayıtlı.' : browser.permission === 'denied' ? 'Tarayıcı izni kapalı. Site ayarlarından izin verebilirsiniz.' : 'Bu tarayıcı henüz bildirimlere kayıtlı değil.';
    const browserActions = h('div', { class: 'row-actions' },
      !browser.subscribed && act('Bu cihazda bildirimleri aç', async () => { await push.enable(deviceName.value); toast('Bu cihaz bildirimlere kaydedildi.'); await settings(); }, 'primary', { disabled: !browser.supported || !key.etkin }),
      browser.subscribed && act('Bildirim kaydını yenile', async () => { await push.enable(deviceName.value); toast('Bu cihazın bildirim kaydı yenilendi.'); await settings(); }, '', { disabled: !browser.supported || !key.etkin }),
      browser.subscribed && act('Test bildirimi gönder', async () => { const result = await push.test(); toast(result.mesaj || (result.basarili ? 'Test gönderildi.' : 'Test gönderilemedi.'), !result.basarili); }),
      browser.subscribed && act('Bu cihazda kapat', async () => { await push.disable(); toast('Bu tarayıcının bildirimleri kapatıldı.'); await settings(); }, 'danger'));
    const deviceList = devices.length ? h('div', {}, devices.map(device => h('div', { class: 'buyer-row' }, h('div', {}, h('strong', {}, device.cihazAdi), h('small', {}, `${device.etkin ? 'Etkin' : 'Kapalı'} · Son başarılı gönderim: ${device.sonBasarili ? dateText(device.sonBasarili) : 'Henüz yok'}`)), act('Kaydı kaldır', async () => { await api(`/api/bildirimler/push/abonelikler/${device.id}`, { method: 'DELETE' }); toast('Seçilen cihaz kaydı kaldırıldı.'); await settings(); }, 'small danger')))) : help('Kayıtlı cihaz yok.');
    formDialog('Bildirim ayarları', h('div', { class: 'stack' }, field('Hatırlatmalar etkin', enabled), h('div', { class: 'form-grid' }, field('Gönderim saati', hour), field('Dakika', minute)), help('Saat dilimi İstanbul. Kart: hesap kesimi, son ödemeden 3 gün önce ve son ödeme günü. Kredi: taksitten 3 gün önce ve taksit günü. Varsayılan saat 09.00.'), section('Bu telefon / bilgisayar', h('div', { class: 'stack' }, h('p', { class: 'plain-note' }, status), !key.etkin && h('div', { class: 'notice' }, 'Sunucuda cihaz bildirimleri henüz etkinleştirilmemiş. Uygulama içi bildirimler ayrı izlenir.'), field('Bu cihazın adı', deviceName), browserActions, help('iPhone/iPad: iOS/iPadOS 16.4 veya üzeri bir sürümde siteyi Ana Ekrana Ekle ile kurun, oradan açıp izin verin. Windows’ta Edge/Chrome üzerinden izin verebilirsiniz. Bildirim teslimi cihazın ve tarayıcının izinlerine bağlıdır.'))), section('Kayıtlı cihazlar', deviceList)), 'Saati ve tercihi kaydet', async () => { await api('/api/bildirimler/ayarlar', { method: 'PUT', body: { etkin: enabled.checked, saat: Number(hour.value), dakika: Number(minute.value), surum: config.surum } }); closeModal(); toast('Bildirim tercihleri kaydedildi.'); }, { wide: true });
  }
  return { render, settings };
}
