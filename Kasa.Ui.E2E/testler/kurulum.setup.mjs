import { randomUUID } from 'node:crypto';
import { expect, test as kurulum } from '@playwright/test';
import { OTURUM } from './ortak.mjs';

// Sunucu her koşuda boş veritabanıyla açılır (bugün 2026-09-25 Cuma, sabit saat). Ekranların boş değil gerçekçi görünmesi
// için tohum veri yalnız API'den, kullanıcının yapacağı sırayla girilir: takip başlangıcı, dönem gelirleri, giderler,
// üç durumda alış ve bir gerçek bakiye karşılaştırması. Ardından editör oturumu (HttpOnly çerez) testlere kaydedilir.
kurulum('tohum veriyi gir ve editör oturumunu kaydet', async ({ request }) => {
  const iste = async (yontem, yol, veri) => {
    const yanit = await request.fetch(yol, { method: yontem, data: veri });
    const metin = await yanit.text();
    expect(yanit.ok(), `${yontem} ${yol}: ${yanit.status()} ${metin}`).toBeTruthy();
    return metin ? JSON.parse(metin) : null;
  };

  await iste('POST', '/api/auth/login', { kullanici: process.env.KASA_E2E_KULLANICI, sifre: process.env.KASA_E2E_SIFRE });
  await iste('PUT', '/api/ayarlar', { takipBaslangic: '2026-09-01', kasaAcilisDevri: 185000 });
  const kanallar = await iste('GET', '/api/kanallar');
  const kanal = ad => kanallar.find(k => k.ad === ad).id;

  const gelirler = [
    ['2026-09-01', 'MEZAT', 48500], ['2026-09-01', 'PERAKENDE', 12750.5], ['2026-09-01', 'TOPTAN', 30400],
    ['2026-09-07', 'MEZAT', 52300], ['2026-09-07', 'PERAKENDE', 9800], ['2026-09-07', 'TOPTAN', 27650],
    ['2026-09-14', 'MEZAT', 47100], ['2026-09-14', 'PERAKENDE', 11250.75], ['2026-09-14', 'TOPTAN', 33900],
    ['2026-09-21', 'MEZAT', 21500], ['2026-09-21', 'PERAKENDE', 6400],
  ];
  for (const [donemStart, ad, tutarTl] of gelirler) await iste('PUT', '/api/gelenler', { donemStart, kanal: ad, tutarTl });

  const giderler = [
    ['2026-09-02', 'Kargo ve nakliye', 3450, 'TOPTAN', 'Cari'],
    ['2026-09-05', 'Dükkân kirası', 18000, 'Ortak', 'SabitGider'],
    ['2026-09-10', 'Ambalaj malzemesi', 2275.4, 'PERAKENDE', 'Cari'],
    ['2026-09-16', 'Elektrik faturası', 1890.65, 'MEZAT', 'SabitGider'],
    ['2026-09-22', 'Yemek kartı yüklemesi', 4200, 'PERAKENDE', 'Cari'],
    ['2026-09-24', 'Toptancı ödemesi', 12500, 'TOPTAN', 'Cari'],
  ];
  for (const [tarih, cari, tutarTl, kanalAdi, tip] of giderler) {
    await iste('POST', '/api/islemler', { tarih, cari, tutarTl, kanal: kanalAdi, tip, not: null, istekId: randomUUID() });
  }

  const alis = (tarih, tedarikci, aciklama, paylar) => iste('POST', '/api/alis', {
    surum: 0, tarih, tedarikci, not: null,
    kalemler: [{ aciklama, tutar: paylar.reduce((t, [, tutar]) => t + tutar, 0), dagilimlar: paylar.map(([ad, tutar]) => ({ kanalId: kanal(ad), tutar })) }],
  });
  const onayli = await alis('2026-09-18', 'Anadolu Toptan Gıda', 'Kuru gıda kolisi', [['MEZAT', 5000], ['PERAKENDE', 3400]]);
  const onayda = await iste('POST', `/api/alis/${onayli.id}/gonder`, { surum: onayli.surum });
  await iste('POST', `/api/alis/${onayli.id}/onayla`, { surum: onayda.surum, not: 'Faturayla karşılaştırıldı.' });
  const incelemede = await alis('2026-09-23', 'Ege Ambalaj', 'Karton kutu (500 adet)', [['TOPTAN', 6250]]);
  await iste('POST', `/api/alis/${incelemede.id}/gonder`, { surum: incelemede.surum });
  await alis('2026-09-25', 'Merkez Kırtasiye', 'Fiş rulosu ve etiket', [['PERAKENDE', 780.9]]);

  const onizleme = await iste('POST', '/api/kasa-kontrol/onizleme', { gercekBakiye: 0, not: null });
  const gercekBakiye = Math.round((onizleme.sistemBakiye - 150) * 100) / 100;
  const kontrol = await iste('POST', '/api/kasa-kontrol/onizleme', { gercekBakiye, not: null });
  await iste('POST', '/api/kasa-kontrol', { istekId: randomUUID(), gercekBakiye, kontrolOzeti: kontrol.kontrolOzeti, not: 'Akşam sayımında 150 ₺ eksik.' });

  await request.storageState({ path: OTURUM });
});
