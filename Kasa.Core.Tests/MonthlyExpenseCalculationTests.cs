namespace Kasa.Core.Tests;

public class MonthlyExpenseCalculationTests
{
    [Fact]
    public void Yeni_aylik_gider_kanal_kasasindan_duser_eski_sabit_gider_davranisi_degismez()
    {
        var date = new DateOnly(2026, 9, 1); var channels = new[] { new Kanal("A") }; var periods = new[] { new Donem(date, date.AddDays(29)) };
        var expenses = new[] { new Islem(date, "Eski", 40m, "A", GiderTipi.SabitGider), new Islem(date, "Aylık", 60m, "A", GiderTipi.SabitGider, AylikGider: true) };
        var week = Assert.Single(HesapMotoru.HaftalikHesapla(1000m, channels, expenses, [], periods));
        Assert.Equal(900m, week.KasaDevir); Assert.Equal(-60m, Assert.Single(week.Kanallar).Devir);
        Assert.Equal(100m, Assert.Single(HesapMotoru.AylikHesapla(2026, 9, channels, expenses, [], periods).Kanallar).SabitGider);
    }
    [Fact]
    public void Genel_aylik_gider_etiket_kanalla_ayni_olsa_da_kanallara_dagitilmaz()
    {
        var date = new DateOnly(2026, 9, 1); var channels = new[] { new Kanal(Kanallar.Ortak), new Kanal("B") }; var periods = new[] { new Donem(date, date.AddDays(29)) };
        var expenses = new[] { new Islem(date, "Genel", 100m, Kanallar.Ortak, GiderTipi.SabitGider, AylikGider: true, YalnizGenelKasa: true) };
        var week = Assert.Single(HesapMotoru.HaftalikHesapla(1000m, channels, expenses, [], periods));
        Assert.Equal(900m, week.KasaDevir); Assert.All(week.Kanallar, c => Assert.Equal(0m, c.Devir));
        var month = HesapMotoru.AylikHesapla(2026, 9, channels, expenses, [], periods);
        Assert.All(month.Kanallar, c => Assert.Equal(0m, c.AySonucu)); Assert.Equal(100m, month.GenelGider); Assert.Equal(0m, month.DagilimBekleyenTutar);
    }
}
