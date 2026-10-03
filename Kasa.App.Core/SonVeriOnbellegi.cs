namespace Kasa.App.Core;

/// <summary>
/// Oturum boyunca ekranların son başarılı verisi (tasarım 2026-10-02 §3, ekran denemesi H-1). Shell, DI'dan kurulan sayfayı
/// başka menü öğesine geçince bırakır (MAUI <c>ShellContent.EvaluateDisconnect</c>); dönüşte yeni sayfa ve yeni görünüm modeli
/// kurulur. Görünüm modeli her başarılı yüklemenin ham yanıtını sorgu anahtarıyla buraya yazar; yeni kurulan model aynı sorgunun
/// verisini hemen (eski, soluk) gösterir, sonra yüklemeyi dener. Tek örnek <see cref="AuthViewModel.SonVeri"/>'dir: oturum ya da rol
/// değişince temizlenir (başka kullanıcının verisi görünmez). Görünüm modelleri tekil yapılmaz: sayfalar onların olaylarına abone
/// olur, her yeni sayfa aboneliği sızdırırdı. Önbellek sınırsız büyümez (Küçük-7): ekran başına (anahtarın "|" öncesi) en son
/// kullanılan (yazılan ya da okunan) <see cref="EkranBasinaSinir"/> sorgu tutulur, en eski kullanılan silinir.
/// </summary>
public sealed class SonVeriOnbellegi
{
    /// <summary>Ekran başına tutulan sorgu sayısı (ör. Aylık'ta gezilen aylar, İşlemler'de süzgeçler).</summary>
    public const int EkranBasinaSinir = 10;

    private readonly Dictionary<string, (object Veri, DateTimeOffset Zaman)> _kayitlar = new(StringComparer.Ordinal);
    /// <summary>Ekran → anahtarları, en son kullanılan başta.</summary>
    private readonly Dictionary<string, LinkedList<string>> _ekranSirasi = new(StringComparer.Ordinal);
    private readonly Lock _kilit = new();

    /// <summary>Anahtarın son başarılı verisi ve alındığı an.</summary>
    public void Yaz<T>(string anahtar, T veri, DateTimeOffset zaman) where T : notnull
    {
        lock (_kilit)
        {
            _kayitlar[anahtar] = (veri, zaman);
            Kullanildi(anahtar);
        }
    }

    private static string Ekran(string anahtar) => anahtar.IndexOf('|') is var i and >= 0 ? anahtar[..i] : anahtar;

    /// <summary>Anahtarı ekranının sırasında başa alır; ekran sınırı aşılırsa en eski kullanılanı siler. Kilit içinde çağrılır.</summary>
    private void Kullanildi(string anahtar)
    {
        var ekran = Ekran(anahtar);
        if (!_ekranSirasi.TryGetValue(ekran, out var sira))
            _ekranSirasi[ekran] = sira = new();
        sira.Remove(anahtar);
        sira.AddFirst(anahtar);
        while (sira.Count > EkranBasinaSinir)
        {
            _kayitlar.Remove(sira.Last!.Value);
            sira.RemoveLast();
        }
    }

    /// <summary>Anahtarın verisi varsa (ve türü uyuşuyorsa) true.</summary>
    public bool Oku<T>(string anahtar, out T veri, out DateTimeOffset zaman) where T : notnull
    {
        lock (_kilit)
        {
            if (_kayitlar.TryGetValue(anahtar, out var kayit) && kayit.Veri is T v)
            {
                Kullanildi(anahtar);
                (veri, zaman) = (v, kayit.Zaman);
                return true;
            }
        }
        (veri, zaman) = (default!, default);
        return false;
    }

    public void Sil(string anahtar)
    {
        lock (_kilit)
        {
            _kayitlar.Remove(anahtar);
            if (_ekranSirasi.TryGetValue(Ekran(anahtar), out var sira))
                sira.Remove(anahtar);
        }
    }

    /// <summary>Oturum ya da rol değişti: hiçbir veri yeni oturuma taşınmaz.</summary>
    public void Temizle()
    {
        lock (_kilit)
        {
            _kayitlar.Clear();
            _ekranSirasi.Clear();
        }
    }
}
