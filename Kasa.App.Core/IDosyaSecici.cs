namespace Kasa.App.Core;

/// <summary>
/// Cihazdan seçilen dosya. <see cref="Boyut"/> gerçek boyuttur; dosya sınırı aşıyorsa platform
/// seçicisi içeriği hiç okumayabilir (<see cref="Icerik"/> boş kalır, doğrulama boyuta bakar).
/// </summary>
public sealed record SecilenDosya(string Ad, byte[] Icerik, long Boyut)
{
    public SecilenDosya(string ad, byte[] icerik) : this(ad, icerik, icerik.LongLength) { }
}

/// <summary>Platform dosya/fotoğraf seçicisi (Windows: FilePicker, kamerası olan cihazda MediaPicker).</summary>
public interface IDosyaSecici
{
    /// <summary>Bu cihazda fotoğraf çekilebiliyor mu ("Fotoğraf çek" düğmesi buna göre görünür).</summary>
    bool KameraVar { get; }

    /// <summary>Fiş/fatura: JPG, PNG, WEBP, HEIC, PDF; çoklu seçim. Vazgeçilirse boş liste.</summary>
    Task<IReadOnlyList<SecilenDosya>> BelgeSecAsync();

    /// <summary>Kamerayla fotoğraf; vazgeçilirse null.</summary>
    Task<SecilenDosya?> FotografCekAsync();

    /// <summary>ERP12 dışa aktarımı gibi bir CSV/TXT dosyası; vazgeçilirse null.</summary>
    Task<SecilenDosya?> CsvSecAsync();
}

/// <summary>İndirilen eki cihazın varsayılan uygulamasıyla açar (geçici klasöre yazıp).</summary>
public interface IEkAcici
{
    Task AcAsync(string dosyaAdi, byte[] icerik);
}

/// <summary>Cihazda saklanan küçük ayarlar (ör. ERP12 sütun eşleştirmesi). Sunucuya gitmez.</summary>
public interface IAyarDeposu
{
    string? Oku(string anahtar);
    void Yaz(string anahtar, string? deger);
}

/// <summary>Bellekte ayar deposu (testler ve deposu olmayan platformlar için).</summary>
public sealed class BellekAyarDeposu : IAyarDeposu
{
    private readonly Dictionary<string, string> _d = new();
    public string? Oku(string anahtar) => _d.GetValueOrDefault(anahtar);
    public void Yaz(string anahtar, string? deger)
    {
        if (deger is null) _d.Remove(anahtar);
        else _d[anahtar] = deger;
    }
}

/// <summary>
/// Platform tablo dosyası seçici (MAUI FilePicker). Toplu yüklemede xlsx ya da CSV dosyası seçmek için;
/// kullanıcı vazgeçerse null.
/// </summary>
public interface ITabloSecici
{
    Task<SecilenDosya?> SecAsync();
}
