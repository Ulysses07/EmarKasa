namespace Kasa.App.Core;

/// <summary>Dosya seçicinin (MAUI FilePicker) sonucu: dosya adı ve içeriği açan işlev. Sayfa FileResult'tan kurar; görünüm
/// modeli MAUI türü görmez, CI'da (Linux) sınanır.</summary>
public sealed record SecilenDosya(string Ad, Func<Task<Stream>> Ac);

/// <summary>Sayfanın kullanıcıya göstereceği uyarı (DisplayAlertAsync başlığı ve iletisi).</summary>
public sealed record DosyaUyarisi(string Baslik, string Mesaj);

public enum DosyaOkumaDurumu { Tamam, SinirAsildi, Vazgecildi }

/// <summary>Sınırlı okumanın sonucu; içerik yalnız <see cref="DosyaOkumaDurumu.Tamam"/> iken doludur.</summary>
public sealed record DosyaOkumasi(DosyaOkumaDurumu Durum, byte[]? Icerik);

/// <summary>maui-8: alış belgesi ve ekstre PDF'i seçimindeki saf kurallar (önceden AlislarPage ve EkstreAktarmaPage
/// kod-arkasındaydı). Sunucu sınırlarıyla aynı: belge PDF/PNG/JPEG, dosya başına en çok 10 MB.</summary>
public static class DosyaSecimKurallari
{
    public const long EnFazlaBayt = 10 * 1024 * 1024;
    public static readonly DosyaUyarisi DesteklenmeyenBelge = new("Desteklenmeyen belge", "PDF, PNG veya JPEG seçin.");
    public static readonly DosyaUyarisi BuyukBelge = new("Belge büyük", "En fazla 10 MB belge yükleyebilirsiniz.");
    public static readonly DosyaUyarisi OkunamayanBelge = new("Belge okunamadı", "Dosyayı kontrol edip yeniden seçin.");

    /// <summary>Alış belgesinin içerik türü uzantıdan (büyük/küçük harf duyarsız); desteklenmeyen uzantıda null.</summary>
    public static string? BelgeIcerikTuru(string dosyaAdi) => Path.GetExtension(dosyaAdi).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => null,
    };

    public static bool PdfMi(string dosyaAdi) => dosyaAdi.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Akışı en çok <paramref name="sinir"/> bayt belleğe okur (80 KB tamponla). Sınır aşılırsa
    /// <see cref="DosyaOkumaDurumu.SinirAsildi"/>; her parçadan önce <paramref name="devam"/> false dönerse (oturum ya da seçim
    /// değişti) <see cref="DosyaOkumaDurumu.Vazgecildi"/>; ikisinde de içerik tutulmaz.</summary>
    public static async Task<DosyaOkumasi> SinirliOkuAsync(Stream akis, long sinir, Func<bool>? devam = null, CancellationToken ct = default)
    {
        using var bellek = new MemoryStream();
        var tampon = new byte[81920];
        int okunan;
        while ((okunan = await akis.ReadAsync(tampon, ct)) > 0)
        {
            if (devam is not null && !devam())
                return new(DosyaOkumaDurumu.Vazgecildi, null);
            if (bellek.Length + okunan > sinir)
                return new(DosyaOkumaDurumu.SinirAsildi, null);
            bellek.Write(tampon, 0, okunan);
        }
        return new(DosyaOkumaDurumu.Tamam, bellek.ToArray());
    }
}
