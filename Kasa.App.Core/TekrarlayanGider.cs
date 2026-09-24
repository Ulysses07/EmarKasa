using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Panel'deki "Girilmesi bekleyen giderler" satırı: vadesi gelmiş bir tekrarlayan gider ayı.
/// Tutar satırda düzenlenebilir (varsayılan: kayıttaki tutar).
/// </summary>
public sealed partial class BekleyenGiderGorunum : ObservableObject
{
    public int TekrarlayanGiderId { get; }
    public string Kalem { get; }
    public string Kanal { get; }
    /// <summary>Kayıttaki (önerilen) tutar.</summary>
    public decimal KayitliTutar { get; }
    /// <summary>Ayın 1'i.</summary>
    public DateOnly Ay { get; }
    public DateOnly Vade { get; }

    /// <summary>Girilecek tutar (kullanıcı değiştirebilir).</summary>
    [ObservableProperty] private decimal _tutar;

    /// <summary>İkinci satır: "MEZAT · Vade 28 Şubat 2026".</summary>
    public string Aciklama => $"{Kanal} · Vade {Vade.ToString("d MMMM yyyy", Kultur.Turkce)}";

    public BekleyenGiderGorunum(BekleyenGiderDto d)
    {
        TekrarlayanGiderId = d.TekrarlayanGiderId; Kalem = d.Kalem; Kanal = d.Kanal;
        KayitliTutar = d.Tutar; Ay = d.Ay; Vade = d.Vade;
        _tutar = d.Tutar;
    }

    /// <summary>Aynı gider ve ay mı (yeniden yüklemede düzenlenmiş tutarı taşımak için).</summary>
    public bool AyniKayit(BekleyenGiderGorunum b) => b.TekrarlayanGiderId == TekrarlayanGiderId && b.Ay == Ay;

    /// <summary>
    /// Yeniden yüklemede kullanıcının değiştirdiği tutarı eski satırdan taşır. Kayıttaki tutar
    /// Ayarlar'da değiştiyse ve kullanıcı dokunmadıysa yeni tutar kalır.
    /// </summary>
    public void GirisiDevral(BekleyenGiderGorunum eski)
    {
        if (eski.Tutar != eski.KayitliTutar) Tutar = eski.Tutar;
    }
}

/// <summary>Ayarlar → Tekrarlayan giderler listesinin satırı.</summary>
public sealed class TekrarlayanGiderSatiri
{
    public TekrarlayanGiderDto Gider { get; }
    public int Id => Gider.Id;
    public string Kalem => Gider.Kalem;
    public bool Aktif => Gider.Aktif;

    /// <summary>İkinci satır: "MEZAT · ayın 5. günü · 25.000,00".</summary>
    public string Aciklama { get; }

    public TekrarlayanGiderSatiri(TekrarlayanGiderDto g)
    {
        Gider = g;
        Aciklama = $"{g.Kanal} · {GunMetni(g.AyinGunu)} · {Bicim.Tl(g.Tutar)}";
    }

    /// <summary>"ayın 5. günü"; 29–31 kısa aylarda ayın son gününe düşer.</summary>
    public static string GunMetni(int gun)
        => gun >= 29 ? $"ayın {gun}. günü (kısa ayda son gün)" : $"ayın {gun}. günü";
}

internal static class TekrarlayanYukleme
{
    /// <summary>
    /// Liste okuması 404 dönerse boş liste: sunucu tekrarlayan giderleri henüz bilmiyor (uygulama
    /// sunucudan önce güncellendi). Sayfanın geri kalanı bu yüzden yüklenemez hale gelmesin.
    /// </summary>
    public static async Task<IReadOnlyList<T>> Oku<T>(Func<Task<IReadOnlyList<T>>> okuma)
    {
        try { return await okuma(); }
        catch (KasaApiException ex) when (ex.DurumKodu == HttpStatusCode.NotFound) { return Array.Empty<T>(); }
    }
}
