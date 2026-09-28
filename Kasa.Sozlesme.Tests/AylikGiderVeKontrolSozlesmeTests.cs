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
}
