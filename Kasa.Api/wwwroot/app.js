import {
  money,
  dateText,
  today,
  cents,
  serverCents,
  sumCents,
  viewerPasswordError,
  VIEWER_PASSWORD_SHORT_MESSAGE,
  newPasswordRepeatError,
  permissions,
  statusLabels,
  filteredPurchases,
  purchasePayload,
  paymentCardChoices,
  childValues,
  logoutAndClear,
  currentPeriod,
  monthlyTotals,
  incomeSelection,
  isAbortError,
  dataHealthWarning,
  trackedCardPayment,
  installmentFields,
  detachAllocations,
} from './ui-core.js?v=2.3.0';
import { createFinanceUi } from './finance-ui.js?v=2.3.0';
import { createNotificationUi } from './notification-ui.js?v=2.3.0';
import { createMonthlyUi } from './monthly-ui.js?v=2.3.0';
import { createCashControlsUi } from './cash-controls-ui.js?v=2.3.0';
import { createStatementImportUi } from './statement-import-ui.js?v=2.3.0';
import { notificationRoute } from './push-client.js?v=2.3.0';
import {
  $,
  h,
  button,
  input,
  field,
  select,
  section,
  badge,
  help,
  monthPicker,
  moneyNode,
  allocationTags,
  values,
  optionalId,
  empty,
  summary,
  table,
  signedAmountField,
} from './ui-dom.js';
import {
  state,
  runtime,
  runtimeReady,
  canEditCash,
  renderId,
  push,
  toast,
  clearSession,
  api,
  run,
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
} from './ui-shell.js';
import {
  purchaseTotals,
  documentFileName,
  linkableExpensesPath,
  documentsPath,
  documentRemovable,
  documentDescription,
  documentDeletePayload,
  backupDiskLines,
  restoreReport,
} from './ui-core.js?v=2.3.0';

let monthlyRequest = 0;
const financeUi = createFinanceUi();
const monthlyUi = createMonthlyUi();
const cashControlsUi = createCashControlsUi();
const statementImportUi = createStatementImportUi();
const notificationUi = createNotificationUi();
// Gezinmenin çizdiği ekranlar (navigate): görünüm → çizim. Alış ekranları çizimden önce alış listesini yükler; bu arada başka
// ekrana geçildiyse çizmez.
registerScreens({
  purchases: async generation => {
    await loadPurchases();
    if (generation !== renderId) return;
    renderPurchases();
  },
  purchase: async (generation, id) => {
    await loadPurchases();
    if (generation !== renderId) return;
    renderPurchase(id);
  },
  home: generation => renderHome(generation),
  weekly: generation => renderWeekly(generation),
  monthly: generation => renderMonthly(generation),
  'monthly-expenses': generation => monthlyUi.render(generation),
  imports: (generation, id) => statementImportUi.render(generation, id),
  transactions: generation => renderTransactions(generation),
  tools: generation => renderTools(generation),
  cards: (generation, id) => financeUi.renderCards(generation, id),
  loans: (generation, id) => financeUi.renderLoans(generation, id),
  notifications: generation => notificationUi.render(generation),
});

async function loadPurchases() {
  const [purchases, channels] = await Promise.all([api('/api/alis'), api('/api/alis/kanallar')]);
  state.purchases = purchases;
  state.channels = channels;
}
async function loadPaymentLookups() {
  state.cards = await api('/api/kredikartlari');
}
async function enter(role) {
  await runtimeReady;
  if (runtime.saltOkunur && !['editor', 'viewer'].includes(role)) throw new Error('Bu görüntüleme sürümünde alıcı girişi kullanılamıyor.');
  state.role = role;
  $('#login-screen').hidden = true;
  $('#application').hidden = false;
  const destination = !runtime.saltOkunur && typeof location !== 'undefined' ? notificationRoute(location.hash, role) : null;
  await navigate(destination?.view || (role === 'alici' ? 'purchases' : 'home'), destination?.id || null);
  if (runtime.saltOkunur) $('#version').textContent = runtime.surum ? `Kasa görüntüleme · ${runtime.surum}` : 'Kasa görüntüleme';
  else
    api('/api/surum')
      .then(version => {
        $('#version').textContent = `Sürüm ${version.surum}`;
      })
      .catch(() => {});
}
$('#login-form').addEventListener('submit', event => {
  event.preventDefault();
  const form = event.currentTarget;
  run(
    form.querySelector('button'),
    async () => {
      const result = await api('/api/auth/login', { method: 'POST', body: values(form) });
      form.reset();
      await enter(result.rol);
    },
    $('#login-error')
  );
});
$('#logout').addEventListener('click', event =>
  run(event.currentTarget, () =>
    logoutAndClear(
      async () => {
        if (canEditCash()) {
          try {
            await push.disable({ bestEffort: true });
          } catch {}
        }
        await api('/api/auth/logout', { method: 'POST' });
      },
      () => {
        clearSession();
        $('#login-form input').focus();
      }
    )
  )
);
if (typeof window !== 'undefined')
  window.addEventListener('hashchange', () => {
    if (!state.role || runtime?.saltOkunur) return;
    const target = notificationRoute(location.hash, state.role);
    if (target) run(null, () => navigate(target.view, target.id));
  });
// Yeni şifre iki kez yazılır: kurtarma kodu tek kullanımlıktır, tekrar uyuşmazsa istek gönderilmez ve kod harcanmaz.
function repeatedPassword(data) {
  const rule = newPasswordRepeatError(data.yeniSifre, data.tekrar);
  if (rule) throw Object.assign(new Error(rule), { fields: { tekrar: rule } });
}
$('#recover-open').addEventListener('click', () =>
  formDialog(
    'Editör hesabını kurtar',
    h(
      'div',
      { class: 'stack' },
      help('Daha önce oluşturduğunuz tek kullanımlık kurtarma kodunu girin. Başarılı kurtarma tüm eski oturumları kapatır.'),
      field('Kullanıcı adı', input('kullanici', '', { required: true, autocomplete: 'username', maxlength: 64 })),
      field('Kurtarma kodu', input('kod', '', { required: true, autocomplete: 'off' })),
      field(
        'Yeni şifre',
        input('yeniSifre', '', { type: 'password', required: true, minlength: 12, maxlength: 1024, autocomplete: 'new-password' }),
        help('En az 12 karakter kullanın.')
      ),
      field(
        'Yeni şifreyi tekrar girin',
        input('tekrar', '', { type: 'password', required: true, minlength: 12, maxlength: 1024, autocomplete: 'new-password' })
      )
    ),
    'Şifreyi yenile',
    async form => {
      const data = values(form);
      repeatedPassword(data);
      await api('/api/auth/kurtar', { method: 'POST', body: { kullanici: data.kullanici, kod: data.kod, yeniSifre: data.yeniSifre } });
      clearSession();
      toast('Şifreniz yenilendi. Yeni şifrenizle giriş yapın.');
    }
  )
);

function renderPurchases() {
  page(state.role === 'alici' ? 'Alışlarım' : 'Alışlar', 'Alış defteri', [button('+ Yeni alış', () => editPurchase(), 'primary')]);
  const { total, remaining, reviewing: count } = purchaseTotals(state.purchases);
  const list = h('div', { class: 'purchase-list' });
  const draw = () => {
    const items = filteredPurchases(state.purchases, state.query, state.status);
    list.replaceChildren(
      ...(items.length
        ? items.map(purchaseRow)
        : [
            empty(
              state.purchases.length ? 'Bu aramada alış yok' : 'İlk alışınızı kaydedin',
              state.purchases.length
                ? 'Arama sözcüğünü veya durum filtresini değiştirin.'
                : 'Tedarikçiyi, alınan malları ve hangi kanala ait olduklarını tek bir kayıtta tutun.',
              state.purchases.length ? null : button('Yeni alış oluştur', () => editPurchase(), 'primary')
            ),
          ])
    );
  };
  const search = input('arama', state.query, {
    type: 'search',
    placeholder: 'Tedarikçi, mal veya alış ara…',
    'aria-label': 'Alışlarda ara',
    oninput: event => {
      state.query = event.target.value;
      draw();
    },
  });
  const filter = select(
    'durum',
    [{ value: '', label: 'Tüm durumlar' }, ...Object.entries(statusLabels).map(([value, label]) => ({ value, label }))],
    state.status,
    {
      'aria-label': 'Alış durumu',
      onchange: event => {
        state.status = event.target.value;
        draw();
      },
    }
  );
  $('#view').replaceChildren(
    h(
      'div',
      { class: 'summary-strip' },
      summary('Alış toplamı', money(total), `${state.purchases.length} kayıt`),
      summary('Ödenmeyi bekleyen', money(remaining), 'Tüm açık alışlar'),
      summary('İnceleme bekleyen', String(count), 'Editörün onayında')
    ),
    h('div', { class: 'toolbar' }, h('div', { class: 'search' }, search), filter),
    list
  );
  draw();
}
function purchaseRow(p) {
  return h(
    'button',
    {
      type: 'button',
      class: 'purchase-row',
      onclick: () => navigate('purchase', p.id),
      'aria-label': `${p.tedarikci}, ${money(p.toplam)}, ${statusLabels[p.durum] || p.durum}`,
    },
    h(
      'span',
      { class: 'purchase-monogram', 'aria-hidden': 'true' },
      String(p.tedarikci || 'A')
        .slice(0, 1)
        .toLocaleUpperCase('tr-TR')
    ),
    h(
      'div',
      {},
      h('div', { class: 'purchase-name' }, p.tedarikci),
      h('div', { class: 'purchase-meta' }, `#${p.id} · ${dateText(p.tarih)}${state.role !== 'alici' ? ` · ${p.alici || 'Editör'}` : ''}`)
    ),
    h('div', { class: 'status-cell' }, badge(p.durum)),
    h(
      'div',
      { class: 'purchase-total money' },
      money(p.toplam),
      h('small', {}, p.kalan > 0 ? `${money(p.kalan)} kalan` : 'Ödeme tamamlandı')
    ),
    h('span', { class: 'chevron', 'aria-hidden': 'true' }, '›')
  );
}
function renderPurchase(id) {
  const p = state.purchases.find(item => item.id === id);
  if (!p) {
    page('Alış bulunamadı', 'Alış defteri');
    $('#view').replaceChildren(
      empty(
        'Bu kayıt artık listede değil',
        'Alış listesine dönerek güncel kayıtları görebilirsiniz.',
        button('Alışlara dön', () => navigate('purchases'))
      )
    );
    return;
  }
  const rights = permissions(state.role, p);
  page(p.tedarikci, `Alış #${p.id}`, [badge(p.durum)]);
  const metadata = h(
    'dl',
    { class: 'metadata' },
    [
      ['Alış tarihi', dateText(p.tarih)],
      ['Alıcı', p.alici || 'Editör'],
    ].map(([title, value]) => h('div', {}, h('dt', {}, title), h('dd', {}, value)))
  );
  const items = h(
    'div',
    {},
    p.kalemler.map(line =>
      h(
        'div',
        { class: 'item-detail' },
        h('div', { class: 'item-heading' }, h('span', {}, line.aciklama), moneyNode(line.tutar)),
        h(
          'div',
          { class: 'allocation-tags' },
          line.dagilimlar.map(d => h('span', { class: 'allocation-tag' }, `${d.kanal}: ${money(d.tutar)}`)),
          !line.dagilimlar.length && h('span', { class: 'badge pending' }, 'Kanal dağılımı bekliyor')
        )
      )
    )
  );
  const paymentSection = section(
    'Ödemeler',
    p.odemeler.length
      ? h(
          'div',
          {},
          p.odemeler.map(payment => paymentRow(p, payment))
        )
      : h('p', { class: 'plain-note' }, 'Bu alışa henüz ödeme kaydedilmedi.'),
    rights.pay ? button('+ Ödeme ekle', event => run(event.currentTarget, () => paymentDialog(p)), 'small') : null
  );
  const docs = h('div', {}, help('Belgeler yükleniyor…'));
  // Editör kaldırılan belgeleri de (kaldıran, zaman ve gerekçesiyle) açıp kapatabilir; alıcı kaldırılanları görmez.
  const removedToggle =
    state.role === 'editor' &&
    button(
      state.showRemovedDocuments ? 'Kaldırılanları gizle' : 'Kaldırılanları göster',
      event => {
        state.showRemovedDocuments = !state.showRemovedDocuments;
        event.currentTarget.textContent = state.showRemovedDocuments ? 'Kaldırılanları gizle' : 'Kaldırılanları göster';
        loadDocuments(p, docs);
      },
      'small'
    );
  const docSection = section(
    'Belgeler',
    docs,
    h(
      'div',
      { class: 'row-actions' },
      removedToggle,
      state.role === 'editor' || p.durum === 'Taslak' ? button('+ Belge ekle', () => documentDialog(p), 'small') : null
    )
  );
  loadDocuments(p, docs);
  const actions = h(
    'div',
    { class: 'detail-actions' },
    rights.edit && button('Alışı düzenle', () => editPurchase(p), 'primary'),
    rights.send && button('İncelemeye gönder', () => statusDialog(p, 'gonder'), 'primary'),
    rights.approve && button('Alışı onayla', () => statusDialog(p, 'onayla'), 'primary'),
    rights.return && button('Açıklamayla iade et', () => statusDialog(p, 'iade')),
    rights.pay && button('Ödeme kaydet', event => run(event.currentTarget, () => paymentDialog(p)))
  );
  const total = section(
    'Alış hesabı',
    h(
      'div',
      {},
      h('div', { class: 'total-line' }, 'Toplam', moneyNode(p.toplam)),
      h('div', { class: 'total-line' }, 'Ödenen', moneyNode(p.odenen)),
      h('div', { class: 'total-line strong' }, 'Kalan', moneyNode(p.kalan)),
      p.durum !== 'Onaylandi' && p.odenen > 0 && help('Gerçekleşen ödeme kasaya yansır. Kanal dağılımı onay bekler.'),
      actions
    )
  );
  $('#view').replaceChildren(
    ...childValues([
      button('← Alışlara dön', () => navigate('purchases'), 'back-link'),
      p.editorNotu &&
        h(
          'div',
          { class: `notice${p.durum === 'Taslak' ? '' : ' success'}` },
          h('strong', {}, p.durum === 'Taslak' ? 'Editörün iade açıklaması' : 'Editör notu'),
          h('p', {}, p.editorNotu)
        ),
      h(
        'div',
        { class: 'detail-grid' },
        h(
          'div',
          {},
          section('Alınan mallar ve kanalları', h('div', {}, metadata, items, p.not && h('p', { class: 'purchase-note' }, p.not))),
          paymentSection,
          docSection
        ),
        h('aside', { class: 'detail-summary' }, total)
      ),
    ])
  );
}
function statusDialog(p, action) {
  const descriptions = {
    gonder: 'Alışı editörün incelemesine gönderin. Gönderildikten sonra değişiklik için editörün iade etmesi gerekir.',
    onayla: 'Malları ve kanal paylarını kontrol ettiniz mi? Onay, mevcut ödemeleri kayıtlı kanal dağılımına geçirir.',
    iade: 'Ne düzeltilmesi gerektiğini açıklayın. Mevcut ödemeler silinmez; kanal dağılımları yeniden onaylanana kadar bekler.',
  };
  const titles = { gonder: 'İncelemeye gönder', onayla: 'Alışı onayla', iade: 'Açıklamayla iade et' };
  formDialog(
    titles[action],
    h(
      'div',
      { class: 'stack' },
      h('p', { class: 'plain-note' }, descriptions[action]),
      action !== 'gonder' &&
        field(
          action === 'iade' ? 'İade açıklaması' : 'Editör notu (isteğe bağlı)',
          h('textarea', { name: 'not', required: action === 'iade', maxlength: 2000 })
        )
    ),
    titles[action],
    async form => {
      await api(`/api/alis/${p.id}/${action}`, { method: 'POST', body: { surum: p.surum, not: values(form).not || null } });
      closeModal();
      toast(
        action === 'gonder' ? 'Alış incelemeye gönderildi.' : action === 'onayla' ? 'Alış onaylandı.' : 'Alış açıklamayla iade edildi.'
      );
      await navigate('purchase', p.id);
    }
  );
}

function editPurchase(p = null) {
  const identity = requestIdentity();
  const draft = {
    surum: p?.surum || 0,
    tarih: p?.tarih || today(),
    tedarikci: p?.tedarikci || '',
    not: p?.not || '',
    kalemler: p?.kalemler.map(line => ({
      aciklama: line.aciklama,
      tutar: line.tutar,
      dagilimlar: line.dagilimlar.map(d => ({ kanalId: d.kanalId, tutar: d.tutar })),
    })) || [{ aciklama: '', tutar: '', dagilimlar: [] }],
  };
  const lines = h('div');
  const total = h('strong', { class: 'money' });
  const updateTotal = () => {
    try {
      total.textContent = money(draft.kalemler.reduce((sum, line) => sum + cents(line.tutar || '0'), 0) / 100);
    } catch {
      total.textContent = 'Tutarı kontrol edin';
    }
  };
  const renderLines = () => {
    lines.replaceChildren(
      ...draft.kalemler.map((line, index) => {
        const lineTotal = h('span', { class: 'money' });
        const allocTotal = h('span');
        const status = h('div', { class: 'allocation-summary' }, lineTotal, allocTotal);
        const allocations = h('div');
        const updateLine = () => {
          try {
            const sum = cents(line.tutar || '0');
            const assigned = line.dagilimlar.reduce((acc, d) => acc + cents(d.tutar || '0'), 0);
            lineTotal.textContent = `Kalem: ${money(sum / 100)}`;
            allocTotal.textContent =
              assigned === sum
                ? 'Dağılım tamam'
                : `${money(Math.abs(sum - assigned) / 100)} ${assigned > sum ? 'fazla pay' : 'dağıtılmadı'}`;
            status.classList.toggle('invalid', assigned !== sum);
          } catch (error) {
            lineTotal.textContent = error.message;
            allocTotal.textContent = '';
          }
          updateTotal();
        };
        const renderAllocations = () =>
          allocations.replaceChildren(
            ...line.dagilimlar.map((allocation, allocationIndex) =>
              h(
                'div',
                { class: 'allocation-row' },
                field(
                  'Kanal',
                  select(
                    `kanal-${index}-${allocationIndex}`,
                    [
                      { value: '', label: 'Kanal seçin' },
                      ...state.channels
                        .filter(k => k.aktif || k.id === Number(allocation.kanalId))
                        .map(k => ({ value: k.id, label: k.ad })),
                    ],
                    allocation.kanalId,
                    {
                      required: true,
                      onchange: event => {
                        allocation.kanalId = event.target.value;
                        updateLine();
                      },
                    }
                  )
                ),
                field(
                  'Pay (₺)',
                  input(`pay-${index}-${allocationIndex}`, allocation.tutar, {
                    inputmode: 'decimal',
                    required: true,
                    oninput: event => {
                      allocation.tutar = event.target.value;
                      updateLine();
                    },
                  })
                ),
                h(
                  'button',
                  {
                    type: 'button',
                    class: 'icon-button',
                    'aria-label': `${index + 1}. kalemin ${allocationIndex + 1}. kanal payını kaldır`,
                    onclick: () => {
                      line.dagilimlar.splice(allocationIndex, 1);
                      renderAllocations();
                      updateLine();
                    },
                  },
                  '×'
                )
              )
            )
          );
        renderAllocations();
        updateLine();
        return h(
          'div',
          { class: 'line-editor' },
          h(
            'div',
            { class: 'line-editor-head' },
            `Kalem ${index + 1}`,
            draft.kalemler.length > 1 &&
              h(
                'button',
                {
                  type: 'button',
                  class: 'icon-button',
                  'aria-label': `${index + 1}. kalemi kaldır`,
                  onclick: () => {
                    draft.kalemler.splice(index, 1);
                    renderLines();
                  },
                },
                '×'
              )
          ),
          h(
            'div',
            { class: 'item-fields' },
            field(
              'Mal açıklaması',
              input(`aciklama-${index}`, line.aciklama, {
                required: true,
                maxlength: 500,
                placeholder: 'Örn. Ambalaj malzemesi',
                oninput: event => {
                  line.aciklama = event.target.value;
                },
              })
            ),
            field(
              'Tutar (₺)',
              input(`tutar-${index}`, line.tutar, {
                inputmode: 'decimal',
                required: true,
                oninput: event => {
                  line.tutar = event.target.value;
                  updateLine();
                },
              })
            )
          ),
          h(
            'div',
            { class: 'allocation-editor' },
            h(
              'div',
              { class: 'allocation-editor-head' },
              h('span', {}, 'Hangi kanala alındı?'),
              button(
                '+ Kanal payı',
                () => {
                  let remainder = '';
                  try {
                    remainder =
                      Math.max(0, cents(line.tutar) - line.dagilimlar.reduce((sum, d) => sum + cents(d.tutar || 0), 0)) / 100 || '';
                  } catch {}
                  line.dagilimlar.push({ kanalId: '', tutar: remainder });
                  renderAllocations();
                  updateLine();
                },
                'small'
              )
            ),
            allocations,
            status
          )
        );
      })
    );
    updateTotal();
  };
  renderLines();
  formDialog(
    p ? `Alış #${p.id} · Düzenle` : 'Yeni alış',
    h(
      'div',
      { class: 'stack' },
      h(
        'div',
        { class: 'form-grid' },
        field(
          'Açıklama / ödeme yapılan yer',
          input('tedarikci', draft.tedarikci, {
            required: true,
            maxlength: 200,
            oninput: event => {
              draft.tedarikci = event.target.value;
            },
          })
        ),
        field(
          'Alış tarihi',
          input('tarih', draft.tarih, {
            type: 'date',
            required: true,
            onchange: event => {
              draft.tarih = event.target.value;
            },
          })
        )
      ),
      h(
        'fieldset',
        {},
        h('legend', {}, 'Alınan mallar'),
        lines,
        button(
          '+ Bir mal daha ekle',
          () => {
            draft.kalemler.push({ aciklama: '', tutar: '', dagilimlar: [] });
            renderLines();
          },
          'small'
        )
      ),
      help(
        'Aynı malı birden fazla kanala bölebilirsiniz. Kanal net değilse pay eklemeyin; editör onaylamadan önce tamamlar. Ortak, belirsiz kanal anlamına gelmez.'
      ),
      field(
        'Alış notu',
        h(
          'textarea',
          {
            name: 'not',
            maxlength: 2000,
            oninput: event => {
              draft.not = event.target.value;
            },
          },
          draft.not
        )
      ),
      h('div', { class: 'draft-total' }, 'Alış toplamı', total)
    ),
    p ? 'Değişiklikleri kaydet' : 'Taslağı kaydet',
    async () => {
      // Yeni taslak istek kimliği taşır (appcore-5): yanıtı kaybolan kayıt aynı gövdeyle yeniden gönderilince sunucu ikinci taslak açmaz.
      const body = purchasePayload(draft);
      const updated = await api(p ? `/api/alis/${p.id}` : '/api/alis', { method: p ? 'PUT' : 'POST', body: p ? body : identity(body) });
      closeModal();
      toast('Alış kaydedildi.');
      await navigate('purchase', updated.id);
    },
    { wide: true }
  );
}

function paymentRow(p, payment) {
  const cardName = payment.krediKartiAdi || `Kart #${payment.krediKartiId}`;
  const card =
    payment.krediKartiId &&
    (['editor', 'viewer'].includes(state.role)
      ? button(cardName, () => navigate('cards', payment.krediKartiId), 'table-link')
      : h('span', {}, cardName));
  return h(
    'div',
    { class: 'payment' },
    h(
      'div',
      {},
      h('div', { class: 'payment-name' }, dateText(payment.tarih)),
      h(
        'div',
        { class: 'payment-info' },
        payment.krediKartiId ? h('span', {}, card, ' ile ödendi · kart borcu ayrı izlenir') : 'Nakit / havale',
        ` · Ödeme #${payment.id}`
      ),
      payment.dagilimBekliyor
        ? h('span', { class: 'badge pending' }, 'Dağılım bekliyor')
        : allocationTags(payment.dagilimlar.filter(d => d.tutar > 0)),
      state.role === 'editor' &&
        h(
          'div',
          { class: 'row-actions' },
          button('Düzelt / taşı', event => run(event.currentTarget, () => paymentDialog(p, payment)), 'small'),
          button('Ödemeyi iptal et', event => run(event.currentTarget, () => cancelPayment(p, payment)), 'small danger')
        )
    ),
    h('div', { class: 'payment-amount money' }, money(payment.tutar))
  );
}
// gap-coklu-giris-cift-sayim-mutabakat-6: takipli kartla yeni kart harcamasının taksit sayısı ve isteğe bağlı ilk kesimi.
function installmentEditor() {
  const count = input('taksitSayisi', '1', { type: 'number', min: 1, max: 60, step: 1, inputmode: 'numeric' });
  const firstCut = input('ilkKesimTarihi', '', { type: 'date' });
  const node = h(
    'div',
    { class: 'stack' },
    h('div', { class: 'form-grid' }, field('Taksit sayısı', count), field('İlk kesim tarihi (isteğe bağlı)', firstCut)),
    help(
      'Bankada taksitle çekildiyse taksit sayısını girin: her ekstreye yalnız taksit tutarı yazılır. İlk kesim boşsa harcamanın düştüğü ilk kesimdir.'
    )
  );
  node.hidden = true;
  return { node, read: date => (node.hidden ? {} : installmentFields(count.value, firstCut.value, date)) };
}
async function paymentDialog(p, payment = null) {
  await loadPaymentLookups();
  const identity = requestIdentity();
  const date = input('tarih', payment?.tarih || today(), { type: 'date', required: true });
  const total = input('tutar', payment?.tutar ?? p.kalan, { inputmode: 'decimal', required: true });
  // K3: yalnız takipteki açık kartlar; düzeltilen ödemenin ya da bağlanan giderin kendi kartı ayrıca listelenir.
  const card = select('krediKartiId', paymentCardChoices(state.cards, payment?.krediKartiId), payment?.krediKartiId);
  const cardChoices = keepId =>
    card.replaceChildren(...paymentCardChoices(state.cards, keepId).map(option => h('option', { value: option.value }, option.label)));
  const existing = select('mevcutIslemId', [{ value: '', label: 'Yeni gider oluştur' }], '');
  const linkHelp =
    'Daha önce gider olarak girdiğiniz (ya da banka ekstresinden aktardığınız) bir ödemeyi bağlarsanız kasadan ikinci kez düşülmez.';
  const existingHelp = h('div', { class: 'stack' }, help(linkHelp));
  // Ters sıra (gap-coklu-giris-cift-sayim-mutabakat-1): kart ekstresi önce aktarıldıysa harcama kart borcundadır. Takipli kart
  // seçilince gidere bağlı olmayan harcamaları listelenir; seçilen harcamaya bağlanan ödeme ikinci harcama oluşturmaz.
  const charge = select('mevcutKartHarcamaId', [{ value: '', label: 'Yeni kart harcaması oluştur' }], '');
  const chargeStatus = h('p', { class: 'help', role: 'status' });
  const chargeField = field(
    'Kart harcaması',
    charge,
    h(
      'div',
      { class: 'stack' },
      chargeStatus,
      help(
        'Kart ekstresi önce içe aktarıldıysa harcamayı seçin: ödeme ona bağlanır, kart borcu ve kasa ikinci kez sayılmaz. Tarih ve tutar harcamadan alınır.'
      )
    )
  );
  chargeField.hidden = true;
  // Taksit yalnız takipli kartla YENİ kart harcaması oluşturulurken: mevcut gider ya da ekstreden gelen harcama bağlanırken plan onundur.
  const installments = installmentEditor();
  const installmentSync = () => {
    installments.node.hidden = Boolean(payment) || !optionalId(card.value) || Boolean(existing.value) || Boolean(charge.value);
  };
  // gap-coklu-giris-cift-sayim-mutabakat-5: kart takibindeki ödemenin tarihi, tutarı ve kartı kart harcamasıdır; yalnız hedef alış seçilir.
  const tracked = trackedCardPayment(state.cards, payment);
  if (tracked) {
    date.disabled = true;
    total.disabled = true;
    card.disabled = true;
  }
  let charges = [];
  let chargeGeneration = 0;
  const chargeSync = () => {
    const selected = charges.find(item => item.id === Number(charge.value));
    if (selected) {
      date.value = selected.tarih;
      total.value = selected.tutar;
    }
    date.disabled = Boolean(selected) || Boolean(existing.value);
    total.disabled = Boolean(selected) || Boolean(existing.value);
    installmentSync();
  };
  const loadCharges = async () => {
    const mine = ++chargeGeneration;
    const cardId = optionalId(card.value);
    charges = [];
    charge.replaceChildren(h('option', { value: '' }, 'Yeni kart harcaması oluştur'));
    charge.value = '';
    chargeStatus.textContent = '';
    chargeField.hidden = payment || !cardId || Boolean(existing.value);
    chargeSync();
    if (chargeField.hidden) return;
    let amountText = '';
    try {
      amountText = (cents(total.value, { allowZero: false }) / 100).toFixed(2);
    } catch {
      amountText = '';
    }
    const list = await api(`/api/alis/baglanabilir-kart-harcamalari?krediKartiId=${cardId}${amountText ? `&tutar=${amountText}` : ''}`);
    if (mine !== chargeGeneration) return;
    charges = Array.isArray(list) ? list : [];
    charge.replaceChildren(
      h('option', { value: '' }, 'Yeni kart harcaması oluştur'),
      ...charges.map(item =>
        h(
          'option',
          { value: item.id },
          `#${item.id} · ${dateText(item.tarih)} · ${item.aciklama} · ${money(item.tutar)}${item.ekstreKayitId ? ' · ekstreden' : ''}`
        )
      )
    );
    charge.value = '';
    chargeStatus.textContent = charges.length
      ? ''
      : 'Bu kartta bu tutarda bağlanabilecek harcama yok; ödeme yeni kart harcaması olarak kaydedilir.';
  };
  if (!payment) {
    // webui-6: bütün gider geçmişi çekilmez. Sunucu yalnız bağlanabilir giderleri (en yeni önce) sayfa sayfa döndürür; eskisi
    // açıklama, not ya da tutarla aranır. Sunucunun süzdüğü bağlı kayıtlar istemcide de savunma olarak elenir.
    // İmleç onu üreten sorguya (cursorQuery: imleçsiz yol) aittir: arama metni sonradan değiştiyse 'Daha eski giderler' eski
    // imleci yeni süzgeçle birleştirmez (daha yeni eşleşmeler atlanır, başka süzgecin kayıtları eklenirdi); aramayı baştan yapar.
    let available = [];
    let cursor = null;
    let cursorQuery = null;
    let generation = 0;
    const search = input('giderArama', '', {
      type: 'search',
      maxlength: 200,
      placeholder: 'Açıklama, not veya tutar',
      'aria-label': 'Bağlanacak gideri ara',
    });
    const status = h('p', { class: 'help', role: 'status' });
    const sync = () => {
      const selected = available.find(e => e.id === Number(existing.value));
      cardChoices(selected?.krediKartiId);
      if (selected) {
        date.value = selected.tarih;
        total.value = selected.tutarTl;
        card.value = selected.krediKartiId || '';
      }
      date.disabled = Boolean(selected);
      total.disabled = Boolean(selected);
      card.disabled = Boolean(selected);
      if (selected) {
        chargeGeneration++;
        charges = [];
        charge.value = '';
        chargeField.hidden = true;
      }
      installmentSync();
    };
    const more = button('Daha eski giderler', event => run(event.currentTarget, () => load(true)), 'small', { hidden: true });
    const load = async append => {
      const mine = ++generation;
      const text = search.value;
      const query = linkableExpensesPath(text);
      if (query !== cursorQuery) append = false;
      const page = await api(append ? linkableExpensesPath(text, cursor) : query);
      if (mine !== generation) return;
      // Banka ekstresi gideri bağlanabilir (sunucu listeler; bağlanınca ekstre satırı eşleşmeye döner).
      const items = (page?.ogeler || []).filter(e => !e.alisId && !e.aylikGiderOdemeId && e.tutarTl > 0);
      available = append ? [...available, ...items.filter(e => !available.some(old => old.id === e.id))] : items;
      cursor = page?.devamVar ? page.sonrakiImlec : null;
      cursorQuery = query;
      const selectedValue = existing.value;
      existing.replaceChildren(
        h('option', { value: '' }, 'Yeni gider oluştur'),
        ...available.map(e =>
          h(
            'option',
            { value: e.id },
            `#${e.id} · ${dateText(e.tarih)} · ${e.cari} · ${money(e.tutarTl)}${e.ekstreKayitId ? ' · banka ekstresinden' : ''}`
          )
        )
      );
      existing.value = available.some(e => String(e.id) === selectedValue) ? selectedValue : '';
      more.hidden = !cursor;
      status.textContent = !available.length
        ? 'Eşleşen bağlanabilir gider yok.'
        : cursor
          ? `En yeni ${available.length} gider gösteriliyor; daha eskisi için arayın veya “Daha eski giderler”e basın.`
          : '';
      sync();
    };
    const find = () => run(null, () => load(false));
    search.addEventListener('change', find);
    // Arama kutusunda Enter ödeme formunu göndermez; yalnız arar.
    search.addEventListener('keydown', event => {
      if (event.key === 'Enter') {
        event.preventDefault();
        find();
      }
    });
    existing.addEventListener('change', () => {
      sync();
      if (!existing.value) run(null, loadCharges);
    });
    card.addEventListener('change', () => run(null, loadCharges));
    total.addEventListener('change', () => {
      if (!charge.value) run(null, loadCharges);
    });
    charge.addEventListener('change', chargeSync);
    await load(false);
    existingHelp.replaceChildren(
      h(
        'div',
        { class: 'row-actions' },
        search,
        button('Ara', event => run(event.currentTarget, () => load(false)), 'small')
      ),
      status,
      more,
      help(linkHelp)
    );
  }
  const target = select('hedefAlisId', [
    { value: '', label: 'Bu alışta kalsın' },
    ...state.purchases
      .filter(other => other.id !== p.id && other.kalan > 0)
      .map(other => ({ value: other.id, label: `#${other.id} · ${other.tedarikci} · ${money(other.kalan)} kalan` })),
  ]);
  formDialog(
    payment ? `Ödeme #${payment.id} · Düzelt / taşı` : 'Ödeme kaydet',
    h(
      'div',
      { class: 'stack' },
      h(
        'div',
        { class: 'notice' },
        tracked
          ? 'Bu ödeme kart takibinde: tarihi, tutarı ve kartı kart harcamasının kendisidir, değiştirilemez. Ödeme başka alışa aitse hedef alışı seçin; kart harcaması, taksitleri ve kart ödemeleri aynen kalır. Kart tarafında yanlışlık varsa ödemeyi alıştan ayırın (Ödemeyi iptal et).'
          : payment
            ? 'Bu işlem kayıtlı ödemeyi değiştirir. Ödeme başka alışa aitse hedef alış seçin; para çıkışını yeniden kaydetmeyin.'
            : `Alışın kalan tutarı ${money(p.kalan)}. ${p.durum !== 'Onaylandi' ? 'Ödeme kasaya yansır; kanal dağılımı onay bekler.' : 'Ödeme onaylı kanal paylarına dağıtılır.'}`
      ),
      !payment && field('Yeni ödeme veya mevcut gider', existing, existingHelp),
      h('div', { class: 'form-grid' }, field('Ödeme tarihi', date), field('Tutar (₺)', total), field('Ödeme yöntemi', card)),
      !payment && chargeField,
      !payment && installments.node,
      payment && field('Ödemeyi başka alışa taşı', target),
      field(
        payment ? 'Düzeltme açıklaması' : 'Ödeme notu (isteğe bağlı)',
        h('textarea', { name: 'aciklama', required: Boolean(payment), maxlength: 2000 })
      ),
      help(
        'Yeni takipte kartla alış, kart borcu oluşturur; kasa yalnız Kredi Kartları ekranında ödeme kaydedildiğinde azalır. Geçiş yapılmamış eski kartlarda önceki kasa kuralı sürer.'
      )
    ),
    payment ? 'Düzeltmeyi kaydet' : 'Ödemeyi kaydet',
    async form => {
      const data = values(form);
      const payload = {
        surum: p.surum,
        tarih: date.value,
        tutar: cents(total.value, { allowZero: false }) / 100,
        krediKartiId: optionalId(card.value),
      };
      if (payment) {
        const destination = state.purchases.find(other => other.id === Number(target.value));
        if (tracked && !destination)
          throw new Error(
            'Kart takibindeki ödemenin tarihi, tutarı ve kartı değiştirilemez: taşımak için hedef alış seçin ya da ödemeyi alıştan ayırın (Ödemeyi iptal et).'
          );
        if (tracked) Object.assign(payload, { tarih: payment.tarih, tutar: payment.tutar, krediKartiId: payment.krediKartiId });
        Object.assign(payload, {
          aciklama: data.aciklama.trim(),
          hedefAlisId: destination?.id || null,
          hedefSurum: destination?.surum ?? null,
        });
        await api(`/api/alis/${p.id}/odemeler/${payment.id}`, { method: 'PUT', body: identity(payload) });
      } else {
        const linkedCharge = existing.value ? null : charges.find(item => item.id === Number(charge.value));
        if (linkedCharge && (linkedCharge.krediKartiId !== payload.krediKartiId || cents(linkedCharge.tutar) !== cents(payload.tutar)))
          throw new Error('Seçilen kart harcaması ödeme kartı ve tutarıyla eşleşmiyor. Kartı yeniden seçin.');
        Object.assign(
          payload,
          { mevcutIslemId: optionalId(existing.value), not: data.aciklama.trim() || null },
          linkedCharge ? { tarih: linkedCharge.tarih, mevcutKartHarcamaId: linkedCharge.id } : {},
          installments.read(payload.tarih)
        );
        const body = identity(payload);
        // Mevcut gidere ya da kart harcamasına bağlama yeni para çıkışı değildir: benzer kayıt sorulmaz.
        if (
          !payload.mevcutIslemId &&
          !payload.mevcutKartHarcamaId &&
          !(await confirmSimilar(
            form,
            { tur: 'AlisOdeme', tarih: payload.tarih, tutar: payload.tutar, krediKartiId: payload.krediKartiId, kanal: null, alisId: p.id },
            body
          ))
        )
          return;
        await api(`/api/alis/${p.id}/odemeler`, { method: 'POST', body });
      }
      closeModal();
      toast(
        payment
          ? tracked
            ? 'Ödeme seçilen alışa taşındı; kart harcaması aynen kaldı.'
            : 'Ödeme düzeltildi.'
          : payload.mevcutKartHarcamaId
            ? 'Kart harcaması alışa bağlandı; ikinci harcama oluşmadı.'
            : payload.taksitSayisi
              ? `Ödeme ${payload.taksitSayisi} taksitli kart harcaması olarak kaydedildi.`
              : 'Ödeme kaydedildi.'
      );
      await navigate('purchase', p.id);
    }
  );
}
// gap-coklu-giris-cift-sayim-mutabakat-5: kart takibindeki ödemenin iptali iki yoldur. Harcama hiç yapılmadıysa (ödenmemişse) harcama ve
// taksitleri de kalkar. Harcama gerçekse ama bu alışa ait değilse ödeme alıştan AYRILIR: harcama girilen gerçek kanal paylarıyla kart gideri
// olarak kalır (paylar ödemenin bugünkü paylarıyla başlar; aynen bırakılırsa önceki kart ödemelerinin kanal payı değişmez).
async function cancelPayment(p, payment) {
  await loadPaymentLookups();
  const identity = requestIdentity();
  const tracked = trackedCardPayment(state.cards, payment);
  const shares = state.channels
    .filter(channel => channel.aktif || payment.dagilimlar.some(d => d.kanalId === channel.id))
    .map(channel => ({
      kanalId: channel.id,
      ad: channel.ad,
      tutar: sumCents(payment.dagilimlar.filter(d => d.kanalId === channel.id).map(d => d.tutar)) / 100 || '',
    }));
  const keep = input('harcamayiKoru', '1', { type: 'checkbox' });
  const shareRows = h(
    'div',
    { class: 'stack' },
    shares.map(row =>
      field(
        `${row.ad} (₺)`,
        input(`ayir-${row.kanalId}`, row.tutar, {
          inputmode: 'decimal',
          oninput: event => {
            row.tutar = event.target.value;
          },
        })
      )
    )
  );
  shareRows.hidden = true;
  keep.addEventListener('change', () => {
    shareRows.hidden = !keep.checked;
  });
  const trackedSection =
    tracked &&
    h(
      'fieldset',
      {},
      h('legend', {}, 'Kart harcaması'),
      h('label', {}, keep, 'Kart harcaması gerçek: ödemeyi alıştan ayır, harcama kart gideri olarak kalsın'),
      help(
        'Seçilmezse kart harcaması hiç yapılmamış sayılır: ödenmemişse harcama ve taksitleri de kalkar; kart ödemesi yapıldıysa sunucu gerçek kanal dağılımını ister.'
      ),
      shareRows
    );
  const notice = tracked
    ? `${dateText(payment.tarih)} tarihli ${money(payment.tutar)} kart ödemesi bu alıştan kaldırılacak. Ödeme başka bir alışa aitse iptal etmeyin; “Düzelt / taşı” ile doğru alışa taşıyın.`
    : `${dateText(payment.tarih)} tarihli ${money(payment.tutar)} ödeme mali kayıtlardan kaldırılacak. Ödeme gerçekten yapıldıysa, yalnız yanlış alışa yazıldığı için iptal etmeyin; “Düzelt / taşı” işlemini kullanın.`;
  formDialog(
    tracked ? 'Kart ödemesini alıştan ayır / iptal et' : 'Gerçek ödemeyi iptal et',
    h(
      'div',
      { class: 'stack' },
      h('div', { class: 'notice danger' }, notice),
      trackedSection,
      field('İptal açıklaması', h('textarea', { name: 'aciklama', required: true, maxlength: 2000 }))
    ),
    'Ödemeyi iptal et',
    async form => {
      const body = {
        surum: p.surum,
        aciklama: values(form).aciklama.trim(),
        ...(tracked && keep.checked ? { kanalDagilimlari: detachAllocations(shares, payment.tutar) } : {}),
      };
      await api(`/api/alis/${p.id}/odemeler/${payment.id}/iptal`, { method: 'POST', body: identity(body) });
      closeModal();
      toast(
        body.kanalDagilimlari
          ? 'Ödeme alıştan ayrıldı; kart harcaması kart gideri olarak kaldı. Doğru alışa mevcut gider olarak bağlayabilirsiniz.'
          : tracked
            ? 'Ödeme iptal edildi; ödenmemiş kart harcaması ve taksitleri de kaldırıldı.'
            : 'Ödeme iptal edildi; alışın kalan tutarı güncellendi.'
      );
      await navigate('purchase', p.id);
    },
    { danger: true }
  );
}
async function loadDocuments(p, container) {
  try {
    const docs = await api(documentsPath(p.id, state.role, state.showRemovedDocuments));
    if (!container.isConnected) return;
    container.replaceChildren(
      ...(docs.length
        ? docs.map(doc =>
            h(
              'div',
              { class: `document-row${doc.silindi ? ' removed' : ''}` },
              h('span', { class: 'document-type', 'aria-hidden': 'true' }, doc.icerikTuru === 'application/pdf' ? 'PDF' : 'GÖRSEL'),
              h(
                'a',
                {
                  href: `/api/belgeler/${doc.id}`,
                  target: '_blank',
                  rel: 'noopener',
                  download: documentFileName(doc.dosyaAdi, doc.icerikTuru),
                },
                documentFileName(doc.dosyaAdi, doc.icerikTuru),
                h('span', { class: 'table-sub' }, `${Math.ceil(doc.boyut / 1024)} KB${doc.odemeId ? ` · Ödeme #${doc.odemeId}` : ''}`),
                h('span', { class: 'table-sub' }, documentDescription(doc))
              ),
              documentRemovable(state.role, p, doc) && button('Kaldır', () => deleteDocument(p, doc), 'small danger')
            )
          )
        : [
            h(
              'p',
              { class: 'plain-note' },
              p.durum === 'Taslak' || state.role === 'editor'
                ? 'Fiş, fatura veya dekont ekleyebilirsiniz. PDF, JPEG ve PNG; en fazla 10 MB.'
                : 'Bu alışa belge eklenmedi. Yeni belge için editörün alışı iade etmesi gerekir.'
            ),
          ])
    );
  } catch (error) {
    if (container.isConnected)
      container.replaceChildren(
        h('p', { class: 'form-error' }, error.message),
        button('Yeniden yükle', () => loadDocuments(p, container), 'small')
      );
  }
}
function documentDialog(p) {
  const file = input('dosya', '', { type: 'file', accept: '.pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg', required: true });
  formDialog(
    'Belge ekle',
    h(
      'div',
      { class: 'stack' },
      field('Fiş, fatura veya dekont', file, help('PDF, JPEG veya PNG; dosya başına en fazla 10 MB.')),
      state.role === 'editor' &&
        field(
          'İlgili kayıt',
          select('odemeId', [
            { value: '', label: 'Alış belgesi' },
            ...p.odemeler.map(payment => ({
              value: payment.id,
              label: `Ödeme #${payment.id} · ${dateText(payment.tarih)} · ${money(payment.tutar)}`,
            })),
          ])
        )
    ),
    'Belgeyi yükle',
    async form => {
      const selected = file.files[0];
      if (!selected || selected.size === 0 || selected.size > 10 * 1024 * 1024)
        throw new Error('Boş olmayan ve 10 MB sınırını aşmayan bir dosya seçin.');
      if (!['application/pdf', 'image/png', 'image/jpeg'].includes(selected.type))
        throw new Error('Yalnız PDF, JPEG veya PNG dosyası yükleyebilirsiniz.');
      const payload = new FormData();
      payload.append('dosya', selected);
      if (values(form).odemeId) payload.append('odemeId', values(form).odemeId);
      await api(`/api/alis/${p.id}/belgeler`, { method: 'POST', body: payload });
      closeModal();
      toast('Belge eklendi.');
      await navigate('purchase', p.id);
    }
  );
}
// Kaldırma yumuşaktır: belge ve kaldırma kaydı saklanır. Editör için gerekçe zorunludur, alıcı için isteğe bağlıdır.
function deleteDocument(p, doc) {
  const editor = state.role === 'editor';
  formDialog(
    'Belgeyi kaldır',
    h(
      'div',
      { class: 'stack' },
      h(
        'p',
        { class: 'plain-note' },
        `“${doc.dosyaAdi}” listeden kaldırılacak. İçeriği, kimin ve ne zaman kaldırdığı saklanır; alış ve ödeme kaydı korunur.`
      ),
      field(
        editor ? 'Kaldırma gerekçesi' : 'Kaldırma gerekçesi (isteğe bağlı)',
        h('textarea', { name: 'gerekce', required: editor, maxlength: 2000 })
      )
    ),
    'Belgeyi kaldır',
    async form => {
      const body = documentDeletePayload(state.role, values(form).gerekce);
      await api(`/api/belgeler/${doc.id}`, { method: 'DELETE', body });
      closeModal();
      toast('Belge kaldırıldı.');
      await navigate('purchase', p.id);
    },
    { danger: true }
  );
}
async function renderTools(generation) {
  page('Ayarlar', 'Kanallar, erişim ve güvenlik');
  const results = await Promise.allSettled([
    api('/api/yedek/durum'),
    api('/api/alicilar'),
    api('/api/surum'),
    api('/api/kanallar'),
    api('/api/ayarlar'),
    api('/api/kasa-esikleri'),
    api('/api/ay-kilidi'),
  ]);
  if (generation !== renderId) return;
  const [backupResult, buyersResult, versionResult, channelsResult, settingsResult, thresholdsResult, lockResult] = results;
  // Kanal formu için ay kilidi (yalnız varsayılan ve not; kuralı sunucu uygular): okunamazsa kilit bilinmiyor sayılır.
  const lockedUntil = lockResult.status === 'fulfilled' ? lockResult.value?.kilitliSonTarih || null : null;
  const backup = backupResult.status === 'fulfilled' ? backupResult.value : null;
  const buyers = buyersResult.status === 'fulfilled' ? buyersResult.value : null;
  const version = versionResult.status === 'fulfilled' ? versionResult.value : null;
  const channels = channelsResult.status === 'fulfilled' ? channelsResult.value : null;
  const settings = settingsResult.status === 'fulfilled' ? settingsResult.value : null;
  if (channels) state.channels = channels;
  const buyersContent = buyers
    ? h(
        'div',
        {},
        buyers.length
          ? buyers.map(buyer =>
              h(
                'div',
                { class: 'buyer-row' },
                h('div', {}, h('strong', {}, buyer.ad), h('small', {}, `${buyer.kullanici} · ${buyer.aktif ? 'Aktif' : 'Pasif'}`)),
                button('Düzenle', () => buyerDialog(buyer), 'small')
              )
            )
          : h('p', { class: 'plain-note' }, 'Henüz alıcı hesabı eklenmedi.')
      )
    : h('p', { class: 'form-error' }, buyersResult.reason.message);
  // Rotasyon uyarısı: yedek alındı ama saklama süresi dolan eski yedek silinemedi (disk dolabilir); hatadan ayrı gösterilir.
  // Son geri yüklemenin raporu: açılışta yapılanlar ve editörün/operatörün yapacakları (yeni izleyici şifresi, kurtarma kodu...).
  const restoreNotice = report =>
    report &&
    h(
      'div',
      { class: 'notice', role: 'status' },
      h('p', {}, report.title),
      report.items.length
        ? h(
            'ul',
            { class: 'similar-records' },
            report.items.map(item => h('li', {}, item))
          )
        : null
    );
  const backupContent = h(
    'div',
    { class: 'stack' },
    backup
      ? h(
          'div',
          {},
          h('p', { class: 'plain-note' }, `Otomatik yedek: ${backup.otomatikEtkin ? 'Açık' : 'Kapalı'}`),
          h(
            'p',
            { class: 'plain-note' },
            `Son yedek: ${backup.sonYedek ? new Date(backup.sonYedek).toLocaleString('tr-TR') : 'Henüz oluşturulmadı'}`
          ),
          h(
            'p',
            { class: 'plain-note' },
            `Son doğrulama: ${backup.sonDogrulama ? new Date(backup.sonDogrulama).toLocaleString('tr-TR') : 'Kayıt yok'}`
          ),
          ...backupDiskLines(backup).map(line => h('p', { class: 'plain-note' }, line)),
          backup.hata && h('p', { class: 'form-error' }, backup.hata),
          backup.rotasyonUyarisi && h('p', { class: 'notice', role: 'alert' }, backup.rotasyonUyarisi),
          backup.diskUyarisi && h('p', { class: 'notice', role: 'alert' }, backup.diskUyarisi),
          backup.belgeUyarisi && h('p', { class: 'notice', role: 'alert' }, backup.belgeUyarisi),
          restoreNotice(restoreReport(backup))
        )
      : h('p', { class: 'form-error' }, backupResult.reason.message),
    button(
      'Şimdi yedek indir',
      event =>
        run(event.currentTarget, async () => {
          const blob = await api('/api/yedek', { method: 'POST', binary: true });
          download(blob, `kasa-yedek-${today()}.zip`);
          toast('Yedek dosyası indirildi.');
          await navigate('tools');
        }),
      'primary'
    ),
    help('Yedeği güvenli bir yerde saklayın. Geri yükleme, çalışan uygulama durdurularak sunucuda yapılır.')
  );
  // Sunucu güvenilmeyen kaynaktan vekil başlığı aldıysa (yanlış vekil ayarı) editöre burada gösterilir.
  const security = h(
    'div',
    { class: 'stack' },
    settings?.vekilUyarisi ? h('p', { class: 'notice danger', role: 'alert' }, settings.vekilUyarisi) : null,
    h('p', { class: 'plain-note' }, 'Şifre değişikliği eski oturumları kapatır ve mevcut kurtarma kodunu geçersiz kılar.'),
    button('Şifremi değiştir', passwordDialog),
    button('Yeni kurtarma kodu oluştur', recoveryCodeDialog),
    help('Kurtarma kodu bir kez gösterilir. Şifrenizden ayrı ve güvenli bir yerde saklayın.')
  );
  security.append(
    help(
      'Değişiklik geçmişi: kasayı değiştiren kayıtların önceki ve yeni değerleri, gerekçeleri, ay kilidi açılışları ve güvenlik olayları.'
    ),
    button('Değişiklik geçmişini aç', event => run(event.currentTarget, openHistory))
  );
  const versionContent = version
    ? h(
        'div',
        { class: 'stack' },
        h('p', { class: 'plain-note' }, `Sunucu sürümü ${version.surum} · En düşük istemci sürümü ${version.minimumIstemci}`),
        version.notlar && h('p', { class: 'plain-note' }, Array.isArray(version.notlar) ? version.notlar.join('\n') : version.notlar),
        safeExternalLink(version.indirmeAdresi, 'Windows uygulamasını indir')
      )
    : h('p', { class: 'form-error' }, versionResult.reason.message);
  const channelContent = channels
    ? h(
        'div',
        {},
        channels.map(channel =>
          h(
            'div',
            { class: 'buyer-row' },
            h(
              'div',
              {},
              h('strong', {}, channel.ad),
              h('small', {}, `${channel.aktif ? 'Aktif' : 'Pasif'} · Açılış ${money(channel.acilisDevri)}`)
            ),
            button('Düzenle', () => channelDialog(channel, lockedUntil), 'small')
          )
        )
      )
    : h('p', { class: 'form-error' }, channelsResult.reason.message);
  const opening = settings
    ? h(
        'div',
        { class: 'stack' },
        h(
          'p',
          { class: 'plain-note' },
          `Takip başlangıcı ${dateText(settings.takipBaslangic)} · Genel kasa açılışı ${money(settings.kasaAcilisDevri)}`
        ),
        button('Kasa başlangıcını düzenle', () => openingDialog(settings)),
        settings.izleyiciSifreKisa ? h('p', { class: 'notice danger' }, VIEWER_PASSWORD_SHORT_MESSAGE) : null,
        button(settings.izleyiciSifreVarMi ? 'İzleyici şifresini değiştir' : 'İzleyici şifresi belirle', viewerPasswordDialog)
      )
    : h('p', { class: 'form-error' }, settingsResult.reason.message);
  const thresholds =
    thresholdsResult.status === 'fulfilled'
      ? cashControlsUi.thresholdSettings(thresholdsResult.value)
      : section('Kanal alt bakiye uyarıları', help(thresholdsResult.reason.message));
  $('#view').replaceChildren(
    h(
      'div',
      { class: 'settings-grid' },
      section(
        'Kanallar',
        channelContent,
        button('+ Kanal ekle', () => channelDialog(null, lockedUntil), 'small')
      ),
      thresholds,
      section('Kasa başlangıcı', opening),
      section(
        'Alıcı hesapları',
        buyersContent,
        button('+ Alıcı ekle', () => buyerDialog(), 'small')
      ),
      section('Hesap güvenliği', security),
      section(
        'Bildirimler',
        h(
          'div',
          { class: 'stack' },
          help('Kart ve kredi hatırlatmalarını telefonunuza veya bu bilgisayara gönderin.'),
          button('İzin, saat ve cihaz ayarları', event => run(event.currentTarget, () => notificationUi.settings()))
        )
      ),
      section(
        'Ekstre / Hareket Yükle',
        h(
          'div',
          { class: 'stack' },
          help('Kart ekstresi ve banka hesap hareketi PDF’lerinden seçtiğin satırları önizleyerek kaydet.'),
          button('PDF yükle ve incele', () => navigate('imports'))
        )
      ),
      section('Yedekleme', backupContent),
      section('Uygulama sürümü', versionContent)
    )
  );
}

// Değişiklik geçmişi modülü yalnız açılınca yüklenir (denetim-ui.js).
async function openHistory() {
  const { createDenetimUi } = await import('./denetim-ui.js?v=2.3.0');
  createDenetimUi().open();
}
function pendingNotice(value) {
  return value > 0
    ? h(
        'div',
        { class: 'notice' },
        h('strong', {}, `${money(value)} dağılım bekliyor. `),
        'Bu ödeme genel kasaya yansımıştır; alış onaylanınca ilgili kanallara dağılır. Ortak gider değildir.'
      )
    : null;
}
function cashActions() {
  return canEditCash()
    ? [
        button('+ Gelir gir', event => run(event.currentTarget, () => incomeDialog())),
        button('+ Gider kaydet', event => run(event.currentTarget, () => expenseDialog()), 'primary'),
      ]
    : [];
}
// Ana sayfa özeti tek istekte: panel, kanal eşikleri ve takip özeti sunucunun tek anlık görüntüsünden gelir; bakiye, eşik
// rozeti ve kart borcu birbiriyle çelişmez, sunucu kart hesabını bir kez yapar. Eski sunucuda uç yoksa (404) panel tek başına
// alınır, eşikler ve özet eski uçlardan ayrıca yüklenir (eski davranış); uç bir kez 404 verdiyse sayfa yenilenene kadar
// yeniden denenmez. Birleşik ucun sunucu hatası (5xx) da ana sayfayı düşürmez: kasa bakiyeleri panel ucundan gelir, eşikler ve
// özet kendi uçlarından yüklenip kendi hatalarını gösterir; uç sonraki açılışta yeniden denenir. Sunucu özeti ya da eşikleri
// hesaplayamayınca paneli onlarsız (null) da döndürebilir; eksik parça aynı yolla ayrıca yüklenir. Görüntüleme sürümü yalnız
// paneli okur.
let homeSummaryMissing = false;
async function loadHomeSummary(days) {
  if (runtime.saltOkunur) return { panel: await api('/api/rapor/panel'), kasaEsikleri: null, takipOzeti: null };
  if (!homeSummaryMissing) {
    try {
      return await api(`/api/rapor/ana-sayfa?gun=${days}`);
    } catch (error) {
      if (error.status === 404) homeSummaryMissing = true;
      else if (!(error.status >= 500)) throw error;
    }
  }
  return { panel: await api('/api/rapor/panel'), kasaEsikleri: null, takipOzeti: null };
}
// İnceleme kutusu (webui-6): ana sayfa bütün alış listesini (kalem, dağılım ve ödemeleriyle) indirmez; sunucu inceleme bekleyen
// sayısını ve en yeni birkaç alışı döndürür. Eski sunucuda uç yoksa (404; GET'i olmayan /api/alis/{id} deseni yüzünden 405)
// eski davranışla liste okunup süzülür ve uç sayfa yenilenene kadar yeniden denenmez. Başka hata ana sayfaya yansır.
let reviewSummaryMissing = false;
async function loadReviewSummary(count) {
  if (!canEditCash()) return { sayi: 0, ogeler: [] };
  if (!reviewSummaryMissing) {
    try {
      return await api(`/api/alis/inceleme-ozeti?adet=${count}`);
    } catch (error) {
      if (error.status !== 404 && error.status !== 405) throw error;
      reviewSummaryMissing = true;
    }
  }
  const review = (await api('/api/alis')).filter(p => p.durum === 'Incelemede');
  return { sayi: review.length, ogeler: review.slice(0, count) };
}
async function renderHome(generation) {
  page('Kasalar', 'Genel kasa ve kanal bakiyeleri', cashActions());
  const [home, review] = await Promise.all([loadHomeSummary(30), loadReviewSummary(4)]);
  if (generation !== renderId) return;
  const panel = home.panel;
  const paymentOverview = h('div');
  const balances = h('div', { class: 'channel-balances' });
  const unassignedDebt = h('div');
  const comparisonHistory = h('div');
  const thresholdStatus = h('div');
  // Kart borcu durumu ayrı tutulur: eşikler sonradan çizilince "yüklenemedi" iletisi "yükleniyor…"a dönmez.
  let cardDebts = null;
  let debtFailed = false;
  let thresholds = [];
  const drawBalances = debts => {
    cardDebts = debts;
    const matches = (channel, debt) =>
      debt.kanalId != null && (channel.kanalId != null ? channel.kanalId === debt.kanalId : channel.kanal === debt.kanal);
    balances.replaceChildren(
      ...panel.kanallar.map(k => {
        const debt = debts ? sumCents(debts.filter(row => matches(k, row)).map(row => row.tutar)) / 100 : undefined;
        const threshold = thresholds.find(row => (k.kanalId != null ? row.kanalId === k.kanalId : row.kanal === k.kanal));
        return h(
          'div',
          { class: `channel-balance${threshold?.etkin && threshold.esikAltinda ? ' below-threshold' : ''}` },
          h('span', { class: 'channel-name' }, k.kanal),
          moneyNode(k.bakiye, k.bakiye < 0 ? 'negative' : ''),
          threshold?.etkin &&
            threshold.esikAltinda &&
            h('span', { class: 'badge pending' }, `Alt sınırın altında · Sınır ${money(threshold.tutar)}`),
          !runtime.saltOkunur &&
            h(
              'div',
              { class: 'channel-card-debt' },
              debt == null
                ? debtFailed
                  ? 'Kart borcu yüklenemedi.'
                  : 'Kart borcu yükleniyor…'
                : h('span', {}, 'Kalan kart borcu: ', moneyNode(debt))
            )
        );
      })
    );
    const other = (debts || []).filter(row => row.tutar > 0 && !panel.kanallar.some(channel => matches(channel, row)));
    unassignedDebt.replaceChildren(
      ...childValues([
        other.length > 0 &&
          h(
            'div',
            { class: 'notice' },
            other.map(row =>
              h('div', {}, `${row.kanalId == null ? 'Kanalı belirsiz kart borcu' : `${row.kanal} kart borcu`}: ${money(row.tutar)}`)
            )
          ),
        debts && help('Kart borçları kasa bakiyesine dahil edilmez; ödeme kaydedildiğinde kasadan düşer.'),
      ])
    );
  };
  drawBalances(null);
  const showOverview = (data, days) => {
    debtFailed = false;
    drawBalances(data.kanalKartBorclari || []);
    paymentOverview.replaceChildren(financeUi.overview(data, days, selected => run(null, () => loadOverview(selected))));
  };
  const loadOverview = async days => {
    try {
      const data = await api(`/api/takip/ozet?gun=${days}`);
      if (generation === renderId) showOverview(data, days);
    } catch (error) {
      if (generation === renderId && !isAbortError(error)) {
        debtFailed = true;
        drawBalances(null);
        paymentOverview.replaceChildren(
          help(`Ödeme özeti yüklenemedi: ${error.message}`),
          button('Yeniden dene', () => run(null, () => loadOverview(days)), 'small')
        );
      }
    }
  };
  const showThresholds = rows => {
    thresholds = rows;
    drawBalances(cardDebts);
    thresholdStatus.replaceChildren();
  };
  const loadThresholds = async () => {
    try {
      const rows = await api('/api/kasa-esikleri');
      if (generation === renderId) showThresholds(rows);
    } catch (error) {
      if (generation === renderId)
        thresholdStatus.replaceChildren(
          help(`Kanal uyarıları yüklenemedi: ${error.message}`),
          button('Uyarıları yeniden yükle', () => run(null, loadThresholds), 'small')
        );
    }
  };
  const loadComparisons = async () => {
    try {
      const rows = await api('/api/kasa-kontrol');
      if (generation === renderId) comparisonHistory.replaceChildren(cashControlsUi.history(rows));
    } catch (error) {
      if (generation === renderId)
        comparisonHistory.replaceChildren(
          help(`Bakiye karşılaştırmaları yüklenemedi: ${error.message}`),
          button('Geçmişi yeniden yükle', () => run(null, loadComparisons), 'small')
        );
    }
  };
  if (!runtime.saltOkunur) {
    if (home.takipOzeti) showOverview(home.takipOzeti, 30);
    else loadOverview(30);
    if (home.kasaEsikleri) showThresholds(home.kasaEsikleri);
    else loadThresholds();
    loadComparisons();
  }
  const inbox = canEditCash()
    ? section(
        'Alışlar',
        h(
          'div',
          {},
          review.sayi
            ? h(
                'p',
                { class: 'plain-note' },
                `${review.sayi} alış inceleme bekliyor. Malları ve kanal paylarını kontrol ederek onaylayabilirsiniz.`
              )
            : h('p', { class: 'plain-note' }, 'İnceleme bekleyen alış yok.'),
          review.ogeler.map(purchaseRow)
        ),
        button('Alışları aç', () => navigate('purchases'), 'small')
      )
    : null;
  const hero = h(
    'div',
    { class: 'cash-hero' },
    h(
      'div',
      {},
      h('span', { class: 'summary-label' }, 'Genel kasa'),
      h('strong', { class: 'cash-total money', role: 'region', 'aria-label': 'Genel kasa', tabindex: '0' }, money(panel.guncelKasa))
    )
  );
  const summaryCards = h(
    'div',
    { class: 'summary-strip' },
    summary('Bu haftanın sonucu', money(panel.buHaftaSonucu)),
    summary('Bu ayın sonucu', money(panel.buAySonucu)),
    summary('Kanal sayısı', String(panel.kanallar.length))
  );
  $('#view').replaceChildren(
    ...childValues([
      hero,
      runtime.saltOkunur && help('Bu ekran canlı kasa kayıtlarını görüntüler. Bu sürümde kayıtlar değiştirilemez.'),
      summaryCards,
      pendingNotice(panel.dagilimBekleyenTutar),
      !runtime.saltOkunur && financeUi.untrackedNotice(home.takipsizKayitlar),
      section('Kanal kasaları', h('div', {}, balances, thresholdStatus, unassignedDebt)),
      !runtime.saltOkunur && comparisonHistory,
      !runtime.saltOkunur && paymentOverview,
      help(
        'Yeni kart takibinde kaydedilen kart ödemeleri kasadan düşer; kredi taksitleri kendi tarihinde otomatik işlenir. Eski kayıtlarda geçiş öncesi kasa kuralı korunur. Sabit, ortak ve dağılım bekleyen giderler nedeniyle kanal toplamı genel kasadan farklı olabilir.'
      ),
      inbox,
    ])
  );
}
async function renderWeekly(generation) {
  page('Haftalık kasa', 'Dönem gelirleri, giderleri ve devirler');
  const weeks = await api('/api/rapor/haftalik');
  if (generation !== renderId) return;
  if (!weeks.length) {
    $('#view').replaceChildren(empty('Henüz kasa dönemi yok', 'Takip başlangıcını Ayarlar ekranından kontrol edin.'));
    return;
  }
  let selected = currentPeriod(weeks);
  const details = h('div');
  const period = select(
    'donem',
    [...weeks].reverse().map(w => ({ value: w.donem.start, label: `${dateText(w.donem.start)} – ${dateText(w.donem.end)}` })),
    selected.donem.start,
    {
      'aria-label': 'Haftalık kasa dönemi',
      onchange: () => {
        selected = weeks.find(w => w.donem.start === period.value);
        draw();
      },
    }
  );
  const draw = () =>
    details.replaceChildren(
      ...childValues([
        h(
          'div',
          { class: 'summary-strip' },
          summary('Genel kasa devri', money(selected.kasaDevir)),
          summary('Dönem gelen', money(selected.toplamGelen)),
          summary('Dönem giden', money(selected.toplamGiden))
        ),
        pendingNotice(selected.dagilimBekleyenTutar),
        table(
          ['Kanal', 'Gelen', 'Diğer gider', 'Dönem sonucu', 'Kanal devri'],
          selected.kanallar.map(k => [k.kanal, moneyNode(k.gelen), moneyNode(k.giden), moneyNode(k.sonuc), moneyNode(k.devir)]),
          'Dönemin kanal sonuçları'
        ),
        h(
          'p',
          { class: 'plan-note' },
          `Genel kasa dönem sonucu: ${money(selected.kasaSonucu)}. Yeni kart takibinde kaydedilen ödemeler kasadan düşer; eski kartlarda geçiş öncesi erteleme kuralı sürer. Kredi taksitleri kendi tarihinde otomatik işlenir.`
        ),
      ])
    );
  // Sunucunun veri sağlığı uyarısı (ör. rapor ufkunun ötesinde tarihli kayıt) yalnız son dönemde gelir ama raporun tamamı için
  // geçerlidir: seçili dönemden bağımsız, listenin üstünde gösterilir.
  const health = dataHealthWarning(weeks);
  $('#view').replaceChildren(
    ...childValues([
      health && h('div', { class: 'notice danger', role: 'alert' }, health),
      h(
        'div',
        { class: 'toolbar' },
        period,
        canEditCash() && button('Dönem geliri gir', event => run(event.currentTarget, () => incomeDialog(selected.donem.start)), 'primary')
      ),
      details,
    ])
  );
  draw();
}
async function renderMonthly(generation, month = today().slice(0, 7)) {
  const request = ++monthlyRequest;
  page('Aylık kasa', 'Kanal bazında aylık gelir ve giderler');
  const [year, period] = month.split('-').map(Number);
  const report = await api(`/api/rapor/aylik?yil=${year}&ay=${period}`);
  if (generation !== renderId || request !== monthlyRequest) return;
  const total = monthlyTotals(report);
  const monthInput = monthPicker('ay', month, { label: 'Rapor ayı' });
  const lock = h('div');
  // Kural 2 (K2): kredi girişi Gelen ve Ay sonucu dışında, ayrı sütunda. Kural 1 ile dondurulmuş kapalı ayda (ya da eski sunucuda)
  // takipli kredi çekimi Gelen'in ve Ay sonucunun içindedir; sütun bilgi amaçlıdır.
  const creditSeparate = (report.kuralSurumu || 1) >= 2;
  const credit = report.krediGirisi || 0;
  // Sunucu sayıları kullanıcı girdisi değildir: eksi ya da üslü kredi girişi sayfayı düşürmez (serverCents).
  const unassignedCredit = (serverCents(credit) - sumCents(report.kanallar.map(k => k.krediGirisi))) / 100;
  const resultLabel = creditSeparate ? 'Ay sonucu (kredi hariç)' : 'Ay sonucu';
  $('#view').replaceChildren(
    ...childValues([
      h(
        'div',
        { class: 'toolbar' },
        monthInput.node,
        button('Ayı göster', event => run(event.currentTarget, () => renderMonthly(generation, monthInput.value)))
      ),
      !runtime.saltOkunur && lock,
      report.dondurulmus &&
        h(
          'div',
          { class: 'notice', role: 'status' },
          h('strong', {}, 'Kapatılmış ay. '),
          `Rapor, ay kapatıldığı andaki haliyle gösterilir; sonraki kural değişiklikleri bu ayı etkilemez${creditSeparate ? '' : ' (eski kural: takipli kredi çekimi Gelen ve Ay sonucu içindedir)'}. Değişiklik için ayı gerekçeyle açın.`
        ),
      report.veriSagligiUyarisi && h('div', { class: 'notice', role: 'status' }, report.veriSagligiUyarisi),
      h(
        'div',
        { class: 'summary-strip' },
        summary('Aylık gelen', money(total.incoming)),
        summary('Aylık gider', money(total.expenses)),
        summary(
          resultLabel,
          money(total.result),
          creditSeparate && credit !== 0 ? `Kredi girişi ${money(credit)} sonuca dahil değildir.` : null
        )
      ),
      pendingNotice(report.dagilimBekleyenTutar),
      table(
        ['Kanal', 'Gelen', 'Kredi girişi', 'Diğer gider', 'Sabit gider', 'Kredi kartı', 'Ortak pay', resultLabel],
        report.kanallar.map(k => [
          k.kanal,
          moneyNode(k.gelen),
          moneyNode(k.krediGirisi || 0),
          moneyNode(k.cariGiden),
          moneyNode(k.sabitGider),
          moneyNode(k.krediKarti),
          moneyNode(k.ortakPay),
          moneyNode(k.aySonucu),
        ]),
        'Aylık kanal sonuçları'
      ),
      creditSeparate &&
        credit !== 0 &&
        h(
          'p',
          { class: 'plan-note' },
          `Kredi girişi: ${money(credit)}. Genel kasaya (takipli kredide kanal kasasına da) girer; aylık gelen ve ay sonucu (faaliyet sonucu) içinde değildir.${unassignedCredit !== 0 ? ` Kanala dağıtılmayan eski kredi çekimi: ${money(unassignedCredit)}.` : ''}`
        ),
      !creditSeparate &&
        report.kanallar.some(k => k.krediGirisi) &&
        h(
          'p',
          { class: 'plan-note' },
          'Bu ayın raporu eski kuralla dondurulmuştur: "Kredi girişi" sütunundaki takipli kredi çekimi "Gelen" ve "Ay sonucu" içindedir.'
        ),
      report.genelGelir > 0 &&
        h(
          'p',
          { class: 'plan-note' },
          `Yalnız genel kasa geliri: ${money(report.genelGelir)}. Yukarıdaki aylık gelen toplamına dahildir; kanal kasalarına dağıtılmaz.`
        ),
      report.genelGider > 0 &&
        h(
          'p',
          { class: 'plan-note' },
          `Yalnız genel kasa gideri: ${money(report.genelGider)}. Yukarıdaki aylık gider toplamına dahildir; kanal kasalarına dağıtılmaz.`
        ),
      h(
        'p',
        { class: 'plan-note' },
        'Yeni kart takibinde ödeme kaydı; eski kartlarda geçiş öncesi erteleme kuralı geçerlidir. Kredi taksitleri tarihinde otomatik işlenir. Ortak giderler ve dağılım bekleyen tutarlar ayrı izlenir.'
      ),
    ])
  );
  if (!runtime.saltOkunur) {
    try {
      const panel = await monthlyUi.lockPanel(
        month,
        () => (generation === renderId ? renderMonthly(generation, month) : Promise.resolve()),
        report
      );
      if (generation === renderId && request === monthlyRequest) lock.replaceChildren(panel);
    } catch (error) {
      if (generation === renderId && request === monthlyRequest) lock.replaceChildren(help(`Ay kilidi yüklenemedi: ${error.message}`));
    }
  }
}
async function renderTransactions(generation, filters = {}) {
  page(
    'İşlemler',
    'Genel kasa gider kayıtları',
    runtime.saltOkunur
      ? []
      : [
          button('Gider raporu indir', event =>
            run(event.currentTarget, async () => {
              state.channels = await api('/api/kanallar');
              exportDialog();
            })
          ),
          ...cashActions(),
        ]
  );
  const start = filters.baslangic || today().slice(0, 8) + '01';
  const end = filters.bitis || today();
  const query = new URLSearchParams({ baslangic: start, bitis: end });
  if (filters.kanal) query.set('kanal', filters.kanal);
  if (filters.cari) query.set('cari', filters.cari);
  const [expenses, channels] = await Promise.all([api(`/api/islemler?${query}`), api('/api/kanallar')]);
  if (generation !== renderId) return;
  state.channels = channels;
  const form = h(
    'form',
    { class: 'filter-period' },
    field('Başlangıç', input('baslangic', start, { type: 'date', required: true })),
    field('Bitiş', input('bitis', end, { type: 'date', required: true })),
    field(
      'Kanal',
      select(
        'kanal',
        [
          { value: '', label: 'Tüm kanallar' },
          ...channels.map(c => ({ value: c.ad, label: c.ad })),
          { value: 'Ortak', label: 'Ortak' },
          { value: 'Dağılım bekliyor', label: 'Dağılım bekliyor' },
        ],
        filters.kanal
      )
    ),
    field('Açıklama / ödeme yapılan yer', input('cari', filters.cari || '', { type: 'search' })),
    h('button', { type: 'submit', class: 'button' }, 'Listele')
  );
  form.addEventListener('submit', event => {
    event.preventDefault();
    const data = values(form);
    run(form.querySelector('button'), async () => {
      if (data.baslangic > data.bitis) throw new Error('Bitiş tarihi başlangıçtan önce olamaz.');
      await renderTransactions(generation, data);
    });
  });
  const rows = expenses.map(e => [
    dateText(e.tarih),
    h('span', {}, e.cari, e.not && h('small', { class: 'table-sub' }, e.not)),
    h('span', {}, e.kanal, e.alisId && h('small', { class: 'table-sub' }, `Alış #${e.alisId}`)),
    { Cari: 'Diğer gider', SabitGider: 'Sabit gider', KrediKarti: 'Kredi kartı' }[e.tip] || e.tip,
    moneyNode(e.tutarTl),
    e.ekstreKayitId
      ? h(
          'div',
          {},
          help('Ekstre / Hareket Yükle bölümünden yönetilir.'),
          canEditCash() && button('Kaynak belgeyi aç', () => navigate('imports', { kayitId: e.ekstreKayitId }), 'small')
        )
      : e.aylikGiderOdemeId
        ? h(
            'div',
            {},
            help('Aylık Giderler bölümünden yönetilir.'),
            !runtime.saltOkunur && button('Aylık Giderler’i aç', () => navigate('monthly-expenses'), 'small')
          )
        : canEditCash()
          ? e.alisId
            ? button('Alışı aç', () => navigate('purchase', e.alisId), 'small')
            : h(
                'div',
                { class: 'row-actions' },
                button('Düzenle', event => run(event.currentTarget, () => expenseDialog(e)), 'small'),
                button('Sil', () => deleteExpense(e), 'small danger')
              )
          : '',
  ]);
  $('#view').replaceChildren(
    form,
    h(
      'p',
      { class: 'plan-note' },
      'Kanal filtresi ilgili giderin tam tutarını gösterir; çok kanallı bir ödemenin kanal payı toplamı değildir. Alışa bağlı ödemeler alış kaydından düzeltilir.'
    ),
    expenses.length
      ? table(['Tarih', 'Açıklama', 'Kanal', 'Tür', 'Tutar', ''], rows, 'Gider kayıtları')
      : empty('Bu aralıkta gider yok', 'Tarih veya kanal filtresini değiştirerek diğer kayıtları görebilirsiniz.')
  );
}
async function incomeDialog(periodStart = null) {
  // Dönem listesi pencerenin verisidir: ekran sinyaline bağlanmaz, pencere açılırken başka ekrana geçilse de pencere açılır.
  const [weeks, channels] = await Promise.all([api('/api/rapor/haftalik', { screen: false }), api('/api/kanallar')]);
  if (!weeks.length) throw new Error('Gelir girmek için geçerli kasa dönemi gerekir.');
  const initial = periodStart || currentPeriod(weeks).donem.start;
  const period = select(
    'donemStart',
    [...weeks].reverse().map(w => ({ value: w.donem.start, label: `${dateText(w.donem.start)} – ${dateText(w.donem.end)}` })),
    initial
  );
  const channel = select('kanal', [{ value: '', label: 'Kanal seçin' }, ...channels.map(c => ({ value: c.ad, label: c.ad }))], '', {
    required: true,
  });
  const total = signedAmountField('tutarTl', 0, 'Bu dönem için toplam gelir (₺)');
  const notice = h('div', { class: 'notice', role: 'status', 'aria-live': 'polite' });
  let incomes = [];
  let loading = false;
  let loadError = false;
  let version = 0;
  let form;
  const selectedChannel = () => channels.find(item => item.ad === channel.value);
  const fill = () => {
    const selected = incomeSelection(incomes, selectedChannel());
    const locked = loading || loadError || !channel.value || selected.readOnly;
    total.set(selected.total);
    total.setReadOnly(locked);
    if (form) form.querySelector('button[type="submit"]').disabled = locked;
    notice.textContent = loading
      ? 'Dönem gelirleri yükleniyor…'
      : loadError
        ? 'Dönem gelirleri yüklenemedi. Başka dönem seçip yeniden deneyin; mevcut bilgilerle kayıt yapılamaz.'
        : selected.readOnly
          ? `Bu dönem ve kanal için ${selected.count} eski gelir kaydı var. Toplam ${money(selected.total)} kasaya dahildir. Geçmiş tutarları korumak için bu grup burada değiştirilemez. Başka dönem veya kanal seçerek normal gelir kaydı yapabilirsiniz.`
          : 'Buraya seçilen kanalın bu dönemdeki toplam gelirini yazın. Kayıt varsa yeni tutar öncekinin yerine geçer; üzerine eklenmez.';
  };
  const refresh = async () => {
    const generation = ++version;
    loading = true;
    loadError = true;
    incomes = [];
    fill();
    try {
      const data = await api(`/api/gelenler?donemStart=${period.value}`);
      if (generation === version) {
        incomes = data;
        loadError = false;
      }
    } finally {
      if (generation === version) {
        loading = false;
        fill();
      }
    }
  };
  period.addEventListener('change', () => run(null, refresh));
  channel.addEventListener('change', fill);
  await refresh();
  form = formDialog(
    'Kanal geliri gir',
    h('div', { class: 'stack' }, field('Kasa dönemi', period), field('Kanal', channel), total.node, notice),
    'Dönem toplamını kaydet',
    async () => {
      if (loading || loadError) throw new Error('Dönem gelirleri yüklenmeden kayıt yapılamaz. Lütfen yeniden deneyin.');
      const selected = incomeSelection(incomes, selectedChannel());
      if (selected.readOnly) throw new Error('Bu eski gelir grubu geçmiş tutarları korumak için değiştirilemez.');
      await refreshOnConflict(
        () =>
          api('/api/gelenler', {
            method: 'PUT',
            body: { donemStart: period.value, kanal: channel.value, tutarTl: total.read(), surum: selected.surum },
          }),
        refresh
      );
      closeModal();
      toast('Kanal geliri kaydedildi.');
      await navigate(state.view);
    }
  );
  fill();
}
async function expenseDialog(expense = null) {
  // Yeni gider istek kimliği taşır (appcore-5): yanıtı kaybolan kayıt aynı gövdeyle yeniden gönderilince ikinci gider oluşmaz.
  const identity = requestIdentity();
  const [channels, cards] = await Promise.all([api('/api/kanallar'), api('/api/kredikartlari')]);
  const type = select(
    'tip',
    [
      { value: 'Cari', label: 'Diğer gider' },
      { value: 'SabitGider', label: 'Sabit gider' },
      { value: 'KrediKarti', label: 'Kredi kartı gideri' },
    ],
    expense?.tip || 'Cari'
  );
  // K3: yeni kredi kartı gideri yalnız yeni takipteki, yeni kullanıma açık bir karta bağlanır. Düzenlenen eski kredi kartı kaydı
  // kendi (eski) kartıyla ya da kartsız kalabilir: tutar/not düzeltilir; yalnız o seçenek ayrıca listelenir.
  const tracked = cards.filter(k => k.yeniTakip && k.aktif);
  const oldCardExpense = expense?.tip === 'KrediKarti' ? expense : null;
  const cardlessOld = Boolean(oldCardExpense) && oldCardExpense.krediKartiId == null;
  const oldCard =
    oldCardExpense && oldCardExpense.krediKartiId != null && !tracked.some(k => k.id === oldCardExpense.krediKartiId)
      ? { value: oldCardExpense.krediKartiId, label: `${cards.find(k => k.id === oldCardExpense.krediKartiId)?.ad || 'Kart'} (eski kayıt)` }
      : null;
  const cardRequired =
    'Kredi kartı gideri için yeni takipteki bir kart seçin. Kart eski takipteyse önce kart ekranından yeni takibe geçirin.';
  const card = select(
    'krediKartiId',
    [
      { value: '', label: cardlessOld ? '— Kartsız eski kayıt —' : 'Kart seçin' },
      ...tracked.map(k => ({ value: k.id, label: k.ad })),
      ...(oldCard ? [oldCard] : []),
    ],
    expense?.krediKartiId
  );
  const cardField = field(
    'Kredi kartı',
    card,
    !tracked.length && !oldCardExpense
      ? help('Takipte kart yok. Kredi Kartları bölümünden kart ekleyin ya da eski kartı yeni takibe geçirin.')
      : null
  );
  // gap-coklu-giris-cift-sayim-mutabakat-6: taksit yalnız yeni kart giderinde; düzenlemede plan değişmez (ödenmemişse gider silinip yeniden girilir).
  const installments = installmentEditor();
  const installmentSync = () => {
    installments.node.hidden = Boolean(expense) || type.value !== 'KrediKarti' || !card.value;
  };
  card.addEventListener('change', installmentSync);
  const cardVisibility = () => {
    cardField.hidden = type.value !== 'KrediKarti';
    card.required = type.value === 'KrediKarti' && !cardlessOld;
    installmentSync();
  };
  type.addEventListener('change', cardVisibility);
  cardVisibility();
  const channel = select(
    'kanal',
    [
      { value: '', label: 'Kanal seçin' },
      ...channels.filter(c => c.aktif || c.ad === expense?.kanal).map(c => ({ value: c.ad, label: c.ad })),
      { value: 'Ortak', label: 'Ortak' },
    ],
    expense?.kanal || '',
    { required: true }
  );
  const total = signedAmountField('tutarTl', expense?.tutarTl ?? '', 'Tutar (₺)');
  formDialog(
    expense ? 'Gideri düzenle' : 'Gider kaydet',
    h(
      'div',
      { class: 'stack' },
      field('Açıklama / ödeme yapılan yer', input('cari', expense?.cari || '', { required: true, maxlength: 200 })),
      h(
        'div',
        { class: 'form-grid' },
        field('Tarih', input('tarih', expense?.tarih || today(), { type: 'date', required: true })),
        total.node,
        field('Kanal', channel),
        field('Gider türü', type)
      ),
      cardField,
      installments.node,
      field('Not', h('textarea', { name: 'not', maxlength: 2000 }, expense?.not || '')),
      help(
        'Alış olarak kaydettiğiniz ödemenin ikinci bir giderini oluşturmayın. O alışın içinden ödeme ekleyin veya mevcut gideri bağlayın.'
      )
    ),
    'Gideri kaydet',
    async form => {
      const data = values(form);
      if (data.tip === 'KrediKarti' && !card.value && !cardlessOld)
        throw Object.assign(new Error(cardRequired), { fields: { krediKartiId: cardRequired } });
      const body = {
        tarih: data.tarih,
        cari: data.cari.trim(),
        tutarTl: total.read(),
        kanal: data.kanal,
        tip: data.tip,
        not: data.not.trim() || null,
        krediKartiId: data.tip === 'KrediKarti' ? optionalId(card.value) : null,
        ...installments.read(data.tarih),
        surum: expense?.surum ?? 0,
      };
      if (
        !expense &&
        !(await confirmSimilar(
          form,
          { tur: 'Gider', tarih: body.tarih, tutar: body.tutarTl, krediKartiId: body.krediKartiId, kanal: body.kanal, alisId: null },
          body
        ))
      )
        return;
      await refreshOnConflict(
        () =>
          api(expense ? `/api/islemler/${expense.id}` : '/api/islemler', {
            method: expense ? 'PUT' : 'POST',
            body: expense ? body : identity(body),
          }),
        () => navigate(state.view)
      );
      closeModal();
      toast('Gider kaydedildi.');
      await navigate(state.view);
    }
  );
}
function deleteExpense(expense) {
  formDialog(
    'Gideri sil',
    h(
      'p',
      { class: 'plain-note' },
      `${dateText(expense.tarih)} tarihli “${expense.cari}” gideri (${money(expense.tutarTl)}) silinecek ve kasa sonuçları güncellenecek.`
    ),
    'Gideri sil',
    async () => {
      await api(`/api/islemler/${expense.id}`, { method: 'DELETE' });
      closeModal();
      toast('Gider silindi.');
      await navigate('transactions');
    },
    { danger: true }
  );
}
// Tamamlanmış ayların kanal kümesi (Ortak gider dağılımı ve rapor satırları) sunucuda dondurulur: kanal eklemek, pasife almak ve
// sırasını değiştirmek ay kilidi varken de serbesttir, yalnız açılış devri kilitte değişmez. Kilit notu formda gösterilir.
function channelDialog(channel = null, lockedUntil = null) {
  const opening = signedAmountField('acilisDevri', channel?.acilisDevri ?? 0, 'Açılış devri (₺)');
  const lockNote =
    lockedUntil &&
    h(
      'p',
      { class: 'notice' },
      `${dateText(lockedUntil)} dahil aylar kilitli. ${channel ? 'Kanal adı, aktifliği ve sırası değiştirilebilir; açılış devri değiştirilemez.' : 'Yeni kanal açılış devri 0 ile eklenir; aktif ya da pasif olabilir.'}`
    );
  formDialog(
    channel ? `${channel.ad} · Kanalı düzenle` : 'Kanal ekle',
    h(
      'div',
      { class: 'stack' },
      field('Kanal adı', input('ad', channel?.ad || '', { required: true, maxlength: 200 })),
      h(
        'div',
        { class: 'form-grid' },
        opening.node,
        field(
          'Görüntüleme sırası',
          input('sira', channel?.sira ?? state.channels.length, { required: true, type: 'number', step: 1, min: 0 })
        )
      ),
      h('label', {}, input('aktif', '1', { type: 'checkbox', checked: channel?.aktif ?? true }), 'Kanal aktif'),
      lockNote,
      help(
        'Geçmiş kayıtları olan kanalı silmek yerine pasife alın. Ortak giderler aylık raporda o ayın aktif kanallarına sıralarına göre bölünür. Tamamlanmış ayların kanal kümesi dondurulur: kanal eklemek, pasife almak ya da sırasını değiştirmek yalnız içinde bulunulan ve sonraki ayların Ortak dağılımını etkiler, tamamlanmış ayların raporu değişmez; bu değişiklikler ay kilidi varken de yapılabilir. Açılış devri takip başlangıcından itibaren kanal bakiyesini değiştirir; ay kilidi varken değiştirilemez.'
      ),
      channel &&
        button(
          'Kanalı sil',
          () =>
            formDialog(
              'Kanalı sil',
              h(
                'p',
                { class: 'plain-note' },
                `${channel.ad} kanalı silinecek; tanımlıysa kasa alt sınırı da silinir. Geçmiş kaydı varsa ya da tamamlanmış bir ayın kanal kümesinde yer alıyorsa silinemez; pasife alabilirsiniz.`
              ),
              'Kanalı sil',
              async () => {
                await api(`/api/kanallar/${channel.id}`, { method: 'DELETE' });
                closeModal();
                toast('Kanal silindi.');
                await navigate('tools');
              },
              { danger: true }
            ),
          'danger small'
        )
    ),
    'Kanalı kaydet',
    async form => {
      const data = values(form);
      await refreshOnConflict(
        () =>
          api(channel ? `/api/kanallar/${channel.id}` : '/api/kanallar', {
            method: channel ? 'PUT' : 'POST',
            body: {
              ad: data.ad.trim(),
              aktif: Boolean(data.aktif),
              sira: Number(data.sira),
              acilisDevri: opening.read(),
              surum: channel?.surum ?? 0,
            },
          }),
        () => navigate('tools')
      );
      closeModal();
      toast('Kanal kaydedildi.');
      await navigate('tools');
    }
  );
}
function openingDialog(settings) {
  const opening = signedAmountField('kasaAcilisDevri', settings.kasaAcilisDevri, 'Genel kasa açılış devri (₺)');
  formDialog(
    'Kasa başlangıcını düzenle',
    h(
      'div',
      { class: 'stack' },
      field('Takip başlangıcı', input('takipBaslangic', settings.takipBaslangic, { type: 'date', required: true })),
      opening.node,
      help('Hareketler kaydedildikten sonra takip başlangıcı değiştirilemez. Açılış devri, tüm sonraki genel kasa bakiyelerini etkiler.')
    ),
    'Başlangıcı kaydet',
    async form => {
      const data = values(form);
      await refreshOnConflict(
        () =>
          api('/api/ayarlar', {
            method: 'PUT',
            body: { takipBaslangic: data.takipBaslangic, kasaAcilisDevri: opening.read(), surum: settings.surum ?? 0 },
          }),
        () => navigate('tools')
      );
      closeModal();
      toast('Kasa başlangıcı kaydedildi.');
      await navigate('tools');
    }
  );
}
function viewerPasswordDialog() {
  formDialog(
    'İzleyici şifresi',
    h(
      'div',
      { class: 'stack' },
      field(
        'Yeni izleyici şifresi',
        input('yeniSifre', '', { type: 'password', required: true, minlength: 12, maxlength: 1024, autocomplete: 'new-password' }),
        help('En az 12 karakter kullanın.')
      ),
      help('İzleyici kasaları ve raporları okuyabilir; kayıtları değiştiremez. Şifre değişince eski izleyici oturumları kapanır.')
    ),
    'Şifreyi kaydet',
    async form => {
      const data = values(form);
      const rule = viewerPasswordError(data.yeniSifre);
      if (rule) throw Object.assign(new Error(rule), { fields: { yeniSifre: rule } });
      await api('/api/ayarlar/izleyici-sifre', { method: 'PUT', body: { yeniSifre: data.yeniSifre } });
      closeModal();
      toast('İzleyici şifresi güncellendi. Eski izleyici oturumları kapandı.');
      await navigate('tools');
    }
  );
}
function download(blob, name) {
  const url = URL.createObjectURL(blob);
  const link = h('a', { href: url, download: name });
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 30000);
}
function safeExternalLink(url, title) {
  if (!url) return null;
  try {
    const parsed = new URL(url, location.origin);
    if (parsed.protocol !== 'https:' && parsed.origin !== location.origin) return null;
    return h('a', { class: 'button', href: parsed.href, rel: 'noopener', target: '_blank' }, title);
  } catch {
    return null;
  }
}
function passwordDialog() {
  formDialog(
    'Şifremi değiştir',
    h(
      'div',
      { class: 'stack' },
      field(
        'Mevcut şifre',
        input('mevcutSifre', '', { type: 'password', required: true, maxlength: 1024, autocomplete: 'current-password' })
      ),
      field(
        'Yeni şifre',
        input('yeniSifre', '', { type: 'password', required: true, minlength: 12, maxlength: 1024, autocomplete: 'new-password' }),
        help('En az 12 karakter kullanın.')
      ),
      field(
        'Yeni şifreyi tekrar girin',
        input('tekrar', '', { type: 'password', required: true, minlength: 12, maxlength: 1024, autocomplete: 'new-password' })
      )
    ),
    'Şifreyi değiştir',
    async form => {
      const data = values(form);
      repeatedPassword(data);
      await api('/api/auth/sifre', { method: 'POST', body: { mevcutSifre: data.mevcutSifre, yeniSifre: data.yeniSifre } });
      clearSession();
      toast('Şifreniz değişti. Yeni şifrenizle giriş yapın.');
    }
  );
}
function recoveryCodeDialog() {
  formDialog(
    'Kurtarma kodu oluştur',
    h(
      'div',
      { class: 'stack' },
      help('Yeni kod oluşturulunca önceki kod geçersiz olur. Kod yalnız bu ekranda bir kez gösterilir.'),
      field(
        'Mevcut şifre',
        input('mevcutSifre', '', { type: 'password', required: true, maxlength: 1024, autocomplete: 'current-password' })
      )
    ),
    'Kodu oluştur',
    async form => {
      const result = await api('/api/auth/kurtarma-kodu', { method: 'POST', body: values(form) });
      const code = h('code', { class: 'recovery-code', tabindex: '0' }, result.kod);
      openModal(
        'Kurtarma kodunuzu saklayın',
        h(
          'div',
          { class: 'stack' },
          h(
            'div',
            { class: 'notice' },
            'Bu kod bir kez gösterilir ve yalnız bir kurtarma işleminde kullanılabilir. Pencereyi kapatmadan önce güvenli bir yere kaydedin.'
          ),
          code,
          button('Kodu kopyala', event =>
            run(event.currentTarget, async () => {
              if (!navigator.clipboard) throw new Error('Tarayıcı kopyalamaya izin vermiyor. Kodu seçip elle kopyalayabilirsiniz.');
              await navigator.clipboard.writeText(code.textContent);
              toast('Kurtarma kodu kopyalandı.');
            })
          ),
          button('Kodu sakladım, kapat', closeModal, 'primary')
        )
      );
      setModalCleanup(() => {
        code.textContent = '';
        result.kod = '';
      });
    }
  );
}
function buyerDialog(buyer = null) {
  formDialog(
    buyer ? `${buyer.ad} · Alıcı hesabı` : 'Alıcı hesabı ekle',
    h(
      'div',
      { class: 'stack' },
      field('Ad soyad', input('ad', buyer?.ad || '', { required: true, maxlength: 200 })),
      field(
        'Kullanıcı adı',
        input('kullanici', buyer?.kullanici || '', {
          required: true,
          minlength: 3,
          maxlength: 64,
          pattern: '[a-z0-9._-]{3,64}',
          autocomplete: 'off',
        }),
        help('Küçük harf, sayı, nokta, tire ve alt çizgi kullanabilirsiniz.')
      ),
      field(
        buyer ? 'Yeni şifre (değiştirmeyecekseniz boş bırakın)' : 'İlk giriş şifresi',
        input('sifre', '', { type: 'password', required: !buyer, minlength: 8, maxlength: 1024, autocomplete: 'new-password' })
      ),
      buyer && h('label', {}, input('aktif', '1', { type: 'checkbox', checked: buyer.aktif }), 'Hesap aktif'),
      help('Alıcı yalnız kendi alışlarını ve belgelerini görür. Mali yönetim ekranlarına erişemez.')
    ),
    'Hesabı kaydet',
    async form => {
      const data = values(form);
      await api(buyer ? `/api/alicilar/${buyer.id}` : '/api/alicilar', {
        method: buyer ? 'PUT' : 'POST',
        body: {
          ad: data.ad.trim(),
          kullanici: data.kullanici.trim(),
          sifre: data.sifre || null,
          aktif: buyer ? Boolean(data.aktif) : true,
        },
      });
      closeModal();
      toast('Alıcı hesabı kaydedildi.');
      await navigate('tools');
    }
  );
}
function exportDialog() {
  const firstDay = today().slice(0, 8) + '01';
  formDialog(
    'Gider raporu hazırla',
    h(
      'div',
      { class: 'stack' },
      h(
        'div',
        { class: 'form-grid' },
        field('Başlangıç', input('baslangic', firstDay, { type: 'date', required: true })),
        field('Bitiş', input('bitis', today(), { type: 'date', required: true }))
      ),
      field(
        'Kanal',
        select('kanal', [
          { value: '', label: 'Tüm kanallar' },
          ...state.channels.map(c => ({ value: c.ad, label: c.ad })),
          { value: 'Dağılım bekliyor', label: 'Dağılım bekliyor' },
        ])
      ),
      field(
        'Dosya biçimi',
        select(
          'bicim',
          [
            { value: 'xlsx', label: 'Excel çalışma kitabı (.xlsx)' },
            { value: 'csv', label: 'CSV veri dosyası (.csv)' },
            { value: 'html', label: 'Yazdır / PDF olarak kaydet' },
          ],
          'xlsx'
        )
      ),
      help(
        'Raporda gerçek gider tutarı bir kez listelenir. Kanal filtresi ödeme payını değil, o kanalla ilişkili ödemenin tam tutarını getirir.'
      )
    ),
    'Raporu aç / indir',
    async form => {
      const data = values(form);
      if (data.baslangic > data.bitis) throw new Error('Bitiş tarihi başlangıçtan önce olamaz.');
      const url = `/api/disari-aktar?${new URLSearchParams(data)}`;
      if (data.bicim === 'html') {
        const link = h('a', { href: url, target: '_blank', rel: 'noopener' });
        document.body.append(link);
        link.click();
        link.remove();
        toast('Rapor yeni sekmede açılıyor. Yazdır menüsünden PDF olarak kaydedebilirsiniz.');
      } else {
        const blob = await api(url, { binary: true });
        download(blob, `kasa-gider-${data.baslangic}-${data.bitis}.${data.bicim}`);
      }
      closeModal();
    }
  );
}

// The session is carried only by the HttpOnly cookie. No credential or token is persisted here.
// Tanıdık cihaz belirteci de yalnız rol başına HttpOnly __Host-kasa_cihaz_<rol> çerezindedir: sunucu girişte ve /api/auth/me doğrulamasında yeniler, tarayıcı girişte kendisi gönderir, betik okumaz ve başlığa eklemez.
runtimeReady
  .then(() => {
    $('#recover-open').hidden = runtime.saltOkunur;
    if (runtime.saltOkunur) {
      $('#login-description').textContent = 'Canlı kasa kayıtlarını görüntülemek için giriş yapın.';
      $('#login-help').textContent =
        'İzleyici girişi için kullanıcı adını boş bırakıp size verilen şifreyi kullanın. Bu sürüm yalnız kasa görüntüleme içindir.';
    }
    return api('/api/auth/me')
      .then(user => enter(user.rol))
      .catch(() => {
        clearSession();
        $('#login-form input').focus();
      });
  })
  .catch(error => {
    $('#login-screen').hidden = false;
    $('#login-form').hidden = true;
    $('#recover-open').hidden = true;
    $('#login-description').textContent = error.message || 'Kasa ayarı yüklenemedi. Sayfayı yenileyin.';
  });

// Testlerin (Kasa.Api.Ui.Tests) doğrudan çalıştırdığı iç işlevler ve ekran modülleri. Tarayıcıda giriş modülünün dışa açtığı
// adları içe aktaran yoktur; davranışı değiştirmez.
export {
  navigate,
  toast,
  incomeDialog,
  expenseDialog,
  paymentDialog,
  paymentRow,
  cancelPayment,
  financeUi,
  notificationUi,
  monthlyUi,
  cashControlsUi,
  statementImportUi,
  renderMonthly,
  clearSession,
  passwordDialog,
  recoveryCodeDialog,
  viewerPasswordDialog,
  channelDialog,
  openingDialog,
  documentDialog,
};
