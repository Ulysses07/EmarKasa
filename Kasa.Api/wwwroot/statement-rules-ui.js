import { h, input, select, field, help, section, table, distribution } from './ui-dom.js';
import { api, act, toast, formDialog, closeModal, isOpen, requestIdentity, requireEditor } from './ui-shell.js';
import { isAbortError } from './ui-core.js';

const base = '/api/ekstre-aktar';
const names = { Gelir: 'Banka girişi', Gider: 'Banka çıkışı', KartHarcama: 'Kart harcaması', Atla: 'Satırı seçmeden bırak' };

export function createStatementRulesUi({ channels, banks, active, documentId = null, proposals = () => {} }) {
  const status = h('p', { class: 'help', role: 'status' });
  const list = h('div');
  let supported = false;
  let sequence = 0;
  const add = act('+ Kural ekle', () => edit(), '', { disabled: true });
  const node = section(
    'Kişisel sınıflandırma kuralları',
    h(
      'div',
      { class: 'stack' },
      help(
        'Açıklamadaki sözcüklerden işlem türü ve kanal dağılımı önerilir. Öneriler satır seçmez veya kayıt oluşturmaz. Çelişen kurallarda seçim sana bırakılır.'
      ),
      status,
      h('div', { class: 'row-actions' }, add, act('Kuralları ve önerileri yenile', refresh)),
      list
    )
  );

  async function refresh() {
    const current = ++sequence;
    try {
      const [rules, suggestions] = await Promise.all([
        api(`${base}/kurallar`),
        documentId == null ? [] : api(`${base}/${documentId}/oneriler`),
      ]);
      if (!active() || current !== sequence) return;
      if (!Array.isArray(rules) || !Array.isArray(suggestions)) throw new Error('Geçerli kural yanıtı alınamadı.');
      supported = true;
      add.disabled = false;
      proposals(suggestions, true);
      status.textContent = `${rules.length} kişisel kural. Elle düzenlenen satırlar yenilemede korunur.`;
      list.replaceChildren(
        rules.length
          ? table(
              ['Kural / koşul', 'Önerilen sonuç', 'Durum', ''],
              rules.map(rule => [
                h(
                  'div',
                  {},
                  rule.ad,
                  h(
                    'small',
                    { class: 'table-sub' },
                    `${rule.kaynak === 'Kart' ? 'Kart' : 'Banka'} · ${banks.find(b => b.kod === rule.banka)?.ad || 'Tüm bankalar'} · ${rule.aciklamaIcerir} · ${rule.yon === 'Giris' ? 'Giriş' : rule.yon === 'Cikis' ? 'Çıkış' : 'Her yön'}`
                  )
                ),
                `${names[rule.islemTuru] || rule.islemTuru} · ${rule.dagilimTuru === 'Genel' ? 'Genel kasa' : (rule.kanalIds || []).map(id => channels.find(c => c.id === id)?.ad || `Silinmiş kanal #${id}`).join(', ')}`,
                rule.aktif ? 'Etkin' : 'Kapalı',
                h(
                  'div',
                  { class: 'row-actions' },
                  act('Düzenle', () => edit(rule), 'small'),
                  act(
                    rule.aktif ? 'Kapat' : 'Etkinleştir',
                    async () => {
                      if (!active()) return;
                      requireEditor();
                      await api(`${base}/kurallar/${rule.id}`, {
                        method: 'PUT',
                        body: { ...writeFields(rule), istekId: crypto.randomUUID(), aktif: !rule.aktif },
                      });
                      if (active()) await refresh();
                    },
                    'small'
                  ),
                  act('Sil', () => remove(rule), 'small danger')
                ),
              ]),
              'Kişisel ekstre kuralları'
            )
          : help('Henüz kural yok. Bir satırdaki seçimini hatırlatabilir veya kendi kuralını ekleyebilirsin.')
      );
    } catch (error) {
      if (isAbortError(error) || !active() || current !== sequence) return;
      supported = false;
      add.disabled = true;
      list.replaceChildren();
      proposals([], false);
      status.textContent =
        error.status === 404
          ? 'Sunucu kişisel kuralları desteklemiyor. PDF satırlarını elle işlemeye devam edebilirsin.'
          : `Kurallar alınamadı; yenileyerek tekrar deneyin. ${error.message}`;
    }
  }

  function edit(initial = {}) {
    if (!active() || !supported) return;
    requireEditor();
    const identity = requestIdentity();
    const name = input('ad', initial.ad || '', { required: true, maxlength: 100 });
    const phrase = input('aciklamaIcerir', initial.aciklamaIcerir || '', { required: true, maxlength: 200 });
    const source = select(
      'kaynak',
      [
        { value: 'Banka', label: 'Banka hareketi' },
        { value: 'Kart', label: 'Kart ekstresi' },
      ],
      initial.kaynak || 'Banka'
    );
    const bank = select(
      'banka',
      [{ value: '', label: 'Tüm bankalar' }, ...banks.map(b => ({ value: b.kod, label: b.ad }))],
      initial.banka || ''
    );
    const direction = select(
      'yon',
      [
        { value: '', label: 'Her yön' },
        { value: 'Giris', label: 'Giriş' },
        { value: 'Cikis', label: 'Çıkış' },
      ],
      initial.yon || ''
    );
    const kind = select('islemTuru', [], '');
    const enabled = input('aktif', '1', { type: 'checkbox', checked: initial.aktif ?? true });
    const allocation = distribution(
      channels,
      { dagilimTuru: initial.dagilimTuru || 'Genel', dagilimlar: (initial.kanalIds || []).map(kanalId => ({ kanalId, tutar: 0 })) },
      {
        prefix: 'kural',
        choices: [],
        note: 'Kuralda yalnız genel kasa veya seçtiğin kanallara eşit dağılım saklanır. Sabit tutar saklanmaz.',
      }
    );
    const updateAllocation = () => {
      const general = kind.value === 'Atla' || source.value === 'Banka';
      const options =
        kind.value === 'Atla'
          ? [{ value: 'Genel', label: 'Dağılım yok (atla)' }]
          : [...(general ? [{ value: 'Genel', label: 'Yalnız genel kasa' }] : []), { value: 'Esit', label: 'Seçilen kanallara eşit' }];
      const previous = allocation.mode.value;
      allocation.mode.replaceChildren(...options.map(o => h('option', { value: o.value }, o.label)));
      allocation.mode.value = options.some(o => o.value === previous) ? previous : options[0].value;
      allocation.redraw();
    };
    const updateSource = () => {
      const options = source.value === 'Kart' ? ['KartHarcama', 'Atla'] : ['Gelir', 'Gider', 'Atla'];
      const previous = kind.value || initial.islemTuru;
      kind.replaceChildren(...options.map(value => h('option', { value }, names[value])));
      kind.value = options.includes(previous) ? previous : options[0];
      updateAllocation();
    };
    source.addEventListener('change', updateSource);
    kind.addEventListener('change', updateAllocation);
    updateSource();
    // Gerçek select, seçenekler henüz yokken verilen değeri saklamaz; hatırlanan dağılımı seçeneklerden sonra kur.
    if ((initial.dagilimTuru === 'Esit' && kind.value !== 'Atla') || (initial.dagilimTuru === 'Genel' && source.value === 'Banka')) {
      allocation.mode.value = initial.dagilimTuru;
      allocation.redraw();
    }
    const form = formDialog(
      initial.id ? 'Kuralı düzenle' : 'Kişisel kural ekle',
      h(
        'div',
        { class: 'stack statement-rule-form' },
        field('Kural adı', name),
        field('Açıklamada geçen sözcük veya ifade', phrase),
        help(
          'En az üç harfli bir sözcük kullan. Örneğin MIGROS, MIGROSAN ile eşleşmez. Türkçe harfler ve noktalama farklılıkları eşleştirmede sadeleştirilir. Koşullar birlikte uygulanır.'
        ),
        h(
          'div',
          { class: 'form-grid' },
          field('Belge türü', source),
          field('Banka', bank),
          field('Hareket yönü', direction),
          field('Önerilen işlem', kind)
        ),
        allocation.node,
        field('Kural etkin', enabled)
      ),
      'Kuralı kaydet',
      async () => {
        if (!active() || !isOpen(form)) return;
        requireEditor();
        const shares = allocation.read(0);
        const payload = identity({
          surum: initial.surum || 0,
          ad: name.value.trim(),
          kaynak: source.value,
          banka: bank.value || null,
          aciklamaIcerir: phrase.value.trim(),
          yon: direction.value || null,
          islemTuru: kind.value,
          dagilimTuru: shares.dagilimTuru,
          kanalIds: shares.dagilimlar.map(s => s.kanalId),
          aktif: enabled.checked,
        });
        try {
          await api(initial.id ? `${base}/kurallar/${initial.id}` : `${base}/kurallar`, {
            method: initial.id ? 'PUT' : 'POST',
            body: payload,
          });
        } catch (error) {
          if (error.status === 409 && active()) await refresh();
          throw error;
        }
        if (!active()) return;
        if (isOpen(form)) closeModal();
        await refresh();
        if (active()) toast('Kural kaydedildi. Önerileri kontrol ederek uygulayabilirsin.');
      },
      { wide: true }
    );
  }

  function remove(rule) {
    if (!active() || !supported) return;
    const form = formDialog(
      'Kuralı sil',
      help(`“${rule.ad}” kuralı silinecek. Bu kuralla daha önce kaydedilen hareketler korunur.`),
      'Kuralı sil',
      async () => {
        if (!active() || !isOpen(form)) return;
        requireEditor();
        try {
          await api(`${base}/kurallar/${rule.id}?surum=${rule.surum}`, { method: 'DELETE' });
        } catch (error) {
          if (error.status === 409 && active()) await refresh();
          throw error;
        }
        if (!active()) return;
        if (isOpen(form)) closeModal();
        await refresh();
      },
      { danger: true }
    );
  }
  return { node, refresh, edit };
}

function writeFields(rule) {
  const { surum, ad, kaynak, banka, aciklamaIcerir, yon, islemTuru, dagilimTuru, kanalIds, aktif } = rule;
  return { surum, ad, kaynak, banka, aciklamaIcerir, yon, islemTuru, dagilimTuru, kanalIds, aktif };
}
