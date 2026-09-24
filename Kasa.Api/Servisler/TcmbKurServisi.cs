using System.Globalization;
using System.Net;
using System.Xml.Linq;

namespace Kasa.Api.Servisler;

/// <summary>TCMB'den kur alınamadı; <see cref="Message"/> kullanıcıya gösterilecek Türkçe metindir.</summary>
public sealed class TcmbHatasi(string mesaj, bool baglanti) : Exception(mesaj)
{
    /// <summary>true: TCMB'ye ulaşılamadı (502); false: o ay için kur yok (400).</summary>
    public bool Baglanti { get; } = baglanti;
}

/// <summary>Bir ayın USD/TRY ve EUR/TRY döviz satış ortalaması ve ortalamaya giren gün sayısı.</summary>
public sealed record TcmbAyOrtalamasi(decimal UsdTry, decimal EurTry, int GunSayisi, DateOnly IlkGun, DateOnly SonGun);

/// <summary>
/// TCMB'nin günlük kur XML'lerinden (https://www.tcmb.gov.tr/kurlar/YYYYMM/GGAAYYYY.xml) ayın iş
/// günlerindeki USD ve EUR döviz satış (ForexSelling) kurlarının ortalamasını hesaplar. Resmî tatil
/// gibi kur yayımlanmayan günler (404) atlanır. Bir gün bile ağ/sunucu hatasıyla alınamazsa eksik
/// veriyle ortalama alınmaz: açık bir Türkçe hata döner. TÜFE ve altın için hiçbir değer üretilmez.
/// </summary>
public sealed class TcmbKurServisi(HttpClient http, TimeProvider saat)
{
    public const string AdresKoku = "https://www.tcmb.gov.tr/kurlar/";
    /// <summary>Tüm ayın alınması için üst süre.</summary>
    public static readonly TimeSpan ToplamSure = TimeSpan.FromSeconds(30);
    private const int EszamanliIstek = 4;

    public static Uri GunAdresi(DateOnly gun)
        => new($"{AdresKoku}{gun.ToString("yyyyMM", CultureInfo.InvariantCulture)}/{gun.ToString("ddMMyyyy", CultureInfo.InvariantCulture)}.xml");

    /// <summary>Ayın bugüne kadarki (bugün dahil) hafta içi günleri.</summary>
    public static IReadOnlyList<DateOnly> IsGunleri(DateOnly ay, DateOnly bugun)
    {
        var l = new List<DateOnly>();
        var bas = new DateOnly(ay.Year, ay.Month, 1);
        for (var d = bas; d.Month == bas.Month && d <= bugun; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) l.Add(d);
        return l;
    }

    public async Task<TcmbAyOrtalamasi> AyOrtalamasiAsync(DateOnly ay, CancellationToken iptal = default)
    {
        var bugun = Saat.Bugun(saat);
        var ayBasi = new DateOnly(ay.Year, ay.Month, 1);
        if (ayBasi > bugun) throw new TcmbHatasi("Henüz başlamamış bir ay için kur alınamaz.", baglanti: false);
        var gunler = IsGunleri(ayBasi, bugun);
        if (gunler.Count == 0) throw new TcmbHatasi("Bu ayda henüz iş günü yok; kur alınamaz.", baglanti: false);

        using var sure = CancellationTokenSource.CreateLinkedTokenSource(iptal);
        sure.CancelAfter(ToplamSure);
        using var sinir = new SemaphoreSlim(EszamanliIstek);
        var gorevler = gunler.Select(async g =>
        {
            await sinir.WaitAsync(sure.Token);
            try { return (Gun: g, Kur: await GunAsync(g, sure.Token)); }
            finally { sinir.Release(); }
        }).ToList();

        List<(DateOnly Gun, GunKuru? Kur)> sonuclar;
        try { sonuclar = (await Task.WhenAll(gorevler)).ToList(); }
        catch (OperationCanceledException) when (!iptal.IsCancellationRequested)
        {
            throw new TcmbHatasi("TCMB zamanında yanıt vermedi. Daha sonra tekrar deneyin.", baglanti: true);
        }
        catch (HttpRequestException ex)
        {
            var neden = ex.StatusCode is { } kod ? $"TCMB hata döndü ({(int)kod})."
                : ex.InnerException is FormatException or System.Xml.XmlException or OverflowException ? "TCMB yanıtı beklenen biçimde değil."
                : "TCMB'ye ulaşılamadı (bağlantı hatası).";
            throw new TcmbHatasi(neden + " Eksik veriyle ortalama alınmadı; daha sonra tekrar deneyin.", baglanti: true);
        }

        var bulunan = sonuclar.Where(s => s.Kur is not null).Select(s => (s.Gun, Kur: s.Kur!)).ToList();
        if (bulunan.Count == 0)
            throw new TcmbHatasi($"{AyAdi(ayBasi)} için TCMB'de yayımlanmış kur bulunamadı.", baglanti: false);
        return new TcmbAyOrtalamasi(
            Ortalama(bulunan.Select(b => b.Kur.Usd)),
            Ortalama(bulunan.Select(b => b.Kur.Eur)),
            bulunan.Count, bulunan.Min(b => b.Gun), bulunan.Max(b => b.Gun));
    }

    private static string AyAdi(DateOnly ay) => $"{Metin.Tr.DateTimeFormat.GetMonthName(ay.Month)} {ay.Year}";

    /// <summary>Ortalama, 4 ondalığa (TCMB kurlarının hassasiyeti) yuvarlanır.</summary>
    public static decimal Ortalama(IEnumerable<decimal> kurlar)
    {
        var l = kurlar.ToList();
        return decimal.Round(l.Sum() / l.Count, 4, MidpointRounding.AwayFromZero);
    }

    public sealed record GunKuru(decimal Usd, decimal Eur);

    /// <summary>Günün kuru; o gün kur yayımlanmadıysa (404) null. Diğer hatalar HttpRequestException.</summary>
    private async Task<GunKuru?> GunAsync(DateOnly gun, CancellationToken iptal)
    {
        using var yanit = await http.GetAsync(GunAdresi(gun), iptal);
        if (yanit.StatusCode == HttpStatusCode.NotFound) return null;
        if (!yanit.IsSuccessStatusCode)
            throw new HttpRequestException($"TCMB {(int)yanit.StatusCode} döndü.", null, yanit.StatusCode);
        var xml = await yanit.Content.ReadAsStringAsync(iptal);
        return Ayristir(xml);
    }

    /// <summary>
    /// Günlük XML'den USD ve EUR döviz satış kurunu (birim başına) okur. Biçim beklenmedikse
    /// HttpRequestException (sunucu hatası gibi) — eksik veriyle ortalama alınmaz.
    /// </summary>
    public static GunKuru Ayristir(string xml)
    {
        try
        {
            var kok = XDocument.Parse(xml).Root ?? throw new FormatException();
            decimal Kur(string kod)
            {
                var c = kok.Elements("Currency").FirstOrDefault(e => (string?)e.Attribute("CurrencyCode") == kod || (string?)e.Attribute("Kod") == kod)
                        ?? throw new FormatException();
                var satis = decimal.Parse(((string?)c.Element("ForexSelling"))?.Trim() ?? throw new FormatException(), NumberStyles.Number, CultureInfo.InvariantCulture);
                var birim = decimal.TryParse(((string?)c.Element("Unit"))?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var b) && b > 0 ? b : 1m;
                if (satis <= 0) throw new FormatException();
                return satis / birim;
            }
            return new GunKuru(Kur("USD"), Kur("EUR"));
        }
        catch (Exception ex) when (ex is FormatException or System.Xml.XmlException or OverflowException)
        {
            throw new HttpRequestException("TCMB yanıtı beklenen biçimde değil.", ex);
        }
    }
}
