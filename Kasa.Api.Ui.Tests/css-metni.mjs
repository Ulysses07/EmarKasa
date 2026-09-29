// Kaynak CSS'i testlerin aradığı sıkışık yazıma indirir: kural ve değer okuması boşluk biçiminden (tek satırlık kural ya
// da Prettier'ın çok satırlı çıktısı) bağımsız olur. Yorumlar atılır, boşluk dizileri tek boşluğa iner; yapı
// karakterlerinin ({ } ; : ,), birleştiricilerin (> + ~) ve !important'ın çevresindeki boşluk silinir. Seçicideki soy
// boşluğu (.sidebar nav) ve değer içindeki anlamlı boşluk (3px solid var(--focus)) korunur. Yalnız testlerin okuması
// içindir; dosyaya geri yazılmaz. Prettier'ın boşluk dışı düzeltmelerine (.82rem → 0.82rem, [type=x] → [type="x"])
// dayanan test yoktur.
export const sikistir = css =>
  css
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/\s+/g, ' ')
    .replace(/\s*([{};:,>+~])\s*/g, '$1')
    .replace(/\s*!\s*important/g, '!important')
    .trim();
