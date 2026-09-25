using System.Net.Http.Json;
using Kasa.Api;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Kredinin hesap motoruna türetilmiş kayıtlarla yansımasını uçtan doğrular:
/// çekim → genel kasa (kanala girmez), taksit → seçilen kanal/Ortak, gelecek taksit
/// güncel kasayı etkilemez, silinince etki kalkar. Kontrollü baseline: KasaAcilisDevri
/// 100000, iki aktif kanal (açılış 0), başka işlem/gelen yok.
/// </summary>
public class KrediMuhasebeTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public KrediMuhasebeTests(KasaWebFactory factory) => _factory = factory;

    private record KanalAylikYanit(
        string Kanal, decimal Gelen, decimal CariGiden, decimal SabitGider,
        decimal KrediKarti, decimal OrtakPay, decimal AySonucu);
    private record AylikYanit(int Yil, int Ay, List<KanalAylikYanit> Kanallar);

    private static readonly DateOnly Baslangic = new(2026, 6, 29);

    private void Tohumla(params KrediEntity[] krediler)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();

        db.Islemler.RemoveRange(db.Islemler);
        db.Gelenler.RemoveRange(db.Gelenler);
        db.Krediler.RemoveRange(db.Krediler);
        db.Kanallar.RemoveRange(db.Kanallar);
        db.Kanallar.AddRange(
            new KanalEntity { Ad = "MEZAT", Sira = 0, Aktif = true, AcilisDevri = 0m },
            new KanalEntity { Ad = "PERAKENDE", Sira = 1, Aktif = true, AcilisDevri = 0m });

        var ayar = db.Ayarlar.First();
        ayar.TakipBaslangic = Baslangic;
        ayar.KasaAcilisDevri = 100_000m;

        if (krediler.Length > 0) db.Krediler.AddRange(krediler);
        db.SaveChanges();
    }

    private async Task<PanelDto> PanelAsync()
    {
        var client = await _factory.EditorClientAsync();
        return (await client.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;
    }

    [Fact]
    public async Task Cekim_genel_kasaya_girer_kanala_girmez()
    {
        // Taksit tutarı 0 → yalnız çekim etkisi izole.
        Tohumla(new KrediEntity
        {
            Ad = "Ziraat", CekilenTutar = 5_000m, CekimTarihi = new DateOnly(2026, 7, 6),
            TaksitSayisi = 1, AylikOdeme = 0m, OdemeGunu = 15, Kanal = "MEZAT"
        });

        var panel = await PanelAsync();

        Assert.Equal(105_000m, panel.GuncelKasa);                       // çekim genel kasaya girdi
        Assert.Equal(0m, panel.Kanallar.Single(k => k.Kanal == "MEZAT").Bakiye);      // kanala girmedi
        Assert.Equal(0m, panel.Kanallar.Single(k => k.Kanal == "PERAKENDE").Bakiye);
    }

    [Fact]
    public async Task Taksit_secili_kanaldan_ve_kasadan_duser()
    {
        // Çekim 0 → yalnız taksit etkisi izole. İlk taksit 2026-07-10 (geçmiş → sayılır).
        Tohumla(new KrediEntity
        {
            Ad = "Ziraat", CekilenTutar = 0m, CekimTarihi = new DateOnly(2026, 7, 6),
            TaksitSayisi = 1, AylikOdeme = 1_000m, OdemeGunu = 10, Kanal = "MEZAT"
        });

        var panel = await PanelAsync();

        Assert.Equal(99_000m, panel.GuncelKasa);                                      // genel kasadan düştü
        Assert.Equal(-1_000m, panel.Kanallar.Single(k => k.Kanal == "MEZAT").Bakiye); // MEZAT'tan düştü
        Assert.Equal(0m, panel.Kanallar.Single(k => k.Kanal == "PERAKENDE").Bakiye);  // diğer kanal etkilenmedi
    }

    [Fact]
    public async Task Ortak_taksit_aylik_raporda_kanallara_esit_bolunur()
    {
        // Ortak taksit: 2 aktif kanala 300 → her birine 150.
        Tohumla(new KrediEntity
        {
            Ad = "Ziraat", CekilenTutar = 0m, CekimTarihi = new DateOnly(2026, 7, 6),
            TaksitSayisi = 1, AylikOdeme = 300m, OdemeGunu = 10, Kanal = Kanallar.Ortak
        });

        var client = await _factory.EditorClientAsync();
        var rapor = (await client.GetFromJsonAsync<AylikYanit>("/api/rapor/aylik?yil=2026&ay=7"))!;

        Assert.Equal(150m, rapor.Kanallar.Single(k => k.Kanal == "MEZAT").OrtakPay);
        Assert.Equal(150m, rapor.Kanallar.Single(k => k.Kanal == "PERAKENDE").OrtakPay);
    }

    [Fact]
    public async Task Gelecek_taksit_guncel_kasayi_etkilemez()
    {
        // Çekim bugün (tutar 0), ödeme günü = bugünün günü → ilk taksit GELECEK ay (kesin sonra).
        var bugun = DateOnly.FromDateTime(DateTime.Today);
        Tohumla(new KrediEntity
        {
            Ad = "Ziraat", CekilenTutar = 0m, CekimTarihi = bugun,
            TaksitSayisi = 1, AylikOdeme = 9_999m, OdemeGunu = bugun.Day, Kanal = "MEZAT"
        });

        var panel = await PanelAsync();

        Assert.Equal(100_000m, panel.GuncelKasa); // gelecekteki taksit güncel kasadan düşmez
    }

    [Fact]
    public async Task Kredi_silinince_muhasebe_etkisi_kalkar()
    {
        Tohumla();
        var client = await _factory.EditorClientAsync();

        var yeni = new KrediEntity
        {
            Ad = "Ziraat", CekilenTutar = 5_000m, CekimTarihi = new DateOnly(2026, 7, 6),
            TaksitSayisi = 1, AylikOdeme = 0m, OdemeGunu = 15, Kanal = "MEZAT"
        };
        var eklenen = LegacyFinanceSeed.Kaydet(_factory, yeni);

        var panelEkli = (await client.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;
        Assert.Equal(105_000m, panelEkli.GuncelKasa);

        var sil = await client.DeleteAsync($"/api/krediler/{eklenen.Id}");
        sil.EnsureSuccessStatusCode();

        var panelSonra = (await client.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;
        Assert.Equal(100_000m, panelSonra.GuncelKasa); // etki tamamen kalktı
    }
}
