using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>
/// Paket B testleri için tohumlu rastgele motor girdisi: bilinen/bilinmeyen kanallar, Ortak,
/// karta bağlı ve kartsız K.K, kart ödemeleri, her yön/durumda çekler, dönem dışına taşan tarihler
/// ve kuruştan fazla ondalıklı tutarlar. Aynı tohum her zaman aynı girdiyi üretir.
/// </summary>
public sealed record MotorGirdisi(
    decimal KasaAcilis,
    IReadOnlyList<Kanal> Kanallar,
    IReadOnlyList<Islem> Islemler,
    IReadOnlyList<Gelen> Gelenler,
    IReadOnlyList<Donem> Donemler,
    IReadOnlyList<KartOdeme> KartOdemeleri,
    IReadOnlyList<Cek> Cekler,
    DateOnly Baslangic,
    DateOnly Bitis)
{
    public IReadOnlyList<HaftalikOzet> Haftalik()
        => HesapMotoru.HaftalikHesapla(KasaAcilis, Kanallar, Islemler, Gelenler, Donemler, KartOdemeleri, Cekler);

    /// <summary>Takvimdeki her ay (yıl, ay) sırayla.</summary>
    public IEnumerable<(int Yil, int Ay)> Aylar()
        => Donemler.Select(d => (d.Yil, d.Ay)).Distinct();
}

public static class MotorVeriUretici
{
    private static readonly string[] Adlar = { "MEZAT", "PERAKENDE", "TOPTAN", Kanallar.Ortak, "BILINMEYEN" };

    public static MotorGirdisi Uret(int tohum)
    {
        var r = new Random(tohum);
        var kanallar = new List<Kanal>
        {
            new("MEZAT", Tutar(r, 200_000), true, 0),
            new("PERAKENDE", Tutar(r, 200_000), r.Next(4) != 0, 1),
            new("TOPTAN", Tutar(r, 200_000) - 50_000m, true, 2),
        };
        var bas = new DateOnly(2025, 1, 1).AddDays(r.Next(0, 500));
        var bitis = bas.AddDays(r.Next(20, 260));
        int gun = bitis.DayNumber - bas.DayNumber;
        DateOnly Tarih() => bas.AddDays(r.Next(-40, gun + 20));
        string Kanal() => Adlar[r.Next(Adlar.Length)];

        var islemler = new List<Islem>();
        int n = r.Next(20, 260);
        for (int i = 0; i < n; i++)
        {
            var tip = (GiderTipi)r.Next(3);
            int? kart = r.Next(3) switch { 0 => 1, 1 when tip == GiderTipi.KrediKarti => null, 1 => null, _ => r.Next(2) == 0 ? 2 : null };
            islemler.Add(new Islem(Tarih(), "Cari" + r.Next(8), Tutar(r, 60_000), Kanal(), tip, null, kart));
        }
        var gelenler = new List<Gelen>();
        int m = r.Next(10, 120);
        for (int i = 0; i < m; i++) gelenler.Add(new Gelen(Tarih(), Kanal(), Tutar(r, 150_000)));
        var odemeler = new List<KartOdeme>();
        int k = r.Next(0, 25);
        for (int i = 0; i < k; i++) odemeler.Add(new KartOdeme(Tarih(), Tutar(r, 40_000)));
        var cekler = new List<Cek>();
        int c = r.Next(0, 40);
        for (int i = 0; i < c; i++)
        {
            var yon = (CekYonu)r.Next(2);
            var durum = (CekDurumu)r.Next(6);
            DateOnly? t = r.Next(5) == 0 ? null : Tarih();
            cekler.Add(new Cek(yon, Tutar(r, 80_000), Kanal(), durum, t));
        }
        return new MotorGirdisi(Tutar(r, 500_000) - 100_000m, kanallar, islemler, gelenler,
            DonemUretici.Uret(bas, bitis), odemeler, cekler, bas, bitis);
    }

    // Çoğu kuruşlu, bazıları 3-4 ondalıklı (motor kuruşa yuvarlar), bazıları sıfır.
    private static decimal Tutar(Random r, int ust)
    {
        var secim = r.Next(10);
        if (secim == 0) return 0m;
        if (secim == 1) return r.Next(0, ust * 100) / 10000m + r.Next(0, ust);
        return r.Next(0, ust * 10) / 100m + r.Next(0, 10);
    }
}
