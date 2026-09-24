using System.Text.Json;
using Kasa.Api.Servisler;
using Kasa.Core;

namespace Kasa.Api.Tests;

/// <summary>Motoru ay ay çağırmak, tek çağrıyla birebir aynı haftalık sonucu vermeli.</summary>
public class AylaraBolmeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Aylara_bolerek_hesap_tek_cagriyla_ayni(int tohum)
    {
        var r = new Random(tohum);
        var takip = new DateOnly(2025, 1, 1).AddDays(r.Next(0, 60));
        var bitis = new DateOnly(2026, 9, 24);
        var kanallar = new List<Kanal> { new("MEZAT", 100m, true, 0), new("PERAKENDE", -5m, true, 1), new("TOPTAN", 0m, false, 2) };
        var adlar = new[] { "MEZAT", "PERAKENDE", "TOPTAN", Kanallar.Ortak };
        int gun = bitis.DayNumber - takip.DayNumber + 60;

        var islemler = Enumerable.Range(0, 3000).Select(i =>
        {
            var tip = (GiderTipi)r.Next(0, 3);
            int? kart = tip == GiderTipi.KrediKarti && r.Next(2) == 0 ? r.Next(1, 3) : null;
            // Takip başlangıcından önceki ay dahil (ertelenen K.K ilk aya düşer).
            return new Islem(takip.AddDays(r.Next(-45, gun - 60)), "C", r.Next(1, 100_000) / 100m, adlar[r.Next(adlar.Length)], tip, null, kart);
        }).ToList();
        var donemler = DonemUretici.Uret(takip, bitis);
        var gelenler = donemler.SelectMany(d => kanallar.Select(k => new Gelen(d.Start, k.Ad, r.Next(0, 50_000))))
            .Append(new Gelen(takip.AddDays(-3), "MEZAT", 999m)).ToList();
        var odemeler = Enumerable.Range(0, 40).Select(_ => new KartOdeme(takip.AddDays(r.Next(0, gun - 60)), r.Next(1, 9000))).ToList();

        var tek = HesapMotoru.HaftalikHesapla(1234.56m, kanallar, islemler, gelenler, donemler, odemeler);
        var parcali = HesapServisi.HaftalikAylaraBolerek(1234.56m, kanallar, islemler, gelenler, donemler, odemeler);

        Assert.Equal(JsonSerializer.Serialize(tek), JsonSerializer.Serialize(parcali));
    }
}
