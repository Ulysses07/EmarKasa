using System.Net;
using Kasa.Api.Data;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>Bildirimler (liste, okundu, ayarlar, push anahtarı, cihazlar) ve ekstre aktarma (yükleme, önizleme, kayıt,
/// kaynak belge, sayfalama, PDF indirme, satır iptali).</summary>
public class BildirimVeEkstreSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(IBildirimApi.BildirimlerAsync), nameof(IBildirimApi.BildirimOkunduAsync), nameof(IBildirimApi.BildirimAyarlariAsync),
        nameof(IBildirimApi.BildirimAyarKaydetAsync), nameof(IBildirimApi.BildirimAnahtariAsync), nameof(IBildirimApi.BildirimCihazlariAsync),
        nameof(IBildirimApi.BildirimCihaziKaldirAsync))]
    public async Task Bildirim_uclari_istemci_turlerine_birebir_uyar()
    {
        var o = await Editor();
        int bildirimId = 0, cihazId = 0;
        F.Veri(db =>
        {
            // Teslim edilmiş bildirim, kaynağı artık üretmese de listede kalır.
            var bildirim = new BildirimEntity { OlayAnahtari = "sozlesme-1", Baslik = "Son ödeme", Mesaj = "Kart ödemesi yaklaşıyor", Tarih = Bugun, Hedef = "/#cards", Tur = "SonOdeme", KaynakId = 7 };
            var cihaz = new PushAbonelikEntity
            {
                Endpoint = "https://fcm.googleapis.com/fcm/send/sozlesme",
                P256dh = "p",
                Auth = "a",
                CihazId = Guid.NewGuid().ToString("D"),
                CihazAdi = "Kasa masası",
                OturumDamgasi = "damga",
                Olusturuldu = 1_790_000_000,
                SonBasarili = 1_790_000_100
            };
            db.AddRange(bildirim, cihaz);
            db.SaveChanges();
            db.Add(new BildirimTeslimEntity { BildirimId = bildirim.Id, AbonelikId = cihaz.Id, Gonderildi = 1_790_000_100 });
            db.SaveChanges();
            bildirimId = bildirim.Id;
            cihazId = cihaz.Id;
        });
        var bildirim = Assert.Single(await o.Bildirim.BildirimlerAsync(), b => b.Id == bildirimId);
        Assert.Equal(("Son ödeme", Bugun, false, "SonOdeme", 7), (bildirim.Baslik, bildirim.Tarih, bildirim.Okundu, bildirim.Tur, bildirim.KaynakId));
        await o.Bildirim.BildirimOkunduAsync(bildirimId);
        Assert.Equal(HttpStatusCode.NoContent, o.SonYanit.Durum);
        Assert.True((await o.Bildirim.BildirimlerAsync()).Single(b => b.Id == bildirimId).Okundu);

        var ayar = await o.Bildirim.BildirimAyarlariAsync();
        Assert.Equal("Europe/Istanbul", ayar.SaatDilimi);
        ayar = await o.Bildirim.BildirimAyarKaydetAsync(new BildirimAyarYaz(true, 8, 30, ayar.Surum));
        Assert.Equal((true, 8, 30), (ayar.Etkin, ayar.Saat, ayar.Dakika));
        var anahtar = await o.Bildirim.BildirimAnahtariAsync();
        Assert.True(anahtar.Etkin);
        Assert.False(string.IsNullOrEmpty(anahtar.PublicKey));

        var cihaz = Assert.Single(await o.Bildirim.BildirimCihazlariAsync());
        Assert.Equal((cihazId, "Kasa masası", true), (cihaz.Id, cihaz.CihazAdi, cihaz.Etkin));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_790_000_100), cihaz.SonBasarili);
        await o.Bildirim.BildirimCihaziKaldirAsync(cihazId);
        Assert.False(Assert.Single(await o.Bildirim.BildirimCihazlariAsync()).Etkin);
    }

    /// <summary>Banka seçenekleri tek kaynaktan (EkstreAktarmaEndpoints.Bankalar) gelir: listedeki her kod yüklemede kabul edilir,
    /// liste dışı kod iletili 400 alır. Web ve masaüstü kendi kopyalarını tutmaz.</summary>
    [Fact]
    [SozlesmeKapsami(nameof(IEkstreAktarmaApi.EkstreBankalarAsync), nameof(IEkstreAktarmaApi.EkstreYukleAsync))]
    public async Task Banka_listesi_tek_kaynaktan_gelir_yukleme_yalniz_listedeki_kodu_kabul_eder()
    {
        var ct = TestContext.Current.CancellationToken;
        var o = await Editor();
        var bankalar = await o.Ekstre.EkstreBankalarAsync();
        Assert.Equal(Kasa.Api.EkstreAktarmaEndpoints.Bankalar.Select(b => (b.Kod, b.Ad)), bankalar.Select(b => (b.Kod, b.Ad)));
        Assert.Contains(bankalar, b => b is { Kod: "Isbank", Ad: "İş Bankası" });
        // Aynı PDF (özet) başka kaynak bilgisiyle ikinci kez yüklenemez: her banka kendi dosyasıyla.
        static byte[] Pdf(string ek) => System.Text.Encoding.UTF8.GetBytes("%PDF-1.7 sozlesme " + ek);
        foreach (var banka in bankalar)
            Assert.Equal(banka.Kod, (await o.Ekstre.EkstreYukleAsync(Pdf(banka.Kod), "ekstre.pdf", "Banka", banka.Kod, "Ana hesap", null, ct)).Banka);
        var hata = await Assert.ThrowsAsync<KasaApiException>(() => o.Ekstre.EkstreYukleAsync(Pdf("liste dışı"), "ekstre.pdf", "Banka", "Ziraat", "Ana hesap", null, ct));
        Assert.Equal(HttpStatusCode.BadRequest, hata.DurumKodu);
        Assert.Contains("banka", hata.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [SozlesmeKapsami(nameof(IEkstreAktarmaApi.EkstreKurallarAsync), nameof(IEkstreAktarmaApi.EkstreKuralEkleAsync),
        nameof(IEkstreAktarmaApi.EkstreKuralDuzenleAsync), nameof(IEkstreAktarmaApi.EkstreKuralSilAsync), nameof(IEkstreAktarmaApi.EkstreOnerilerAsync))]
    public async Task Ekstre_kural_istemcisi_gercek_sunucuda_onerir_mali_kayit_uretmez()
    {
        var o = await Editor();
        var yaz = new EkstreKuralYaz(Guid.NewGuid(), 0, "Masraf", "Banka", "Akbank", "KOMİSYON", "Cikis", "Gider", "Genel", []);
        var k = await o.Ekstre.EkstreKuralEkleAsync(yaz);
        Assert.Equal(("Masraf", "Genel", true), (k.Ad, k.DagilimTuru, k.Aktif));
        Assert.Equal(k.Id, Assert.Single(await o.Ekstre.EkstreKurallarAsync()).Id);
        var belge = await o.Ekstre.EkstreYukleAsync("%PDF-1.7 kurallar"u8.ToArray(), "ekstre.pdf", "Banka", "Akbank", "Ana", null, TestContext.Current.CancellationToken);
        var oneri = Assert.Single(await o.Ekstre.EkstreOnerilerAsync(belge.Id));
        Assert.Equal(("Oneri", "Gider", "Genel"), (oneri.Durum, oneri.IslemTuru, oneri.DagilimTuru));
        Assert.Empty((await o.Ekstre.EkstreBelgeAsync(belge.Id)).Kayitlar);
        k = await o.Ekstre.EkstreKuralDuzenleAsync(k.Id, yaz with { IstekId = Guid.NewGuid(), Surum = k.Surum, Aktif = false });
        Assert.False(k.Aktif);
        Assert.Equal("Yok", Assert.Single(await o.Ekstre.EkstreOnerilerAsync(belge.Id)).Durum);
        await o.Ekstre.EkstreKuralSilAsync(k.Id, k.Surum);
        Assert.Empty(await o.Ekstre.EkstreKurallarAsync());
    }

    [Fact]
    [SozlesmeKapsami(nameof(IEkstreAktarmaApi.EkstreYukleAsync), nameof(IEkstreAktarmaApi.EkstreOnizlemeAsync), nameof(IEkstreAktarmaApi.EkstreKaydetAsync),
        nameof(IEkstreAktarmaApi.EkstreKaynakBelgeAsync), nameof(IEkstreAktarmaApi.EkstreBelgelerAsync), nameof(IEkstreAktarmaApi.EkstreBelgeAsync),
        nameof(IEkstreAktarmaApi.EkstreDosyaAsync), nameof(IEkstreAktarmaApi.EkstreKayitIptalAsync))]
    public async Task Ekstre_aktarma_istemci_turlerine_birebir_uyar()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var belge = await o.Ekstre.EkstreYukleAsync("%PDF-1.7 sozlesme"u8.ToArray(), "ekstre.pdf", "Banka", "Akbank", "Ana hesap", null, TestContext.Current.CancellationToken);
        Assert.Equal(("Banka", "Akbank", "Ana hesap", "ekstre.pdf"), (belge.Kaynak, belge.Banka, belge.HesapAdi, belge.DosyaAdi));
        Assert.Empty(belge.Kayitlar);
        var okunan = Assert.Single(belge.Satirlar);
        Assert.Equal((Bugun, "TRY"), (okunan.Tarih, okunan.ParaBirimi));

        var istek = new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [new EkstreSatirYaz(okunan.No, okunan.Tarih!.Value, okunan.Aciklama, Math.Abs(okunan.Tutar!.Value), "Gider", "Genel", [])]);
        var onizleme = await o.Ekstre.EkstreOnizlemeAsync(belge.Id, istek);
        Assert.Equal(-10m, onizleme.KasaEtkisi);
        Assert.False(string.IsNullOrEmpty(onizleme.OnizlemeOzeti));
        belge = await o.Ekstre.EkstreKaydetAsync(belge.Id, istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = true });
        var kayit = Assert.Single(belge.Kayitlar);
        Assert.Equal(("Gider", 10m, false), (kayit.IslemTuru, kayit.Tutar, kayit.Iptal));
        Assert.NotNull(kayit.IslemId);

        Assert.Equal(belge.Id, (await o.Ekstre.EkstreKaynakBelgeAsync(kayit.Id)).Id);
        var ozet = Assert.Single(await o.Ekstre.EkstreBelgelerAsync());
        Assert.Equal((belge.Id, 1, 1), (ozet.Id, ozet.SatirSayisi, ozet.KayitSayisi));
        Assert.Empty(await o.Ekstre.EkstreBelgelerAsync(belge.Id));
        Assert.Equal(belge.Surum, (await o.Ekstre.EkstreBelgeAsync(belge.Id)).Surum);
        using var pdf = new MemoryStream();
        var dosya = await o.Ekstre.EkstreDosyaAsync(belge.Id, pdf, TestContext.Current.CancellationToken);
        Assert.Equal(("ekstre.pdf", "application/pdf"), (dosya.DosyaAdi, dosya.IcerikTuru));
        Assert.Equal("%PDF-1.7 sozlesme"u8.ToArray(), pdf.ToArray());

        belge = await o.Ekstre.EkstreKayitIptalAsync(belge.Id, kayit.Id, new EkstreIptalYaz(Guid.NewGuid(), "Yanlış satır"));
        Assert.True(Assert.Single(belge.Kayitlar).Iptal);
    }
}
