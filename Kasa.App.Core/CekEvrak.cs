using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket D (özellik 30 + 43): evrak türü/konumu etiketleri, tek dokunuş görünürlüğü, çoklu seçim
// ve risk dağılımı satırı. Kasa kuralı değişmez: senet de çek gibi "vadede kasaya" işler.

/// <summary>Evrak türü ve konum etiketleri.</summary>
public static class CekEvrakMetin
{
    public static string TurAdi(CekTuru tur) => tur == CekTuru.Senet ? "Senet" : "Çek";

    public static string KonumAdi(CekKonumu konum) => konum switch
    {
        CekKonumu.Elde => "Elde",
        CekKonumu.BankadaTahsilde => "Bankada tahsilde",
        CekKonumu.Teminatta => "Teminatta",
        CekKonumu.Icrada => "İcrada",
        _ => konum.ToString(),
    };

    public static IReadOnlyList<CekTuru> TumTurler { get; } = [CekTuru.Cek, CekTuru.Senet];

    public static IReadOnlyList<CekKonumu> TumKonumlar { get; } =
        [CekKonumu.Elde, CekKonumu.BankadaTahsilde, CekKonumu.Teminatta, CekKonumu.Icrada];

    /// <summary>
    /// Tutar ağırlıklı ortalama vade (gün, bugüne göre; geçmiş vade negatif). Boş ya da toplamı
    /// sıfır seçimde null. Yarım gün yukarı yuvarlanır.
    /// </summary>
    public static int? OrtalamaVadeGunu(IEnumerable<CekDto> cekler, DateOnly bugun)
    {
        var liste = cekler.ToList();
        var toplam = liste.Sum(c => c.Tutar);
        if (liste.Count == 0 || toplam <= 0m) return null;
        var agirlikli = liste.Sum(c => c.Tutar * (c.VadeTarihi.DayNumber - bugun.DayNumber));
        return (int)Math.Round(agirlikli / toplam, MidpointRounding.AwayFromZero);
    }

    /// <summary>"12 Kas 2026 (49 gün sonra)" / "(bugün)" / "(3 gün önce)".</summary>
    public static string VadeMetni(int gun, DateOnly bugun)
    {
        var tarih = bugun.AddDays(gun).ToString("d MMM yyyy", Kultur.Turkce);
        var goreli = gun switch
        {
            0 => "bugün",
            > 0 => $"{gun} gün sonra",
            _ => $"{-gun} gün önce",
        };
        return $"{tarih} ({goreli})";
    }

    /// <summary>Türkçe büyük/küçük harf duyarsız "içerir".</summary>
    public static bool Icerir(string metin, string aranan)
        => Kultur.Turkce.CompareInfo.IndexOf(metin, aranan, CompareOptions.IgnoreCase) >= 0;
}

/// <summary>Çek satırının Paket D eki: seçim kutusu, tek dokunuş düğmeleri, tür/konum/ciro etiketi.</summary>
public sealed partial class CekGorunum : ObservableObject
{
    /// <summary>Çoklu seçim kutusu (ortalama vade ve toplam için).</summary>
    [ObservableProperty] private bool _secili;
    /// <summary>"Ciro et" satır içi formu açık mı?</summary>
    [ObservableProperty] private bool _ciroAcik;

    public CekTuru Tur => Dto.Tur;
    public CekKonumu Konum => Dto.Konum;
    public string TurAdi => CekEvrakMetin.TurAdi(Dto.Tur);
    public bool Portfoyde => Dto.Durum == CekDurumu.Portfoyde;

    /// <summary>Alınan evrak portföydeyse: "Tahsil edildi", "Ciro et", "Karşılıksız".</summary>
    public bool AlinanTekDokunus => AlinanMi && Portfoyde;
    /// <summary>Verilen evrak ödenmediyse: "Ödendi".</summary>
    public bool VerilenTekDokunus => !AlinanMi && Portfoyde;

    /// <summary>
    /// Varsayılandan farklı olan evrak bilgileri: "Senet · Bankada tahsilde · Ciro: Yılmaz Gıda".
    /// Çek + Elde + cirosuz evrakta boş (mevcut satır görünümü değişmez).
    /// </summary>
    public string EvrakEtiketi
    {
        get
        {
            var parca = new List<string>();
            if (Dto.Tur == CekTuru.Senet) parca.Add("Senet");
            if (AlinanMi && Portfoyde && Dto.Konum != CekKonumu.Elde) parca.Add(CekEvrakMetin.KonumAdi(Dto.Konum));
            if (Dto.Durum == CekDurumu.CiroEdildi && !string.IsNullOrWhiteSpace(Dto.CiroEdilenCari))
                parca.Add($"Ciro: {Dto.CiroEdilenCari}");
            return string.Join(" · ", parca);
        }
    }

    public bool EvrakEtiketiVar => EvrakEtiketi.Length > 0;
}

/// <summary>Evrak türü çipi (null = tümü).</summary>
public sealed class TurCipi : SecimCipi
{
    public CekTuru? Tur { get; }
    public TurCipi(string ad, CekTuru? tur) : base(ad) => Tur = tur;
}

/// <summary>Konum çipi (null = tümü).</summary>
public sealed class KonumCipi : SecimCipi
{
    public CekKonumu? Konum { get; }
    public KonumCipi(string ad, CekKonumu? konum) : base(ad) => Konum = konum;
}

/// <summary>Risk dağılımı satırı (keşideci ya da banka).</summary>
public sealed class CekRiskSatiri
{
    public CekRiskSatiri(CekRiskKalemiDto k)
    {
        Ad = k.Ad;
        Tutar = k.Tutar;
        Adet = k.Adet;
        Oran = (double)Math.Clamp(k.Oran, 0m, 1m);
        Metin = $"{Bicim.Tl(k.Tutar)} ₺ · {k.Adet} evrak · %{Math.Round(k.Oran * 100m, 1, MidpointRounding.AwayFromZero).ToString("0.#", Kultur.Turkce)}";
    }

    public string Ad { get; }
    public decimal Tutar { get; }
    public int Adet { get; }
    /// <summary>ProgressBar için 0–1.</summary>
    public double Oran { get; }
    /// <summary>"45.000,00 ₺ · 3 evrak · %38,5".</summary>
    public string Metin { get; }
}
