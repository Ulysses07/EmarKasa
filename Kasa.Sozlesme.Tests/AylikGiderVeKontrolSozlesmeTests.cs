using System.Net;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>Aylık gider şablonları ve ödemeleri, ay kilidi, kasa eşikleri ve kasa kontrolü.</summary>
public class AylikGiderVeKontrolSozlesmeTests : SozlesmeTemeli
{
    private static Guid Yeni() => Guid.NewGuid();

    [Fact]
    [SozlesmeKapsami(nameof(IAylikGiderApi.AylikGiderSablonKaydetAsync), nameof(IAylikGiderApi.AylikGiderSablonlariAsync), nameof(IAylikGiderApi.AylikGiderlerAsync),
        nameof(IAylikGiderApi.AylikGiderOdeAsync), nameof(IAylikGiderApi.AylikGiderIptalAsync), nameof(IAylikGiderApi.AyKilidiAsync), nameof(IAylikGiderApi.AyKilidiDegistirAsync))]
    public async Task Aylik_gider_ve_ay_kilidi_istemci_turlerine_birebir_uyar()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var buAy = new DateOnly(Bugun.Year, Bugun.Month, 1);
        var sablon = await o.AylikGider.AylikGiderSablonKaydetAsync(null, new AylikGiderSablonYaz(Yeni(), 0, "Kira", "Kira", 5000m, 5, "Ozel", [new(1, 3000m), new(2, 2000m)], buAy));
        sablon = await o.AylikGider.AylikGiderSablonKaydetAsync(sablon.Id, new AylikGiderSablonYaz(Yeni(), sablon.Surum, "Kira", "Kira", 5500.5m, 5, "Ozel", [new(1, 3300.5m), new(2, 2200m)], buAy));
        Assert.Equal((2, 5500.5m, buAy), (sablon.Surum, sablon.Tutar, sablon.GecerliAy)); Assert.Equal(5500.5m, sablon.Dagilimlar.Sum(p => p.Tutar));
        Assert.Equal(sablon.Id, Assert.Single(await o.AylikGider.AylikGiderSablonlariAsync()).Id);

        var ay = await o.AylikGider.AylikGiderlerAsync(Bugun.Year, Bugun.Month);
        var satir = Assert.Single(ay.Kayitlar);
        Assert.Equal(("Planlandi", new DateOnly(Bugun.Year, Bugun.Month, 5), 5500.5m), (satir.Durum, satir.PlanlananTarih, ay.PlanlananToplam));
        var odenen = await o.AylikGider.AylikGiderOdeAsync(sablon.Id, new AylikGiderOdemeYaz(Yeni(), satir.SablonSurum, Bugun.Year, Bugun.Month, Bugun, "Havale"));
        Assert.Equal(("Odendi", Bugun), (odenen.Durum, odenen.OdemeTarihi)); Assert.NotNull(odenen.IslemId);
        Assert.Equal(5500.5m, (await o.AylikGider.AylikGiderlerAsync(Bugun.Year, Bugun.Month)).OdenenToplam);
        var iptal = await o.AylikGider.AylikGiderIptalAsync(odenen.OdemeId!.Value, new AylikGiderIptalYaz(Yeni(), "Yanlış hesap"));
        Assert.Equal("Iptal", iptal.Durum); Assert.Null(iptal.IslemId);

        var kilit = await o.AylikGider.AyKilidiAsync();
        Assert.Null(kilit.KilitliSonTarih); Assert.Empty(kilit.Gecmis);
        kilit = await o.AylikGider.AyKilidiDegistirAsync(true, new AyKilidiYaz(Yeni(), kilit.Surum, Baslangic.Year, Baslangic.Month, "Ocak tamamlandı"));
        Assert.Equal(Baslangic.AddMonths(1).AddDays(-1), kilit.KilitliSonTarih);
        var olay = Assert.Single(kilit.Gecmis); Assert.Equal(("Ocak tamamlandı", (DateOnly?)null), (olay.Aciklama, olay.OncekiSonTarih));
        kilit = await o.AylikGider.AyKilidiDegistirAsync(false, new AyKilidiYaz(Yeni(), kilit.Surum, Baslangic.Year, Baslangic.Month, "Düzeltme için açıldı"));
        Assert.Null(kilit.KilitliSonTarih); Assert.Equal(2, kilit.Gecmis.Count);
    }

    /// <summary>Benzer kayıt (BNZ): masaüstü BenzerOnay alanını göndermez (izin listesinde gerekçesiyle). Aynı tutarda
    /// yakın tarihli kayıt varken ilk ödeme isteği hiçbir şey yazmadan okunur iletili 409 alır (vekil iletinin okunduğunu
    /// denetler); aynı istek kimliğiyle değişmeyen gövdenin yeniden gönderimi onay sayılır ve ödemeyi kaydeder.</summary>
    [Fact]
    [SozlesmeKapsami(nameof(IAylikGiderApi.AylikGiderSablonKaydetAsync), nameof(IAylikGiderApi.AylikGiderlerAsync), nameof(IAylikGiderApi.AylikGiderOdeAsync),
        nameof(IKasaApi.IslemOlusturAsync))]
    public async Task Aylik_gider_odemesi_benzer_kayitta_okunur_409_alir_ayni_istek_kimligiyle_kaydedilir()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var buAy = new DateOnly(Bugun.Year, Bugun.Month, 1);
        var sablon = await o.AylikGider.AylikGiderSablonKaydetAsync(null, new AylikGiderSablonYaz(Yeni(), 0, "Kira", "Kira", 4000m, 5, "Ozel", [new(1, 4000m)], buAy));
        // Aynı kira bankadan gider olarak önceden işlenmiş: aynı tutar, ±3 gün, şablonun kanalı.
        var banka = Bugun.AddDays(-1);
        await o.Kasa.IslemOlusturAsync(new IslemYaz(banka, "Kira (banka)", 4000m, "MEZAT", GiderTipi.SabitGider, null));
        var satir = Assert.Single((await o.AylikGider.AylikGiderlerAsync(Bugun.Year, Bugun.Month)).Kayitlar);
        var istek = new AylikGiderOdemeYaz(Yeni(), satir.SablonSurum, Bugun.Year, Bugun.Month, Bugun, "Havale");

        var uyari = await Assert.ThrowsAsync<KasaApiException>(() => o.AylikGider.AylikGiderOdeAsync(sablon.Id, istek));
        Assert.Equal(HttpStatusCode.Conflict, uyari.DurumKodu);
        Assert.Contains($"{banka:dd.MM.yyyy} · 4.000,00 TL", uyari.Message); Assert.Contains("değiştirmeden", uyari.Message);
        Assert.Equal("Planlandi", Assert.Single((await o.AylikGider.AylikGiderlerAsync(Bugun.Year, Bugun.Month)).Kayitlar).Durum);

        var odenen = await o.AylikGider.AylikGiderOdeAsync(sablon.Id, istek);
        Assert.Equal(("Odendi", Bugun), (odenen.Durum, odenen.OdemeTarihi)); Assert.NotNull(odenen.IslemId);
    }

    [Fact]
    [SozlesmeKapsami(nameof(IKasaKontrolApi.KasaEsikleriAsync), nameof(IKasaKontrolApi.KasaEsigiKaydetAsync), nameof(IKasaKontrolApi.KasaKontrolOnizleAsync),
        nameof(IKasaKontrolApi.KasaKontrolKaydetAsync), nameof(IKasaKontrolApi.KasaKontrolleriAsync))]
    public async Task Kasa_esikleri_ve_kasa_kontrolu_istemci_turlerine_birebir_uyar()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var esikler = await o.Kontrol.KasaEsikleriAsync();
        Assert.Equal(3, esikler.Count);
        var ilk = esikler[0];
        var esik = await o.Kontrol.KasaEsigiKaydetAsync(ilk.KanalId, new KasaEsikYaz(ilk.Surum, 500.5m, true));
        Assert.Equal((ilk.KanalId, 1, 500.5m, true, true), (esik.KanalId, esik.Surum, esik.Tutar, esik.Etkin, esik.EsikAltinda));

        var onizleme = await o.Kontrol.KasaKontrolOnizleAsync(new KasaKontrolOnizle(950.5m, "Akşam sayımı"));
        Assert.Equal((1000m, 950.5m, -49.5m), (onizleme.SistemBakiye, onizleme.GercekBakiye, onizleme.Fark));
        var kontrol = await o.Kontrol.KasaKontrolKaydetAsync(new KasaKontrolYaz(Yeni(), 950.5m, onizleme.KontrolOzeti, "Akşam sayımı"));
        Assert.Equal((-49.5m, "Akşam sayımı"), (kontrol.Fark, kontrol.Not));
        Assert.Equal(kontrol.Id, Assert.Single(await o.Kontrol.KasaKontrolleriAsync()).Id);
    }

    /// <summary>Kasa kontrolünün filigranı, zorunlu fark açıklaması (okunur 400), sonradan açıklama, "kontrolden beri değişenler" ve
    /// kasa hareket dökümü (gap-denetim-izi-gozlemlenebilirlik-3, gap-coklu-giris-cift-sayim-mutabakat-17).</summary>
    [Fact]
    [SozlesmeKapsami(nameof(IKasaKontrolApi.KasaKontrolOnizleAsync), nameof(IKasaKontrolApi.KasaKontrolKaydetAsync), nameof(IKasaKontrolApi.KasaKontrolAciklaAsync),
        nameof(IKasaKontrolApi.KasaKontrolleriAsync), nameof(IKasaKontrolApi.KasaKontrolSonrasiAsync), nameof(IKasaKontrolApi.KasaHareketleriAsync),
        nameof(IKasaApi.IslemOlusturAsync))]
    public async Task Kasa_kontrolu_filigrani_aciklamasi_sonrasi_ve_hareket_dokumu_istemci_turlerine_birebir_uyar()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        await o.Kasa.IslemOlusturAsync(new IslemYaz(Bugun.AddDays(-1), "Nakliye", 40m, "MEZAT", GiderTipi.Cari, null));
        var onizleme = await o.Kontrol.KasaKontrolOnizleAsync(new KasaKontrolOnizle(900m));
        Assert.Equal((960m, -60m, (DateOnly?)Bugun), (onizleme.SistemBakiye, onizleme.Fark, onizleme.HesapTarihi));
        Assert.Equal("MEZAT", onizleme.KanalBakiyeleri!.Single(k => k.KanalId == 1).Kanal);

        var hata = await Assert.ThrowsAsync<KasaApiException>(() => o.Kontrol.KasaKontrolKaydetAsync(new KasaKontrolYaz(Yeni(), 900m, onizleme.KontrolOzeti)));
        Assert.Equal(HttpStatusCode.BadRequest, hata.DurumKodu); Assert.Contains("Fark varsa açıklama girin.", hata.Message);
        var kontrol = await o.Kontrol.KasaKontrolKaydetAsync(new KasaKontrolYaz(Yeni(), 900m, onizleme.KontrolOzeti, "Sayım"));
        Assert.Equal((1, (DateOnly?)Bugun, onizleme.KanalBakiyeleri!.Count), (kontrol.Surum, kontrol.HesapTarihi, kontrol.KanalBakiyeleri!.Count));
        kontrol = await o.Kontrol.KasaKontrolAciklaAsync(kontrol.Id, new KasaKontrolAciklamaYaz(Yeni(), kontrol.Surum, "Bankaya yatırıldı"));
        Assert.Equal((2, "Bankaya yatırıldı"), (kontrol.Surum, kontrol.FarkAciklamasi)); Assert.NotNull(kontrol.FarkAciklamaZamani);
        var liste = Assert.Single(await o.Kontrol.KasaKontrolleriAsync());
        Assert.Equal(((decimal?)960m, (decimal?)-60m, false), (liste.GuncelSistemBakiye, liste.GuncelFark, liste.SonradanDegisti));
        Assert.All(liste.KanalBakiyeleri!, k => Assert.Equal(k.Bakiye, k.GuncelBakiye));

        // Kontrol gününden önceye sonradan girilen gider: liste işaretler, "sonrası" olayı ve hareketi verir.
        await o.Kasa.IslemOlusturAsync(new IslemYaz(Bugun.AddDays(-2), "Geriye dönük", 25m, "PERAKENDE", GiderTipi.Cari, null));
        Assert.True(Assert.Single(await o.Kontrol.KasaKontrolleriAsync()).SonradanDegisti);
        var sonra = await o.Kontrol.KasaKontrolSonrasiAsync(kontrol.Id);
        Assert.Equal((true, Bugun, 960m, 935m, 935m), (sonra.FiligranVar, sonra.EsasTarih, sonra.SistemBakiye, sonra.GuncelSistemBakiye, sonra.BugunkuSistemBakiye));
        Assert.Contains(sonra.Degisiklikler, d => (d.Varlik, d.Tur) == ("Islem", "Ekle"));
        Assert.Contains(sonra.Hareketler, h => (h.Aciklama, h.GenelKasaEtkisi, h.KanalId, h.Tur) == ("Geriye dönük", -25m, (int?)2, "Gider"));

        var dokum = await o.Kontrol.KasaHareketleriAsync(Baslangic, Bugun);
        Assert.Equal((1000m, 935m, Bugun), (dokum.AcilisBakiyesi, dokum.KapanisBakiyesi, dokum.Bitis));
        var mezat = await o.Kontrol.KasaHareketleriAsync(Baslangic, Bugun, 1);
        Assert.Equal(("MEZAT", -40m), (mezat.Kanal, mezat.Hareketler.Sum(h => h.KanalEtkisi)));
    }
}
