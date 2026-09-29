// Kasa.Api/wwwroot JS denetimi (ESLint düz yapılandırma). Yalnız hata yakalayan kurallar açıktır: tanımsız ad, yinelenen
// anahtar/dal, sabite ya da içe aktarılana atama, erişilemeyen kod, NaN ile karşılaştırma, geçersiz typeof, her zaman aynı
// sonucu veren ifade... Biçim/üslup kuralı yoktur (satır uzunluğu, tırnak, boşluk, kullanılmayan değişken).
// Depo kökünden çalışır: `npm run lint:web` (Kasa.Ui.E2E içinde). --config ile verildiği için desenler depo köküne göredir.
import globals from 'globals';

const hataKurallari = {
  'no-undef': 'error',
  'no-global-assign': 'error',
  'no-shadow-restricted-names': 'error',
  'no-delete-var': 'error',
  'no-redeclare': 'error',
  'no-const-assign': 'error',
  'no-import-assign': 'error',
  'no-func-assign': 'error',
  'no-class-assign': 'error',
  'no-dupe-keys': 'error',
  'no-dupe-class-members': 'error',
  'no-duplicate-case': 'error',
  'no-dupe-else-if': 'error',
  'no-self-assign': 'error',
  'no-unreachable': 'error',
  'no-unsafe-negation': 'error',
  'no-unsafe-finally': 'error',
  'no-unsafe-optional-chaining': 'error',
  'no-constant-binary-expression': 'error',
  'no-compare-neg-zero': 'error',
  'no-loss-of-precision': 'error',
  'use-isnan': 'error',
  'valid-typeof': 'error',
  'no-obj-calls': 'error',
  'no-new-native-nonconstructor': 'error',
  'no-invalid-regexp': 'error',
  'no-empty-pattern': 'error',
  'no-async-promise-executor': 'error',
  'getter-return': 'error',
  'no-setter-return': 'error',
  // Arayüz hatası kalıbı: h()/childValues sayıyı bilerek metin olarak basar (0 geçerli içeriktir), bu yüzden
  // `dizi.length && düğüm` boş dizide ekrana tek başına "0" yazar. Koşul her yerde boolean yazılır (`dizi.length > 0 && …`).
  'no-restricted-syntax': ['error',
    ...[
      "LogicalExpression[operator='&&'][left.type='MemberExpression'][left.property.name=/^(length|size)$/]",
      "LogicalExpression[operator='&&'][left.type='ChainExpression'][left.expression.property.name=/^(length|size)$/]",
    ].map(selector => ({ selector, message: 'Sayı (length/size) && ile koşul yapılmaz: boşken 0 döner ve h() onu metin olarak basar. `> 0` ile boolean yazın.' })),
    // table() kaydırılabilir kapsayıcıyı klavyeyle odaklanan bir bölge yapar; adsız bölge olmaz (WAI-ARIA: region adı zorunlu).
    { selector: "CallExpression[callee.name='table'][arguments.length<3]", message: 'table(başlıklar, satırlar, ad): üçüncü bağımsız değişken tablo bölgesinin erişilebilir adıdır.' },
  ],
};

const ortak = { ecmaVersion: 'latest' };

export default [
  { linterOptions: { reportUnusedDisableDirectives: 'error' } },
  // Sayfa modülleri (index.html ve m/index.html type="module" ile yükler).
  {
    files: ['Kasa.Api/wwwroot/**/*.js'],
    ignores: ['Kasa.Api/wwwroot/service-worker.js', 'Kasa.Api/wwwroot/telefon-yonlendir.js'],
    languageOptions: { ...ortak, sourceType: 'module', globals: { ...globals.browser } },
    rules: hataKurallari,
  },
  // Telefon yönlendirmesi <head>'de klasik betik olarak çalışır (modül değildir).
  {
    files: ['Kasa.Api/wwwroot/telefon-yonlendir.js'],
    languageOptions: { ...ortak, sourceType: 'script', globals: { ...globals.browser } },
    rules: hataKurallari,
  },
  // Service worker klasik betiktir (push-client.js type olmadan kaydeder); pencere/belge globalleri yoktur.
  {
    files: ['Kasa.Api/wwwroot/service-worker.js'],
    languageOptions: { ...ortak, sourceType: 'script', globals: { ...globals.serviceworker } },
    rules: hataKurallari,
  },
];
