using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.Sozlesme.Tests;

/// <summary>Çek ve senet (docs/specs/2026-10-01-cekler.md): ekleme, düzeltme, liste ve süzgeçler, hareket, geri alma, silme ve panel
/// özeti; reddedilen geçişin Türkçe iletisi istemciye ulaşır.</summary>
public class CekSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(ICekApi.CekKaydetAsync), nameof(ICekApi.CeklerAsync), nameof(ICekApi.CekAsync), nameof(ICekApi.CekHareketEkleAsync),
        nameof(ICekApi.CekHareketGeriAlAsync), nameof(ICekApi.CekSilAsync), nameof(ICekApi.CekOzetAsync))]
    public async Task Cek_yasam_dongusu_istemci_turlerine_birebir_uyar()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var yaz = new CekYaz(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Alinan, "12345", "Ziraat", "Ahmet Yılmaz", 50_000m, Bugun.AddDays(10), null, false, null, "İlk not");
        var cek = await o.Cek.CekKaydetAsync(null, yaz);
        Assert.Equal((CekDurumlari.Portfoyde, 50_000m, CekKonumlari.Elde, 1), (cek.Durum, cek.Kalan, cek.Konum, cek.Surum));
        cek = await o.Cek.CekKaydetAsync(cek.Id, yaz with { IstekId = Guid.NewGuid(), Surum = cek.Surum, Konum = CekKonumlari.BankadaTahsilde });
        Assert.Equal(CekKonumlari.BankadaTahsilde, cek.Konum);

        cek = await o.Cek.CekHareketEkleAsync(cek.Id, new CekHareketYaz(Guid.NewGuid(), cek.Surum, CekHareketTurleri.Kirdirma, Bugun, 50_000m, 48_750.25m, "MEZAT", "Faktoring A.Ş."));
        var kirdirma = Assert.Single(cek.Hareketler);
        Assert.Equal((CekDurumlari.Kirdirildi, 48_750.25m, "MEZAT", "Faktoring A.Ş."), (cek.Durum, kirdirma.NetTutar, kirdirma.Kanal, kirdirma.Karsi));
        var hata = await Assert.ThrowsAsync<KasaApiException>(() => o.Cek.CekHareketEkleAsync(cek.Id,
            new CekHareketYaz(Guid.NewGuid(), cek.Surum, CekHareketTurleri.Tahsilat, Bugun, 1m, null, "MEZAT", null)));
        Assert.Equal("Bu kayıt kırdırıldı; şu an yalnız şu hareketler girilebilir: Dönüş.", hata.Message);

        Assert.Equal(cek.Id, Assert.Single(await o.Cek.CeklerAsync(CekYonleri.Alinan, CekSuzgecleri.Kapanan, "ahmet", Bugun, Bugun.AddDays(30))).Id);
        Assert.Empty(await o.Cek.CeklerAsync(durum: CekSuzgecleri.Portfoyde));
        var ozet = await o.Cek.CekOzetAsync();
        Assert.Equal((Bugun, 0), (ozet.Tarih, ozet.PortfoydekiAlinan.Adet));

        cek = await o.Cek.CekHareketGeriAlAsync(cek.Id, new CekSilYaz(Guid.NewGuid(), cek.Surum));
        Assert.Equal(CekDurumlari.Portfoyde, (await o.Cek.CekAsync(cek.Id)).Durum);
        var verilen = await o.Cek.CekKaydetAsync(null, new CekYaz(Guid.NewGuid(), 0, CekTurleri.Senet, CekYonleri.Verilen, "S-1", null, "Mehmet Ticaret", 7_000m, Bugun.AddDays(5),
            KanalEtiketleri.Ortak, false, null, null));
        Assert.Equal((KanalEtiketleri.Ortak, (int?)null), (verilen.Kanal, verilen.KanalId));
        await o.Cek.CekSilAsync(verilen.Id, new CekSilYaz(Guid.NewGuid(), verilen.Surum));
        Assert.Equal(cek.Id, Assert.Single(await o.Cek.CeklerAsync()).Id);
    }
}
