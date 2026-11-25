using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>ERP12 cari hareketleri (docs/specs/2026-11-25-erp12-aktarimi.md) gerçek sunucuya karşı: yükleme, karşılık önerisi ve adayı, eşleşen
/// kaydın cari adı, cari listesi ile hareketleri ve belgenin silinmesi istemci türlerine uyar.</summary>
public class ErpSozlesmeTests : SozlesmeTemeli
{
    private static byte[] Dosya(params object?[][] satirlar) =>
        TestExcel.Xlsx(("Cari", [["Tarih", "Cari Kodu", "Cari Ünvanı", "Evrak Tipi", "Evrak No", "Açıklama", "Borç", "Alacak"], .. satirlar]));

    [Fact]
    [SozlesmeKapsami(nameof(IErpApi.ErpCarilerAsync), nameof(IErpApi.ErpCariHareketleriAsync), nameof(IEkstreAktarmaApi.ErpBelgeSilAsync), nameof(IEkstreAktarmaApi.EkstreYukleAsync),
        nameof(IEkstreAktarmaApi.EkstreOnerilerAsync), nameof(IEkstreAktarmaApi.EkstreEslesmeAdaylariAsync), nameof(IKasaApi.IslemlerAsync))]
    public async Task Erp_aktarimi_eslesme_ve_cariler_istemci_turlerine_uyar()
    {
        var ct = TestContext.Current.CancellationToken;
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        Assert.Empty(await o.Erp.ErpCarilerAsync());
        var gider = await o.Kasa.IslemOlusturAsync(new IslemYaz(Bugun, "Deniz Nakliyat", 500m, "MEZAT", GiderTipi.Cari, null));
        var belge = await o.Ekstre.EkstreYukleAsync(Dosya([Bugun, "320.01", "DENİZ NAKLİYAT LTD", "Havale Ödeme", "O1", "Kasım", 500m, null], [Bugun, "320.01", "DENİZ NAKLİYAT LTD", "Alış Faturası", "F1", null, null, 700m]),
            "erp.xlsx", "Erp", "", "", null, ct);
        var satir = Assert.Single(belge.Satirlar);
        Assert.Equal(("DENİZ NAKLİYAT LTD", "Havale Ödeme · Belge O1 · Kasım", "Cikis"), (satir.Aciklama, satir.EkBilgi, satir.Yon));

        var oneri = Assert.Single(await o.Ekstre.EkstreOnerilerAsync(belge.Id));
        Assert.Equal(("ErpEslesme", "Eslestir"), (oneri.Durum, oneri.IslemTuru));
        var aday = Assert.Single(oneri.Adaylar!);
        Assert.Equal(("Gider", gider.Id, 500m), (aday.Tur, aday.Id, aday.Tutar));
        Assert.Equal(aday, Assert.Single(await o.Ekstre.EkstreEslesmeAdaylariAsync(belge.Id, new EkstreEslesmeAdayiSorgu(satir.Tarih!.Value, 500m, satir.No))));

        var istek = new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [new EkstreSatirYaz(satir.No, satir.Tarih!.Value, satir.Aciklama, 500m, "Eslestir", "Eslesme", [], EslesenKayitTuru: aday.Tur, EslesenKayitId: aday.Id)]);
        var onizleme = await o.Ekstre.EkstreOnizlemeAsync(belge.Id, istek);
        Assert.Equal(0m, onizleme.KasaEtkisi);
        belge = await o.Ekstre.EkstreKaydetAsync(belge.Id, istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti });
        Assert.Single(belge.Kayitlar);
        Assert.Equal("DENİZ NAKLİYAT LTD", Assert.Single(await o.Kasa.IslemlerAsync()).ErpCari);

        var cari = Assert.Single(await o.Erp.ErpCarilerAsync("deniz"));
        Assert.Equal(new ErpCariDto("K:320 01", "320.01", "DENİZ NAKLİYAT LTD", 500m, 700m, -200m, 2, 0, Bugun) with { Anahtar = cari.Anahtar }, cari);
        var hareketler = await o.Erp.ErpCariHareketleriAsync(cari.Anahtar);
        Assert.Equal([("ErpEslesti", "Gider", (int?)gider.Id), ("ErpKasaDisi", null, null)], hareketler.OrderByDescending(h => h.Borc).Select(h => (h.Durum, h.EslesmeTuru, h.EslesmeId)));

        var yanlis = await o.Ekstre.EkstreYukleAsync(Dosya([Bugun, "320.02", "YANLIŞ", "Nakit Ödeme", "O9", null, 10m, null]), "erp2.xlsx", "Erp", "", "", null, ct);
        Assert.Equal(("ErpNakit", "Gider"), (Assert.Single(await o.Ekstre.EkstreOnerilerAsync(yanlis.Id)) is var n ? n.Durum : "", n.IslemTuru));
        await o.Ekstre.ErpBelgeSilAsync(yanlis.Id);
        Assert.Single(await o.Erp.ErpCarilerAsync());
        Assert.Single(await o.Ekstre.EkstreBelgelerAsync());
    }

    /// <summary>Banka ekstresinden yazılan gelire eşleşen carinin adı kaydında döner (EkstreKayitDto.ErpCari).</summary>
    [Fact]
    [SozlesmeKapsami(nameof(IEkstreAktarmaApi.EkstreBelgeAsync), nameof(IEkstreAktarmaApi.EkstreKaydetAsync))]
    public async Task Banka_girisine_eslesen_cari_kayitta_gorunur()
    {
        var ct = TestContext.Current.CancellationToken;
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        async Task<EkstreBelgeDto> Kaydet(EkstreBelgeDto d, EkstreSatirYaz s)
        {
            var istek = new EkstreKaydetYaz(Guid.NewGuid(), d.Surum, [s]);
            var onizleme = await o.Ekstre.EkstreOnizlemeAsync(d.Id, istek);
            return await o.Ekstre.EkstreKaydetAsync(d.Id, istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = onizleme.TekrarOnayGerekli });
        }
        var banka = await o.Ekstre.EkstreYukleAsync(TestExcel.Xlsx(("H", [["Tarih", "Açıklama", "Tutar (TL)"], [Bugun, "GELEN EFT AHMET YILMAZ", 250m]])), "banka.xlsx", "Banka", "Akbank", "Ana hesap", null, ct);
        banka = await Kaydet(banka, new(1, Bugun, "GELEN EFT AHMET YILMAZ", 250m, "Gelir", "Genel", []));
        var erp = await o.Ekstre.EkstreYukleAsync(Dosya([Bugun, null, "AHMET YILMAZ", "Gelen Havale", "T1", null, null, 250m]), "erp.xlsx", "Erp", "", "", null, ct);
        var aday = Assert.Single(Assert.Single(await o.Ekstre.EkstreOnerilerAsync(erp.Id)).Adaylar!);
        Assert.Equal(("EkstreGelir", banka.Kayitlar[0].Id), (aday.Tur, aday.Id));
        await Kaydet(erp, new(1, Bugun, "AHMET YILMAZ", 250m, "Eslestir", "Eslesme", [], EslesenKayitTuru: aday.Tur, EslesenKayitId: aday.Id));
        Assert.Equal("AHMET YILMAZ", Assert.Single((await o.Ekstre.EkstreBelgeAsync(banka.Id)).Kayitlar).ErpCari);
    }
}
