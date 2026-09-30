using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>Ekstre satırının mevcut kayıtla eşleşmesi ve ters sırada alış ödemesinin mevcut kart harcamasına bağlanması
/// (gap-coklu-giris-cift-sayim-mutabakat-1) gerçek sunucuya karşı: aday türü, 'Eslestir' satırı ve kaydın eşleşme alanları,
/// bağlanabilir kart harcaması türü ve ödemenin MevcutKartHarcamaId alanı istemci türleriyle birebir uyar.</summary>
public class EkstreEslesmeSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(IEkstreAktarmaApi.EkstreEslesmeAdaylariAsync), nameof(IEkstreAktarmaApi.EkstreYukleAsync), nameof(IEkstreAktarmaApi.EkstreOnizlemeAsync),
        nameof(IEkstreAktarmaApi.EkstreKaydetAsync), nameof(IKasaApi.IslemOlusturAsync))]
    public async Task Eslesme_adayi_ve_eslestir_satiri_istemci_turlerine_uyar()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var belge = await o.Ekstre.EkstreYukleAsync("%PDF-1.7 eslesme"u8.ToArray(), "ekstre.pdf", "Banka", "Akbank", "Ana hesap", null, TestContext.Current.CancellationToken);
        var okunan = Assert.Single(belge.Satirlar);
        var tutar = Math.Abs(okunan.Tutar!.Value);
        var gider = await o.Kasa.IslemOlusturAsync(new IslemYaz(okunan.Tarih!.Value.AddDays(-2), "Kargo", tutar, "MEZAT", GiderTipi.Cari, null));

        var aday = Assert.Single(await o.Ekstre.EkstreEslesmeAdaylariAsync(belge.Id, new EkstreEslesmeAdayiSorgu(okunan.Tarih!.Value, tutar)));
        Assert.Equal(("Gider", gider.Id, tutar, "MEZAT"), (aday.Tur, aday.Id, aday.Tutar, aday.KanalEtiketi));
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [new EkstreSatirYaz(okunan.No, okunan.Tarih!.Value, okunan.Aciklama, tutar, "Eslestir", "Eslesme", [],
            EslesenKayitTuru: aday.Tur, EslesenKayitId: aday.Id)]);
        var onizleme = await o.Ekstre.EkstreOnizlemeAsync(belge.Id, istek);
        Assert.Equal(0m, onizleme.KasaEtkisi);
        belge = await o.Ekstre.EkstreKaydetAsync(belge.Id, istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = onizleme.TekrarOnayGerekli });
        var kayit = Assert.Single(belge.Kayitlar);
        Assert.Equal(("Eslestir", "Gider", (int?)gider.Id, "Eslesti", (int?)null), (kayit.IslemTuru, kayit.EslesmeTuru, kayit.EslesmeId, kayit.EslesmeDurumu, kayit.IslemId));
    }

    [Fact]
    [SozlesmeKapsami(nameof(IAlisApi.BaglanabilirKartHarcamalariAsync), nameof(IFinansTakipApi.TakipKartKaydetAsync), nameof(IFinansTakipApi.TakipHarcamaKaydetAsync),
        nameof(IAlisApi.AlisOlusturAsync), nameof(IAlisApi.AlisOdemeKaydetAsync))]
    public async Task Baglanabilir_kart_harcamasi_ve_mevcut_kart_harcamasiyla_odeme_istemci_turlerine_uyar()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var kart = await o.Takip.TakipKartKaydetAsync(null, new KartTakipYaz(Guid.NewGuid(), 0, "Eşleşme kartı", 10000m, 5, 25, Baslangic, 0m, []));
        kart = await o.Takip.TakipHarcamaKaydetAsync(kart.Id, new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, Bugun, "Ekstreden harcama", 750m, 1, null, [new(1, 750m)]));
        var harcama = Assert.Single(await o.Alis.BaglanabilirKartHarcamalariAsync(kart.Id, 750m));
        Assert.Equal((kart.Harcamalar.Single().Id, kart.Id, Bugun, 750m, (int?)null), (harcama.Id, harcama.KrediKartiId, harcama.Tarih, harcama.Tutar, harcama.EkstreKayitId));

        var alis = await o.Alis.AlisOlusturAsync(new AlisYaz(0, Bugun, "Kart tedarik", null, [new AlisKalemYaz("Mal", 750m, [new(1, 750m)])]));
        alis = await o.Alis.AlisOdemeKaydetAsync(alis.Id, new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), harcama.Tarih, harcama.Tutar, kart.Id, MevcutKartHarcamaId: harcama.Id));
        Assert.Equal((0m, (int?)kart.Id), (alis.Kalan, Assert.Single(alis.Odemeler).KrediKartiId));
        Assert.Empty(await o.Alis.BaglanabilirKartHarcamalariAsync(kart.Id));
    }
}
