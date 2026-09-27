using System.Net;
using Kasa.Api.Data;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>Yeni takip: kart (düzenleme, harcama, ekstre asgarisi, ödeme önizleme/kayıt/iptal, masraf, iptal, durum,
/// eski karttan geçiş) ve kredi (taksit planı, erken kapama, durum, eski krediden geçiş), takip özeti.</summary>
public class TakipSozlesmeTests : SozlesmeTemeli
{
    private static Guid Yeni() => Guid.NewGuid();

    [Fact]
    [SozlesmeKapsami(nameof(IFinansTakipApi.TakipKartKaydetAsync), nameof(IFinansTakipApi.TakipHarcamaKaydetAsync), nameof(IFinansTakipApi.TakipEkstreKaydetAsync),
        nameof(IFinansTakipApi.TakipOdemeOnizlemeAsync), nameof(IFinansTakipApi.TakipOdemeKaydetAsync), nameof(IFinansTakipApi.TakipOdemeIptalAsync),
        nameof(IFinansTakipApi.TakipHarcamaIptalAsync), nameof(IFinansTakipApi.TakipKartDurumAsync), nameof(IFinansTakipApi.TakipKartGecisOnizlemeAsync),
        nameof(IFinansTakipApi.TakipKartGecisAsync), nameof(IFinansTakipApi.TakipKartlarAsync), nameof(IFinansTakipApi.TakipKartAsync),
        nameof(IFinansTakipApi.TakipOzetAsync), nameof(IKasaKontrolApi.KartMasrafOnizleAsync), nameof(IKasaKontrolApi.KartMasrafKaydetAsync))]
    public async Task Takip_karti_yasam_dongusu_istemci_turlerine_birebir_uyar()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var kart = await o.Takip.TakipKartKaydetAsync(null, new KartTakipYaz(Yeni(), 0, "Sözleşme kartı", 10000m, 5, 25, Baslangic, 0m, []));
        Assert.True(kart.Surum > 0); Assert.True(kart.YeniTakip); Assert.True(kart.Aktif);
        kart = await o.Takip.TakipKartKaydetAsync(kart.Id, new KartTakipYaz(Yeni(), kart.Surum, "Sözleşme kartı 2", 12000m, 5, 25, Baslangic, 0m, []));
        Assert.Equal(("Sözleşme kartı 2", 12000m), (kart.Ad, kart.Limit));
        kart = await o.Takip.TakipHarcamaKaydetAsync(kart.Id, new KartHarcamaYaz(Yeni(), kart.Surum, Baslangic, "Malzeme", 300m, 3, null, [new(1, 180m), new(2, 120m)]));
        var harcama = Assert.Single(kart.Harcamalar);
        Assert.Equal((300m, 3), (harcama.Tutar, harcama.TaksitSayisi)); Assert.Equal(300m, harcama.Dagilimlar.Sum(p => p.Tutar));

        var ekstre = kart.Ekstreler.First(e => e.Borc > 0);
        kart = await o.Takip.TakipEkstreKaydetAsync(kart.Id, ekstre.Id, new KartEkstreYaz(Yeni(), kart.Surum, ekstre.SonOdemeTarihi, 50m, "Banka asgarisi"));
        Assert.Equal(50m, kart.Ekstreler.Single(e => e.Id == ekstre.Id).AsgariKalan);
        var odemeIstegi = new KartTakipOdemeYaz(Yeni(), kart.Surum, Bugun, 60m, ekstre.Id, "Kısmi ödeme");
        var onizleme = await o.Takip.TakipOdemeOnizlemeAsync(kart.Id, odemeIstegi);
        Assert.Equal(60m, onizleme.Tutar); Assert.Equal(ekstre.Id, Assert.Single(onizleme.Ekstreler).EkstreId); Assert.Equal(60m, onizleme.Dagilimlar.Sum(p => p.Tutar));
        kart = await o.Takip.TakipOdemeKaydetAsync(kart.Id, odemeIstegi);
        var odeme = Assert.Single(kart.Odemeler);
        Assert.Equal((60m, "Kısmi ödeme", false), (odeme.Tutar, odeme.Not, odeme.Iptal));

        var kesilmis = kart.Ekstreler.Last(e => e.Id > 0 && e.KesimTarihi <= Bugun && e.Kalan > 0);
        var masrafIstegi = new KartMasrafYaz(Yeni(), kart.Surum, kesilmis.Id, Bugun, 12.34m, "Gecikme faizi");
        var masraf = await o.Kontrol.KartMasrafOnizleAsync(kart.Id, masrafIstegi);
        Assert.Equal((kart.Id, kesilmis.Id, 12.34m), (masraf.KartId, masraf.EkstreId, masraf.Tutar)); Assert.False(string.IsNullOrEmpty(masraf.DagilimOzeti));
        kart = await o.Kontrol.KartMasrafKaydetAsync(kart.Id, masrafIstegi with { DagilimOzeti = masraf.DagilimOzeti });
        Assert.Contains(kart.Harcamalar, h => h.Tutar == 12.34m);

        kart = await o.Takip.TakipHarcamaKaydetAsync(kart.Id, new KartHarcamaYaz(Yeni(), kart.Surum, Bugun, "Yanlış harcama", 40m, 1, null, [new(1, 40m)]));
        var yanlis = kart.Harcamalar.Single(h => h.Aciklama == "Yanlış harcama");
        kart = await o.Takip.TakipHarcamaIptalAsync(kart.Id, yanlis.Id, new TakipIptalYaz(Yeni(), kart.Surum, "Yanlış kayıt"));
        Assert.True(kart.Harcamalar.Single(h => h.Id == yanlis.Id).Iptal);
        kart = await o.Takip.TakipOdemeIptalAsync(kart.Id, odeme.Id, new TakipIptalYaz(Yeni(), kart.Surum, "Banka iade etti"));
        Assert.True(Assert.Single(kart.Odemeler).Iptal);

        // Eski karttan geçiş: denetim izi (Gecis, Onizleme) iç içe türleriyle döner.
        var eskiKart = 0;
        F.Veri(db =>
        {
            var k = new KrediKartiEntity { Ad = "Eski kart", KesimTarihi = Baslangic.AddDays(4), SonOdemeTarihi = Baslangic.AddDays(24), Limit = 10000m };
            db.KrediKartlari.Add(k); db.SaveChanges(); eskiKart = k.Id;
            db.Islemler.Add(new() { Tarih = Baslangic, Cari = "Eski harcama", TutarTl = 100m, KanalId = 1, Kanal = "MEZAT", Tip = Kasa.Core.GiderTipi.KrediKarti, KrediKartiId = k.Id });
            db.SaveChanges();
        });
        var gecisIstegi = new KartGecisYaz(Yeni(), 0, Bugun, 100m, 100m, [new(1, 100m)], "Borç kasada önceden sayılmış", false);
        var gecisOnizleme = await o.Takip.TakipKartGecisOnizlemeAsync(eskiKart, gecisIstegi);
        Assert.True(gecisOnizleme.KabulEdilebilir); Assert.Equal(("Kart", 100m), (gecisOnizleme.Kaynak, gecisOnizleme.SistemKartBorcu)); Assert.NotEmpty(gecisOnizleme.Aciklamalar);
        var gecen = await o.Takip.TakipKartGecisAsync(eskiKart, gecisIstegi with { Onay = true });
        Assert.True(gecen.YeniTakip); Assert.Equal(100m, gecen.Gecis!.Onizleme!.KalanBorc);
        Assert.Equal(gecen.Gecis.Kural, (await o.Takip.TakipKartAsync(eskiKart)).Gecis!.Kural);

        kart = await o.Takip.TakipKartDurumAsync(kart.Id, new TakipDurumYaz(Yeni(), kart.Surum, false, "Kart kapatıldı"));
        Assert.False(kart.Aktif);
        Assert.Equal(2, (await o.Takip.TakipKartlarAsync()).Count);
        Assert.Equal(kart.Surum, (await o.Takip.TakipKartAsync(kart.Id)).Surum);
        var ozet = await o.Takip.TakipOzetAsync(60);
        Assert.Equal(Bugun, ozet.Tarih); Assert.NotEmpty(ozet.Olaylar); Assert.NotNull(ozet.KanalKartBorclari);
        Bitir();
    }

    [Fact]
    [SozlesmeKapsami(nameof(IFinansTakipApi.TakipKrediKaydetAsync), nameof(IFinansTakipApi.TakipTaksitKaydetAsync), nameof(IFinansTakipApi.TakipKrediKapatAsync),
        nameof(IFinansTakipApi.TakipKrediDurumAsync), nameof(IFinansTakipApi.TakipKrediGecisOnizlemeAsync), nameof(IFinansTakipApi.TakipKrediGecisAsync),
        nameof(IFinansTakipApi.TakipKredilerAsync), nameof(IFinansTakipApi.TakipKrediAsync), nameof(IFinansTakipApi.TakipOzetAsync))]
    public async Task Takip_kredisi_yasam_dongusu_istemci_turlerine_birebir_uyar()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var kredi = await o.Takip.TakipKrediKaydetAsync(new KrediTakipYaz(Yeni(), "Sözleşme kredisi", 12000m, Baslangic, Baslangic.AddMonths(1), 12, 1000m, [1, 2]));
        Assert.Equal((12, 12000m), (kredi.Taksitler.Count, kredi.KanalPaylari.Sum(p => p.Tutar)));
        Assert.Contains(kredi.Taksitler, t => t.Durum == "KasayaIslendi"); Assert.Contains(kredi.Taksitler, t => t.Durum == "Bekliyor");
        var ileri = kredi.Taksitler.First(t => t.Tarih > Bugun);
        kredi = await o.Takip.TakipTaksitKaydetAsync(kredi.Id, ileri.Id, new KrediTaksitYaz(Yeni(), kredi.Surum, ileri.Tarih.AddDays(2), 1100m, "Banka planı", false, "Plan değişti"));
        Assert.Equal((ileri.Tarih.AddDays(2), 1100m, "Banka planı"), kredi.Taksitler.Where(t => t.Id == ileri.Id).Select(t => (t.Tarih, t.Tutar, t.Not)).Single());
        var ozet = await o.Takip.TakipOzetAsync(30);
        Assert.Contains(ozet.Olaylar, e => e.Kaynak == "Kredi" && e.KaynakId == kredi.Id);
        kredi = await o.Takip.TakipKrediKapatAsync(kredi.Id, new KrediKapatYaz(Yeni(), kredi.Surum, Bugun.AddDays(1), 4000m, "Erken kapama"));
        Assert.Contains(kredi.Taksitler, t => t.Tarih == Bugun.AddDays(1) && t.Tutar == 4000m && t.Durum == "Bekliyor");
        Assert.All(kredi.Taksitler.Where(t => t.Tarih > Bugun.AddDays(1)), t => Assert.Equal("Iptal", t.Durum));
        kredi = await o.Takip.TakipKrediDurumAsync(kredi.Id, new TakipDurumYaz(Yeni(), kredi.Surum, false, "Kapandı"));
        Assert.False(kredi.Aktif);

        var eskiKredi = 0;
        F.Veri(db =>
        {
            var k = new KrediEntity { Ad = "Eski kredi", CekilenTutar = 6000m, CekimTarihi = Baslangic, TaksitSayisi = 12, AylikOdeme = 500m, OdemeGunu = 15, Kanal = "MEZAT", KanalId = 1 };
            db.Krediler.Add(k); db.SaveChanges(); eskiKredi = k.Id;
        });
        Assert.False((await o.Takip.TakipKrediAsync(eskiKredi)).YeniTakip);
        var gecisIstegi = new KrediGecisYaz(Yeni(), 0, Bugun.AddDays(1), [1], "Eski kredi takibe", false);
        var onizleme = await o.Takip.TakipKrediGecisOnizlemeAsync(eskiKredi, gecisIstegi);
        Assert.Equal(("Kredi", eskiKredi, true), (onizleme.Kaynak, onizleme.KaynakId, onizleme.KabulEdilebilir)); Assert.Null(onizleme.SistemKartBorcu);
        var gecen = await o.Takip.TakipKrediGecisAsync(eskiKredi, gecisIstegi with { Onay = true });
        Assert.True(gecen.YeniTakip); Assert.All(gecen.Taksitler, t => Assert.True(t.Tarih >= Bugun.AddDays(1)));
        Assert.Equal(2, (await o.Takip.TakipKredilerAsync()).Count);
        Bitir();
    }
}
