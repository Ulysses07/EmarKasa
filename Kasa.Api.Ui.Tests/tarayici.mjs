import { registerHooks } from 'node:module';

// Tarayıcı modüllerini (Kasa.Api/wwwroot) Node'un kendi ES modül yükleyicisiyle, gerçek import/export bağlarıyla çalıştırır;
// paket bağımlılığı (jsdom vb.) yoktur. İki şey eklenir (Node module.registerHooks, eşzamanlı çözümleme/yükleme kancaları):
//   1. Her örnek ayrı bir modül çizgesidir. webModulu() giriş adresine ?kasa-ornek=N ekler; wwwroot içindeki her içe aktarma
//      (statik ve dinamik) bu işareti devralır. Aynı dosya her örnekte yeniden değerlendirilir: oturum durumu, ekran nesli ve
//      pencere durumu testler arasında taşınmaz. Örnek içinde modüller birbirini tarayıcıdaki gibi tek kopya olarak görür.
//   2. Tarayıcı globalleri örneğe bağlıdır. Yükleme kancası her modülün ilk satırının başına, verilen ortamın anahtarlarını
//      yerel sabit olarak bağlayan tek bir bildirim ekler (satır numaraları değişmez). Modül kodu `document`, `fetch`,
//      `setTimeout`... adlarını o örneğin sahtesinde bulur; önceki testte bekleyen bir iş sonraki testin belgesine yazmaz.
//      Ortamda anahtarı olmayan adlar Node'un kendi globalleridir (URL, Headers, AbortController, Intl...).
const WWWROOT = new URL('../Kasa.Api/wwwroot/', import.meta.url).href;
const ISARET = 'kasa-ornek';
const ortamlar = new Map();
globalThis[Symbol.for('kasa.test.ortamlar')] = ortamlar;
let sonOrnek = 0;

const ornegi = url => (url?.startsWith(WWWROOT) ? new URL(url).searchParams.get(ISARET) : null);

registerHooks({
  resolve(specifier, context, nextResolve) {
    const sonuc = nextResolve(specifier, context);
    const ornek = ornegi(context.parentURL);
    if (!ornek || !sonuc.url.startsWith(WWWROOT)) return sonuc;
    const url = new URL(sonuc.url);
    url.searchParams.set(ISARET, ornek);
    return { ...sonuc, url: url.href };
  },
  load(url, context, nextLoad) {
    const ornek = ornegi(url);
    if (!ornek) return nextLoad(url, context);
    const sonuc = nextLoad(url, { ...context, format: 'module' });
    const kaynak = typeof sonuc.source === 'string' ? sonuc.source : Buffer.from(sonuc.source).toString('utf8');
    const adlar = Object.keys(ortamlar.get(ornek)).join(', ');
    const ortam = `data:text/javascript,export default globalThis[Symbol.for("kasa.test.ortamlar")].get("${ornek}")`;
    return { ...sonuc, format: 'module', source: `import __kasaOrtam from '${ortam}';const { ${adlar} } = __kasaOrtam;${kaynak}` };
  },
});

/** wwwroot'taki bir modülü yeni bir örnek olarak yükler; ortam: bu örneğin tarayıcı globalleri ({ document, fetch, ... }). */
export function webModulu(dosya, ortam) {
  for (const ad of Object.keys(ortam)) if (!/^[A-Za-z_$][\w$]*$/.test(ad)) throw new Error(`Geçersiz global adı: ${ad}`);
  const ornek = String(++sonOrnek);
  ortamlar.set(ornek, ortam);
  const url = new URL(dosya, WWWROOT);
  url.searchParams.set(ISARET, ornek);
  return import(url.href);
}

// Etkisiz DOM düğümü: testlerin sahte belgesi. Gerçek DOM'un testlerde kullanılan küçük bir alt kümesidir.
export class Element {
  constructor(tag = 'div') {
    this.tag = tag;
    this.children = [];
    this.attributes = {};
    this.listeners = {};
    this.classList = { toggle() {} };
    this.open = false;
    this.checked = false;
  }
  // Gerçek DOM gibi: yalnız belge köküne (document.querySelector düğümleri) zincirle bağlı düğüm bağlıdır; içerikten çıkarılan düğüm kopar.
  get isConnected() {
    let node = this;
    while (node.parentNode) node = node.parentNode;
    return node.root === true;
  }
  closest(selector) {
    const matches = node =>
      selector.startsWith('.') ? (node.className || '').split(' ').includes(selector.slice(1)) : node.tag === selector;
    for (let node = this; node; node = node.parentNode) if (matches(node)) return node;
    return null;
  }
  detachChildren(kept = []) {
    for (const child of this.children)
      if (child instanceof Element && child.parentNode === this && !kept.includes(child)) child.parentNode = null;
  }
  set value(value) {
    this.currentValue = String(value);
  }
  get value() {
    return this.currentValue || '';
  }
  setAttribute(name, value) {
    this.attributes[name] = value;
    if (['disabled', 'hidden', 'readonly'].includes(name)) this[name === 'readonly' ? 'readOnly' : name] = true;
  }
  addEventListener(name, handler) {
    this.listeners[name] = handler;
  }
  append(...children) {
    for (const child of children) if (child instanceof Element) child.parentNode = this;
    this.children.push(...children);
  }
  replaceChildren(...children) {
    this.detachChildren(children);
    for (const child of children) if (child instanceof Element) child.parentNode = this;
    this.children = children;
  }
  remove() {
    if (this.parentNode) this.parentNode.children = this.parentNode.children.filter(child => child !== this);
    this.parentNode = null;
  }
  removeAttribute(name) {
    delete this.attributes[name];
  }
  set textContent(value) {
    this.detachChildren();
    this.children = [String(value)];
  }
  get textContent() {
    return this.children.map(child => (typeof child === 'string' ? child : child.textContent)).join('');
  }
  querySelector(selector) {
    const named = /^\[name="([^"]+)"\]$/.exec(selector);
    return this.find(node =>
      named
        ? node.attributes.name === named[1]
        : selector === 'button[type="submit"]'
          ? node.tag === 'button' && node.attributes.type === 'submit'
          : selector.startsWith('.')
            ? (node.className || '').split(' ').includes(selector.slice(1))
            : node.tag === selector
    );
  }
  find(predicate) {
    for (const node of this.children) {
      if (typeof node === 'string') continue;
      if (predicate(node)) return node;
      const child = node.find(predicate);
      if (child) return child;
    }
    return null;
  }
  focus() {}
  close() {
    this.open = false;
  }
  showModal() {
    this.open = true;
  }
  reportValidity() {
    return true;
  }
  requestSubmit() {
    this.listeners.submit?.({ preventDefault() {} });
  }
  reset() {
    this.children.forEach(child => {
      if (child instanceof Element) {
        child.currentValue = '';
        child.reset();
      }
    });
  }
  scrollIntoView() {}
}
export class TestFormData {
  constructor(form) {
    this.items = [];
    const visit = node => {
      if (typeof node === 'string' || node.disabled) return;
      if (
        ['input', 'select', 'textarea'].includes(node.tag) &&
        node.attributes.name &&
        (node.attributes.type !== 'checkbox' || node.checked)
      )
        this.items.push([node.attributes.name, node.value]);
      node.children.forEach(visit);
    };
    if (form) visit(form);
  }
  append(name, value) {
    this.items.push([name, value]);
  }
  [Symbol.iterator]() {
    return this.items[Symbol.iterator]();
  }
}

/** Tarayıcıda olmayan (sahtesi verilmeyen) adlar: Node'un kendi navigator'ı gibi globalleri modüle sızmaz. */
export const TARAYICI_DISI = { window: undefined, location: undefined, navigator: undefined };

/** Yalnız dışa açık saf işlevleri okunacak modüller için sessiz ortam: belge etkisiz, çalışma ayarı yok (404). */
export const sessizOrtam = () => ({
  ...TARAYICI_DISI,
  globalThis,
  document: {
    querySelector: () => new Element(),
    createElement: tag => new Element(tag),
    createTextNode: text => String(text),
  },
  fetch: async () => ({ ok: false, status: 404 }),
  setTimeout: () => 0,
  crypto: { randomUUID: () => '00000000-0000-4000-8000-000000000000' },
  FormData: TestFormData,
  Node: Element,
  localStorage: null,
  sessionStorage: null,
});
