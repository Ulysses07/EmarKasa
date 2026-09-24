using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class RaporTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public RaporTests(KasaWebFactory factory) => _factory = factory;

    private record KanalHaftalikYanit(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir);
    private record HaftalikOzetYanit(Donem Donem, List<KanalHaftalikYanit> Kanallar, decimal ToplamGelen, decimal ToplamGiden, decimal KasaSonucu, decimal KasaDevir);

    private void Tohumla()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();

        // İki test aynı DB'yi paylaştığından sıra-bağımsızlık için tümünü temizle.
        db.Islemler.RemoveRange(db.Islemler);
        db.Gelenler.RemoveRange(db.Gelenler);
        db.Kanallar.RemoveRange(db.Kanallar);
        db.Kanallar.AddRange(
            new KanalEntity { Ad = "MEZAT", Sira = 0, AcilisDevri = 4_991_052m },
            new KanalEntity { Ad = "PERAKENDE", Sira = 1, AcilisDevri = 2_013_516m },
            new KanalEntity { Ad = "TOPTAN", Sira = 2, AcilisDevri = 619_647m });

        var ayar = db.Ayarlar.First();
        ayar.TakipBaslangic = new DateOnly(2026, 6, 29);
        ayar.KasaAcilisDevri = 2_907_053.21m;

        db.Gelenler.AddRange(
            new GelenEntity { DonemStart = new DateOnly(2026, 6, 29), Kanal = "MEZAT", TutarTl = 289_425m },
            new GelenEntity { DonemStart = new DateOnly(2026, 6, 29), Kanal = "PERAKENDE", TutarTl = 271_006m },
            new GelenEntity { DonemStart = new DateOnly(2026, 6, 29), Kanal = "TOPTAN", TutarTl = 207_000m });

        db.Islemler.AddRange(
            new IslemEntity { Tarih = new DateOnly(2026, 6, 29), Cari = "MEZAT-cari", TutarTl = 1_308_800m, Kanal = "MEZAT", Tip = GiderTipi.Cari },
            new IslemEntity { Tarih = new DateOnly(2026, 6, 29), Cari = "PER-cari", TutarTl = 1_221_374m, Kanal = "PERAKENDE", Tip = GiderTipi.Cari },
            new IslemEntity { Tarih = new DateOnly(2026, 6, 29), Cari = "TOP-cari", TutarTl = 360_000m, Kanal = "TOPTAN", Tip = GiderTipi.Cari },
            new IslemEntity { Tarih = new DateOnly(2026, 6, 30), Cari = "SGK/Vergi", TutarTl = 455_321m, Kanal = Kanallar.Ortak, Tip = GiderTipi.SabitGider });

        db.SaveChanges();
    }

    [Fact]
    public async Task Haftalik_rapor_haziran_excel_rakamlarini_uretir()
    {
        Tohumla();
        var client = await _factory.EditorClientAsync();

        var ozetler = await client.GetFromJsonAsync<List<HaftalikOzetYanit>>("/api/rapor/haftalik");
        var d = ozetler!.Single(o => o.Donem.Start == new DateOnly(2026, 6, 29));

        var mezat = d.Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal(-1_019_375m, mezat.Sonuc);
        Assert.Equal(3_971_677m, mezat.Devir);

        Assert.Equal(767_431m, d.ToplamGelen);
        Assert.Equal(3_345_495m, d.ToplamGiden);
        Assert.Equal(328_989.21m, d.KasaDevir);
    }

    [Fact]
    public async Task Panel_guncel_kasayi_ve_kanal_bakiyelerini_doner()
    {
        Tohumla();
        var client = await _factory.EditorClientAsync();

        var panel = await client.GetFromJsonAsync<PanelDto>("/api/rapor/panel");
        Assert.NotNull(panel);
        // 29-30 Haziran sonrası boş dönemler kasayı değiştirmez.
        Assert.Equal(328_989.21m, panel!.GuncelKasa);
        Assert.Equal(3_971_677m, panel.Kanallar.Single(k => k.Kanal == "MEZAT").Bakiye);
    }
    [Fact]
    public async Task Aylik_rapor_onceki_ayin_kartsiz_KKsini_dusurur()
    {
        // Aylık rapor yalnız o ayın dönemleriyle hesaplanır; önceki ayın (takip içindeki)
        // kartsız K.K'sı yine de bu aya yazılmalı.
        Tohumla();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Islemler.Add(new IslemEntity { Tarih = new DateOnly(2026, 6, 30), Cari = "K.K", TutarTl = 1_000m, Kanal = "MEZAT", Tip = GiderTipi.KrediKarti });
            db.SaveChanges();
        }
        var client = await _factory.EditorClientAsync();

        var rapor = await client.GetFromJsonAsync<AylikRapor>("/api/rapor/aylik?yil=2026&ay=7");
        Assert.Equal(1_000m, rapor!.Kanallar.Single(k => k.Kanal == "MEZAT").KrediKarti);
    }
}
