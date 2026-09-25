import { money, dateText, today, cents, amount, errorMessage, permissions, statusLabels, filteredPurchases, purchasePayload, childValues, logoutAndClear, navigationFor, currentPeriod, monthlyTotals, loadRuntime, runtimeRequestAllowed, cashEditingAllowed, incomeSelection } from './ui-core.js?v=2.3.0';
import { createFinanceUi } from './finance-ui.js?v=2.3.0';
import { createNotificationUi } from './notification-ui.js?v=2.3.0';
import { createMonthlyUi } from './monthly-ui.js?v=2.3.0';
import { createCashControlsUi } from './cash-controls-ui.js?v=2.3.0';
import { createStatementImportUi } from './statement-import-ui.js?v=2.3.0';
import { createPushClient, notificationRoute } from './push-client.js?v=2.3.0';

const $ = selector => document.querySelector(selector);
const state = { role: null, view: 'home', purchases: [], channels: [], cards: [], query: '', status: '', selected: null, epoch: 0 };
let runtime = null;
const runtimeReady = loadRuntime(fetch).then(config => { runtime = config; return config; });
const canEditCash = () => cashEditingAllowed(state.role, runtime);
const modal = $('#modal');
let modalCleanup = null;
let renderId = 0;
let monthlyRequest = 0;
const isOpen = form => modal.open && $('#modal-content').querySelector('form') === form;
const push = createPushClient({ api, session: () => canEditCash() ? state.epoch : null });
const financeUi = createFinanceUi({ api, h, button, input, field, select, help, section, table, money, moneyNode, dateText, today, cents, amount, signedAmount, formDialog, openModal, closeModal, page, navigate, run, toast, summary, childValues, requestIdentity, confirmSimilar, isOpen, canEdit: canEditCash, isCurrent: generation => generation === renderId, view: () => $('#view') });
const monthlyUi = createMonthlyUi({ api, h, button, input, field, select, help, section, table, money, moneyNode, dateText, today, cents, formDialog, closeModal, page, run, toast, summary, childValues, requestIdentity, canEdit: canEditCash, isCurrent: generation => generation === renderId, view: () => $('#view') });
const cashControlsUi = createCashControlsUi({ api, h, button, input, field, help, section, table, money, moneyNode, signedAmount, amount, formDialog, closeModal, run, toast, summary, requestIdentity, isOpen, canEdit: canEditCash, navigate });
const statementImportUi = createStatementImportUi({ api, h, button, input, field, select, help, section, table, money, moneyNode, dateText, cents, formDialog, closeModal, page, navigate, run, toast, summary, requestIdentity, isOpen, canEdit: canEditCash, isCurrent: generation => generation === renderId, session: () => state.epoch, view: () => $('#view'), createFormData: () => new FormData() });
const notificationUi = createNotificationUi({ api, h, button, input, field, help, section, page, dateText, formDialog, closeModal, run, toast, navigate, view: () => $('#view'), isCurrent: generation => generation === renderId, push, notificationRoute, role: () => state.role });

function h(tag, props = {}, ...children) {
  const element = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (value == null || value === false) continue;
    if (key.startsWith('on')) element.addEventListener(key.slice(2).toLowerCase(), value);
    else if (key === 'class') element.className = value;
    else if (key === 'text') element.textContent = value;
    else if (key === 'value') element.value = value;
    else if (key === 'checked') element.checked = Boolean(value);
    else element.setAttribute(key, value === true ? '' : String(value));
  }
  for (const child of childValues(children)) element.append(child instanceof Node ? child : document.createTextNode(String(child)));
  return element;
}
function button(text, action, kind = '', props = {}) { return h('button', { type: 'button', class: `button ${kind}`, onclick: action, ...props }, text); }
function input(name, value = '', props = {}) { return h('input', { name, value: value ?? '', ...props }); }
function field(label, control, extra = null) { return h('label', {}, label, control, extra); }
function select(name, choices, value = '', props = {}) {
  const control = h('select', { name, ...props }, choices.map(option => h('option', { value: option.value, disabled: option.disabled }, option.label)));
  control.value = value == null ? '' : String(value);
  return control;
}
function section(title, content, action) { return h('section', { class: 'section' }, h('div', { class: 'section-head' }, h('h2', {}, title), action), content); }
function badge(status) { return h('span', { class: `badge ${status === 'Onaylandi' ? 'approved' : status === 'Incelemede' ? 'review' : ''}` }, statusLabels[status] || status); }
function help(text) { return h('p', { class: 'help' }, text); }
function moneyNode(value, className = '') { return h('span', { class: `money ${className}` }, money(value)); }
function toast(message, error = false) {
  const item = h('div', { class: `toast${error ? ' error' : ''}` }, message);
  $('#notifications').append(item);
  setTimeout(() => item.remove(), error ? 10000 : 6000);
}
function clearSession() {
  state.epoch++;
  renderId++;
  state.role = null; state.purchases = []; state.channels = []; state.cards = []; state.selected = null; state.query = ''; state.status = '';
  $('#view').replaceChildren(); $('#navigation').replaceChildren(); $('#application').hidden = true; $('#login-screen').hidden = false;
  closeModal();
}
async function api(path, options = {}) {
  await runtimeReady;
  const headers = new Headers(options.headers || {});
  const method = (options.method || 'GET').toUpperCase();
  if (!runtimeRequestAllowed(runtime, path, method)) throw new Error('Bu sürüm yalnız kasa görüntüleme içindir. Kayıtlar değiştirilemez.');
  if (!['GET', 'HEAD'].includes(method)) headers.set('X-Kasa-Request', '1');
  let body = options.body;
  if (body != null && !(body instanceof FormData)) { headers.set('Content-Type', 'application/json'); body = JSON.stringify(body); }
  const epoch = state.epoch;
  let response;
  try { response = await fetch(path, { ...options, method, body, headers, credentials: 'same-origin', cache: 'no-store' }); }
  catch { throw new Error('Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.'); }
  if (epoch !== state.epoch) throw new Error('Oturum değişti. Lütfen yeniden deneyin.');
  if (!response.ok) {
    let result; try { result = await response.json(); } catch { result = null; }
    const error = new Error(errorMessage(result, response.status)); error.status = response.status;
    if (response.status === 401 && !path.startsWith('/api/auth/login') && !path.startsWith('/api/auth/kurtar')) clearSession();
    throw error;
  }
  if (options.binary) { const result = await response.blob(); if (epoch !== state.epoch) throw new Error('Oturum değişti. Lütfen yeniden deneyin.'); return result; }
  if (response.status === 204) return null;
  const text = await response.text();
  if (epoch !== state.epoch) throw new Error('Oturum değişti. Lütfen yeniden deneyin.');
  return text ? JSON.parse(text) : null;
}
async function run(control, work, errorBox = null) {
  if (control?.disabled) return;
  if (control) control.disabled = true;
  if (errorBox) { errorBox.hidden = true; errorBox.textContent = ''; }
  try { await work(); }
  catch (error) {
    if (errorBox && errorBox.isConnected) { errorBox.textContent = error.message; errorBox.hidden = false; errorBox.scrollIntoView({ block: 'nearest', behavior: 'smooth' }); }
    else toast(error.message, true);
  } finally { if (control) control.disabled = false; }
}
function closeModal() { if (modal.open) modal.close(); if (modalCleanup) modalCleanup(); modalCleanup = null; $('#modal-content').replaceChildren(); }
function openModal(title, content, wide = false) {
  closeModal(); $('#modal-title').textContent = title; $('#modal-content').replaceChildren(content); modal.classList.toggle('wide', wide); modal.showModal();
}
$('#modal-close').addEventListener('click', closeModal);
modal.addEventListener('cancel', () => { if (modalCleanup) modalCleanup(); modalCleanup = null; });
function formDialog(title, content, submitLabel, save, { wide = false, danger = false } = {}) {
  const errors = h('p', { class: 'form-error', role: 'alert', hidden: true });
  const submit = h('button', { type: 'submit', class: `button ${danger ? 'danger' : 'primary'}` }, submitLabel);
  const form = h('form', { class: 'stack' }, content, errors, h('div', { class: 'modal-actions' }, button('Vazgeç', closeModal), submit));
  form.addEventListener('submit', event => { event.preventDefault(); if (form.reportValidity()) run(submit, () => save(form), errors); });
  openModal(title, form, wide);
  return form;
}
const similarApprovals = new WeakMap();
const similarPanels = new WeakMap();
async function confirmSimilar(form, query, payload) {
  const signature = JSON.stringify({ query, payload });
  if (!form.isConnected || !modal.open) return false;
  if (similarApprovals.get(form) === signature) return true;
  const oldPanel = similarPanels.get(form); if (oldPanel) oldPanel.hidden = true;
  let records;
  try { records = await api('/api/islemler/benzerlik', { method: 'POST', body: query }); }
  catch (error) { throw new Error(`Benzer kayıt kontrolü tamamlanamadı. Kayıt yapılmadı; yeniden deneyin. ${error.message}`); }
  if (!Array.isArray(records)) throw new Error('Benzer kayıt kontrolünden geçerli yanıt alınamadı. Kayıt yapılmadı; yeniden deneyin.');
  if (!form.isConnected || !modal.open) return false;
  if (!records.length) return true;
  const panel = oldPanel || h('div', { class: 'notice similar-warning', role: 'status', tabindex: '-1' });
  const sourceNames = { Islem: 'Gider', KartHarcama: 'Kart harcaması', KartOdeme: 'Kart ödemesi', EskiKartOdeme: 'Eski kart ödemesi' };
  panel.replaceChildren(
    h('strong', {}, 'Benzer kayıt bulundu'),
    help('Aynı tarih, tutar ve kart veya kanalla bir kayıt var. Aynı ödemeyi yeniden girmediğinizi kontrol edin. Ayrı bir işlemse yine kaydedebilirsiniz.'),
    h('ul', { class: 'similar-records' }, records.map(record => h('li', {}, `${sourceNames[record.kaynak] || 'Kayıt'} #${record.id} · ${dateText(record.tarih)} · ${money(record.tutar)} · ${record.aciklama || 'Açıklama yok'}${record.alisId ? ` · Alış #${record.alisId}` : ''}`))),
    h('div', { class: 'row-actions' }, button('Vazgeç', closeModal), button('Ayrı işlem olarak kaydet', () => { similarApprovals.set(form, signature); form.requestSubmit(); }, 'primary'))
  );
  panel.hidden = false;
  if (!oldPanel) { form.append(panel); similarPanels.set(form, panel); }
  panel.scrollIntoView({ block: 'nearest', behavior: 'smooth' }); panel.focus();
  return false;
}
// Reuse the same key after an uncertain response; changed fields get a fresh key.
function requestIdentity() {
  let previous, id;
  return payload => {
    const { surum, hedefSurum, ...stable } = payload;
    const serialized = JSON.stringify(stable);
    if (serialized !== previous) { previous = serialized; id = crypto.randomUUID(); }
    return { ...payload, istekId: id };
  };
}
function values(form) { return Object.fromEntries(new FormData(form)); }
function optionalId(value) { return value ? Number(value) : null; }
function page(title, context, actions = []) { $('#page-title').textContent = title; $('#page-context').textContent = runtime?.saltOkunur ? `Kasa görüntüleme · ${context}` : context; $('#page-actions').replaceChildren(...actions); }
function empty(title, text, action) { return h('div', { class: 'empty' }, h('span', { class: 'empty-mark', 'aria-hidden': 'true' }, '↳'), h('h2', {}, title), h('p', {}, text), action); }
function summary(label, value, note) { return h('div', { class: 'summary' }, h('span', { class: 'summary-label' }, label), h('strong', { class: 'summary-value' }, value), note && h('div', { class: 'summary-note' }, note)); }
function table(headers, rows) {
  return h('div', { class: 'table-wrap' }, h('table', {}, h('thead', {}, h('tr', {}, headers.map(label => h('th', { scope: 'col' }, label)))), h('tbody', {}, rows.map(cells => h('tr', {}, cells.map(cell => h('td', {}, cell)))))));
}
function nav() {
  const items = navigationFor(state.role, runtime);
  $('#navigation').replaceChildren(...items.map(([key, title, icon]) => h('button', { type: 'button', class: `nav-button${state.view === key || key === 'purchases' && state.view === 'purchase' ? ' active' : ''}`, 'aria-current': state.view === key ? 'page' : null, onclick: () => navigate(key) }, h('span', { class: 'nav-icon', 'aria-hidden': 'true' }, icon), title)));
  $('#role-label').textContent = state.role === 'editor' ? 'Editör hesabı' : state.role === 'alici' ? 'Alıcı hesabı' : 'İzleyici hesabı';
}
async function loadPurchases() {
  const [purchases, channels] = await Promise.all([api('/api/alis'), api('/api/alis/kanallar')]);
  state.purchases = purchases; state.channels = channels;
}
async function loadPaymentLookups() {
  state.cards = await api('/api/kredikartlari');
}
async function navigate(view, id = null) {
  if (!navigationFor(state.role, runtime).some(([key]) => key === (view === 'purchase' ? 'purchases' : view))) throw new Error('Bu ekran için erişiminiz yok.');
  state.view = view; state.selected = id; nav();
  const generation = ++renderId;
  const content = $('#view'); content.setAttribute('aria-busy', 'true');
  content.replaceChildren(h('div', { class: 'empty' }, h('span', { class: 'loader', 'aria-hidden': 'true' }), h('p', {}, 'Kayıtlar yükleniyor…')));
  try {
    if (view === 'purchases' || view === 'purchase') {
      await loadPurchases(); if (generation !== renderId) return;
      if (view === 'purchase') renderPurchase(id); else renderPurchases();
    } else if (view === 'home') await renderHome(generation);
    else if (view === 'weekly') await renderWeekly(generation);
    else if (view === 'monthly') await renderMonthly(generation);
    else if (view === 'monthly-expenses') await monthlyUi.render(generation);
    else if (view === 'imports') await statementImportUi.render(generation, id);
    else if (view === 'transactions') await renderTransactions(generation);
    else if (view === 'tools') await renderTools(generation);
    else if (view === 'cards') await financeUi.renderCards(generation, id);
    else if (view === 'loans') await financeUi.renderLoans(generation, id);
    else if (view === 'notifications') await notificationUi.render(generation);
  } catch (error) {
    if (generation === renderId && state.role) content.replaceChildren(empty('Kayıtlar yüklenemedi', error.message, button('Yeniden dene', () => navigate(view, id), 'primary')));
  } finally {
    if (generation === renderId) {
      content.setAttribute('aria-busy', 'false');
      if (state.role && !modal.open) $('#main').focus({ preventScroll: true });
    }
  }
}
async function enter(role) {
  await runtimeReady;
  if (runtime.saltOkunur && !['editor', 'viewer'].includes(role)) throw new Error('Bu görüntüleme sürümünde alıcı girişi kullanılamıyor.');
  state.role = role; $('#login-screen').hidden = true; $('#application').hidden = false;
  const destination = !runtime.saltOkunur && typeof location !== 'undefined' ? notificationRoute(location.hash, role) : null;
  await navigate(destination?.view || (role === 'alici' ? 'purchases' : 'home'), destination?.id || null);
  if (runtime.saltOkunur) $('#version').textContent = runtime.surum ? `Kasa görüntüleme · ${runtime.surum}` : 'Kasa görüntüleme';
  else api('/api/surum').then(version => { $('#version').textContent = `Sürüm ${version.surum}`; }).catch(() => {});
}
$('#login-form').addEventListener('submit', event => {
  event.preventDefault(); const form = event.currentTarget;
  run(form.querySelector('button'), async () => {
    const result = await api('/api/auth/login', { method: 'POST', body: values(form) });
    form.reset(); await enter(result.rol);
  }, $('#login-error'));
});
$('#logout').addEventListener('click', event => run(event.currentTarget, () => logoutAndClear(async () => { if (canEditCash()) { try { await push.disable({ bestEffort: true }); } catch {} } await api('/api/auth/logout', { method: 'POST' }); }, () => { clearSession(); $('#login-form input').focus(); })));
if (typeof window !== 'undefined') window.addEventListener('hashchange', () => { if (!state.role || runtime?.saltOkunur) return; const target = notificationRoute(location.hash, state.role); if (target) run(null, () => navigate(target.view, target.id)); });
$('#recover-open').addEventListener('click', () => formDialog('Editör hesabını kurtar', h('div', { class: 'stack' }, help('Daha önce oluşturduğunuz tek kullanımlık kurtarma kodunu girin. Başarılı kurtarma tüm eski oturumları kapatır.'), field('Kullanıcı adı', input('kullanici', '', { required: true, autocomplete: 'username', maxlength: 64 })), field('Kurtarma kodu', input('kod', '', { required: true, autocomplete: 'off' })), field('Yeni şifre', input('yeniSifre', '', { type: 'password', required: true, minlength: 12, maxlength: 1024, autocomplete: 'new-password' }), help('En az 12 karakter kullanın.'))), 'Şifreyi yenile', async form => { await api('/api/auth/kurtar', { method: 'POST', body: values(form) }); clearSession(); toast('Şifreniz yenilendi. Yeni şifrenizle giriş yapın.'); }));

function renderPurchases() {
  page(state.role === 'alici' ? 'Alışlarım' : 'Alışlar', 'Alış defteri', [button('+ Yeni alış', () => editPurchase(), 'primary')]);
  const count = state.purchases.filter(p => p.durum === 'Incelemede').length;
  const total = state.purchases.reduce((sum, p) => sum + Number(p.toplam), 0);
  const remaining = state.purchases.reduce((sum, p) => sum + Number(p.kalan), 0);
  const list = h('div', { class: 'purchase-list' });
  const draw = () => {
    const items = filteredPurchases(state.purchases, state.query, state.status);
    list.replaceChildren(...(items.length ? items.map(purchaseRow) : [empty(state.purchases.length ? 'Bu aramada alış yok' : 'İlk alışınızı kaydedin', state.purchases.length ? 'Arama sözcüğünü veya durum filtresini değiştirin.' : 'Tedarikçiyi, alınan malları ve hangi kanala ait olduklarını tek bir kayıtta tutun.', state.purchases.length ? null : button('Yeni alış oluştur', () => editPurchase(), 'primary'))]));
  };
  const search = input('arama', state.query, { type: 'search', placeholder: 'Tedarikçi, mal veya alış ara…', 'aria-label': 'Alışlarda ara', oninput: event => { state.query = event.target.value; draw(); } });
  const filter = select('durum', [{ value: '', label: 'Tüm durumlar' }, ...Object.entries(statusLabels).map(([value, label]) => ({ value, label }))], state.status, { 'aria-label': 'Alış durumu', onchange: event => { state.status = event.target.value; draw(); } });
  $('#view').replaceChildren(h('div', { class: 'summary-strip' }, summary('Alış toplamı', money(total), `${state.purchases.length} kayıt`), summary('Ödenmeyi bekleyen', money(remaining), 'Tüm açık alışlar'), summary('İnceleme bekleyen', String(count), 'Editörün onayında')), h('div', { class: 'toolbar' }, h('div', { class: 'search' }, search), filter), list);
  draw();
}
function purchaseRow(p) {
  return h('button', { type: 'button', class: 'purchase-row', onclick: () => navigate('purchase', p.id), 'aria-label': `${p.tedarikci}, ${money(p.toplam)}, ${statusLabels[p.durum] || p.durum}` }, h('span', { class: 'purchase-monogram', 'aria-hidden': 'true' }, String(p.tedarikci || 'A').slice(0, 1).toLocaleUpperCase('tr-TR')), h('div', {}, h('div', { class: 'purchase-name' }, p.tedarikci), h('div', { class: 'purchase-meta' }, `#${p.id} · ${dateText(p.tarih)}${state.role !== 'alici' ? ` · ${p.alici || 'Editör'}` : ''}`)), h('div', { class: 'status-cell' }, badge(p.durum)), h('div', { class: 'purchase-total money' }, money(p.toplam), h('small', {}, p.kalan > 0 ? `${money(p.kalan)} kalan` : 'Ödeme tamamlandı')), h('span', { class: 'chevron', 'aria-hidden': 'true' }, '›'));
}
function renderPurchase(id) {
  const p = state.purchases.find(item => item.id === id);
  if (!p) { page('Alış bulunamadı', 'Alış defteri'); $('#view').replaceChildren(empty('Bu kayıt artık listede değil', 'Alış listesine dönerek güncel kayıtları görebilirsiniz.', button('Alışlara dön', () => navigate('purchases')))); return; }
  const rights = permissions(state.role, p);
  page(p.tedarikci, `Alış #${p.id}`, [badge(p.durum)]);
  const metadata = h('dl', { class: 'metadata' }, [['Alış tarihi', dateText(p.tarih)], ['Alıcı', p.alici || 'Editör']].map(([title, value]) => h('div', {}, h('dt', {}, title), h('dd', {}, value))));
  const items = h('div', {}, p.kalemler.map(line => h('div', { class: 'item-detail' }, h('div', { class: 'item-heading' }, h('span', {}, line.aciklama), moneyNode(line.tutar)), h('div', { class: 'allocation-tags' }, line.dagilimlar.map(d => h('span', { class: 'allocation-tag' }, `${d.kanal}: ${money(d.tutar)}`)), !line.dagilimlar.length && h('span', { class: 'badge pending' }, 'Kanal dağılımı bekliyor')))));
  const paymentSection = section('Ödemeler', p.odemeler.length ? h('div', {}, p.odemeler.map(payment => paymentRow(p, payment))) : h('p', { class: 'plain-note' }, 'Bu alışa henüz ödeme kaydedilmedi.'), rights.pay ? button('+ Ödeme ekle', event => run(event.currentTarget, () => paymentDialog(p)), 'small') : null);
  const docs = h('div', {}, help('Belgeler yükleniyor…'));
  const docSection = section('Belgeler', docs, (state.role === 'editor' || p.durum === 'Taslak') ? button('+ Belge ekle', () => documentDialog(p), 'small') : null);
  loadDocuments(p, docs);
  const actions = h('div', { class: 'detail-actions' }, rights.edit && button('Alışı düzenle', () => editPurchase(p), 'primary'), rights.send && button('İncelemeye gönder', () => statusDialog(p, 'gonder'), 'primary'), rights.approve && button('Alışı onayla', () => statusDialog(p, 'onayla'), 'primary'), rights.return && button('Açıklamayla iade et', () => statusDialog(p, 'iade')), rights.pay && button('Ödeme kaydet', event => run(event.currentTarget, () => paymentDialog(p))));
  const total = section('Alış hesabı', h('div', {}, h('div', { class: 'total-line' }, 'Toplam', moneyNode(p.toplam)), h('div', { class: 'total-line' }, 'Ödenen', moneyNode(p.odenen)), h('div', { class: 'total-line strong' }, 'Kalan', moneyNode(p.kalan)), p.durum !== 'Onaylandi' && p.odenen > 0 && help('Gerçekleşen ödeme kasaya yansır. Kanal dağılımı onay bekler.'), actions));
  $('#view').replaceChildren(...childValues([button('← Alışlara dön', () => navigate('purchases'), 'back-link'), p.editorNotu && h('div', { class: `notice${p.durum === 'Taslak' ? '' : ' success'}` }, h('strong', {}, p.durum === 'Taslak' ? 'Editörün iade açıklaması' : 'Editör notu'), h('p', {}, p.editorNotu)), h('div', { class: 'detail-grid' }, h('div', {}, section('Alınan mallar ve kanalları', h('div', {}, metadata, items, p.not && h('p', { class: 'purchase-note' }, p.not))), paymentSection, docSection), h('aside', { class: 'detail-summary' }, total))]));
}
function statusDialog(p, action) {
  const descriptions = { gonder: 'Alışı editörün incelemesine gönderin. Gönderildikten sonra değişiklik için editörün iade etmesi gerekir.', onayla: 'Malları ve kanal paylarını kontrol ettiniz mi? Onay, mevcut ödemeleri kayıtlı kanal dağılımına geçirir.', iade: 'Ne düzeltilmesi gerektiğini açıklayın. Mevcut ödemeler silinmez; kanal dağılımları yeniden onaylanana kadar bekler.' };
  const titles = { gonder: 'İncelemeye gönder', onayla: 'Alışı onayla', iade: 'Açıklamayla iade et' };
  formDialog(titles[action], h('div', { class: 'stack' }, h('p', { class: 'plain-note' }, descriptions[action]), action !== 'gonder' && field(action === 'iade' ? 'İade açıklaması' : 'Editör notu (isteğe bağlı)', h('textarea', { name: 'not', required: action === 'iade', maxlength: 2000 }))), titles[action], async form => { await api(`/api/alis/${p.id}/${action}`, { method: 'POST', body: { surum: p.surum, not: values(form).not || null } }); closeModal(); toast(action === 'gonder' ? 'Alış incelemeye gönderildi.' : action === 'onayla' ? 'Alış onaylandı.' : 'Alış açıklamayla iade edildi.'); await navigate('purchase', p.id); });
}

function editPurchase(p = null) {
  const draft = { surum: p?.surum || 0, tarih: p?.tarih || today(), tedarikci: p?.tedarikci || '', not: p?.not || '', kalemler: p?.kalemler.map(line => ({ aciklama: line.aciklama, tutar: line.tutar, dagilimlar: line.dagilimlar.map(d => ({ kanalId: d.kanalId, tutar: d.tutar })) })) || [{ aciklama: '', tutar: '', dagilimlar: [] }] };
  const lines = h('div'); const total = h('strong', { class: 'money' });
  const updateTotal = () => { try { total.textContent = money(draft.kalemler.reduce((sum, line) => sum + cents(line.tutar || '0'), 0) / 100); } catch { total.textContent = 'Tutarı kontrol edin'; } };
  const renderLines = () => {
    lines.replaceChildren(...draft.kalemler.map((line, index) => {
      const lineTotal = h('span', { class: 'money' }); const allocTotal = h('span'); const status = h('div', { class: 'allocation-summary' }, lineTotal, allocTotal); const allocations = h('div');
      const updateLine = () => {
        try { const sum = cents(line.tutar || '0'); const assigned = line.dagilimlar.reduce((acc, d) => acc + cents(d.tutar || '0'), 0); lineTotal.textContent = `Kalem: ${money(sum / 100)}`; allocTotal.textContent = assigned === sum ? 'Dağılım tamam' : `${money(Math.abs(sum - assigned) / 100)} ${assigned > sum ? 'fazla pay' : 'dağıtılmadı'}`; status.classList.toggle('invalid', assigned !== sum); } catch (error) { lineTotal.textContent = error.message; allocTotal.textContent = ''; }
        updateTotal();
      };
      const renderAllocations = () => allocations.replaceChildren(...line.dagilimlar.map((allocation, allocationIndex) => h('div', { class: 'allocation-row' }, field('Kanal', select(`kanal-${index}-${allocationIndex}`, [{ value: '', label: 'Kanal seçin' }, ...state.channels.filter(k => k.aktif || k.id === Number(allocation.kanalId)).map(k => ({ value: k.id, label: k.ad }))], allocation.kanalId, { required: true, onchange: event => { allocation.kanalId = event.target.value; updateLine(); } })), field('Pay (₺)', input(`pay-${index}-${allocationIndex}`, allocation.tutar, { inputmode: 'decimal', required: true, oninput: event => { allocation.tutar = event.target.value; updateLine(); } })), h('button', { type: 'button', class: 'icon-button', 'aria-label': `${index + 1}. kalemin ${allocationIndex + 1}. kanal payını kaldır`, onclick: () => { line.dagilimlar.splice(allocationIndex, 1); renderAllocations(); updateLine(); } }, '×'))));
      renderAllocations(); updateLine();
      return h('div', { class: 'line-editor' }, h('div', { class: 'line-editor-head' }, `Kalem ${index + 1}`, draft.kalemler.length > 1 && h('button', { type: 'button', class: 'icon-button', 'aria-label': `${index + 1}. kalemi kaldır`, onclick: () => { draft.kalemler.splice(index, 1); renderLines(); } }, '×')), h('div', { class: 'item-fields' }, field('Mal açıklaması', input(`aciklama-${index}`, line.aciklama, { required: true, maxlength: 500, placeholder: 'Örn. Ambalaj malzemesi', oninput: event => { line.aciklama = event.target.value; } })), field('Tutar (₺)', input(`tutar-${index}`, line.tutar, { inputmode: 'decimal', required: true, oninput: event => { line.tutar = event.target.value; updateLine(); } }))), h('div', { class: 'allocation-editor' }, h('div', { class: 'allocation-editor-head' }, h('span', {}, 'Hangi kanala alındı?'), button('+ Kanal payı', () => { let remainder = ''; try { remainder = Math.max(0, cents(line.tutar) - line.dagilimlar.reduce((sum, d) => sum + cents(d.tutar || 0), 0)) / 100 || ''; } catch {} line.dagilimlar.push({ kanalId: '', tutar: remainder }); renderAllocations(); updateLine(); }, 'small')), allocations, status));
    })); updateTotal();
  };
  renderLines();
  formDialog(p ? `Alış #${p.id} · Düzenle` : 'Yeni alış', h('div', { class: 'stack' }, h('div', { class: 'form-grid' }, field('Açıklama / ödeme yapılan yer', input('tedarikci', draft.tedarikci, { required: true, maxlength: 200, oninput: event => { draft.tedarikci = event.target.value; } })), field('Alış tarihi', input('tarih', draft.tarih, { type: 'date', required: true, onchange: event => { draft.tarih = event.target.value; } }))), h('fieldset', {}, h('legend', {}, 'Alınan mallar'), lines, button('+ Bir mal daha ekle', () => { draft.kalemler.push({ aciklama: '', tutar: '', dagilimlar: [] }); renderLines(); }, 'small')), help('Aynı malı birden fazla kanala bölebilirsiniz. Kanal net değilse pay eklemeyin; editör onaylamadan önce tamamlar. Ortak, belirsiz kanal anlamına gelmez.'), field('Alış notu', h('textarea', { name: 'not', maxlength: 2000, oninput: event => { draft.not = event.target.value; } }, draft.not)), h('div', { class: 'draft-total' }, 'Alış toplamı', total)), p ? 'Değişiklikleri kaydet' : 'Taslağı kaydet', async () => {
    const updated = await api(p ? `/api/alis/${p.id}` : '/api/alis', { method: p ? 'PUT' : 'POST', body: purchasePayload(draft) });
    closeModal(); toast('Alış kaydedildi.'); await navigate('purchase', updated.id);
  }, { wide: true });
}

function paymentRow(p, payment) {
  const cardName = payment.krediKartiAdi || `Kart #${payment.krediKartiId}`;
  const card = payment.krediKartiId && (['editor', 'viewer'].includes(state.role) ? button(cardName, () => navigate('cards', payment.krediKartiId), 'table-link') : h('span', {}, cardName));
  return h('div', { class: 'payment' }, h('div', {}, h('div', { class: 'payment-name' }, dateText(payment.tarih)), h('div', { class: 'payment-info' }, payment.krediKartiId ? h('span', {}, card, ' ile ödendi · kart borcu ayrı izlenir') : 'Nakit / havale', ` · Ödeme #${payment.id}`), payment.dagilimBekliyor ? h('span', { class: 'badge pending' }, 'Dağılım bekliyor') : h('div', { class: 'allocation-tags' }, payment.dagilimlar.filter(d => d.tutar > 0).map(d => h('span', { class: 'allocation-tag' }, `${d.kanal}: ${money(d.tutar)}`))), state.role === 'editor' && h('div', { class: 'row-actions' }, button('Düzelt / taşı', event => run(event.currentTarget, () => paymentDialog(p, payment)), 'small'), button('Ödemeyi iptal et', () => cancelPayment(p, payment), 'small danger'))), h('div', { class: 'payment-amount money' }, money(payment.tutar)));
}
async function paymentDialog(p, payment = null) {
  await loadPaymentLookups();
  const identity = requestIdentity();
  const date = input('tarih', payment?.tarih || today(), { type: 'date', required: true });
  const total = input('tutar', payment?.tutar ?? p.kalan, { inputmode: 'decimal', required: true });
  const card = select('krediKartiId', [{ value: '', label: 'Nakit / havale' }, ...state.cards.map(k => ({ value: k.id, label: k.ad }))], payment?.krediKartiId);
  const existing = select('mevcutIslemId', [{ value: '', label: 'Yeni gider oluştur' }], '');
  const existingHelp = help('Daha önce gider olarak girdiğiniz bir ödemeyi bağlarsanız kasadan ikinci kez düşülmez.');
  if (!payment) {
    const expenses = await api('/api/islemler');
    const available = expenses.filter(e => !e.alisId && !e.aylikGiderOdemeId && !e.ekstreKayitId && e.tutarTl > 0);
    existing.replaceChildren(h('option', { value: '' }, 'Yeni gider oluştur'), ...available.map(e => h('option', { value: e.id }, `#${e.id} · ${dateText(e.tarih)} · ${e.cari} · ${money(e.tutarTl)}`)));
    existing.addEventListener('change', () => {
      const selected = available.find(e => e.id === Number(existing.value));
      if (selected) { date.value = selected.tarih; total.value = selected.tutarTl; card.value = selected.krediKartiId || ''; }
      date.disabled = Boolean(selected); total.disabled = Boolean(selected); card.disabled = Boolean(selected);
    });
  }
  const target = select('hedefAlisId', [{ value: '', label: 'Bu alışta kalsın' }, ...state.purchases.filter(other => other.id !== p.id && other.kalan > 0).map(other => ({ value: other.id, label: `#${other.id} · ${other.tedarikci} · ${money(other.kalan)} kalan` }))]);
  formDialog(payment ? `Ödeme #${payment.id} · Düzelt / taşı` : 'Ödeme kaydet', h('div', { class: 'stack' }, h('div', { class: 'notice' }, payment ? 'Bu işlem kayıtlı ödemeyi değiştirir. Ödeme başka alışa aitse hedef alış seçin; para çıkışını yeniden kaydetmeyin.' : `Alışın kalan tutarı ${money(p.kalan)}. ${p.durum !== 'Onaylandi' ? 'Ödeme kasaya yansır; kanal dağılımı onay bekler.' : 'Ödeme onaylı kanal paylarına dağıtılır.'}`), !payment && field('Yeni ödeme veya mevcut gider', existing, existingHelp), h('div', { class: 'form-grid' }, field('Ödeme tarihi', date), field('Tutar (₺)', total), field('Ödeme yöntemi', card)), payment && field('Ödemeyi başka alışa taşı', target), field(payment ? 'Düzeltme açıklaması' : 'Ödeme notu (isteğe bağlı)', h('textarea', { name: 'aciklama', required: Boolean(payment), maxlength: 2000 })), help('Yeni takipte kartla alış, kart borcu oluşturur; kasa yalnız Kredi Kartları ekranında ödeme kaydedildiğinde azalır. Geçiş yapılmamış eski kartlarda önceki kasa kuralı sürer.')), payment ? 'Düzeltmeyi kaydet' : 'Ödemeyi kaydet', async form => {
    const data = values(form);
    const payload = { surum: p.surum, tarih: date.value, tutar: cents(total.value, { allowZero: false }) / 100, krediKartiId: optionalId(card.value) };
    if (payment) {
      const destination = state.purchases.find(other => other.id === Number(target.value));
      Object.assign(payload, { aciklama: data.aciklama.trim(), hedefAlisId: destination?.id || null, hedefSurum: destination?.surum ?? null });
      await api(`/api/alis/${p.id}/odemeler/${payment.id}`, { method: 'PUT', body: identity(payload) });
    } else {
      Object.assign(payload, { mevcutIslemId: optionalId(existing.value), not: data.aciklama.trim() || null });
      const body = identity(payload);
      if (!payload.mevcutIslemId && !await confirmSimilar(form, { tur: 'AlisOdeme', tarih: payload.tarih, tutar: payload.tutar, krediKartiId: payload.krediKartiId, kanal: null, alisId: p.id }, body)) return;
      await api(`/api/alis/${p.id}/odemeler`, { method: 'POST', body });
    }
    closeModal(); toast(payment ? 'Ödeme düzeltildi.' : 'Ödeme kaydedildi.'); await navigate('purchase', p.id);
  });
}
function cancelPayment(p, payment) {
  const identity = requestIdentity();
  formDialog('Gerçek ödemeyi iptal et', h('div', { class: 'stack' }, h('div', { class: 'notice danger' }, `${dateText(payment.tarih)} tarihli ${money(payment.tutar)} ödeme mali kayıtlardan kaldırılacak. Ödeme gerçekten yapıldıysa, yalnız yanlış alışa yazıldığı için iptal etmeyin; “Düzelt / taşı” işlemini kullanın.`), field('İptal açıklaması', h('textarea', { name: 'aciklama', required: true, maxlength: 2000 }))), 'Ödemeyi iptal et', async form => { await api(`/api/alis/${p.id}/odemeler/${payment.id}/iptal`, { method: 'POST', body: identity({ surum: p.surum, aciklama: values(form).aciklama.trim() }) }); closeModal(); toast('Ödeme iptal edildi; alışın kalan tutarı güncellendi.'); await navigate('purchase', p.id); }, { danger: true });
}
async function loadDocuments(p, container) {
  try {
    const docs = await api(`/api/alis/${p.id}/belgeler`);
    if (!container.isConnected) return;
    container.replaceChildren(...(docs.length ? docs.map(doc => h('div', { class: 'document-row' }, h('span', { class: 'document-type', 'aria-hidden': 'true' }, doc.icerikTuru === 'application/pdf' ? 'PDF' : 'GÖRSEL'), h('a', { href: `/api/belgeler/${doc.id}`, target: '_blank', rel: 'noopener', download: doc.dosyaAdi }, doc.dosyaAdi, h('span', { class: 'table-sub' }, `${Math.ceil(doc.boyut / 1024)} KB${doc.odemeId ? ` · Ödeme #${doc.odemeId}` : ''}`)), (state.role === 'editor' || p.durum === 'Taslak' && !doc.odemeId) && button('Sil', () => deleteDocument(p, doc), 'small danger'))) : [h('p', { class: 'plain-note' }, p.durum === 'Taslak' || state.role === 'editor' ? 'Fiş, fatura veya dekont ekleyebilirsiniz. PDF, JPEG ve PNG; en fazla 10 MB.' : 'Bu alışa belge eklenmedi. Yeni belge için editörün alışı iade etmesi gerekir.') ]));
  } catch (error) { if (container.isConnected) container.replaceChildren(h('p', { class: 'form-error' }, error.message), button('Yeniden yükle', () => loadDocuments(p, container), 'small')); }
}
function documentDialog(p) {
  const file = input('dosya', '', { type: 'file', accept: '.pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg', required: true });
  formDialog('Belge ekle', h('div', { class: 'stack' }, field('Fiş, fatura veya dekont', file, help('PDF, JPEG veya PNG; dosya başına en fazla 10 MB.')), state.role === 'editor' && field('İlgili kayıt', select('odemeId', [{ value: '', label: 'Alış belgesi' }, ...p.odemeler.map(payment => ({ value: payment.id, label: `Ödeme #${payment.id} · ${dateText(payment.tarih)} · ${money(payment.tutar)}` }))]))), 'Belgeyi yükle', async form => {
    const selected = file.files[0];
    if (!selected || selected.size === 0 || selected.size > 10 * 1024 * 1024) throw new Error('Boş olmayan ve 10 MB sınırını aşmayan bir dosya seçin.');
    if (!['application/pdf', 'image/png', 'image/jpeg'].includes(selected.type)) throw new Error('Yalnız PDF, JPEG veya PNG dosyası yükleyebilirsiniz.');
    const payload = new FormData(); payload.append('dosya', selected); if (values(form).odemeId) payload.append('odemeId', values(form).odemeId);
    await api(`/api/alis/${p.id}/belgeler`, { method: 'POST', body: payload }); closeModal(); toast('Belge eklendi.'); await navigate('purchase', p.id);
  });
}
function deleteDocument(p, doc) {
  formDialog('Belgeyi sil', h('p', { class: 'plain-note' }, `“${doc.dosyaAdi}” dosyası silinecek. Alış ve ödeme kaydı korunur.`), 'Belgeyi sil', async () => { await api(`/api/belgeler/${doc.id}`, { method: 'DELETE' }); closeModal(); toast('Belge silindi.'); await navigate('purchase', p.id); }, { danger: true });
}
async function renderTools(generation) {
  page('Ayarlar', 'Kanallar, erişim ve güvenlik');
  const results = await Promise.allSettled([api('/api/yedek/durum'), api('/api/alicilar'), api('/api/surum'), api('/api/kanallar'), api('/api/ayarlar'), api('/api/kasa-esikleri')]);
  if (generation !== renderId) return;
  const [backupResult, buyersResult, versionResult, channelsResult, settingsResult, thresholdsResult] = results;
  const backup = backupResult.status === 'fulfilled' ? backupResult.value : null;
  const buyers = buyersResult.status === 'fulfilled' ? buyersResult.value : null;
  const version = versionResult.status === 'fulfilled' ? versionResult.value : null;
  const channels = channelsResult.status === 'fulfilled' ? channelsResult.value : null;
  const settings = settingsResult.status === 'fulfilled' ? settingsResult.value : null;
  if (channels) state.channels = channels;
  const buyersContent = buyers ? h('div', {}, buyers.length ? buyers.map(buyer => h('div', { class: 'buyer-row' }, h('div', {}, h('strong', {}, buyer.ad), h('small', {}, `${buyer.kullanici} · ${buyer.aktif ? 'Aktif' : 'Pasif'}`)), button('Düzenle', () => buyerDialog(buyer), 'small'))) : h('p', { class: 'plain-note' }, 'Henüz alıcı hesabı eklenmedi.')) : h('p', { class: 'form-error' }, buyersResult.reason.message);
  const backupContent = h('div', { class: 'stack' }, backup ? h('div', {}, h('p', { class: 'plain-note' }, `Otomatik yedek: ${backup.otomatikEtkin ? 'Açık' : 'Kapalı'}`), h('p', { class: 'plain-note' }, `Son yedek: ${backup.sonYedek ? new Date(backup.sonYedek).toLocaleString('tr-TR') : 'Henüz oluşturulmadı'}`), h('p', { class: 'plain-note' }, `Son doğrulama: ${backup.sonDogrulama ? new Date(backup.sonDogrulama).toLocaleString('tr-TR') : 'Kayıt yok'}`), backup.hata && h('p', { class: 'form-error' }, backup.hata)) : h('p', { class: 'form-error' }, backupResult.reason.message), button('Şimdi yedek indir', event => run(event.currentTarget, async () => { const blob = await api('/api/yedek', { method: 'POST', binary: true }); download(blob, `kasa-yedek-${today()}.zip`); toast('Yedek dosyası indirildi.'); await navigate('tools'); }), 'primary'), help('Yedeği güvenli bir yerde saklayın. Geri yükleme, çalışan uygulama durdurularak sunucuda yapılır.'));
  const security = h('div', { class: 'stack' }, h('p', { class: 'plain-note' }, 'Şifre değişikliği eski oturumları kapatır ve mevcut kurtarma kodunu geçersiz kılar.'), button('Şifremi değiştir', passwordDialog), button('Yeni kurtarma kodu oluştur', recoveryCodeDialog), help('Kurtarma kodu bir kez gösterilir. Şifrenizden ayrı ve güvenli bir yerde saklayın.'));
  const versionContent = version ? h('div', { class: 'stack' }, h('p', { class: 'plain-note' }, `Sunucu sürümü ${version.surum} · En düşük istemci sürümü ${version.minimumIstemci}`), version.notlar && h('p', { class: 'plain-note' }, Array.isArray(version.notlar) ? version.notlar.join('\n') : version.notlar), safeExternalLink(version.indirmeAdresi, 'Windows uygulamasını indir')) : h('p', { class: 'form-error' }, versionResult.reason.message);
  const channelContent = channels ? h('div', {}, channels.map(channel => h('div', { class: 'buyer-row' }, h('div', {}, h('strong', {}, channel.ad), h('small', {}, `${channel.aktif ? 'Aktif' : 'Pasif'} · Açılış ${money(channel.acilisDevri)}`)), button('Düzenle', () => channelDialog(channel), 'small')))) : h('p', { class: 'form-error' }, channelsResult.reason.message);
  const opening = settings ? h('div', { class: 'stack' }, h('p', { class: 'plain-note' }, `Takip başlangıcı ${dateText(settings.takipBaslangic)} · Genel kasa açılışı ${money(settings.kasaAcilisDevri)}`), button('Kasa başlangıcını düzenle', () => openingDialog(settings)), button(settings.izleyiciSifreVarMi ? 'İzleyici şifresini değiştir' : 'İzleyici şifresi belirle', viewerPasswordDialog)) : h('p', { class: 'form-error' }, settingsResult.reason.message);
  const thresholds = thresholdsResult.status === 'fulfilled' ? cashControlsUi.thresholdSettings(thresholdsResult.value) : section('Kanal alt bakiye uyarıları', help(thresholdsResult.reason.message));
  $('#view').replaceChildren(h('div', { class: 'settings-grid' }, section('Kanallar', channelContent, button('+ Kanal ekle', () => channelDialog(), 'small')), thresholds, section('Kasa başlangıcı', opening), section('Alıcı hesapları', buyersContent, button('+ Alıcı ekle', () => buyerDialog(), 'small')), section('Hesap güvenliği', security), section('Bildirimler', h('div', { class: 'stack' }, help('Kart ve kredi hatırlatmalarını telefonunuza veya bu bilgisayara gönderin.'), button('İzin, saat ve cihaz ayarları', event => run(event.currentTarget, () => notificationUi.settings())))), section('Ekstre / Hareket Yükle', h('div', { class: 'stack' }, help('Kart ekstresi ve banka hesap hareketi PDF’lerinden seçtiğin satırları önizleyerek kaydet.'), button('PDF yükle ve incele', () => navigate('imports')))), section('Yedekleme', backupContent), section('Uygulama sürümü', versionContent)));
}

function pendingNotice(value) {
  return value > 0 ? h('div', { class: 'notice' }, h('strong', {}, `${money(value)} dağılım bekliyor. `), 'Bu ödeme genel kasaya yansımıştır; alış onaylanınca ilgili kanallara dağılır. Ortak gider değildir.') : null;
}
function cashActions() {
  return canEditCash() ? [button('+ Gelir gir', event => run(event.currentTarget, () => incomeDialog())), button('+ Gider kaydet', event => run(event.currentTarget, () => expenseDialog()), 'primary')] : [];
}
async function renderHome(generation) {
  page('Kasalar', 'Genel kasa ve kanal bakiyeleri', cashActions());
  const [panel, purchases] = await Promise.all([api('/api/rapor/panel'), canEditCash() ? api('/api/alis') : Promise.resolve([])]);
  if (generation !== renderId) return;
  const review = purchases.filter(p => p.durum === 'Incelemede');
  const paymentOverview = h('div');
  const balances = h('div', { class: 'channel-balances' });
  const unassignedDebt = h('div');
  const comparisonHistory = h('div');
  const thresholdStatus = h('div');
  let cardDebts = null;
  let thresholds = [];
  const drawBalances = debts => {
    cardDebts = debts;
    const matches = (channel, debt) => debt.kanalId != null && (channel.kanalId != null ? channel.kanalId === debt.kanalId : channel.kanal === debt.kanal);
    balances.replaceChildren(...panel.kanallar.map(k => {
      const debt = debts?.filter(row => matches(k, row)).reduce((total, row) => total + row.tutar, 0);
      const threshold = thresholds.find(row => k.kanalId != null ? row.kanalId === k.kanalId : row.kanal === k.kanal);
      return h('div', { class: `channel-balance${threshold?.etkin && threshold.esikAltinda ? ' below-threshold' : ''}` }, h('span', { class: 'channel-name' }, k.kanal), moneyNode(k.bakiye, k.bakiye < 0 ? 'negative' : ''), threshold?.etkin && threshold.esikAltinda && h('span', { class: 'badge pending' }, `Alt sınırın altında · Sınır ${money(threshold.tutar)}`), !runtime.saltOkunur && h('div', { class: 'channel-card-debt' }, debt == null ? 'Kart borcu yükleniyor…' : h('span', {}, 'Kalan kart borcu: ', moneyNode(debt))));
    }));
    const other = (debts || []).filter(row => row.tutar > 0 && !panel.kanallar.some(channel => matches(channel, row)));
    unassignedDebt.replaceChildren(...childValues([other.length && h('div', { class: 'notice' }, other.map(row => h('div', {}, `${row.kanalId == null ? 'Kanalı belirsiz kart borcu' : `${row.kanal} kart borcu`}: ${money(row.tutar)}`))), debts && help('Kart borçları kasa bakiyesine dahil edilmez; ödeme kaydedildiğinde kasadan düşer.')]));
  };
  drawBalances(null);
  const loadOverview = async days => {
    try {
      const data = await api(`/api/takip/ozet?gun=${days}`);
      if (generation === renderId) { drawBalances(data.kanalKartBorclari || []); paymentOverview.replaceChildren(financeUi.overview(data, days, selected => run(null, () => loadOverview(selected)))); }
    } catch (error) {
      if (generation === renderId) {
        for (const card of balances.children) { const note = card.querySelector('.channel-card-debt'); if (note) note.textContent = 'Kart borcu yüklenemedi.'; }
        paymentOverview.replaceChildren(help(`Ödeme özeti yüklenemedi: ${error.message}`), button('Yeniden dene', () => run(null, () => loadOverview(days)), 'small'));
      }
    }
  };
  if (!runtime.saltOkunur) loadOverview(30);
  const loadThresholds = async () => {
    try { const rows = await api('/api/kasa-esikleri'); if (generation === renderId) { thresholds = rows; drawBalances(cardDebts); thresholdStatus.replaceChildren(); } }
    catch (error) { if (generation === renderId) thresholdStatus.replaceChildren(help(`Kanal uyarıları yüklenemedi: ${error.message}`), button('Uyarıları yeniden yükle', () => run(null, loadThresholds), 'small')); }
  };
  const loadComparisons = async () => {
    try { const rows = await api('/api/kasa-kontrol'); if (generation === renderId) comparisonHistory.replaceChildren(cashControlsUi.history(rows)); }
    catch (error) { if (generation === renderId) comparisonHistory.replaceChildren(help(`Bakiye karşılaştırmaları yüklenemedi: ${error.message}`), button('Geçmişi yeniden yükle', () => run(null, loadComparisons), 'small')); }
  };
  if (!runtime.saltOkunur) { loadThresholds(); loadComparisons(); }
  const inbox = canEditCash() ? section('Alışlar', h('div', {}, review.length ? h('p', { class: 'plain-note' }, `${review.length} alış inceleme bekliyor. Malları ve kanal paylarını kontrol ederek onaylayabilirsiniz.`) : h('p', { class: 'plain-note' }, 'İnceleme bekleyen alış yok.'), review.slice(0, 4).map(purchaseRow)), button('Alışları aç', () => navigate('purchases'), 'small')) : null;
  const hero = h('div', { class: 'cash-hero' },
    h('div', {},
      h('span', { class: 'summary-label' }, 'Genel kasa'),
      h('strong', { class: 'cash-total money' }, money(panel.guncelKasa))));
  const summaryCards = h('div', { class: 'summary-strip' },
    summary('Bu haftanın sonucu', money(panel.buHaftaSonucu)),
    summary('Bu ayın sonucu', money(panel.buAySonucu)),
    summary('Kanal sayısı', String(panel.kanallar.length)));
  $('#view').replaceChildren(...childValues([
    hero,
    runtime.saltOkunur && help('Bu ekran canlı kasa kayıtlarını görüntüler. Bu sürümde kayıtlar değiştirilemez.'),
    summaryCards,
    pendingNotice(panel.dagilimBekleyenTutar),
    section('Kanal kasaları', h('div', {}, balances, thresholdStatus, unassignedDebt)),
    !runtime.saltOkunur && comparisonHistory,
    !runtime.saltOkunur && paymentOverview,
    help('Yeni kart takibinde kaydedilen kart ödemeleri kasadan düşer; kredi taksitleri kendi tarihinde otomatik işlenir. Eski kayıtlarda geçiş öncesi kasa kuralı korunur. Sabit, ortak ve dağılım bekleyen giderler nedeniyle kanal toplamı genel kasadan farklı olabilir.'),
    inbox
  ]));
}
async function renderWeekly(generation) {
  page('Haftalık kasa', 'Dönem gelirleri, giderleri ve devirler');
  const weeks = await api('/api/rapor/haftalik'); if (generation !== renderId) return;
  if (!weeks.length) { $('#view').replaceChildren(empty('Henüz kasa dönemi yok', 'Takip başlangıcını Ayarlar ekranından kontrol edin.')); return; }
  let selected = currentPeriod(weeks);
  const details = h('div');
  const period = select('donem', [...weeks].reverse().map(w => ({ value: w.donem.start, label: `${dateText(w.donem.start)} – ${dateText(w.donem.end)}` })), selected.donem.start, { 'aria-label': 'Haftalık kasa dönemi', onchange: () => { selected = weeks.find(w => w.donem.start === period.value); draw(); } });
  const draw = () => details.replaceChildren(...childValues([h('div', { class: 'summary-strip' }, summary('Genel kasa devri', money(selected.kasaDevir)), summary('Dönem gelen', money(selected.toplamGelen)), summary('Dönem giden', money(selected.toplamGiden))), pendingNotice(selected.dagilimBekleyenTutar), table(['Kanal', 'Gelen', 'Diğer gider', 'Dönem sonucu', 'Kanal devri'], selected.kanallar.map(k => [k.kanal, moneyNode(k.gelen), moneyNode(k.giden), moneyNode(k.sonuc), moneyNode(k.devir)])), h('p', { class: 'plan-note' }, `Genel kasa dönem sonucu: ${money(selected.kasaSonucu)}. Yeni kart takibinde kaydedilen ödemeler kasadan düşer; eski kartlarda geçiş öncesi erteleme kuralı sürer. Kredi taksitleri kendi tarihinde otomatik işlenir.`)]));
  $('#view').replaceChildren(h('div', { class: 'toolbar' }, period, canEditCash() && button('Dönem geliri gir', event => run(event.currentTarget, () => incomeDialog(selected.donem.start)), 'primary')), details); draw();
}
async function renderMonthly(generation, month = today().slice(0, 7)) {
  const request = ++monthlyRequest;
  page('Aylık kasa', 'Kanal bazında aylık gelir ve giderler');
  const [year, period] = month.split('-').map(Number);
  const report = await api(`/api/rapor/aylik?yil=${year}&ay=${period}`); if (generation !== renderId || request !== monthlyRequest) return;
  const total = monthlyTotals(report); const monthInput = input('ay', month, { type: 'month', required: true, 'aria-label': 'Rapor ayı' });
  const lock = h('div');
  $('#view').replaceChildren(...childValues([h('div', { class: 'toolbar' }, monthInput, button('Ayı göster', event => run(event.currentTarget, () => { if (monthInput.reportValidity()) return renderMonthly(generation, monthInput.value); }))), !runtime.saltOkunur && lock, h('div', { class: 'summary-strip' }, summary('Aylık gelen', money(total.incoming)), summary('Aylık gider', money(total.expenses)), summary('Ay sonucu', money(total.result))), pendingNotice(report.dagilimBekleyenTutar), table(['Kanal', 'Gelen', 'Diğer gider', 'Sabit gider', 'Kredi kartı', 'Ortak pay', 'Ay sonucu'], report.kanallar.map(k => [k.kanal, moneyNode(k.gelen), moneyNode(k.cariGiden), moneyNode(k.sabitGider), moneyNode(k.krediKarti), moneyNode(k.ortakPay), moneyNode(k.aySonucu)])), report.genelGelir > 0 && h('p', { class: 'plan-note' }, `Yalnız genel kasa geliri: ${money(report.genelGelir)}. Yukarıdaki aylık gelen toplamına dahildir; kanal kasalarına dağıtılmaz.`), report.genelGider > 0 && h('p', { class: 'plan-note' }, `Yalnız genel kasa gideri: ${money(report.genelGider)}. Yukarıdaki aylık gider toplamına dahildir; kanal kasalarına dağıtılmaz.`), h('p', { class: 'plan-note' }, 'Yeni kart takibinde ödeme kaydı; eski kartlarda geçiş öncesi erteleme kuralı geçerlidir. Kredi taksitleri tarihinde otomatik işlenir. Ortak giderler ve dağılım bekleyen tutarlar ayrı izlenir.')]));
  if (!runtime.saltOkunur) {
    try { const panel = await monthlyUi.lockPanel(month, () => generation === renderId ? renderMonthly(generation, month) : Promise.resolve()); if (generation === renderId && request === monthlyRequest) lock.replaceChildren(panel); }
    catch (error) { if (generation === renderId && request === monthlyRequest) lock.replaceChildren(help(`Ay kilidi yüklenemedi: ${error.message}`)); }
  }
}
async function renderTransactions(generation, filters = {}) {
  page('İşlemler', 'Genel kasa gider kayıtları', runtime.saltOkunur ? [] : [button('Gider raporu indir', event => run(event.currentTarget, async () => { state.channels = await api('/api/kanallar'); exportDialog(); })), ...cashActions()]);
  const start = filters.baslangic || today().slice(0, 8) + '01'; const end = filters.bitis || today();
  const query = new URLSearchParams({ baslangic: start, bitis: end }); if (filters.kanal) query.set('kanal', filters.kanal); if (filters.cari) query.set('cari', filters.cari);
  const [expenses, channels] = await Promise.all([api(`/api/islemler?${query}`), api('/api/kanallar')]); if (generation !== renderId) return; state.channels = channels;
  const form = h('form', { class: 'filter-period' }, field('Başlangıç', input('baslangic', start, { type: 'date', required: true })), field('Bitiş', input('bitis', end, { type: 'date', required: true })), field('Kanal', select('kanal', [{ value: '', label: 'Tüm kanallar' }, ...channels.map(c => ({ value: c.ad, label: c.ad })), { value: 'Ortak', label: 'Ortak' }, { value: 'Dağılım bekliyor', label: 'Dağılım bekliyor' }], filters.kanal)), field('Açıklama / ödeme yapılan yer', input('cari', filters.cari || '', { type: 'search' })), h('button', { type: 'submit', class: 'button' }, 'Listele'));
  form.addEventListener('submit', event => { event.preventDefault(); const data = values(form); run(form.querySelector('button'), async () => { if (data.baslangic > data.bitis) throw new Error('Bitiş tarihi başlangıçtan önce olamaz.'); await renderTransactions(generation, data); }); });
  const rows = expenses.map(e => [dateText(e.tarih), h('span', {}, e.cari, e.not && h('small', { class: 'table-sub' }, e.not)), h('span', {}, e.kanal, e.alisId && h('small', { class: 'table-sub' }, `Alış #${e.alisId}`)), ({ Cari: 'Diğer gider', SabitGider: 'Sabit gider', KrediKarti: 'Kredi kartı' })[e.tip] || e.tip, moneyNode(e.tutarTl), e.ekstreKayitId ? h('div', {}, help('Ekstre / Hareket Yükle bölümünden yönetilir.'), canEditCash() && button('Kaynak belgeyi aç', () => navigate('imports', { kayitId: e.ekstreKayitId }), 'small')) : e.aylikGiderOdemeId ? h('div', {}, help('Aylık Giderler bölümünden yönetilir.'), !runtime.saltOkunur && button('Aylık Giderler’i aç', () => navigate('monthly-expenses'), 'small')) : canEditCash() ? e.alisId ? button('Alışı aç', () => navigate('purchase', e.alisId), 'small') : h('div', { class: 'row-actions' }, button('Düzenle', event => run(event.currentTarget, () => expenseDialog(e)), 'small'), button('Sil', () => deleteExpense(e), 'small danger')) : '']);
  $('#view').replaceChildren(form, h('p', { class: 'plan-note' }, 'Kanal filtresi ilgili giderin tam tutarını gösterir; çok kanallı bir ödemenin kanal payı toplamı değildir. Alışa bağlı ödemeler alış kaydından düzeltilir.'), expenses.length ? table(['Tarih', 'Açıklama', 'Kanal', 'Tür', 'Tutar', ''], rows) : empty('Bu aralıkta gider yok', 'Tarih veya kanal filtresini değiştirerek diğer kayıtları görebilirsiniz.'));
}
function signedAmount(value) { const text = String(value).trim().replace(',', '.'); return text.startsWith('-') ? -amount(text.slice(1)) : amount(text); }
async function incomeDialog(periodStart = null) {
  const [weeks, channels] = await Promise.all([api('/api/rapor/haftalik'), api('/api/kanallar')]);
  if (!weeks.length) throw new Error('Gelir girmek için geçerli kasa dönemi gerekir.');
  const initial = periodStart || currentPeriod(weeks).donem.start;
  const period = select('donemStart', [...weeks].reverse().map(w => ({ value: w.donem.start, label: `${dateText(w.donem.start)} – ${dateText(w.donem.end)}` })), initial);
  const channel = select('kanal', [{ value: '', label: 'Kanal seçin' }, ...channels.map(c => ({ value: c.ad, label: c.ad }))], '', { required: true });
  const total = input('tutarTl', '0', { inputmode: 'decimal', required: true });
  const notice = h('div', { class: 'notice', role: 'status', 'aria-live': 'polite' });
  let incomes = []; let loading = false; let loadError = false; let version = 0; let form;
  const selectedChannel = () => channels.find(item => item.ad === channel.value);
  const fill = () => {
    const selected = incomeSelection(incomes, selectedChannel());
    total.value = selected.total;
    total.readOnly = loading || loadError || !channel.value || selected.readOnly;
    if (form) form.querySelector('button[type="submit"]').disabled = total.readOnly;
    notice.textContent = loading ? 'Dönem gelirleri yükleniyor…' : loadError ? 'Dönem gelirleri yüklenemedi. Başka dönem seçip yeniden deneyin; mevcut bilgilerle kayıt yapılamaz.' : selected.readOnly
      ? `Bu dönem ve kanal için ${selected.count} eski gelir kaydı var. Toplam ${money(selected.total)} kasaya dahildir. Geçmiş tutarları korumak için bu grup burada değiştirilemez. Başka dönem veya kanal seçerek normal gelir kaydı yapabilirsiniz.`
      : 'Buraya seçilen kanalın bu dönemdeki toplam gelirini yazın. Kayıt varsa yeni tutar öncekinin yerine geçer; üzerine eklenmez.';
  };
  const refresh = async () => {
    const generation = ++version; loading = true; loadError = true; incomes = []; fill();
    try {
      const data = await api(`/api/gelenler?donemStart=${period.value}`);
      if (generation === version) { incomes = data; loadError = false; }
    } finally { if (generation === version) { loading = false; fill(); } }
  };
  period.addEventListener('change', () => run(null, refresh)); channel.addEventListener('change', fill);
  await refresh();
  form = formDialog('Kanal geliri gir', h('div', { class: 'stack' }, field('Kasa dönemi', period), field('Kanal', channel), field('Bu dönem için toplam gelir (₺)', total), notice), 'Dönem toplamını kaydet', async () => {
    if (loading || loadError) throw new Error('Dönem gelirleri yüklenmeden kayıt yapılamaz. Lütfen yeniden deneyin.');
    if (incomeSelection(incomes, selectedChannel()).readOnly) throw new Error('Bu eski gelir grubu geçmiş tutarları korumak için değiştirilemez.');
    await api('/api/gelenler', { method: 'PUT', body: { donemStart: period.value, kanal: channel.value, tutarTl: signedAmount(total.value) } }); closeModal(); toast('Kanal geliri kaydedildi.'); await navigate(state.view);
  });
  fill();
}
async function expenseDialog(expense = null) {
  const [channels, cards] = await Promise.all([api('/api/kanallar'), api('/api/kredikartlari')]);
  const type = select('tip', [{ value: 'Cari', label: 'Diğer gider' }, { value: 'SabitGider', label: 'Sabit gider' }, { value: 'KrediKarti', label: 'Kredi kartı gideri' }], expense?.tip || 'Cari');
  const card = select('krediKartiId', [{ value: '', label: 'Kart seçilmedi' }, ...cards.map(k => ({ value: k.id, label: k.ad }))], expense?.krediKartiId);
  const cardField = field('Kredi kartı', card); const cardVisibility = () => { cardField.hidden = type.value !== 'KrediKarti'; }; type.addEventListener('change', cardVisibility); cardVisibility();
  const channel = select('kanal', [{ value: '', label: 'Kanal seçin' }, ...channels.filter(c => c.aktif || c.ad === expense?.kanal).map(c => ({ value: c.ad, label: c.ad })), { value: 'Ortak', label: 'Ortak' }], expense?.kanal || '', { required: true });
  formDialog(expense ? 'Gideri düzenle' : 'Gider kaydet', h('div', { class: 'stack' }, field('Açıklama / ödeme yapılan yer', input('cari', expense?.cari || '', { required: true, maxlength: 200 })), h('div', { class: 'form-grid' }, field('Tarih', input('tarih', expense?.tarih || today(), { type: 'date', required: true })), field('Tutar (₺)', input('tutarTl', expense?.tutarTl ?? '', { inputmode: 'decimal', required: true })), field('Kanal', channel), field('Gider türü', type)), cardField, field('Not', h('textarea', { name: 'not', maxlength: 2000 }, expense?.not || '')), help('Alış olarak kaydettiğiniz ödemenin ikinci bir giderini oluşturmayın. O alışın içinden ödeme ekleyin veya mevcut gideri bağlayın.')), 'Gideri kaydet', async form => {
    const data = values(form);
    const body = { tarih: data.tarih, cari: data.cari.trim(), tutarTl: signedAmount(data.tutarTl), kanal: data.kanal, tip: data.tip, not: data.not.trim() || null, krediKartiId: data.tip === 'KrediKarti' ? optionalId(card.value) : null };
    if (!expense && !await confirmSimilar(form, { tur: 'Gider', tarih: body.tarih, tutar: body.tutarTl, krediKartiId: body.krediKartiId, kanal: body.kanal, alisId: null }, body)) return;
    await api(expense ? `/api/islemler/${expense.id}` : '/api/islemler', { method: expense ? 'PUT' : 'POST', body }); closeModal(); toast('Gider kaydedildi.'); await navigate(state.view);
  });
}
function deleteExpense(expense) {
  formDialog('Gideri sil', h('p', { class: 'plain-note' }, `${dateText(expense.tarih)} tarihli “${expense.cari}” gideri (${money(expense.tutarTl)}) silinecek ve kasa sonuçları güncellenecek.`), 'Gideri sil', async () => { await api(`/api/islemler/${expense.id}`, { method: 'DELETE' }); closeModal(); toast('Gider silindi.'); await navigate('transactions'); }, { danger: true });
}
function channelDialog(channel = null) {
  formDialog(channel ? `${channel.ad} · Kanalı düzenle` : 'Kanal ekle', h('div', { class: 'stack' }, field('Kanal adı', input('ad', channel?.ad || '', { required: true, maxlength: 200 })), h('div', { class: 'form-grid' }, field('Açılış devri (₺)', input('acilisDevri', channel?.acilisDevri ?? '0', { required: true, inputmode: 'decimal' })), field('Görüntüleme sırası', input('sira', channel?.sira ?? state.channels.length, { required: true, type: 'number', step: 1, min: 0 }))), h('label', {}, input('aktif', '1', { type: 'checkbox', checked: channel?.aktif ?? true }), 'Kanal aktif'), help('Geçmiş kayıtları olan kanalı silmek yerine pasife alın. Açılış devri değişikliği kanal bakiyesini etkiler.'), channel && button('Kanalı sil', () => formDialog('Kanalı sil', h('p', { class: 'plain-note' }, `${channel.ad} kanalı silinecek. Geçmiş kaydı varsa silinemez.`), 'Kanalı sil', async () => { await api(`/api/kanallar/${channel.id}`, { method: 'DELETE' }); closeModal(); toast('Kanal silindi.'); await navigate('tools'); }, { danger: true }), 'danger small')), 'Kanalı kaydet', async form => { const data = values(form); await api(channel ? `/api/kanallar/${channel.id}` : '/api/kanallar', { method: channel ? 'PUT' : 'POST', body: { ad: data.ad.trim(), aktif: Boolean(data.aktif), sira: Number(data.sira), acilisDevri: signedAmount(data.acilisDevri) } }); closeModal(); toast('Kanal kaydedildi.'); await navigate('tools'); });
}
function openingDialog(settings) {
  formDialog('Kasa başlangıcını düzenle', h('div', { class: 'stack' }, field('Takip başlangıcı', input('takipBaslangic', settings.takipBaslangic, { type: 'date', required: true })), field('Genel kasa açılış devri (₺)', input('kasaAcilisDevri', settings.kasaAcilisDevri, { required: true, inputmode: 'decimal' })), help('Hareketler kaydedildikten sonra takip başlangıcı değiştirilemez. Açılış devri, tüm sonraki genel kasa bakiyelerini etkiler.')), 'Başlangıcı kaydet', async form => { const data = values(form); await api('/api/ayarlar', { method: 'PUT', body: { takipBaslangic: data.takipBaslangic, kasaAcilisDevri: signedAmount(data.kasaAcilisDevri) } }); closeModal(); toast('Kasa başlangıcı kaydedildi.'); await navigate('tools'); });
}
function viewerPasswordDialog() {
  formDialog('İzleyici şifresi', h('div', { class: 'stack' }, field('Yeni izleyici şifresi', input('yeniSifre', '', { type: 'password', required: true, minlength: 8, maxlength: 1024, autocomplete: 'new-password' })), help('İzleyici kasaları ve raporları okuyabilir; kayıtları değiştiremez. Şifre değişince eski izleyici oturumları kapanır.')), 'Şifreyi kaydet', async form => { await api('/api/ayarlar/izleyici-sifre', { method: 'PUT', body: values(form) }); closeModal(); toast('İzleyici şifresi güncellendi.'); });
}
function download(blob, name) {
  const url = URL.createObjectURL(blob); const link = h('a', { href: url, download: name }); document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 30000);
}
function safeExternalLink(url, title) {
  if (!url) return null;
  try { const parsed = new URL(url, location.origin); if (parsed.protocol !== 'https:' && parsed.origin !== location.origin) return null; return h('a', { class: 'button', href: parsed.href, rel: 'noopener', target: '_blank' }, title); } catch { return null; }
}
function passwordDialog() {
  formDialog('Şifremi değiştir', h('div', { class: 'stack' }, field('Mevcut şifre', input('mevcutSifre', '', { type: 'password', required: true, maxlength: 1024, autocomplete: 'current-password' })), field('Yeni şifre', input('yeniSifre', '', { type: 'password', required: true, minlength: 12, maxlength: 1024, autocomplete: 'new-password' }), help('En az 12 karakter kullanın.')), field('Yeni şifreyi tekrar girin', input('tekrar', '', { type: 'password', required: true, minlength: 12, maxlength: 1024, autocomplete: 'new-password' }))), 'Şifreyi değiştir', async form => { const data = values(form); if (data.yeniSifre !== data.tekrar) throw new Error('Yeni şifreler aynı olmalı.'); await api('/api/auth/sifre', { method: 'POST', body: { mevcutSifre: data.mevcutSifre, yeniSifre: data.yeniSifre } }); clearSession(); toast('Şifreniz değişti. Yeni şifrenizle giriş yapın.'); });
}
function recoveryCodeDialog() {
  formDialog('Kurtarma kodu oluştur', h('div', { class: 'stack' }, help('Yeni kod oluşturulunca önceki kod geçersiz olur. Kod yalnız bu ekranda bir kez gösterilir.'), field('Mevcut şifre', input('mevcutSifre', '', { type: 'password', required: true, maxlength: 1024, autocomplete: 'current-password' }))), 'Kodu oluştur', async form => {
    const result = await api('/api/auth/kurtarma-kodu', { method: 'POST', body: values(form) });
    const code = h('code', { class: 'recovery-code', tabindex: '0' }, result.kod);
    openModal('Kurtarma kodunuzu saklayın', h('div', { class: 'stack' }, h('div', { class: 'notice' }, 'Bu kod bir kez gösterilir ve yalnız bir kurtarma işleminde kullanılabilir. Pencereyi kapatmadan önce güvenli bir yere kaydedin.'), code, button('Kodu kopyala', event => run(event.currentTarget, async () => { if (!navigator.clipboard) throw new Error('Tarayıcı kopyalamaya izin vermiyor. Kodu seçip elle kopyalayabilirsiniz.'); await navigator.clipboard.writeText(code.textContent); toast('Kurtarma kodu kopyalandı.'); })), button('Kodu sakladım, kapat', closeModal, 'primary')));
    modalCleanup = () => { code.textContent = ''; result.kod = ''; };
  });
}
function buyerDialog(buyer = null) {
  formDialog(buyer ? `${buyer.ad} · Alıcı hesabı` : 'Alıcı hesabı ekle', h('div', { class: 'stack' }, field('Ad soyad', input('ad', buyer?.ad || '', { required: true, maxlength: 200 })), field('Kullanıcı adı', input('kullanici', buyer?.kullanici || '', { required: true, minlength: 3, maxlength: 64, pattern: '[a-z0-9._-]{3,64}', autocomplete: 'off' }), help('Küçük harf, sayı, nokta, tire ve alt çizgi kullanabilirsiniz.')), field(buyer ? 'Yeni şifre (değiştirmeyecekseniz boş bırakın)' : 'İlk giriş şifresi', input('sifre', '', { type: 'password', required: !buyer, minlength: 8, maxlength: 1024, autocomplete: 'new-password' })), buyer && h('label', {}, input('aktif', '1', { type: 'checkbox', checked: buyer.aktif }), 'Hesap aktif'), help('Alıcı yalnız kendi alışlarını ve belgelerini görür. Mali yönetim ekranlarına erişemez.')), 'Hesabı kaydet', async form => { const data = values(form); await api(buyer ? `/api/alicilar/${buyer.id}` : '/api/alicilar', { method: buyer ? 'PUT' : 'POST', body: { ad: data.ad.trim(), kullanici: data.kullanici.trim(), sifre: data.sifre || null, aktif: buyer ? Boolean(data.aktif) : true } }); closeModal(); toast('Alıcı hesabı kaydedildi.'); await navigate('tools'); });
}
function exportDialog() {
  const firstDay = today().slice(0, 8) + '01';
  formDialog('Gider raporu hazırla', h('div', { class: 'stack' }, h('div', { class: 'form-grid' }, field('Başlangıç', input('baslangic', firstDay, { type: 'date', required: true })), field('Bitiş', input('bitis', today(), { type: 'date', required: true }))), field('Kanal', select('kanal', [{ value: '', label: 'Tüm kanallar' }, ...state.channels.map(c => ({ value: c.ad, label: c.ad })), { value: 'Dağılım bekliyor', label: 'Dağılım bekliyor' }])), field('Dosya biçimi', select('bicim', [{ value: 'xlsx', label: 'Excel çalışma kitabı (.xlsx)' }, { value: 'csv', label: 'CSV veri dosyası (.csv)' }, { value: 'html', label: 'Yazdır / PDF olarak kaydet' }], 'xlsx')), help('Raporda gerçek gider tutarı bir kez listelenir. Kanal filtresi ödeme payını değil, o kanalla ilişkili ödemenin tam tutarını getirir.')), 'Raporu aç / indir', async form => {
    const data = values(form); if (data.baslangic > data.bitis) throw new Error('Bitiş tarihi başlangıçtan önce olamaz.');
    const url = `/api/disari-aktar?${new URLSearchParams(data)}`;
    if (data.bicim === 'html') { const link = h('a', { href: url, target: '_blank', rel: 'noopener' }); document.body.append(link); link.click(); link.remove(); toast('Rapor yeni sekmede açılıyor. Yazdır menüsünden PDF olarak kaydedebilirsiniz.'); }
    else { const blob = await api(url, { binary: true }); download(blob, `kasa-gider-${data.baslangic}-${data.bitis}.${data.bicim}`); }
    closeModal();
  });
}

// The session is carried only by the HttpOnly cookie. No credential or token is persisted here.
runtimeReady.then(() => {
  $('#recover-open').hidden = runtime.saltOkunur;
  if (runtime.saltOkunur) {
    $('#login-description').textContent = 'Canlı kasa kayıtlarını görüntülemek için giriş yapın.';
    $('#login-help').textContent = 'İzleyici girişi için kullanıcı adını boş bırakıp size verilen şifreyi kullanın. Bu sürüm yalnız kasa görüntüleme içindir.';
  }
  return api('/api/auth/me').then(user => enter(user.rol)).catch(() => { clearSession(); $('#login-form input').focus(); });
}).catch(error => {
  $('#login-screen').hidden = false;
  $('#login-form').hidden = true;
  $('#recover-open').hidden = true;
  $('#login-description').textContent = error.message || 'Kasa ayarı yüklenemedi. Sayfayı yenileyin.';
});
