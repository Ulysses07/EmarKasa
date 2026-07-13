using Kasa.Api;
using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api.Servisler;

/// <summary>DB'den veriyi yükler, dönem takvimini üretir ve HesapMotoru'nu çağırır.</summary>
public class HesapServisi
{
    private readonly KasaDbContext _db;
    public HesapServisi(KasaDbContext db) => _db = db;

    private record Yuk(
        IReadOnlyList<Kanal> Kanallar,
        IReadOnlyList<Islem> Islemler,
        IReadOnlyList<Gelen> Gelenler,
        IReadOnlyList<Donem> Donemler,
        decimal KasaAcilis);

    private Yuk Yukle()
    {
        var kanallar = _db.Kanallar.OrderBy(k => k.Sira).ToList().Select(e => e.ToCore()).ToList();
        var islemler = _db.Islemler.ToList().Select(e => e.ToCore()).ToList();
        var gelenler = _db.Gelenler.ToList().Select(e => e.ToCore()).ToList();
        var ayar = _db.Ayarlar.First();

        var baslangic = ayar.TakipBaslangic;
        var bugun = DateOnly.FromDateTime(DateTime.Today);
        var enGecIslem = islemler.Select(i => i.Tarih).DefaultIfEmpty(bugun).Max();
        var bitis = new[] { bugun, enGecIslem, baslangic }.Max();

        var donemler = DonemUretici.Uret(baslangic, bitis);
        return new Yuk(kanallar, islemler, gelenler, donemler, ayar.KasaAcilisDevri);
    }

    public IReadOnlyList<HaftalikOzet> Haftalik()
    {
        var y = Yukle();
        return HesapMotoru.HaftalikHesapla(y.KasaAcilis, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
    }

    public AylikRapor Aylik(int yil, int ay)
    {
        var y = Yukle();
        return HesapMotoru.AylikHesapla(yil, ay, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
    }

    public IReadOnlyList<Donem> Donemler() => Yukle().Donemler;

    public PanelDto Panel()
    {
        var y = Yukle();
        var haftalik = HesapMotoru.HaftalikHesapla(y.KasaAcilis, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
        var son = haftalik.Count > 0 ? haftalik[^1] : null;

        var guncelKasa = son?.KasaDevir ?? y.KasaAcilis;
        var buHafta = son?.KasaSonucu ?? 0m;
        var kanalBakiyeleri = son is not null
            ? son.Kanallar.Select(k => new KanalBakiye(k.Kanal, k.Devir)).ToList()
            : y.Kanallar.Select(k => new KanalBakiye(k.Ad, k.AcilisDevri)).ToList();

        var bugun = DateOnly.FromDateTime(DateTime.Today);
        var buAyRapor = HesapMotoru.AylikHesapla(bugun.Year, bugun.Month, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
        var buAy = buAyRapor.Kanallar.Sum(k => k.AySonucu);

        return new PanelDto(guncelKasa, kanalBakiyeleri, buHafta, buAy);
    }
}
