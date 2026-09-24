using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Çek etiketleri ve istemci tarafı kuralları (sunucudaki <c>Kasa.Core.CekKurali</c> ile birebir aynı;
/// App.Core, Kasa.Core'a bağlı olmadığı için burada tekrarlanır).
/// </summary>
public static class CekMetin
{
    public static string YonAdi(CekYonu yon) => yon == CekYonu.Alinan ? "Alınan" : "Verilen";

    /// <summary>Durum etiketi. Verilen çekte "Portföyde" = ödenmedi → "Ödenecek".</summary>
    public static string DurumAdi(CekYonu? yon, CekDurumu durum) => durum switch
    {
        CekDurumu.Portfoyde => yon == CekYonu.Verilen ? "Ödenecek" : "Portföyde",
        CekDurumu.TahsilEdildi => "Tahsil edildi",
        CekDurumu.Odendi => "Ödendi",
        CekDurumu.CiroEdildi => "Ciro edildi",
        CekDurumu.Karsiliksiz => "Karşılıksız",
        CekDurumu.IadeEdildi => "İade edildi",
        _ => durum.ToString(),
    };

    /// <summary>Yöne göre seçilebilen durumlar (form sırasıyla).</summary>
    public static IReadOnlyList<CekDurumu> GecerliDurumlar(CekYonu yon) => yon == CekYonu.Alinan
        ? [CekDurumu.Portfoyde, CekDurumu.TahsilEdildi, CekDurumu.CiroEdildi, CekDurumu.Karsiliksiz, CekDurumu.IadeEdildi]
        : [CekDurumu.Portfoyde, CekDurumu.Odendi, CekDurumu.IadeEdildi];

    /// <summary>Tüm durumlar (yön filtresi "Tümü" iken).</summary>
    public static IReadOnlyList<CekDurumu> TumDurumlar { get; } =
        [CekDurumu.Portfoyde, CekDurumu.TahsilEdildi, CekDurumu.Odendi, CekDurumu.CiroEdildi, CekDurumu.Karsiliksiz, CekDurumu.IadeEdildi];

    public static bool DurumGecerliMi(CekYonu yon, CekDurumu durum) => GecerliDurumlar(yon).Contains(durum);

    /// <summary>Tahsil / ödeme / ciro günü bu durumda zorunlu mu?</summary>
    public static bool IslemTarihiGerekli(CekDurumu durum)
        => durum is CekDurumu.TahsilEdildi or CekDurumu.Odendi or CekDurumu.CiroEdildi;

    /// <summary>İşlem tarihi alanının etiketi.</summary>
    public static string IslemTarihiEtiketi(CekDurumu durum) => durum switch
    {
        CekDurumu.TahsilEdildi => "Tahsil tarihi",
        CekDurumu.Odendi => "Ödeme tarihi",
        CekDurumu.CiroEdildi => "Ciro tarihi",
        _ => "İşlem tarihi",
    };

    /// <summary>Durum rozeti tonu: "olumlu" (tamamlandı), "bekliyor" (portföyde), "olumsuz" (karşılıksız), "notr".</summary>
    public static string Ton(CekDurumu durum) => durum switch
    {
        CekDurumu.TahsilEdildi or CekDurumu.Odendi => "olumlu",
        CekDurumu.Portfoyde => "bekliyor",
        CekDurumu.Karsiliksiz => "olumsuz",
        _ => "notr",
    };
}

/// <summary>Çek satırı: DTO + liste görünümü için hazır metinler.</summary>
public sealed class CekGorunum
{
    public CekDto Dto { get; }
    public int Id => Dto.Id;
    public CekYonu Yon => Dto.Yon;
    public bool AlinanMi => Dto.Yon == CekYonu.Alinan;
    public string YonAdi => CekMetin.YonAdi(Dto.Yon);
    public string Kisi => Dto.Kisi;
    public string Kanal => Dto.Kanal;
    public decimal Tutar => Dto.Tutar;
    public DateOnly VadeTarihi => Dto.VadeTarihi;
    public CekDurumu Durum => Dto.Durum;
    public string DurumAdi => CekMetin.DurumAdi(Dto.Yon, Dto.Durum);
    /// <summary>Durum rozeti tonu (bkz. <see cref="CekMetin.Ton"/>).</summary>
    public string Ton => CekMetin.Ton(Dto.Durum);
    /// <summary>Alınan +, verilen − işaretli tutar.</summary>
    public string ImzaliTutar => (AlinanMi ? "+" : "−") + Bicim.Tl(Dto.Tutar);
    /// <summary>Vadesi geçtiği hâlde hâlâ portföyde (tahsil edilmedi / ödenmedi).</summary>
    public bool VadesiGecti { get; }
    /// <summary>"Ziraat · No 0012 · MEZAT" gibi ikincil satır.</summary>
    public string Ayrinti { get; }
    /// <summary>Vade ve (varsa) işlem tarihi: "Vade 15 Eki 2026 · Tahsil 20 Eyl 2026".</summary>
    public string TarihMetni { get; }

    public CekGorunum(CekDto d, DateOnly bugun)
    {
        Dto = d;
        VadesiGecti = d.Durum == CekDurumu.Portfoyde && d.VadeTarihi < bugun;
        var parca = new List<string> { YonAdi };
        if (!string.IsNullOrWhiteSpace(d.Banka)) parca.Add(d.Banka!);
        if (!string.IsNullOrWhiteSpace(d.CekNo)) parca.Add($"No {d.CekNo}");
        parca.Add(d.Kanal);
        Ayrinti = string.Join(" · ", parca);
        var tarih = $"Vade {d.VadeTarihi.ToString("d MMM yyyy", Kultur.Turkce)}";
        var islem = d.Durum switch
        {
            CekDurumu.TahsilEdildi => "Tahsil",
            CekDurumu.Odendi => "Ödeme",
            CekDurumu.CiroEdildi => "Ciro",
            _ => null,
        };
        if (d.IslemTarihi is { } it && islem is not null)
            tarih += $" · {islem} {it.ToString("d MMM yyyy", Kultur.Turkce)}";
        TarihMetni = tarih;
    }
}

/// <summary>Yön seçim çipi (null = tümü).</summary>
public sealed class YonCipi : SecimCipi
{
    public CekYonu? Yon { get; }
    public YonCipi(string ad, CekYonu? yon) : base(ad) => Yon = yon;
}

/// <summary>Durum seçim çipi (null = tümü).</summary>
public sealed class DurumCipi : SecimCipi
{
    public CekDurumu? Durum { get; }
    public DurumCipi(string ad, CekDurumu? durum) : base(ad) => Durum = durum;
}
