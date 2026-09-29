using Kasa.Api.Data;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>
/// Eski (takip öncesi) kart listesi: masaüstü gider ve alış formları kart seçimini GET api/kredikartlari'den alır.
/// Eski kayıtlar API'den oluşturulamadığından veritabanına doğrudan yazılır. Eski kart, kredi ve kart ödemesi yazma
/// uçlarının masaüstünde çağıranı yoktur (istemci sarmalayıcıları kaldırıldı); sunucudaki davranışları (yeni kayıtta 409,
/// eski kredide yalnız ad düzeltmesi, geçmiş etkili kredinin silinememesi) Kasa.Api.Tests'te (CardLoanTrackingTests,
/// EskiKrediKorumaTests) sınanır.
/// </summary>
public class EskiKartKrediSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(IKasaApi.KrediKartlariAsync))]
    public async Task Eski_kart_listesi_turetilmis_borc_alanlariyla_okunur()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        Assert.Empty(await o.Kasa.KrediKartlariAsync());
        var kartId = 0;
        F.Veri(db =>
        {
            var kart = new KrediKartiEntity { Ad = "Eski kart", KesimTarihi = new(2000, 1, 5), SonOdemeTarihi = new(2000, 1, 25), Limit = 10000m, Borc = 250.5m };
            db.Add(kart); db.SaveChanges(); kartId = kart.Id;
            db.Islemler.Add(new() { Tarih = Bugun, Cari = "Eski kart harcaması", TutarTl = 100m, KanalId = 1, Kanal = "MEZAT", Tip = Kasa.Core.GiderTipi.KrediKarti, KrediKartiId = kart.Id });
            db.KartOdemeler.Add(new() { KrediKartiId = kart.Id, Tarih = Bugun, Tutar = 50.25m, Not = "Eski ödeme" });
            db.SaveChanges();
        });

        var kart = Assert.Single(await o.Kasa.KrediKartlariAsync());
        // Güncel borç = açılış + harcamalar − ödemeler (sunucuda türetilir).
        Assert.Equal((kartId, 250.5m, 250.5m, 100m, 50.25m, 300.25m), (kart.Id, kart.Borc, kart.AcilisBorc, kart.HarcamaToplam, kart.OdemeToplam, kart.GuncelBorc));
        Assert.Equal((false, true), (kart.YeniTakip, kart.Aktif)); // K3: eski kart yeni gidere seçilemez
    }
}
