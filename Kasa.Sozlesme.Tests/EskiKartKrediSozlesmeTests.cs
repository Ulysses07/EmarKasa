using System.Net;
using Kasa.Api.Data;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>
/// Eski (takip öncesi) kart ve kredi uçları. Yeni kayıt uçları sunucuda koşulsuz 409 döner: istemcinin
/// KrediKartiOlusturAsync ve KrediEkleAsync metotları çalışan bir özellik değildir. Kasa.ApiClient.Tests/MutasyonTests
/// bu iki metot için 201 kurgulayıp başarı bekliyor; o kurgu gerçek sunucuyla çelişir (tests-8). Eski kayıtlar API'den
/// oluşturulamadığından veritabanına doğrudan yazılır.
/// </summary>
public class EskiKartKrediSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(IKasaApi.KrediKartiOlusturAsync), nameof(IKasaApi.KrediEkleAsync))]
    public async Task Eski_kart_ve_kredi_olusturma_gercek_sunucuda_409_ve_okunur_iletiyle_reddedilir()
    {
        var o = await Editor();
        var kart = await Assert.ThrowsAsync<KasaApiException>(() => o.Kasa.KrediKartiOlusturAsync(new KrediKartiYaz("Yeni", new(2000, 1, 5), new(2000, 1, 25), 1000m, 0m)));
        Assert.Equal(HttpStatusCode.Conflict, kart.DurumKodu); Assert.Contains("Kredi Kartları ekranından", kart.Message);
        var kredi = await Assert.ThrowsAsync<KasaApiException>(() => o.Kasa.KrediEkleAsync(new KrediDto(0, "Yeni", 1000m, Bugun, 10, 100m, 5, "MEZAT")));
        Assert.Equal(HttpStatusCode.Conflict, kredi.DurumKodu); Assert.Contains("Krediler ekranından", kredi.Message);
        Assert.Empty(await o.Kasa.KrediKartlariAsync());
    }

    [Fact]
    [SozlesmeKapsami(nameof(IKasaApi.KrediKartlariAsync), nameof(IKasaApi.KrediKartiGuncelleAsync), nameof(IKasaApi.KrediKartiSilAsync),
        nameof(IKasaApi.KartOdemelerAsync), nameof(IKasaApi.KartOdemeKaydetAsync), nameof(IKasaApi.KartOdemeSilAsync),
        nameof(IKasaApi.KredilerAsync), nameof(IKasaApi.KrediGuncelleAsync), nameof(IKasaApi.KrediSilAsync))]
    public async Task Eski_kart_odeme_ve_kredi_kayitlari_okunur_duzeltilir_silinir()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        int kartId = 0, krediId = 0;
        F.Veri(db =>
        {
            var kart = new KrediKartiEntity { Ad = "Eski kart", KesimTarihi = new(2000, 1, 5), SonOdemeTarihi = new(2000, 1, 25), Limit = 10000m, Borc = 250.5m };
            var kredi = new KrediEntity { Ad = "Eski kredi", CekilenTutar = 12000m, CekimTarihi = Baslangic, TaksitSayisi = 12, AylikOdeme = 1000m, OdemeGunu = 10, Kanal = "MEZAT", KanalId = 1 };
            db.AddRange(kart, kredi); db.SaveChanges(); kartId = kart.Id; krediId = kredi.Id;
            db.Islemler.Add(new() { Tarih = Bugun, Cari = "Eski kart harcaması", TutarTl = 100m, KanalId = 1, Kanal = "MEZAT", Tip = Kasa.Core.GiderTipi.KrediKarti, KrediKartiId = kart.Id });
            db.SaveChanges();
        });

        var kart = Assert.Single(await o.Kasa.KrediKartlariAsync());
        Assert.Equal((kartId, 250.5m, 250.5m, 100m, 350.5m), (kart.Id, kart.Borc, kart.AcilisBorc, kart.HarcamaToplam, kart.GuncelBorc));
        Assert.Equal((false, true), (kart.YeniTakip, kart.Aktif)); // K3: eski kart yeni gidere seçilemez
        var odeme = await o.Kasa.KartOdemeKaydetAsync(new KartOdemeYaz(kartId, Bugun, 50.25m, "Eski ödeme"));
        Assert.Equal(HttpStatusCode.Created, o.SonYanit.Durum); Assert.Equal((kartId, Bugun, 50.25m), (odeme.KrediKartiId, odeme.Tarih, odeme.Tutar));
        Assert.Equal(odeme.Id, Assert.Single(await o.Kasa.KartOdemelerAsync(kartId)).Id);
        Assert.Equal(300.25m, Assert.Single(await o.Kasa.KrediKartlariAsync()).GuncelBorc);
        await o.Kasa.KartOdemeSilAsync(odeme.Id);
        Assert.Equal(HttpStatusCode.NoContent, o.SonYanit.Durum);
        Assert.Empty(await o.Kasa.KartOdemelerAsync(kartId));

        var duzelen = await o.Kasa.KrediKartiGuncelleAsync(kartId, new KrediKartiYaz("Eski kart 2", new(2000, 1, 6), new(2000, 1, 26), 12000m, 250.5m));
        Assert.Equal(("Eski kart 2", 12000m), (duzelen.Ad, duzelen.Limit));
        // Düzeltme yanıtı kayıt varlığını döner; türetilmiş borç alanları yalnız listede dolu.
        Assert.Equal(350.5m, Assert.Single(await o.Kasa.KrediKartlariAsync()).GuncelBorc);

        var kredi = Assert.Single(await o.Kasa.KredilerAsync());
        Assert.Equal((krediId, "MEZAT", 12000m), (kredi.Id, kredi.Kanal, kredi.CekilenTutar));
        // gT6: çekimi geçmişte olan eski kredinin yalnız adı düzeltilir; mali alan düzeltmesi ve silme okunur iletiyle 409.
        var mali = await Assert.ThrowsAsync<KasaApiException>(() => o.Kasa.KrediGuncelleAsync(krediId, kredi with { Ad = "Eski kredi 2", AylikOdeme = 1100m }));
        Assert.Equal(HttpStatusCode.Conflict, mali.DurumKodu); Assert.Contains("yalnız ad düzeltilebilir", mali.Message);
        await o.Kasa.KrediGuncelleAsync(krediId, kredi with { Ad = "Eski kredi 2" });
        Assert.Equal(("Eski kredi 2", 1000m), (Assert.Single(await o.Kasa.KredilerAsync()).Ad, Assert.Single(await o.Kasa.KredilerAsync()).AylikOdeme));
        var sil = await Assert.ThrowsAsync<KasaApiException>(() => o.Kasa.KrediSilAsync(krediId));
        Assert.Equal(HttpStatusCode.Conflict, sil.DurumKodu); Assert.Contains("eski kredi silinemez", sil.Message);
        // Çekimi ileride olan (geçmiş etkisi olmayan) eski kredi silinir.
        var ileri = 0;
        F.Veri(db =>
        {
            var k = new KrediEntity { Ad = "İleri kredi", CekilenTutar = 5000m, CekimTarihi = Bugun.AddDays(10), TaksitSayisi = 5, AylikOdeme = 1000m, OdemeGunu = 10, Kanal = "MEZAT", KanalId = 1 };
            db.Add(k); db.SaveChanges(); ileri = k.Id;
        });
        await o.Kasa.KrediSilAsync(ileri);
        Assert.Equal(HttpStatusCode.NoContent, o.SonYanit.Durum);
        Assert.Equal(krediId, Assert.Single(await o.Kasa.KredilerAsync()).Id);
        await o.Kasa.KrediKartiSilAsync(kartId);
        Assert.Equal(HttpStatusCode.NoContent, o.SonYanit.Durum);
        Assert.Empty(await o.Kasa.KrediKartlariAsync());
    }
}
