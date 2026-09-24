using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Aylık rapordaki kanal satırı: ay sonucu, gelir hedefi (varsa) ve İşlemler'e inilen rakamlar.
/// Satıra dokunmak kanalın o ayki tüm işlemlerini açar.
/// </summary>
public sealed record AylikKanalSatiri(KanalAylikDto Rapor, KanalHedefDto? Hedef, IReadOnlyList<DrillRakam> Rakamlar, IslemSuzgeci Suzgec)
{
    public string Kanal => Rapor.Kanal;
    public decimal AySonucu => Rapor.AySonucu;
    public bool CekVar => Rapor.CekVar;
    public decimal CekGelen => Rapor.CekGelen;
    public decimal CekGiden => Rapor.CekGiden;

    public bool HedefVar => Hedef?.Hedef is not null;
    /// <summary>"Hedef 50.000,00 ₺ · gerçekleşen 40.000,00 ₺ · %80,0".</summary>
    public string HedefMetni => Hedef is { Hedef: { } h } k
        ? $"Hedef {Bicim.Tl(h)} ₺ · gerçekleşen {Bicim.Tl(k.Gerceklesen)} ₺" + (k.Yuzde is { } y ? $" · %{y.ToString("0.0", Kultur.Turkce)}" : "")
        : "";
    /// <summary>İlerleme çubuğu (0–1).</summary>
    public double HedefOrani => Hedef is { Hedef: > 0m } k ? Math.Clamp((double)(k.Gerceklesen / k.Hedef!.Value), 0d, 1d) : 0d;
    public bool HedefTuttu => Hedef is { Yuzde: >= 100m };
}

/// <summary>Sabit gider kalemi bütçesi: bütçe, gerçekleşen (kartsız sabit gider), yüzde ve tekrarlayan şablon tutarı.</summary>
public sealed record ButceSatiri(GiderButceDto Dto)
{
    public string Kalem => Dto.Kalem;
    public string ButceMetni => Dto.Butce is { } b ? Bicim.Tl(b) + " ₺" : "Bütçe yok";
    public string GerceklesenMetni => Bicim.Tl(Dto.Gerceklesen) + " ₺";
    public string YuzdeMetni => Dto.Yuzde is { } y ? "%" + y.ToString("0.0", Kultur.Turkce) : "";
    public string SablonMetni => Dto.Sablon is { } s ? $"Şablon {Bicim.Tl(s)} ₺" : "";
    public bool Asti => Dto.Yuzde is > 100m;
    public double Oran => Dto.Butce is > 0m ? Math.Clamp((double)(Dto.Gerceklesen / Dto.Butce!.Value), 0d, 1d) : 0d;
}

/// <summary>Yayından sonra değişen rakam: "MEZAT · Gelen: 30.000,00 → 32.000,00".</summary>
public sealed record AyFarkiSatiri(AyFarkiDto Dto)
{
    public string Metin => $"{Dto.Kalem}: {Deger(Dto.Eski)} → {Deger(Dto.Yeni)}";
    private static string Deger(decimal? d) => d is { } v ? Bicim.Tl(v) : "—";
}

/// <summary>Aylık raporun satırlarını kurar (saf; birim testli).</summary>
public static class AylikGorunum
{
    /// <summary>
    /// Kanal satırları: Cari ve sabit gider o ayın o tipteki işlemlerine iner; kredi kartı sütunu
    /// (kart kuralı gereği) bir önceki ayın kart harcamalarıdır, o yüzden geçen aya iner. Ortak pay o
    /// ayın kart dışı Ortak işlemlerine iner: pay, bu havuzun (ve geçen ayın Ortak K.K'sının) kanallara
    /// bölünmüş kısmıdır, bu ayın Ortak K.K'sı içinde değildir.
    /// </summary>
    public static IReadOnlyList<AylikKanalSatiri> KanalSatirlari(AylikRaporDto rapor, HedefButceDto? hedef)
    {
        var (bas, bit) = KasaDokumuGorunum.AyAraligi(rapor.Yil, rapor.Ay);
        var onceki = bas.AddMonths(-1);
        var (obas, obit) = KasaDokumuGorunum.AyAraligi(onceki.Year, onceki.Month);
        var liste = new List<AylikKanalSatiri>(rapor.Kanallar.Count);
        foreach (var k in rapor.Kanallar)
        {
            var rakamlar = new List<DrillRakam>();
            void Ekle(string etiket, decimal tutar, IslemSuzgeci? s)
            {
                if (tutar != 0m) rakamlar.Add(new DrillRakam(etiket, tutar, s));
            }
            Ekle("Gelen", k.Gelen, null);
            Ekle("Cari", k.CariGiden, new IslemSuzgeci(bas, bit, k.Kanal, IslemTipSuzgeci.Cari));
            Ekle("Sabit gider", k.SabitGider, new IslemSuzgeci(bas, bit, k.Kanal, IslemTipSuzgeci.SabitGider));
            Ekle("Kredi kartı (geçen ay)", k.KrediKarti, new IslemSuzgeci(obas, obit, k.Kanal, IslemTipSuzgeci.KrediKarti));
            Ekle("Ortak pay", k.OrtakPay, new IslemSuzgeci(bas, bit, IslemlerViewModel.OrtakKanal, IslemTipSuzgeci.Nakit));
            var h = hedef?.Kanallar.FirstOrDefault(x => x.Kanal == k.Kanal);
            liste.Add(new AylikKanalSatiri(k, h, rakamlar, new IslemSuzgeci(bas, bit, k.Kanal)));
        }
        return liste;
    }

    /// <summary>Bütçe kartı: bütçesi, şablonu ya da gerçekleşeni olan kalemler (aktif ya da değil).</summary>
    public static IReadOnlyList<ButceSatiri> ButceSatirlari(HedefButceDto? hedef)
        => hedef is null ? []
            : hedef.Kalemler.Where(k => k.Butce is not null || k.Sablon is not null || k.Gerceklesen != 0m)
                .Select(k => new ButceSatiri(k)).ToList();
}
