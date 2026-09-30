// Alış defteri ekranları: alış listesi ve ayrıntısı, alış düzenleme (kalem ve kanal payı editörü), durum geçişleri (gönder,
// onayla, iade), alış ödemesi (yeni, düzelt / taşı, iptal; bağlanabilir gider ve kart harcaması, taksit) ve belgeler. Liste ve
// ayrıntı app.js'teki görünüm tablosundan (registerScreens) alış verisi okunduktan sonra çizilir. Ekranların dışında
// kullanılanlar: ana sayfanın inceleme kutusu (purchaseRow) ve gider penceresinin taksit alanları (installmentEditor); ödeme ve
// belge pencereleri testler için app.js'ten yeniden dışa açılır.
import {
  money,
  dateText,
  today,
  cents,
  sumCents,
  permissions,
  statusLabels,
  filteredPurchases,
  purchasePayload,
  paymentCardChoices,
  childValues,
  trackedCardPayment,
  installmentFields,
  detachAllocations,
  purchaseTotals,
  documentFileName,
  linkableExpensesPath,
  documentsPath,
  documentRemovable,
  documentDescription,
  documentDeletePayload,
} from './ui-core.js';
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
  moneyNode,
  allocationTags,
  values,
  optionalId,
  empty,
  summary,
} from './ui-dom.js';
import { state, toast, api, run, act, closeModal, formDialog, confirmSimilar, requestIdentity, page, navigate } from './ui-shell.js';

async function loadPurchases() {
  const [purchases, channels] = await Promise.all([api('/api/alis'), api('/api/alis/kanallar')]);
  state.purchases = purchases;
  state.channels = channels;
}
async function loadPaymentLookups() {
  state.cards = await api('/api/kredikartlari');
}
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
        allocationTags(line.dagilimlar, { pendingBadge: 'Kanal dağılımı bekliyor' })
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
    rights.pay ? act('+ Ödeme ekle', () => paymentDialog(p), 'small') : null
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
    rights.pay && act('Ödeme kaydet', () => paymentDialog(p))
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
          act('Düzelt / taşı', () => paymentDialog(p, payment), 'small'),
          act('Ödemeyi iptal et', () => cancelPayment(p, payment), 'small danger')
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
    const more = act('Daha eski giderler', () => load(true), 'small', { hidden: true });
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
        act('Ara', () => load(false), 'small')
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

export {
  loadPurchases,
  renderPurchases,
  renderPurchase,
  purchaseRow,
  installmentEditor,
  paymentRow,
  paymentDialog,
  cancelPayment,
  documentDialog,
};
