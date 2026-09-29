using Kasa.Api.Data;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>Kart takibi düzeltmeleri: eski borç devrinin okunması ve gerekçeli düzeltmesi, devir iadesinin kasaya dönen
/// önceden sayılmış tutarı (koşullu alan) ve kilitli döneme düşen avansın dağıtım kaydı (koşullu alan).</summary>
public class KartTakipDuzeltmeSozlesmeTests : SozlesmeTemeli
{
    private static Guid Yeni() => Guid.NewGuid();

    [Fact]
    [SozlesmeKapsami(nameof(IFinansTakipApi.TakipKartDevirAsync), nameof(IFinansTakipApi.TakipKartDevirDuzeltAsync))]
    [KosulluAlanSenaryosu(typeof(Kasa.Api.KartHarcamaDto), nameof(Kasa.Api.KartHarcamaDto.KasadaSayilanDuzeltme))]
    [KosulluAlanSenaryosu(typeof(Kasa.Api.KartTakipOdemeDto), nameof(Kasa.Api.KartTakipOdemeDto.AvansKaynakOdemeId))]
    public async Task Devir_duzeltmesi_devir_iadesi_ve_kilitli_avans_dagitimi_istemci_turlerine_birebir_uyar()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var eskiKart = 0;
        F.Veri(db =>
        {
            var k = new KrediKartiEntity { Ad = "Eski kart", KesimTarihi = Baslangic.AddDays(4), SonOdemeTarihi = Baslangic.AddDays(24), Limit = 10000m };
            db.KrediKartlari.Add(k); db.SaveChanges(); eskiKart = k.Id;
            db.Islemler.Add(new() { Tarih = Baslangic, Cari = "Eski harcama", TutarTl = 100m, KanalId = 1, Kanal = "MEZAT", Tip = Kasa.Core.GiderTipi.KrediKarti, KrediKartiId = k.Id });
            db.SaveChanges();
        });
        var kart = await o.Takip.TakipKartGecisAsync(eskiKart, new KartGecisYaz(Yeni(), 0, Bugun, 120m, 100m, [new(1, 120m)], "Borç kasada önceden sayılmış", true));
        var devir = await o.Takip.TakipKartDevirAsync(eskiKart);
        Assert.Equal((120m, 100m, 100m, true), (devir.KalanBorc, devir.KasadaOncedenSayilanTutar, devir.SistemKartBorcu, devir.Duzeltilebilir));
        kart = await o.Takip.TakipKartDevirDuzeltAsync(eskiKart, new KartDevirDuzeltYaz(Yeni(), kart.Surum, devir.HarcamaId, 100m, 100m, [new(1, 100m)], "Banka ekstresine göre"));
        var yeniDevir = kart.Harcamalar.Single(h => !h.Iptal);
        Assert.Equal(100m, yeniDevir.Tutar); Assert.True(kart.Harcamalar.Single(h => h.Id == devir.HarcamaId).Iptal);
        kart = await o.Takip.TakipHarcamaKaydetAsync(eskiKart, new KartHarcamaYaz(Yeni(), kart.Surum, Bugun, "Satıcı iadesi", -30m, 1, null, [], yeniDevir.Id));
        Assert.Equal(30m, kart.Harcamalar.Single(h => h.Tutar < 0).KasadaSayilanDuzeltme);

        // Kilitli döneme düşen avans: önceki ay ödenmiş, ay kapatılmış; yeni harcama avansı kilit sonrası dağıtımla bağlar.
        var oncekiAy = new DateOnly(Bugun.Year, Bugun.Month, 1).AddMonths(-1);
        var avansKarti = await o.Takip.TakipKartKaydetAsync(null, new KartTakipYaz(Yeni(), 0, "Avans kartı", 5000m, 5, 25, oncekiAy, 0m, []));
        avansKarti = await o.Takip.TakipOdemeKaydetAsync(avansKarti.Id, new KartTakipOdemeYaz(Yeni(), avansKarti.Surum, oncekiAy, 40m, null, "Avans"));
        var kilit = await o.AylikGider.AyKilidiAsync();
        await o.AylikGider.AyKilidiDegistirAsync(true, new AyKilidiYaz(Yeni(), kilit.Surum, oncekiAy.Year, oncekiAy.Month, "Ay tamamlandı"));
        avansKarti = await o.Takip.TakipHarcamaKaydetAsync(avansKarti.Id, new KartHarcamaYaz(Yeni(), avansKarti.Surum, Bugun, "Yeni harcama", 60m, 1, null, [new(2, 60m)]));
        var dagitim = avansKarti.Odemeler.Single(p => p.AvansKaynakOdemeId is not null);
        Assert.Equal((0m, 0m, Bugun), (dagitim.Tutar, dagitim.KasaEtkisi, dagitim.Tarih));
        Assert.Equal(20m, avansKarti.Borc);
    }
}
