// Emar Kasa · telefon görünümü (salt okunur PWA).
// Kurallar: satır içi script/stil yok (CSP), HTML metni DOM'a yazılmaz (yalnız textContent), veri yalnız oturum
// çereziyle /api'den; hiçbir veri cihazda saklanmaz (service worker /api'yi asla önbelleğe almaz).
'use strict';

(() => {
  const TL = new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const AYLAR = ['Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran', 'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık'];
  const EKRANLAR = {
    panel: { baslik: 'Panel', ciz: panelCiz },
    haftalik: { baslik: 'Haftalık', ciz: haftalikCiz },
    aylik: { baslik: 'Aylık', ciz: aylikCiz },
    cekler: { baslik: 'Çekler', ciz: ceklerCiz },
    kartlar: { baslik: 'Kredi kartları', ciz: kartlarCiz },
  };

  const durum = { rol: null, yil: 0, ay: 0, haftalikGoster: 6, surum: 0 };
  const $ = (id) => document.getElementById(id);

  class OturumYok extends Error {}

  // ------------------------------------------------------------------ yardımcılar

  /** Güvenli öğe üretici: metinler textContent/Text düğümü olur, HTML asla yorumlanmaz. */
  function el(etiket, ozellik, ...cocuklar) {
    const e = document.createElement(etiket);
    for (const [k, v] of Object.entries(ozellik || {})) {
      if (v === null || v === undefined || v === false) continue;
      if (k === 'sinif') e.className = v;
      else if (k === 'metin') e.textContent = v;
      else if (k.startsWith('on') || k === 'style') throw new Error('izin verilmeyen özellik: ' + k);
      else e.setAttribute(k, v === true ? '' : String(v));
    }
    for (const c of cocuklar.flat()) {
      if (c === null || c === undefined || c === false) continue;
      e.append(c instanceof Node ? c : document.createTextNode(String(c)));
    }
    return e;
  }

  const tl = (n) => TL.format(Number(n) || 0) + ' ₺';
  const imzali = (n) => (Number(n) < 0 ? '−' : '+') + TL.format(Math.abs(Number(n) || 0)) + ' ₺';
  const renk = (n) => (Number(n) < 0 ? 'eksi' : Number(n) > 0 ? 'arti' : 'soluk');
  const para = (n, sinif) => el('span', { sinif: 'para ' + (sinif || ''), metin: tl(n) });
  const imzaliPara = (n) => el('span', { sinif: 'para ' + renk(n), metin: imzali(n) });

  /** "2026-09-24" → "24.09.2026" (saat dilimi kaymasın diye Date kullanılmaz). */
  function tarih(s) {
    const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(s || '');
    return m ? `${m[3]}.${m[2]}.${m[1]}` : '';
  }
  function uzunTarih(s) {
    const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(s || '');
    return m ? `${Number(m[3])} ${AYLAR[Number(m[2]) - 1]} ${m[1]}` : '';
  }
  /** "21–24 Eylül 2026", "28 Eylül – 4 Ekim 2026", "29 Aralık 2025 – 4 Ocak 2026". */
  function aralik(bas, bit) {
    const a = /^(\d{4})-(\d{2})-(\d{2})/.exec(bas || ''), b = /^(\d{4})-(\d{2})-(\d{2})/.exec(bit || '');
    if (!a || !b) return `${uzunTarih(bas)} – ${uzunTarih(bit)}`;
    if (a[1] === b[1] && a[2] === b[2]) return `${Number(a[3])}–${Number(b[3])} ${AYLAR[Number(b[2]) - 1]} ${b[1]}`;
    if (a[1] === b[1]) return `${Number(a[3])} ${AYLAR[Number(a[2]) - 1]} – ${Number(b[3])} ${AYLAR[Number(b[2]) - 1]} ${b[1]}`;
    return `${uzunTarih(bas)} – ${uzunTarih(bit)}`;
  }
  /**
   * Türkiye saatiyle bugünün yılı ve ayı. Cihaz saatine/saat dilimine güvenilmez (yurt dışında ya da yanlış
   * ayarlı telefonda ay başı kayardı); sunucu da "bugün"ü Türkiye saatiyle keser.
   */
  const TR_TARIH = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Istanbul', year: 'numeric', month: '2-digit', day: '2-digit' });
  function turkiyeBugun() {
    const p = {};
    for (const x of TR_TARIH.formatToParts(new Date())) p[x.type] = x.value;
    return { yil: Number(p.year), ay: Number(p.month) };
  }

  async function api(yol, secenek) {
    const s = secenek || {};
    const basliklar = { Accept: 'application/json' };
    if (s.govde !== undefined) basliklar['Content-Type'] = 'application/json';
    let yanit;
    try {
      yanit = await fetch('/api' + yol, {
        method: s.method || 'GET',
        credentials: 'same-origin',
        cache: 'no-store',
        headers: basliklar,
        body: s.govde !== undefined ? JSON.stringify(s.govde) : undefined,
      });
    } catch {
      throw new Error('Sunucuya ulaşılamadı. İnternet bağlantınızı kontrol edin.');
    }
    if (yanit.status === 401) throw new OturumYok();
    if (yanit.status === 429) throw new Error('Çok fazla deneme. Bir dakika sonra tekrar deneyin.');
    if (!yanit.ok) {
      let mesaj = null;
      try { const j = await yanit.json(); mesaj = j && (j.hata || j.detail || j.title); } catch { /* gövde yok */ }
      throw new Error(mesaj || `Sunucu hatası (${yanit.status}).`);
    }
    if (yanit.status === 204) return null;
    return (yanit.headers.get('content-type') || '').includes('json') ? yanit.json() : null;
  }

  // ------------------------------------------------------------------ oturum

  function girisGoster(mesaj) {
    durum.rol = null;
    document.body.classList.add('girissiz');
    $('sekmeler').hidden = true;
    $('cikis').hidden = true;
    $('yenile').hidden = true;
    $('baslik').textContent = 'Salt okunur';
    const icerik = $('icerik');
    icerik.replaceChildren($('giris-sablonu').content.cloneNode(true));
    const form = $('giris-formu');
    const hata = $('giris-hata');
    if (mesaj) { hata.textContent = mesaj; hata.hidden = false; }
    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      const dugme = $('giris-dugme');
      dugme.disabled = true;
      hata.hidden = true;
      try {
        const kullanici = $('kullanici').value.trim();
        const kodAlani = $('kod-alani');
        const kod = kodAlani.hidden ? '' : $('kod').value.trim();
        const yanit = await fetch('/api/auth/login', {
          method: 'POST',
          credentials: 'same-origin',
          cache: 'no-store',
          headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
          body: JSON.stringify({ kullanici: kullanici || null, sifre: $('sifre').value, kod: kod || null }),
        });
        if (yanit.status === 401 || yanit.status === 429) {
          // Sunucunun açıklaması gösterilir ("Bu hesap pasif…", "Kod hatalı…"); iki adımlı hesapta
          // şifre doğruysa 401 + kodGerekli gelir: kod alanı açılır, aynı bilgilerle kodla yeniden gönderilir.
          let govde = null;
          try { govde = await yanit.json(); } catch { /* gövde yok */ }
          if (govde && govde.kodGerekli === true) {
            kodAlani.hidden = false;
            $('kod').value = '';
            $('kod').focus();
          }
          const varsayilan = yanit.status === 429
            ? 'Çok fazla deneme. Bir dakika sonra tekrar deneyin.'
            : 'Kullanıcı adı ya da şifre hatalı.';
          throw new Error((govde && typeof govde.hata === 'string' && govde.hata) || varsayilan);
        }
        if (!yanit.ok) throw new Error(`Giriş yapılamadı (${yanit.status}).`);
        // Yanıttaki token kullanılmaz ve saklanmaz: oturum HttpOnly çerezdedir.
        $('sifre').value = '';
        $('kod').value = '';
        await oturumuAc();
      } catch (hataNesnesi) {
        hata.textContent = hataNesnesi instanceof TypeError
          ? 'Sunucuya ulaşılamadı. İnternet bağlantınızı kontrol edin.'
          : hataNesnesi.message;
        hata.hidden = false;
        dugme.disabled = false;
      }
    });
    const ilk = $('sifre');
    if (ilk) ilk.focus();
  }

  async function oturumuAc() {
    const ben = await api('/auth/me');
    durum.rol = ben && ben.rol;
    document.body.classList.remove('girissiz');
    $('sekmeler').hidden = false;
    $('cikis').hidden = false;
    $('yenile').hidden = false;
    await ekraniCiz();
  }

  async function cikisYap() {
    try { await api('/auth/logout', { method: 'POST' }); } catch { /* çevrimdışı: çerez süresi dolunca biter */ }
    girisGoster();
  }

  // ------------------------------------------------------------------ yönlendirme

  function aktifEkran() {
    const ad = location.hash.replace(/^#\/?/, '').split(/[/?]/)[0];
    return EKRANLAR[ad] ? ad : 'panel';
  }

  async function ekraniCiz() {
    if (!durum.rol) return;
    const ad = aktifEkran();
    const ekran = EKRANLAR[ad];
    const surum = ++durum.surum;
    for (const a of document.querySelectorAll('#sekmeler a')) {
      if (a.dataset.sekme === ad) a.setAttribute('aria-current', 'page');
      else a.removeAttribute('aria-current');
    }
    $('baslik').textContent = ekran.baslik + (durum.rol === 'editor' ? ' · editör' : ' · izleyici');
    const icerik = $('icerik');
    icerik.replaceChildren(el('p', { sinif: 'bilgi', metin: 'Yükleniyor…' }));
    try {
      const parcalar = await ekran.ciz();
      if (surum !== durum.surum) return;   // bu arada başka sekmeye geçildi
      icerik.replaceChildren(...parcalar.flat(Infinity).filter((p) => p instanceof Node));
    } catch (hata) {
      if (surum !== durum.surum) return;
      if (hata instanceof OturumYok) { girisGoster('Oturumunuz sona erdi. Tekrar giriş yapın.'); return; }
      const tekrar = el('button', { sinif: 'dugme-ikincil kucuk', type: 'button', metin: 'Tekrar dene' });
      tekrar.addEventListener('click', ekraniCiz);
      icerik.replaceChildren(el('div', { sinif: 'hata-kutu', role: 'alert' }, el('div', { metin: hata.message }), tekrar));
    }
  }

  // ------------------------------------------------------------------ ekranlar

  async function panelCiz() {
    const p = await api('/rapor/panel');
    const kanallar = (p.kanallar || []).map((k) =>
      el('div', { sinif: 'satir' }, el('div', { sinif: 'sol' }, el('div', { sinif: 'ad', metin: k.kanal })),
        el('div', { sinif: 'sag' }, para(k.bakiye, renk(k.bakiye)))));
    return [
      el('section', { sinif: 'kahraman' },
        el('div', { sinif: 'etiket', metin: 'Güncel kasa' }),
        el('div', { sinif: 'tutar', metin: tl(p.guncelKasa) })),
      el('div', { sinif: 'ikili' },
        istatistik('Bu hafta', imzaliPara(p.buHaftaSonucu), 'Dönem sonucu'),
        istatistik('Bu ay', imzaliPara(p.buAySonucu), 'Ay sonucu')),
      el('h2', { sinif: 'bolum-baslik', metin: 'Kanal bakiyeleri' }),
      kanallar.length ? el('div', { sinif: 'kart liste' }, kanallar) : el('p', { sinif: 'bos', metin: 'Kanal yok.' }),
    ];
  }

  function istatistik(etiket, deger, alt) {
    return el('div', { sinif: 'kart istatistik' },
      el('div', { sinif: 'etiket', metin: etiket }),
      el('div', { sinif: 'tutar' }, deger),
      alt ? el('div', { sinif: 'alt', metin: alt }) : null);
  }

  async function haftalikCiz() {
    // Sunucu takvimi Türkiye saatiyle bugünde keser: cihaz tarihine göre ayrıca süzülmez.
    const liste = await api('/rapor/haftalik');
    const donemler = (liste || []).filter((d) => d.donem).reverse();
    if (donemler.length === 0) return [el('p', { sinif: 'bos', metin: 'Henüz dönem yok.' })];
    const gosterilen = donemler.slice(0, durum.haftalikGoster);
    const parcalar = gosterilen.map((d, i) => donemKarti(d, i === 0));
    if (donemler.length > gosterilen.length) {
      const daha = el('button', { sinif: 'dugme-ikincil', type: 'button', metin: 'Daha eski dönemler' });
      daha.addEventListener('click', () => { durum.haftalikGoster += 8; ekraniCiz(); });
      parcalar.push(daha);
    }
    return parcalar;
  }

  function donemKarti(d, acik) {
    const cekVar = (d.kanallar || []).some((k) => Number(k.cekGelen) || Number(k.cekGiden));
    const basliklar = ['Kanal', 'Gelen', 'Giden'].concat(cekVar ? ['Çek'] : [], ['Sonuç', 'Devir']);
    const satirlar = (d.kanallar || []).map((k) => el('tr', null,
      el('td', { metin: k.kanal }),
      el('td', { metin: TL.format(k.gelen) }),
      el('td', { metin: TL.format(k.giden) }),
      cekVar ? el('td', { metin: TL.format((Number(k.cekGelen) || 0) - (Number(k.cekGiden) || 0)) }) : null,
      el('td', { sinif: renk(k.sonuc), metin: TL.format(k.sonuc) }),
      el('td', { metin: TL.format(k.devir) })));
    const tablo = el('table', { sinif: 'tablo' },
      el('thead', null, el('tr', null, basliklar.map((b) => el('th', { scope: 'col', metin: b })))),
      el('tbody', null, satirlar));
    return el('details', { sinif: 'donem', open: acik },
      el('summary', null,
        el('div', { sinif: 'ozet' },
          el('div', { sinif: 'ad', metin: aralik(d.donem.start, d.donem.end) }),
          el('div', { sinif: 'aciklama soluk', metin: `Kasa devri ${tl(d.kasaDevir)}` })),
        imzaliPara(d.kasaSonucu)),
      el('div', { sinif: 'tablo-sar' }, tablo));
  }

  async function aylikCiz() {
    if (!durum.yil) { const b = turkiyeBugun(); durum.yil = b.yil; durum.ay = b.ay; }
    const r = await api(`/rapor/aylik?yil=${durum.yil}&ay=${durum.ay}`);
    const onceki = el('button', { sinif: 'dugme-ikincil', type: 'button', 'aria-label': 'Önceki ay', metin: '‹' });
    const sonraki = el('button', { sinif: 'dugme-ikincil', type: 'button', 'aria-label': 'Sonraki ay', metin: '›' });
    onceki.addEventListener('click', () => { ayKaydir(-1); });
    sonraki.addEventListener('click', () => { ayKaydir(1); });
    const kanallar = (r.kanallar || []);
    const toplam = kanallar.reduce((t, k) => t + (Number(k.aySonucu) || 0), 0);
    const kartlar = kanallar.map((k) => {
      const kalemler = [
        ['Gelen', k.gelen], ['Cari gider', k.cariGiden], ['Sabit gider', k.sabitGider],
        ['Kredi kartı', k.krediKarti], ['Ortak pay', k.ortakPay],
      ];
      if (Number(k.cekGelen)) kalemler.push(['Çek tahsilatı', k.cekGelen]);
      if (Number(k.cekGiden)) kalemler.push(['Çek ödemesi', k.cekGiden]);
      return el('div', { sinif: 'kart' },
        el('div', { sinif: 'kart-baslik' }, el('span', { sinif: 'ad', metin: k.kanal }), imzaliPara(k.aySonucu)),
        kalemler.map(([ad, deger]) => el('div', { sinif: 'kv' }, el('span', { sinif: 'k', metin: ad }), para(deger))));
    });
    return [
      el('div', { sinif: 'ay-secici' }, onceki, el('div', { sinif: 'ay', metin: `${AYLAR[durum.ay - 1]} ${durum.yil}` }), sonraki),
      el('section', { sinif: 'kahraman' },
        el('div', { sinif: 'etiket', metin: 'Ay sonucu (tüm kanallar)' }),
        el('div', { sinif: 'tutar', metin: imzali(toplam) })),
      kartlar.length ? kartlar : el('p', { sinif: 'bos', metin: 'Bu ay için kayıt yok.' }),
    ];
  }

  function ayKaydir(fark) {
    let ay = durum.ay + fark, yil = durum.yil;
    if (ay < 1) { ay = 12; yil--; }
    if (ay > 12) { ay = 1; yil++; }
    durum.ay = ay; durum.yil = yil;
    ekraniCiz();
  }

  async function ceklerCiz() {
    const o = await api('/cekler/ozet');
    return [
      el('div', { sinif: 'ikili' },
        istatistik('Portföydeki alınan', para(o.portfoydekiAlinanToplam, 'arti'), `${o.portfoydekiAlinanAdet} çek · tahsil bekliyor`),
        istatistik('Ödenecek verilen', para(o.odenecekVerilenToplam, 'eksi'), `${o.odenecekVerilenAdet} çek · ödeme bekliyor`)),
      el('h2', { sinif: 'bolum-baslik', metin: 'Vadesi geçmiş (portföyde)' }),
      cekListesi(o.vadesiGecenler, 'Vadesi geçmiş çek yok.'),
      el('h2', { sinif: 'bolum-baslik', metin: `Vadesi ${o.yaklasanGun} gün içinde` }),
      cekListesi(o.yaklasanlar, 'Yaklaşan vade yok.'),
    ];
  }

  function cekListesi(liste, bos) {
    if (!liste || liste.length === 0) return el('p', { sinif: 'bos', metin: bos });
    return el('div', { sinif: 'kart liste' }, liste.map((c) => {
      const alinan = c.yon === 'Alinan';
      const ayrinti = [alinan ? 'Alınan' : 'Verilen', c.kanal, c.banka, c.cekNo ? 'No ' + c.cekNo : null].filter(Boolean).join(' · ');
      return el('div', { sinif: 'satir' },
        el('div', { sinif: 'sol' },
          el('div', { sinif: 'ad', metin: c.kisi }),
          el('div', { sinif: 'aciklama', metin: ayrinti })),
        el('div', { sinif: 'sag' },
          el('div', { sinif: 'para ' + (alinan ? 'arti' : 'eksi'), metin: (alinan ? '+' : '−') + tl(c.tutar) }),
          el('div', { sinif: 'aciklama', metin: 'Vade ' + tarih(c.vadeTarihi) })));
    }));
  }

  async function kartlarCiz() {
    const kartlar = await api('/kredikartlari');
    if (!kartlar || kartlar.length === 0) return [el('p', { sinif: 'bos', metin: 'Kayıtlı kart yok.' })];
    const toplam = kartlar.reduce((t, k) => t + (Number(k.guncelBorc) || 0), 0);
    return [
      el('section', { sinif: 'kahraman' },
        el('div', { sinif: 'etiket', metin: 'Toplam güncel borç' }),
        el('div', { sinif: 'tutar', metin: tl(toplam) })),
      kartlar.map((k) => {
        const limit = Number(k.limit) || 0;
        const kalan = limit - (Number(k.guncelBorc) || 0);
        return el('div', { sinif: 'kart' },
          el('div', { sinif: 'kart-baslik' }, el('span', { sinif: 'ad', metin: k.ad }), para(k.guncelBorc, 'eksi')),
          limit > 0 ? el('meter', { sinif: 'limit', min: 0, max: limit, value: Math.max(0, Math.min(limit, kalan)) }) : null,
          el('div', { sinif: 'kv' }, el('span', { sinif: 'k', metin: 'Ekstre borcu' }), para(k.ekstreBorc)),
          limit > 0 ? el('div', { sinif: 'kv' }, el('span', { sinif: 'k', metin: 'Kalan limit' }), para(kalan, renk(kalan))) : null,
          el('div', { sinif: 'kv' }, el('span', { sinif: 'k', metin: 'Kesim' }), el('span', { metin: tarih(k.kesimTarihi) })),
          el('div', { sinif: 'kv' }, el('span', { sinif: 'k', metin: 'Son ödeme' }), el('span', { metin: tarih(k.sonOdemeTarihi) })));
      }),
    ];
  }

  // ------------------------------------------------------------------ başlangıç

  function baglantiDurumu() { $('baglanti').hidden = navigator.onLine !== false; }

  async function basla() {
    $('cikis').addEventListener('click', cikisYap);
    $('yenile').addEventListener('click', ekraniCiz);
    window.addEventListener('hashchange', ekraniCiz);
    window.addEventListener('online', baglantiDurumu);
    window.addEventListener('offline', baglantiDurumu);
    baglantiDurumu();
    if ('serviceWorker' in navigator) {
      navigator.serviceWorker.register('/m/sw.js', { scope: '/m/' }).catch(() => { /* PWA kurulumu isteğe bağlı */ });
    }
    try {
      await oturumuAc();
    } catch (hata) {
      if (hata instanceof OturumYok) girisGoster();
      else {
        const tekrar = el('button', { sinif: 'dugme-ikincil kucuk', type: 'button', metin: 'Tekrar dene' });
        tekrar.addEventListener('click', () => location.reload());
        $('icerik').replaceChildren(el('div', { sinif: 'hata-kutu', role: 'alert' }, el('div', { metin: hata.message }), tekrar));
      }
    }
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', basla);
  else basla();
})();
