using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>"Kasa neden değişti?" tablosunun bir satırı (açılış ve kapanış dahil).</summary>
/// <param name="Etiket">"Açılış", "MEZAT · Gelen", "Kredi kartı ödemesi", "Kapanış"…</param>
/// <param name="Tutar">Adımda işaretli tutar (giren +, çıkan −); açılış/kapanışta null.</param>
/// <param name="Bakiye">Adımdan sonraki kasa.</param>
/// <param name="Suzgec">Satırdan İşlemler'e iniş (yalnız gider adımlarında).</param>
public sealed record KasaDokumSatiri(string Etiket, decimal? Tutar, decimal Bakiye, bool UcSatir, IslemSuzgeci? Suzgec)
{
    public string TutarMetni => Tutar is { } t ? Bicim.ImzaliTl(t) : "";
    /// <summary>ParaRenk dönüştürücüsü için (giren yeşil, çıkan kırmızı; uç satırlar bakiyenin rengi).</summary>
    public decimal RenkDegeri => Tutar ?? Bakiye;
    public string BakiyeMetni => Bicim.Tl(Bakiye);
    public bool Inilebilir => Suzgec is not null;
}

/// <summary>Kasa dökümü DTO'sunu tablo satırlarına çevirir (saf; birim testli).</summary>
public static class KasaDokumuGorunum
{
    /// <summary>Açılış + adımlar + kapanış. Gider adımları (Cari, sabit, Ortak) İşlemler'e iner.</summary>
    public static IReadOnlyList<KasaDokumSatiri> Satirlar(KasaDokumuDto d)
    {
        var liste = new List<KasaDokumSatiri>(d.Adimlar.Count + 2)
        {
            new("Açılış", null, d.Acilis, true, null),
        };
        foreach (var a in d.Adimlar)
            liste.Add(new KasaDokumSatiri(Etiket(a), a.Tutar, a.Bakiye, false, Suzgec(a, d.Baslangic, d.Bitis)));
        liste.Add(new KasaDokumSatiri("Kapanış", null, d.Kapanis, true, null));
        return liste;
    }

    /// <summary>"MEZAT · Gelen"; kanalsız adımda yalnız tür adı.</summary>
    public static string Etiket(KasaDokumAdimiDto a)
        => string.IsNullOrEmpty(a.Kanal) ? a.TurAdi : $"{a.Kanal} · {a.TurAdi}";

    /// <summary>Adımdan İşlemler süzgeci: Cari / sabit gider kanal + tip, Ortak gider Ortak kanalı; diğerleri iniş yok.</summary>
    public static IslemSuzgeci? Suzgec(KasaDokumAdimiDto a, DateOnly bas, DateOnly bit) => a.Tur switch
    {
        "CariGider" when a.Kanal is { } k => new IslemSuzgeci(bas, bit, k, GiderTipi.Cari),
        "SabitGider" when a.Kanal is { } k => new IslemSuzgeci(bas, bit, k, GiderTipi.SabitGider),
        "OrtakGider" => new IslemSuzgeci(bas, bit, IslemlerViewModel.OrtakKanal),
        _ => null,
    };

    /// <summary>Özet cümle: "Kasa 1.000,00'dan 2.500,00'a: 3.000,00 girdi, 1.500,00 çıktı."</summary>
    public static string Ozet(KasaDokumuDto d)
        => $"Kasa {Bicim.Tl(d.Acilis)} ₺ ile açıldı, {Bicim.Tl(d.ToplamGiren)} ₺ girdi, {Bicim.Tl(d.ToplamCikan)} ₺ çıktı; "
           + $"{Bicim.Tl(d.Kapanis)} ₺ ile kapandı.";

    /// <summary>"3 Ağu 2026 – 9 Ağu 2026".</summary>
    public static string AralikMetni(DateOnly b, DateOnly s)
        => $"{b.ToString("d MMM yyyy", Kultur.Turkce)} – {s.ToString("d MMM yyyy", Kultur.Turkce)}";

    /// <summary>"Ağustos 2026".</summary>
    public static string AyEtiketi(int yil, int ay)
        => new DateOnly(yil, ay, 1).ToString("MMMM yyyy", Kultur.Turkce);

    /// <summary>Ayın ilk ve son günü.</summary>
    public static (DateOnly Bas, DateOnly Bit) AyAraligi(int yil, int ay)
        => (new DateOnly(yil, ay, 1), new DateOnly(yil, ay, DateTime.DaysInMonth(yil, ay)));
}
