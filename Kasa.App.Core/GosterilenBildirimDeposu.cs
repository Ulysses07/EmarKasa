using System.Text.Json;

namespace Kasa.App.Core;

/// <summary>Bu bilgisayarda Windows bildirimi olarak gösterilmiş sunucu bildirimlerinin kimlikleri (tekrar göstermeme; tasarım
/// 2026-09-30 masaüstü bildirimleri §1).</summary>
public interface IGosterilenBildirimDeposu
{
    /// <summary><paramref name="adaylar"/> içinden bu bilgisayarda daha önce gösterilmemiş olanları döndürür ve aynı kilit altında
    /// gösterilmiş olarak kaydeder: uygulama ile pencere açmadan çalışan görev aynı anda baksa da her bildirimi yalnız biri alır.
    /// Dosya kilitli kalır ya da açılamazsa <see cref="IOException"/> ya da <see cref="UnauthorizedAccessException"/> fırlatır.</summary>
    IReadOnlyList<int> YenileriAyir(IReadOnlyCollection<int> adaylar);
}

/// <summary>
/// %LOCALAPPDATA%\EmarKasa\gosterilen-bildirimler.json: kimliklerin JSON dizisi, eskiden yeniye; en çok <see cref="Sinir"/> kimlik
/// (en eskiler atılır). Dosya FileShare.None ile açılır; başka süreç tutuyorsa kısa aralıklarla yeniden denenir. Bozuk ya da boş
/// dosya boş liste sayılır ve üzerine yazılır: sonuç en çok bir kez fazladan bildirimdir, kayıp olmaz.
/// </summary>
public sealed class DosyaGosterilenBildirimDeposu : IGosterilenBildirimDeposu
{
    public const string DosyaAdi = "gosterilen-bildirimler.json";
    public const int VarsayilanSinir = 500;

    private readonly string _klasor;
    private readonly string _yol;
    private readonly int _deneme;
    private readonly TimeSpan _bekleme;

    /// <param name="deneme">Kilitli dosyada en çok deneme sayısı (varsayılan 10).</param>
    /// <param name="bekleme">Denemeler arası bekleme (varsayılan 100 ms).</param>
    public DosyaGosterilenBildirimDeposu(string klasor, int sinir = VarsayilanSinir, int deneme = 10, TimeSpan? bekleme = null)
    {
        _klasor = klasor;
        _yol = Path.Combine(klasor, DosyaAdi);
        Sinir = sinir;
        _deneme = deneme;
        _bekleme = bekleme ?? TimeSpan.FromMilliseconds(100);
    }

    public static DosyaGosterilenBildirimDeposu Varsayilan() => new(YerelKlasor.Yol);

    public int Sinir { get; }

    public IReadOnlyList<int> YenileriAyir(IReadOnlyCollection<int> adaylar)
    {
        Directory.CreateDirectory(_klasor);
        using var dosya = Ac();
        var kayitli = Oku(dosya);
        var bilinen = kayitli.ToHashSet();
        var yeniler = adaylar.Where(bilinen.Add).ToList();
        if (yeniler.Count == 0)
            return [];
        kayitli.AddRange(yeniler);
        var yazilacak = kayitli.Count > Sinir ? kayitli.GetRange(kayitli.Count - Sinir, Sinir) : kayitli;
        dosya.SetLength(0);
        dosya.Position = 0;
        JsonSerializer.Serialize(dosya, yazilacak);
        return yeniler;
    }

    /// <summary>Kayıtlı kimlikler (eskiden yeniye; testler ve tanı için). Dosya yoksa ya da bozuksa boş.</summary>
    public IReadOnlyList<int> Kayitlilar()
    {
        if (!File.Exists(_yol))
            return [];
        using var dosya = Ac();
        return Oku(dosya);
    }

    private FileStream Ac()
    {
        for (var sira = 1; ; sira++)
        {
            try
            {
                return new FileStream(_yol, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (sira < _deneme)
            {
                Thread.Sleep(_bekleme);
            }
        }
    }

    private static List<int> Oku(FileStream dosya)
    {
        if (dosya.Length == 0)
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<int>>(dosya) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
