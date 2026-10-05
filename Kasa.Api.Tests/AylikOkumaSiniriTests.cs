using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class AylikOkumaSiniriTests
{
    [Fact]
    public async Task Aylik_rapor_yalniz_aya_dokunan_kaynak_giderleri_okur_ama_onceki_ay_kartini_ve_alis_odemelerini_korur()
    {
        await using var f = new KartHesapMaliyetiTests.SayacliFabrika();
        using var c = await f.EditorClientAsync();
        var ct = TestContext.Current.CancellationToken;
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(2026, 1, 1), kasaAcilisDevri = 0m }, cancellationToken: ct)).EnsureSuccessStatusCode();
        string kanalIkiAdi;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            kanalIkiAdi = db.Kanallar.Single(k => k.Id == 2).Ad;
            db.Islemler.AddRange(
                Gider(new(2026, 1, 12), 100m, GiderTipi.Cari),
                Gider(new(2026, 4, 12), 30m, GiderTipi.KrediKarti),
                Gider(new(2026, 5, 12), 20m, GiderTipi.KrediKarti),
                Gider(new(2026, 6, 12), 40m, GiderTipi.Cari),
                Gider(new(2026, 7, 12), 1000m, GiderTipi.Cari));
            var alis = new AlisEntity
            {
                Tarih = new(2026, 5, 1),
                Tedarikci = "Tedarikçi",
                Durum = AlisDurumlari.Onaylandi,
                Kalemler = [new AlisKalemEntity
                {
                    Aciklama = "Mal", Tutar = 0.02m,
                    Dagilimlar = [new() { KanalId = 1, Tutar = 0.01m }, new() { KanalId = 2, Tutar = 0.01m }],
                }],
                Odemeler =
                [
                    new AlisOdemeEntity { Islem = Gider(new(2026, 5, 10), 0.01m, GiderTipi.Cari), IstekId = Guid.NewGuid(), IstekOzeti = "mayıs" },
                    new AlisOdemeEntity { Islem = Gider(new(2026, 6, 10), 0.01m, GiderTipi.Cari), IstekId = Guid.NewGuid(), IstekOzeti = "haziran" },
                ],
            };
            db.Alislar.Add(alis);
            db.SaveChanges();
        }

        f.Sayac.Sifirla();
        f.Sayac.Etkin = true;
        var rapor = (await c.GetFromJsonAsync<AylikRapor>("/api/rapor/aylik?yil=2026&ay=6", cancellationToken: ct))!;
        f.Sayac.Etkin = false;

        var mezat = Assert.Single(rapor.Kanallar, k => k.Kanal == "MEZAT");
        var ikinciKanal = Assert.Single(rapor.Kanallar, k => k.Kanal == kanalIkiAdi);
        Assert.Equal(40m, mezat.CariGiden);
        Assert.Equal(20m, mezat.KrediKarti);
        Assert.Equal(0.01m, ikinciKanal.CariGiden);
        // Kaynak gider materyalizasyonu (KanalKaydi Include) tek sorgudur; başka bir tam geçmiş
        // sorgusu varken bir filtreli sorgu bulunması tek başına yeterli güvence olmaz.
        var kaynakSorgulari = f.Sayac.Komutlar.Where(sql => sql.StartsWith("SELECT \"i\".\"Id\", \"i\".\"Cari\"", StringComparison.Ordinal)
            && sql.Contains("FROM \"Islemler\" AS \"i\"", StringComparison.Ordinal)).ToList();
        var kaynakSorgusu = Assert.Single(kaynakSorgulari);
        Assert.Contains("WHERE", kaynakSorgusu, StringComparison.Ordinal);
        Assert.Equal(2, kaynakSorgusu.Split("\"i\".\"Tarih\" >=", StringSplitOptions.None).Length - 1);
        Assert.Contains("\"i\".\"Tarih\" <=", kaynakSorgusu, StringComparison.Ordinal);
        Assert.Matches("\\\"i\\\"\\.\\\"Tarih\\\" <(?![=])", kaynakSorgusu);
        Assert.Contains("\"i\".\"Tip\" =", kaynakSorgusu, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Aylik_rapor_ay_sonundaki_gideri_sonraki_aya_tasimaz()
    {
        await using var f = KasaWebFactory.Sabit(new DateOnly(2026, 9, 25));
        using var c = await f.EditorClientAsync();
        var ct = TestContext.Current.CancellationToken;
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(2026, 6, 1), kasaAcilisDevri = 0m }, cancellationToken: ct)).EnsureSuccessStatusCode();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Islemler.AddRange(
                Gider(new(2026, 6, 30), 3m, GiderTipi.Cari),
                Gider(new(2026, 7, 1), 5m, GiderTipi.Cari));
            db.SaveChanges();
        }

        var haziran = (await c.GetFromJsonAsync<AylikRapor>("/api/rapor/aylik?yil=2026&ay=6", cancellationToken: ct))!;
        var temmuz = (await c.GetFromJsonAsync<AylikRapor>("/api/rapor/aylik?yil=2026&ay=7", cancellationToken: ct))!;
        Assert.Equal(3m, Assert.Single(haziran.Kanallar, k => k.Kanal == "MEZAT").CariGiden);
        Assert.Equal(5m, Assert.Single(temmuz.Kanallar, k => k.Kanal == "MEZAT").CariGiden);
    }

    private static IslemEntity Gider(DateOnly tarih, decimal tutar, GiderTipi tip) => new()
    {
        Tarih = tarih,
        Cari = "Gider",
        TutarTl = tutar,
        Kanal = "MEZAT",
        KanalId = 1,
        Tip = tip,
    };
}
