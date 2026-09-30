// Kaynak CSS'i testlerin aradığı sıkışık yazıma indirir: kural ve değer okuması boşluk biçiminden (tek satırlık kural ya
// da Prettier'ın çok satırlı çıktısı) bağımsız olur. Yorumlar atılır, boşluk dizileri tek boşluğa iner; yapı
// karakterlerinin ({ } ; : ,), birleştiricilerin (> + ~) ve !important'ın çevresindeki boşluk silinir. Seçicideki soy
// boşluğu (.sidebar nav) ve değer içindeki anlamlı boşluk (3px solid var(--focus)) korunur. Katman sarmalayıcıları
// (@layer sıra bildirimi ve @layer ad { … } blokları) açılır: kurallar katmansız yazımdaki sırayla okunur; katman sırası ve
// kapsamı katman.test.mjs'te ayrıca denetlenir. Yalnız testlerin okuması içindir; dosyaya geri yazılmaz. Prettier'ın
// boşluk dışı düzeltmelerine (.82rem → 0.82rem, [type=x] → [type="x"]) dayanan test yoktur.
export const sikistir = css =>
  katmanlariAc(
    css
      .replace(/\/\*[\s\S]*?\*\//g, '')
      .replace(/\s+/g, ' ')
      .replace(/\s*([{};:,>+~])\s*/g, '$1')
      .replace(/\s*!\s*important/g, '!important')
      .trim()
  );

// Sıkışık yazımda @layer sıra bildirimini siler ve her @layer ad{…} bloğunun sarmalayıcısını (açılış ve eşleşen kapanış
// parantezi) kaldırır; içindeki kurallar ve medya blokları olduğu gibi kalır.
function katmanlariAc(css) {
  let metin = css.replace(/@layer [^{};]+;/g, '');
  for (let bas = metin.search(/@layer [^{};]*\{/); bas >= 0; bas = metin.search(/@layer [^{};]*\{/)) {
    const ac = metin.indexOf('{', bas);
    let derinlik = 1;
    let son = ac + 1;
    for (; son < metin.length && derinlik > 0; son++) {
      if (metin[son] === '{') derinlik++;
      else if (metin[son] === '}') derinlik--;
    }
    if (derinlik !== 0) throw new Error('Kapanmayan @layer bloğu');
    metin = metin.slice(0, bas) + metin.slice(ac + 1, son - 1) + metin.slice(son);
  }
  return metin;
}
