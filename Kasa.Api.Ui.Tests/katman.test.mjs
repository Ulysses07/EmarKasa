import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

// styles.css kaskad katmanlarıyla (CSS Cascade 5 @layer) düzenlenir. Katman sırası dosyanın başında tek bildirimle verilir
// ve kaynaktaki sırayla aynıdır; her kural bir katmanın içindedir. Katmansız kural bütün katmanları özgüllükten bağımsız
// ezer (MDN @layer): sonradan eklenen tek bir katmansız kural sessizce öncelik sırasını değiştirirdi.
const SIRA = ['tokens', 'reset', 'base', 'components', 'screens'];

// Yorumsuz metnin en dış düzeydeki deyimleri: [başlık, gövde] (gövdesiz deyim için gövde null).
function ustDeyimler(css) {
  const metin = css.replace(/\/\*[\s\S]*?\*\//g, '');
  const deyimler = [];
  let bas = 0;
  for (let i = 0; i < metin.length; i++) {
    if (metin[i] === ';') {
      deyimler.push([metin.slice(bas, i).trim(), null]);
      bas = i + 1;
    } else if (metin[i] === '{') {
      let derinlik = 1;
      let son = i + 1;
      for (; son < metin.length && derinlik > 0; son++) {
        if (metin[son] === '{') derinlik++;
        else if (metin[son] === '}') derinlik--;
      }
      assert.equal(derinlik, 0, 'kapanmayan blok');
      deyimler.push([metin.slice(bas, i).trim(), metin.slice(i + 1, son - 1)]);
      i = son - 1;
      bas = son;
    }
  }
  assert.equal(metin.slice(bas).trim(), '', 'dosya sonunda yarım deyim');
  return deyimler;
}

test('styles.css: katman sırası tek bildirimde, kaynak sırasıyla; her kural bir katmanın içinde', async () => {
  const deyimler = ustDeyimler(await readFile(new URL('../Kasa.Api/wwwroot/styles.css', import.meta.url), 'utf8'));
  const [ilk, ...bloklar] = deyimler;
  assert.deepEqual(ilk, [`@layer ${SIRA.join(', ')}`, null], 'dosya katman sırası bildirimiyle başlar');
  const katmanlar = bloklar.map(([baslik, govde]) => {
    const eslesme = /^@layer ([\w-]+)$/.exec(baslik);
    assert.ok(eslesme && govde !== null, `katmansız deyim: ${baslik.slice(0, 80)}`);
    assert.ok(SIRA.includes(eslesme[1]), `bildirilmemiş katman: ${eslesme[1]}`);
    return eslesme[1];
  });
  // Bloklar bildirilen sırayla ve ardışık gelir: sonraki kural hiçbir zaman önceki katmana yazılmaz.
  assert.deepEqual(
    katmanlar,
    [...katmanlar].sort((a, b) => SIRA.indexOf(a) - SIRA.indexOf(b))
  );
  assert.deepEqual([...new Set(katmanlar)], SIRA, 'her katman kullanılır');
});
