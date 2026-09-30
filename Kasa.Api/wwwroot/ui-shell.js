// Uygulama kabuğu: oturum ve çalışma ayarı durumu, API çağrısı, bildirim, işlem düğmesi (run/act), pencere ve form akışı
// (formDialog, benzer kayıt onayı, istek kimliği), sayfa başlığı ve gezinme. Ekranlar app.js'te registerScreens ile kaydedilir;
// bu modül ekranları içe aktarmaz.
import {
  loadRuntime,
  cashEditingAllowed,
  errorMessage,
  fieldErrors,
  sessionExpired,
  runtimeRequestAllowed,
  screenBoundRead,
  abortedRequestError,
  isAbortError,
  SIMILAR_RULE_TEXT,
  money,
  dateText,
  navigationFor,
} from './ui-core.js';
import { createPushClient } from './push-client.js';
import { $, h, button, help, empty } from './ui-dom.js';

const state = { role: null, view: 'home', purchases: [], channels: [], cards: [], query: '', status: '', selected: null, epoch: 0 };
let runtime = null;
const runtimeReady = loadRuntime(fetch).then(config => {
  runtime = config;
  return config;
});
const canEditCash = () => cashEditingAllowed(state.role, runtime);
// Kasa düzenleme koruması (aylık gider, kasa kontrolü, ekstre aktarma ve kart ekranları): editör değilse ya da salt okunur
// sürümdeyse işlem başlamaz. İleti ekrana göre verilebilir.
function requireEditor(message = 'Bu işlem için editör hesabı gerekir.') {
  if (!canEditCash()) throw new Error(message);
}
const modal = $('#modal');
let modalCleanup = null;
let renderId = 0;
// Ekranın rapor okumaları (screenBoundRead) bu denetleyicinin sinyaliyle gider: ekran değişince ya da oturum kapanınca istek
// tarayıcıda iptal edilir, sunucu da hesabı keser; geç yanıt ekrana yansımaz.
let screenAbort = null;
// Ekran çizimleri görünüm adıyla kaydedilir (registerScreens): gezinme ekranların kendisini bilmez.
const screens = new Map();
const isOpen = form => modal.open && $('#modal-content').querySelector('form') === form;
const push = createPushClient({ api, session: () => (canEditCash() ? state.epoch : null) });
// Bilgi iletisi kibar (polite) #notifications bölgesinde duyurulur ve 6 sn sonra kalkar. Hata iletisi kendiliğinden kaybolmaz
// (WAI-ARIA APG uyarı deseni; WCAG 2.2.3): assertive #alerts (role="alert") bölgesinde kalır, kapatma düğmesiyle kapanır ve
// odağı almaz. Odaktaki kapatma düğmesi kalkınca odak belge başına düşmez, ana içeriğe geçer.
function toast(message, error = false) {
  if (!error) {
    const item = h('div', { class: 'toast' }, message);
    $('#notifications').append(item);
    setTimeout(() => item.remove(), 6000);
    return;
  }
  const close = h('button', { type: 'button', class: 'toast-close', 'aria-label': 'Hata iletisini kapat' }, '×');
  const item = h('div', { class: 'toast error' }, h('span', {}, message), close);
  close.addEventListener('click', () => {
    const focused = document.activeElement === close;
    item.remove();
    if (focused) $('#main').focus();
  });
  $('#alerts').append(item);
}
function clearSession() {
  state.epoch++;
  renderId++;
  state.role = null;
  state.purchases = [];
  state.channels = [];
  state.cards = [];
  state.selected = null;
  state.query = '';
  state.status = '';
  $('#view').replaceChildren();
  $('#navigation').replaceChildren();
  $('#application').hidden = true;
  $('#login-screen').hidden = false;
  screenAbort?.abort();
  screenAbort = null;
  $('#alerts').replaceChildren(); // önceki oturumun hataları sonraki kullanıcıya kalmaz
  closeModal(true);
}
async function api(path, options = {}) {
  await runtimeReady;
  const headers = new Headers(options.headers || {});
  const method = (options.method || 'GET').toUpperCase();
  if (!runtimeRequestAllowed(runtime, path, method)) throw new Error('Bu sürüm yalnız kasa görüntüleme içindir. Kayıtlar değiştirilemez.');
  if (!['GET', 'HEAD'].includes(method)) headers.set('X-Kasa-Request', '1');
  let body = options.body;
  if (body != null && !(body instanceof FormData)) {
    headers.set('Content-Type', 'application/json');
    body = JSON.stringify(body);
  }
  const epoch = state.epoch;
  // options.screen === false: pencere (diyalog) verisi ekrana ait değildir; rapor ucundan okunsa da gezinmede iptal edilmez.
  const signal = options.signal ?? (options.screen !== false && screenBoundRead(path, method) ? screenAbort?.signal : undefined);
  let response;
  try {
    response = await fetch(path, { ...options, method, body, headers, signal, credentials: 'same-origin', cache: 'no-store' });
  } catch {
    if (signal?.aborted) throw abortedRequestError();
    throw new Error('Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.');
  }
  if (epoch !== state.epoch) throw new Error('Oturum değişti. Lütfen yeniden deneyin.');
  if (!response.ok) {
    let result;
    try {
      result = await response.json();
    } catch {
      result = null;
    }
    const error = new Error(errorMessage(result, response.status));
    error.status = response.status;
    error.fields = fieldErrors(result);
    if (sessionExpired(response.status, path)) clearSession();
    throw error;
  }
  if (options.binary) {
    const result = await response.blob();
    if (epoch !== state.epoch) throw new Error('Oturum değişti. Lütfen yeniden deneyin.');
    return result;
  }
  if (response.status === 204) return null;
  let text;
  try {
    text = await response.text();
  } catch (error) {
    if (signal?.aborted) throw abortedRequestError();
    throw error;
  }
  if (epoch !== state.epoch) throw new Error('Oturum değişti. Lütfen yeniden deneyin.');
  return text ? JSON.parse(text) : null;
}
// Hata kutusu yalnız belgeye bağlıysa ve (diyalogdaysa) diyaloğu açıksa görünür.
function gorunur(node) {
  if (!node?.isConnected) return false;
  const dialog = node.closest?.('dialog');
  return !dialog || dialog.open;
}
// Kapanmış pencerenin hatası görünmeyen kutuya yazılmaz; hangi pencereden geldiği belirtilerek bildirim olarak gösterilir.
// Ekran değişince iptal edilen okuma hata değildir; bildirim çıkmaz.
async function run(control, work, errorBox = null, title = '') {
  if (control?.disabled) return;
  if (control) control.disabled = true;
  if (errorBox) {
    errorBox.hidden = true;
    errorBox.textContent = '';
  }
  try {
    await work();
  } catch (error) {
    if (isAbortError(error)) return;
    if (errorBox && gorunur(errorBox)) {
      errorBox.textContent = error.message;
      errorBox.hidden = false;
      errorBox.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    } else toast(title ? `${title}: ${error.message}` : error.message, true);
  } finally {
    if (control) control.disabled = false;
  }
}
// İşlem düğmesi (aylık gider, kart/kredi, ekstre ve bildirim ekranları): iş sürerken düğme kapalıdır, ikinci basış yok sayılır,
// hata bildirim olarak görünür (run). props düğmeye aynen geçer (ör. disabled).
function act(label, work, style = '', props = {}) {
  return button(label, event => run(event.currentTarget, work), style, props);
}
// Kaydı süren form: yanıt gelene kadar pencere (iptal edilebilir) ESC/geri hareketiyle kazara kapanmaz. Vazgeç ve × açık kalır:
// iOS'ta ESC/geri hareketi yok, isteğin de zaman aşımı yok; kapatılan pencerenin sonucu run() ile bildirim olarak görünür.
let busyForm = null;
// Yanıt bekleyen kayıt formları. Kayıt sürerken kapatılıp (Vazgeç, ×, ESC) yerine yeni pencere açıldıysa, önceki kaydın geç
// gelen başarısındaki argümansız closeModal() çağrısı yeni pencereyi kapatmaz: açık pencere kendi kaydını beklemiyorken
// kapatılmış bir pencerenin kaydı sürüyorsa çağrı o kayda aittir. Kullanıcı eylemleri (olay nesnesiyle) ve iç çağrılar
// (true) her zaman kapatır. Sınır: iki kayıt aynı anda sürerken önce biten eski kayıt, yenisinin penceresini kapatabilir;
// o zaman yeni kaydın sonucu run() ile bildirim olarak görünür.
const savingForms = new Set();
function strayClose() {
  const open = modal.open ? $('#modal-content').querySelector('form') : null;
  if (open && savingForms.has(open)) return false;
  return [...savingForms].some(form => !isOpen(form));
}
function closeModal(explicit) {
  if (explicit === undefined && strayClose()) return;
  busyForm = null;
  if (modal.open) modal.close();
  if (modalCleanup) modalCleanup();
  modalCleanup = null;
  $('#modal-content').replaceChildren();
}
// Pencere kapanınca çalışacak temizlik (ör. kurtarma kodunu ekrandan silmek): pencereyi açan akış verir.
function setModalCleanup(cleanup) {
  modalCleanup = cleanup;
}
function openModal(title, content, wide = false) {
  closeModal(true);
  $('#modal-title').textContent = title;
  $('#modal-content').replaceChildren(content);
  modal.classList.toggle('wide', wide);
  modal.showModal();
}
$('#modal-close').addEventListener('click', closeModal);
// ESC ve Android geri hareketi. Yalnız diyaloğun kendi kapatma isteği işlenir: dosya alanı da seçici kapatılınca ya da aynı
// dosya yeniden seçilince yukarı taşınan, iptal edilemez bir cancel olayı gönderir; o olay pencereyi kapatmaz.
// Tarayıcı olayı iptal edilemez gönderirse (art arda basış) pencere kapanır ve içerik de temizlenir; kayıt sonradan hata
// verirse run() onu bildirim olarak gösterir; başarıda kaydın kendi bildirimi ya da sayfa yenilemesi görünür.
// Engellenen kapatmanın bildirimi gerçek davranışı söyler: yanıt gelene kadar pencere açık kalır, hata pencerede görünür.
// Vazgeç (ya da ×) kaydı durdurmaz; kapatılan pencerenin hatası bildirim olarak çıkar, başarılı kayıt ekrana yansır ve o sırada
// açılmış başka pencereyi kapatmaz.
const BUSY_CLOSE_MESSAGE =
  'Kayıt sürüyor; yanıt gelene kadar pencere açık kalır ve hata olursa burada görünür. Beklemeden kapatmak için Vazgeç’e basın: kayıt durmaz, tamamlanabilir; hata olursa bildirim olarak gösterilir, başarılı kayıt ekrana yansır.';
modal.addEventListener('cancel', event => {
  if (event.target !== modal) return;
  if (busyForm && isOpen(busyForm) && event.cancelable) {
    event.preventDefault();
    toast(BUSY_CLOSE_MESSAGE);
    return;
  }
  closeModal(event);
});
function formDialog(title, content, submitLabel, save, { wide = false, danger = false } = {}) {
  const errors = h('p', { class: 'form-error', role: 'alert', hidden: true });
  const submit = h('button', { type: 'submit', class: `button ${danger ? 'danger' : 'primary'}` }, submitLabel);
  const form = h('form', { class: 'stack' }, content, errors, h('div', { class: 'modal-actions' }, button('Vazgeç', closeModal), submit));
  // Meşgul işareti yalnız bu form açıkken konur; kayıt başka pencere açtıysa (önizleme → onay) closeModal onu zaten kaldırmıştır.
  const markBusy = busy => {
    if (busy ? isOpen(form) : busyForm === form) busyForm = busy ? form : null;
  };
  // Sunucunun alan hataları (ValidationProblem) ilgili denetimin altında da gösterilir; sonraki denemede silinir.
  let marked = [];
  const clearFields = () => {
    for (const [control, note] of marked) {
      control.removeAttribute('aria-invalid');
      note.remove();
    }
    marked = [];
  };
  const markFields = fields => {
    for (const [name, message] of Object.entries(fields || {})) {
      let control = null;
      try {
        control = form.querySelector(`[name="${name}"]`);
      } catch {
        control = null;
      }
      if (!control?.parentNode) continue;
      const note = h('p', { class: 'form-error field-error' }, message);
      // İşaretli tutarda (± seçici + tutar ızgarası) not ızgaraya değil, tutar etiketinin altına eklenir.
      control.setAttribute('aria-invalid', 'true');
      (control.closest('.signed-field') || control.parentNode).append(note);
      marked.push([control, note]);
    }
  };
  form.addEventListener('submit', event => {
    event.preventDefault();
    if (form.reportValidity())
      run(
        submit,
        async () => {
          markBusy(true);
          savingForms.add(form);
          clearFields();
          try {
            await save(form);
          } catch (error) {
            markFields(error?.fields);
            throw error;
          } finally {
            savingForms.delete(form);
            markBusy(false);
          }
        },
        errors,
        title
      );
  });
  openModal(title, form, wide);
  return form;
}
// contract-6: gider, gelir, kanal ve ayar düzenlemesi okunan kaydın sürümünü gönderir. Kayıt arada başka oturumda değiştiyse sunucu
// 409 verir: ileti formda görünür, arkadaki liste (ya da formun verisi) güncel kayıtlarla yenilenir; kayıt yeniden açılınca güncel
// sürümle kaydedilir. Yenileme hatası kayıt hatasını örtmez.
async function refreshOnConflict(work, refresh) {
  try {
    return await work();
  } catch (error) {
    if (error?.status === 409) {
      try {
        await refresh();
      } catch {
        /* kayıt hatası gösterilir */
      }
    }
    throw error;
  }
}
const similarApprovals = new WeakMap();
const similarPanels = new WeakMap();
async function confirmSimilar(form, query, payload) {
  const signature = JSON.stringify({ query, payload });
  if (!form.isConnected || !modal.open) return false;
  if (similarApprovals.get(form) === signature) return true;
  const oldPanel = similarPanels.get(form);
  if (oldPanel) oldPanel.hidden = true;
  let records;
  try {
    records = await api('/api/islemler/benzerlik', { method: 'POST', body: query });
  } catch (error) {
    throw new Error(`Benzer kayıt kontrolü tamamlanamadı. Kayıt yapılmadı; yeniden deneyin. ${error.message}`);
  }
  if (!Array.isArray(records)) throw new Error('Benzer kayıt kontrolünden geçerli yanıt alınamadı. Kayıt yapılmadı; yeniden deneyin.');
  if (!form.isConnected || !modal.open) return false;
  if (!records.length) return true;
  const panel = oldPanel || h('div', { class: 'notice similar-warning', role: 'status', tabindex: '-1' });
  const sourceNames = {
    Islem: 'Gider',
    KartHarcama: 'Kart harcaması',
    KartOdeme: 'Kart ödemesi',
    EskiKartOdeme: 'Eski kart ödemesi',
    KrediTaksidi: 'Kredi taksidi',
    EskiKrediTaksidi: 'Eski kredi taksidi',
  };
  // Kural metni sunucunun kuralını anlatır (±3 gün; kanalsız/çok kanallı kayıtlar ve kart ödemeleri her kanalda); kanal etiketi
  // kaydın kasadan düştüğü kanal(lar)dır.
  panel.replaceChildren(
    h('strong', {}, 'Benzer kayıt bulundu'),
    help(`${SIMILAR_RULE_TEXT} Aynı ödemeyi yeniden girmediğinizi kontrol edin. Ayrı bir işlemse yine kaydedebilirsiniz.`),
    h(
      'ul',
      { class: 'similar-records' },
      records.map(record =>
        h(
          'li',
          {},
          `${sourceNames[record.kaynak] || 'Kayıt'} #${record.id} · ${dateText(record.tarih)} · ${money(record.tutar)} · ${record.aciklama || 'Açıklama yok'}${record.kanalEtiketi ? ` · ${record.kanalEtiketi}` : ''}${record.alisId ? ` · Alış #${record.alisId}` : ''}`
        )
      )
    ),
    h(
      'div',
      { class: 'row-actions' },
      button('Vazgeç', closeModal),
      button(
        'Ayrı işlem olarak kaydet',
        () => {
          similarApprovals.set(form, signature);
          form.requestSubmit();
        },
        'primary'
      )
    )
  );
  panel.hidden = false;
  if (!oldPanel) {
    form.append(panel);
    similarPanels.set(form, panel);
  }
  panel.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  panel.focus();
  return false;
}
// Reuse the same key after an uncertain response; changed fields get a fresh key.
function requestIdentity() {
  let previous, id;
  return payload => {
    const { surum, hedefSurum, ...stable } = payload;
    const serialized = JSON.stringify(stable);
    if (serialized !== previous) {
      previous = serialized;
      id = crypto.randomUUID();
    }
    return { ...payload, istekId: id };
  };
}
function page(title, context, actions = []) {
  $('#page-title').textContent = title;
  $('#page-context').textContent = runtime?.saltOkunur ? `Kasa görüntüleme · ${context}` : context;
  $('#page-actions').replaceChildren(...actions);
}
function nav() {
  const items = navigationFor(state.role, runtime);
  $('#navigation').replaceChildren(
    ...items.map(([key, title, icon]) =>
      h(
        'button',
        {
          type: 'button',
          class: `nav-button${state.view === key || (key === 'purchases' && state.view === 'purchase') ? ' active' : ''}`,
          'aria-current': state.view === key ? 'page' : null,
          onclick: () => navigate(key),
        },
        h('span', { class: 'nav-icon', 'aria-hidden': 'true' }, icon),
        title
      )
    )
  );
  $('#role-label').textContent = state.role === 'editor' ? 'Editör hesabı' : state.role === 'alici' ? 'Alıcı hesabı' : 'İzleyici hesabı';
}
function registerScreens(entries) {
  for (const [view, render] of Object.entries(entries)) screens.set(view, render);
}
async function navigate(view, id = null) {
  if (!navigationFor(state.role, runtime).some(([key]) => key === (view === 'purchase' ? 'purchases' : view)))
    throw new Error('Bu ekran için erişiminiz yok.');
  state.view = view;
  state.selected = id;
  nav();
  screenAbort?.abort();
  screenAbort = new AbortController();
  const generation = ++renderId;
  const content = $('#view');
  content.setAttribute('aria-busy', 'true');
  content.replaceChildren(
    h('div', { class: 'empty' }, h('span', { class: 'loader', 'aria-hidden': 'true' }), h('p', {}, 'Kayıtlar yükleniyor…'))
  );
  try {
    await screens.get(view)?.(generation, id);
  } catch (error) {
    if (generation === renderId && state.role)
      content.replaceChildren(
        empty(
          'Kayıtlar yüklenemedi',
          error.message,
          button('Yeniden dene', () => navigate(view, id), 'primary')
        )
      );
  } finally {
    if (generation === renderId) {
      content.setAttribute('aria-busy', 'false');
      if (state.role && !modal.open) $('#main').focus({ preventScroll: true });
    }
  }
}

export {
  state,
  runtime,
  runtimeReady,
  canEditCash,
  requireEditor,
  renderId,
  isOpen,
  push,
  toast,
  clearSession,
  api,
  run,
  act,
  closeModal,
  setModalCleanup,
  openModal,
  formDialog,
  refreshOnConflict,
  confirmSimilar,
  requestIdentity,
  page,
  registerScreens,
  navigate,
};
