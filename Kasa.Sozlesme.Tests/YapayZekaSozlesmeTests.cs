using System.Net;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>Yapay zekâ uçları (docs/specs/2026-11-25-yapay-zeka-onerileri.md) gerçek sunucuya karşı: durum, önizleme, soru, öneri ve ölçüm istemci türlerine
/// uyar. Model sahtedir; dış bağlantı kurulmaz.</summary>
public class YapayZekaSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(IEkstreAktarmaApi.YapayZekaDurumuAsync), nameof(IEkstreAktarmaApi.YapayZekaOnizlemeAsync), nameof(IEkstreAktarmaApi.YapayZekaSorAsync),
        nameof(IEkstreAktarmaApi.YapayZekaOlcAsync), nameof(IEkstreAktarmaApi.EkstreOnerilerAsync))]
    public async Task Yapay_zeka_uclari_istemci_turlerine_uyar()
    {
        var ct = TestContext.Current.CancellationToken;
        var o = await Editor(Fabrika(new() { ["YapayZeka:Etkin"] = "true", ["YapayZeka:Anahtar"] = "test-anahtari" }));
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        Task<EkstreBelgeDto> Yukle(params object?[][] satirlar) => o.Ekstre.EkstreYukleAsync(TestExcel.Xlsx(("H", [["Tarih", "Açıklama", "Tutar (TL)"], .. satirlar])), "banka.xlsx", "Banka", "Akbank", "Ana hesap", null, ct);
        var durum = await o.Ekstre.YapayZekaDurumuAsync();
        Assert.Equal((true, (string?)null, "claude-haiku-5-5", 0m, 2m), (durum.Acik, durum.KapaliNedeni, durum.Model, durum.AyHarcamasiUsd, durum.AylikSinirUsd));

        var belge = await Yukle([Bugun, "AHMET KARGO", -50m]);
        var onizleme = await o.Ekstre.YapayZekaOnizlemeAsync(belge.Id);
        Assert.Equal("1 | CIKIS | 0-100 | [1] KARGO", Assert.Single(onizleme.Satirlar));
        var sonuc = await o.Ekstre.YapayZekaSorAsync(belge.Id, new YapayZekaSorYaz(Guid.NewGuid()));
        Assert.Equal((1, 1, 0, 0.0003m, (string?)null), (sonuc.Sorulan, sonuc.Onerilen, sonuc.KalanSatir, sonuc.MaliyetUsd, sonuc.Not));
        var oneri = Assert.Single(await o.Ekstre.EkstreOnerilerAsync(belge.Id));
        Assert.Equal(("YapayZekaOnerisi", "Gider", "Genel", new EkstreYapayZekaOnerisi("Yuksek", "kargo", "AHMET kargo")), (oneri.Durum, oneri.IslemTuru, oneri.DagilimTuru, oneri.YapayZeka));

        Assert.Equal(HttpStatusCode.Conflict, (await Assert.ThrowsAsync<KasaApiException>(() => o.Ekstre.YapayZekaOlcAsync(new YapayZekaOlcumYaz(Guid.NewGuid(), "Banka")))).DurumKodu);
        string[] adlar = ["KARGO", "ELEKTRIK", "DOGALGAZ", "MARKET", "YAKIT", "REKLAM", "KIRTASIYE", "SIGORTA", "INTERNET", "NAKLIYE"];
        var gecmis = await Yukle([.. adlar.Select((a, i) => new object?[] { Bugun, $"{a} MASRAF", -20m - i })]);
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), gecmis.Surum, gecmis.Satirlar.Select(s => new EkstreSatirYaz(s.No, s.Tarih!.Value, s.Aciklama, s.Tutar!.Value, "Gider", "Genel", [])).ToList());
        var ekstreOnizleme = await o.Ekstre.EkstreOnizlemeAsync(gecmis.Id, istek);
        await o.Ekstre.EkstreKaydetAsync(gecmis.Id, istek with { OnizlemeOzeti = ekstreOnizleme.OnizlemeOzeti, TekrarOnay = true });
        var olcum = await o.Ekstre.YapayZekaOlcAsync(new YapayZekaOlcumYaz(Guid.NewGuid(), "Banka"));
        Assert.Equal(("Banka", 5, 1, 5, 1, 1, 1, 4), (olcum.Kaynak, olcum.Satir, olcum.TurDogru, olcum.DagilimSatiri, olcum.DagilimDogru, olcum.YuksekGuven, olcum.YuksekDogru, olcum.Onerisiz));
        durum = await o.Ekstre.YapayZekaDurumuAsync();
        Assert.Equal(olcum, Assert.Single(durum.Olcumler));
        Assert.Equal((0.0006m, 2), (durum.AyHarcamasiUsd, durum.AyCagri));
    }
}
