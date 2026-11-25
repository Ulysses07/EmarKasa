using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using ExcelDataReader.Exceptions;
using Kasa.Core.Kodlar;

namespace Kasa.Api;

/// <summary>Okunamayan ya da hatalı Excel; iletisi kullanıcıya gösterilir (hata listesi satır satırdır).</summary>
public sealed class ExcelOkumaException(string message, int statusCode = 422) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Excel (.xlsx, .xls) hareket dosyası okuyucusu (docs/specs/2026-11-25-excel-yukleme.md): tablo başlıktan bulunur, satırlar PDF
/// okuyucusuyla aynı biçime (<see cref="EkstreOkunanSatir"/>) çevrilir. Hareket satırlarında tek hata bile varsa dosya reddedilir ve bütün
/// hatalar Excel satır numarasıyla döner; hiçbir değer tahmin edilmez.
/// </summary>
public static partial class EkstreExcelOkuyucu
{
    private const int EnCokSatir = 5000, EnCokSutun = 60, BaslikSatiri = 60, EnCokHata = 50;
    private const long EnCokAcilmis = 100L * 1024 * 1024;

    static EkstreExcelOkuyucu() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private enum Alan { Tarih, Valor, Aciklama, Islem, Giris, Cikis, Tutar, DovizTutar, Yon, Bakiye, ParaBirimi, Taksit, Cari, CariKodu, BelgeNo }

    private static readonly Dictionary<string, Alan> Basliklar = new(StringComparer.Ordinal)
    {
        ["TARIH"] = Alan.Tarih,
        ["ISLEM TARIHI"] = Alan.Tarih,
        ["ISLEM TARIHI SAATI"] = Alan.Tarih,
        ["ISLEM TARIH SAAT"] = Alan.Tarih,
        ["ISLEM ZAMANI"] = Alan.Tarih,
        ["TARIH SAAT"] = Alan.Tarih,
        ["HAREKET TARIHI"] = Alan.Tarih,
        ["HARCAMA TARIHI"] = Alan.Tarih,
        ["VALOR"] = Alan.Valor,
        ["VALOR TARIHI"] = Alan.Valor,
        ["ACIKLAMA"] = Alan.Aciklama,
        ["ACIKLAMALAR"] = Alan.Aciklama,
        ["ISLEM ACIKLAMASI"] = Alan.Aciklama,
        ["ISLEM ACIKLAMA"] = Alan.Aciklama,
        ["HAREKET ACIKLAMASI"] = Alan.Aciklama,
        ["HARCAMA ACIKLAMASI"] = Alan.Aciklama,
        ["DETAY"] = Alan.Aciklama,
        ["ISLEM DETAYI"] = Alan.Aciklama,
        ["ISYERI"] = Alan.Aciklama,
        ["ISYERI ADI"] = Alan.Aciklama,
        ["UYE ISYERI"] = Alan.Aciklama,
        ["UYE ISYERI ADI"] = Alan.Aciklama,
        ["HARCAMA YERI"] = Alan.Aciklama,
        ["ISLEM"] = Alan.Islem,
        ["ISLEM TURU"] = Alan.Islem,
        ["ISLEM TIPI"] = Alan.Islem,
        ["ISLEM ADI"] = Alan.Islem,
        ["HAREKET TIPI"] = Alan.Islem,
        ["GIRIS"] = Alan.Giris,
        ["GIRIS TUTARI"] = Alan.Giris,
        ["ALACAK"] = Alan.Giris,
        ["ALACAK TUTARI"] = Alan.Giris,
        ["YATAN"] = Alan.Giris,
        ["YATAN TUTAR"] = Alan.Giris,
        ["GELEN"] = Alan.Giris,
        ["GELEN TUTAR"] = Alan.Giris,
        ["CIKIS"] = Alan.Cikis,
        ["CIKIS TUTARI"] = Alan.Cikis,
        ["BORC"] = Alan.Cikis,
        ["BORC TUTARI"] = Alan.Cikis,
        ["CEKILEN"] = Alan.Cikis,
        ["CEKILEN TUTAR"] = Alan.Cikis,
        ["GIDEN"] = Alan.Cikis,
        ["GIDEN TUTAR"] = Alan.Cikis,
        ["HARCAMA"] = Alan.Cikis,
        ["HARCAMA TUTARI"] = Alan.Cikis,
        ["TUTAR"] = Alan.Tutar,
        ["ISLEM TUTARI"] = Alan.Tutar,
        ["HAREKET TUTARI"] = Alan.Tutar,
        ["MEBLAG"] = Alan.Tutar,
        ["B A"] = Alan.Yon,
        ["BA"] = Alan.Yon,
        ["A B"] = Alan.Yon,
        ["BORC ALACAK"] = Alan.Yon,
        ["ALACAK BORC"] = Alan.Yon,
        ["GIRIS CIKIS"] = Alan.Yon,
        ["YON"] = Alan.Yon,
        ["ISLEM YONU"] = Alan.Yon,
        ["BAKIYE"] = Alan.Bakiye,
        ["KALAN BAKIYE"] = Alan.Bakiye,
        ["SON BAKIYE"] = Alan.Bakiye,
        ["HESAP BAKIYESI"] = Alan.Bakiye,
        ["ISLEM SONRASI BAKIYE"] = Alan.Bakiye,
        ["PARA BIRIMI"] = Alan.ParaBirimi,
        ["DOVIZ"] = Alan.ParaBirimi,
        ["DOVIZ CINSI"] = Alan.ParaBirimi,
        ["DOVIZ KODU"] = Alan.ParaBirimi,
        ["TAKSIT"] = Alan.Taksit,
        ["TAKSIT BILGISI"] = Alan.Taksit,
    };

    /// <summary>Hücre: değer (metin, sayı, tarih) ya da Excel hatası.</summary>
    private readonly record struct Hucre(object? Deger, bool Hata)
    {
        public bool Bos => !Hata && (Deger is null || Deger is string s && string.IsNullOrWhiteSpace(s));
        public string Metin => Hata ? "#HATA" : Deger switch
        {
            null => "",
            string s => Bosluk().Replace(s.Replace(' ', ' '), " ").Trim(),
            DateTime t => t.TimeOfDay == TimeSpan.Zero ? t.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : t.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
            double d => d.ToString("0.##########", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => Deger.ToString() ?? "",
        };
    }

    private sealed record Satir(int No, Hucre[] Hucreler)
    {
        public Hucre this[int? sutun] => sutun is { } s && s < Hucreler.Length ? Hucreler[s] : default;
        public string Metin => string.Join(" | ", Hucreler.Where(h => !h.Bos).Select(h => h.Metin));
        public string Normal => EkstreKuralServisi.Normalize(string.Join(' ', Hucreler.Where(h => h.Deger is string).Select(h => h.Metin)));
    }

    private sealed record Sayfa(string Ad, List<Satir> Satirlar);

    private sealed class Duzen
    {
        public required int BaslikNo { get; init; }
        public Dictionary<Alan, int> Sutunlar { get; } = [];
        /// <summary>Aynı alanın ek sütunları (ERP12: "İşlem türü" ve "Ödeme şekli" birlikte okunur).</summary>
        public Dictionary<Alan, List<int>> Ekler { get; } = [];
        public string? Para { get; set; }
        public string? TutarDovizi { get; set; }
        public int? this[Alan a] => Sutunlar.TryGetValue(a, out var s) ? s : null;
        public IEnumerable<int> Hepsi(Alan a) => (this[a] is { } s ? [s] : Enumerable.Empty<int>()).Concat(Ekler.GetValueOrDefault(a) ?? []);
    }

    /// <summary>Dosya Excel mi (xlsx/xls imzası); HTML ve diğerleri değil.</summary>
    public static bool ExcelMi(ReadOnlySpan<byte> bytes) => Zip(bytes) || Ole(bytes);
    private static bool Zip(ReadOnlySpan<byte> b) => b.Length > 4 && b[..4].SequenceEqual("PK\u0003\u0004"u8);
    private static bool Ole(ReadOnlySpan<byte> b) => b.Length > 8 && b[..8].SequenceEqual((ReadOnlySpan<byte>)[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]);
    /// <summary>Uzantısı .xls olsa da içeriği web sayfası (bankaların "Excel" diye verdiği HTML).</summary>
    public static bool WebSayfasiMi(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes[..Math.Min(bytes.Length, 512)]).TrimStart('ï', '»', '¿', ' ', '\t', '\r', '\n') is var bas
        && (bas.StartsWith("<html", StringComparison.OrdinalIgnoreCase) || bas.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || bas.StartsWith("<table", StringComparison.OrdinalIgnoreCase)
            || bas.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) || bas.StartsWith("<meta", StringComparison.OrdinalIgnoreCase));

    public static EkstreOkumaSonucu Oku(byte[] bytes, string kaynak)
    {
        var sayfalar = Sayfalar(bytes);
        var adaylar = sayfalar.Select(s => (Sayfa: s, Duzen: DuzenBul(s, erp: false))).Where(x => x.Duzen is not null).ToList();
        if (adaylar.Count == 0)
            throw new ExcelOkumaException("Hareket tablosu bulunamadı. Başlık satırında Tarih, Açıklama ve tutar (Giriş ve Çıkış ya da Tutar) sütunları olmalı. "
                + $"Görülen başlıklar: {GorulenBasliklar(sayfalar)}. Örnek Excel'i indirip ona göre doldurabilirsiniz.");
        if (adaylar.Count > 1)
            throw new ExcelOkumaException($"Birden çok sayfada hareket tablosu var: {string.Join(", ", adaylar.Select(a => a.Sayfa.Ad))}. Yalnız bir sayfa bırakıp yeniden yükleyin.");
        return new Okuma(adaylar[0].Sayfa, adaylar[0].Duzen!, kaynak).Sonuc();
    }

    // ---- Dosya ----

    private static List<Sayfa> Sayfalar(byte[] bytes)
    {
        if (Zip(bytes))
            ZipDenetle(bytes);
        try
        {
            using var akis = new MemoryStream(bytes, writable: false);
            using var okuyucu = ExcelReaderFactory.CreateReader(akis, new ExcelReaderConfiguration { FallbackEncoding = Encoding.GetEncoding(1254) });
            var sayfalar = new List<Sayfa>();
            var dolu = 0;
            do
            {
                var satirlar = new List<Satir>();
                for (var no = 1; okuyucu.Read(); no++)
                {
                    if (no > 200_000)
                        throw new ExcelOkumaException($"'{okuyucu.Name}' sayfası çok uzun; yalnız hareketleri bırakıp yeniden kaydedin.");
                    var n = okuyucu.FieldCount;
                    var hucreler = new Hucre[Math.Min(n, EnCokSutun)];
                    for (var i = 0; i < hucreler.Length; i++)
                        hucreler[i] = new(okuyucu.GetValue(i), okuyucu.GetCellError(i) is not null);
                    if (hucreler.All(h => h.Bos))
                        continue;
                    if (++dolu > EnCokSatir)
                        throw new ExcelOkumaException($"Excel en çok {EnCokSatir} dolu satır içerebilir; daha kısa tarih aralığı seçin.");
                    satirlar.Add(new(no, hucreler));
                }
                sayfalar.Add(new(okuyucu.Name ?? "Sayfa", satirlar));
            } while (okuyucu.NextResult());
            return sayfalar;
        }
        catch (InvalidPasswordException) { throw new ExcelOkumaException("Parola korumalı Excel okunamıyor. Parolayı kaldırıp yeniden kaydedin."); }
        catch (Exception e) when (e is not (ExcelOkumaException or OutOfMemoryException))
        { throw new ExcelOkumaException("Excel okunamadı. Dosya bozuk ya da desteklenmeyen bir yapıda; Excel'de açıp .xlsx olarak yeniden kaydedin."); }
    }

    /// <summary>Zip'i açmadan önce sınırlar: dosya sayısı ve gerçekten açılmış boyut (bildirilen boyuta güvenilmez).</summary>
    private static void ZipDenetle(byte[] bytes)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            if (zip.Entries.Count > 2000)
                throw new ExcelOkumaException("Excel dosyası çok fazla parça içeriyor; yalnız hareketleri bırakıp yeniden kaydedin.");
            var tampon = new byte[81920];
            long toplam = 0;
            foreach (var parca in zip.Entries)
            {
                using var akis = parca.Open();
                int okunan;
                while ((okunan = akis.Read(tampon)) > 0)
                    if ((toplam += okunan) > EnCokAcilmis)
                        throw new ExcelOkumaException("Excel dosyasının içeriği çok büyük; yalnız hareketleri bırakıp yeniden kaydedin.");
            }
        }
        catch (InvalidDataException) { throw new ExcelOkumaException("Excel okunamadı. Dosya bozuk; Excel'de açıp .xlsx olarak yeniden kaydedin."); }
    }

    // ---- Başlık ----

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Bosluk();
    [GeneratedRegex(@"\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex Parantez();
    [GeneratedRegex(@"(?:^| )(TL|TRY|USD|EUR|GBP|DOLAR|EURO|STERLIN)$", RegexOptions.CultureInvariant)]
    private static partial Regex SondakiPara();

    /// <summary>Başlık hücresi: alan ve (başlıkta yazıyorsa) para birimi. "Tutar (TL)", "TUTAR USD", "Bakiye (isteğe bağlı)".</summary>
    /// <param name="erp">ERP12 tablosunun ek başlıkları (cari, belge no, evrak tipi) da tanınır.</param>
    private static (Alan Alan, string? Para)? BaslikAlani(Hucre h, bool erp)
    {
        if (h.Deger is not string)
            return null;
        var ham = h.Metin.Replace("₺", " TL ").Replace("$", " USD ").Replace("€", " EUR ");
        var tam = EkstreKuralServisi.Normalize(ham);
        var ad = EkstreKuralServisi.Normalize(Parantez().Replace(ham, " "));
        var para = SondakiPara().Match(tam) is { Success: true } m ? ParaKodu(m.Groups[1].Value) : null;
        foreach (var aday in new[] { ad, SondakiPara().Replace(ad, "") })
            if (erp && ErpBasliklar.TryGetValue(aday, out var erpAlani))
                return (erpAlani, para);
            else if (Basliklar.TryGetValue(aday, out var alan))
                return (alan, para);
        return null;
    }

    private static string ParaKodu(string metin) => metin switch { "TL" or "TRY" => "TRY", "DOLAR" => "USD", "EURO" => "EUR", "STERLIN" => "GBP", _ => metin };

    private static Duzen? DuzenBul(Sayfa sayfa, bool erp)
    {
        foreach (var satir in sayfa.Satirlar.TakeWhile(s => s.No <= BaslikSatiri * 5).Take(BaslikSatiri))
        {
            var alanlar = satir.Hucreler.Select((h, i) => (Sutun: i, Baslik: BaslikAlani(h, erp), h.Metin)).Where(x => x.Baslik is not null).ToList();
            bool Var(params Alan[] a) => alanlar.Any(x => a.Contains(x.Baslik!.Value.Alan));
            if (!Var(Alan.Tarih, Alan.Valor) || !(erp ? Var(Alan.Cari) : Var(Alan.Aciklama, Alan.Islem)) || !Var(Alan.Tutar, Alan.Giris, Alan.Cikis))
                continue;
            var duzen = new Duzen { BaslikNo = satir.No };
            foreach (var grup in alanlar.GroupBy(x => x.Baslik!.Value.Alan))
            {
                var alan = grup.Key;
                var secenekler = grup.ToList();
                if (alan == Alan.Tutar)
                {
                    // "Tutar (TL)" ve "Tutar (USD)" yan yana: TL sütunu tutardır, döviz sütunu yalnız TL boşsa bilgidir.
                    var doviz = secenekler.Where(x => x.Baslik!.Value.Para is { } p && p != "TRY").ToList();
                    if (doviz.Count == 1 && secenekler.Count == 2)
                    {
                        duzen.Sutunlar[Alan.DovizTutar] = doviz[0].Sutun;
                        duzen.TutarDovizi = doviz[0].Baslik!.Value.Para;
                        secenekler = secenekler.Except(doviz).ToList();
                    }
                }
                if (erp && alan is Alan.Islem or Alan.Aciklama or Alan.BelgeNo && secenekler.Count > 1)
                {
                    duzen.Ekler[alan] = secenekler.Skip(1).Select(x => x.Sutun).ToList();
                    secenekler = secenekler.Take(1).ToList();
                }
                if (secenekler.Count > 1)
                    throw new ExcelOkumaException($"'{sayfa.Ad}' sayfası, satır {satir.No}: aynı sütun birden çok kez var ({string.Join(", ", secenekler.Select(x => $"{x.Metin} {SutunAdi(x.Sutun)}"))}). Fazla sütunu silip yeniden yükleyin.");
                duzen.Sutunlar[alan] = secenekler[0].Sutun;
                if (alan is Alan.Tutar or Alan.Giris or Alan.Cikis && secenekler[0].Baslik!.Value.Para is { } para)
                {
                    if (duzen.Para is { } onceki && onceki != para)
                        throw new ExcelOkumaException($"'{sayfa.Ad}' sayfası, satır {satir.No}: tutar sütunlarının para birimleri farklı ({onceki} ve {para}). Yalnız TL hareketlerini bırakıp yeniden yükleyin.");
                    duzen.Para = para;
                }
            }
            duzen.Para ??= UstBilgiParasi(sayfa, satir.No);
            return duzen;
        }
        return null;
    }

    [GeneratedRegex(@"(?:PARA BIRIMI|DOVIZ CINSI|DOVIZ KODU|HESAP CINSI|HESAP TURU)(?: [A-Z]+)? (TL|TRY|USD|EUR|GBP)\b", RegexOptions.CultureInvariant)]
    private static partial Regex UstBilgi();

    /// <summary>Başlığın üstündeki hesap bilgisinde yazan para birimi ("Para Birimi: TL").</summary>
    private static string? UstBilgiParasi(Sayfa sayfa, int baslikNo)
    {
        var bulunan = sayfa.Satirlar.TakeWhile(s => s.No < baslikNo).Select(s => UstBilgi().Match(s.Normal)).Where(m => m.Success).Select(m => ParaKodu(m.Groups[1].Value)).Distinct().ToList();
        return bulunan.Count == 1 ? bulunan[0] : null;
    }

    private static string GorulenBasliklar(List<Sayfa> sayfalar)
    {
        var metinler = sayfalar.SelectMany(s => s.Satirlar.Take(BaslikSatiri)).Where(s => s.Hucreler.Count(h => h.Deger is string) >= 3)
            .SelectMany(s => s.Hucreler.Where(h => h.Deger is string && !h.Bos).Select(h => h.Metin)).Distinct().Take(12).Select(m => m.Length > 30 ? m[..30] + "…" : m).ToList();
        return metinler.Count == 0 ? "yok" : GirdiDogrulama.Temizle(string.Join(", ", metinler));
    }

    /// <summary>0 → A, 26 → AA.</summary>
    private static string SutunAdi(int i) => i < 26 ? ((char)('A' + i)).ToString() : SutunAdi(i / 26 - 1) + (char)('A' + i % 26);

    // ---- Hücre değerleri ----

    private static readonly string[] TarihBicimleri = ["d.M.yyyy", "d/M/yyyy", "d-M-yyyy", "yyyy-M-d", "yyyy/M/d", "yyyy.M.d"];
    [GeneratedRegex(@"^(?<tarih>[0-9]{1,4}[./-][0-9]{1,2}[./-][0-9]{1,4})(?:(?:\s+|\s*-\s*|T)[0-9]{1,2}[:.][0-9]{2}(?:[:.][0-9]{2})?(?:\.[0-9]+)?Z?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex TarihMetni();

    private static DateOnly? Tarih(Hucre h)
    {
        DateOnly? gun = h.Deger switch
        {
            DateTime t => DateOnly.FromDateTime(t),
            // Tarih biçimi verilmemiş seri numara (45000 gibi).
            double d when d is >= 36526 and < 73416 => DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(d))),
            string when TarihMetni().Match(h.Metin) is { Success: true } m
                && DateOnly.TryParseExact(m.Groups["tarih"].Value, TarihBicimleri, CultureInfo.InvariantCulture, DateTimeStyles.None, out var g) => g,
            _ => null,
        };
        return gun is { Year: >= 2000 and <= 2100 } ? gun : null;
    }

    /// <summary>Okunan tutar: işaretli değer, yazılı işaret ya da B/A ekiyle gelen yön.</summary>
    private readonly record struct Para(decimal Deger, bool Isaretli, string? EkYonu);

    [GeneratedRegex(@"^(?<bas>[-+−])?\(?(?<sayi>[0-9][0-9.,]*)\)?(?<son>[-+−])?(?<ek>[BA])?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ParaMetni();
    [GeneratedRegex(@"^[0-9]{1,3}(?:\.[0-9]{3})+(?:,[0-9]{1,2})?$|^[0-9]+(?:,[0-9]{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex VirgulOndalik();
    [GeneratedRegex(@"^[0-9]{1,3}(?:,[0-9]{3})+\.[0-9]{1,2}$|^[0-9]+\.[0-9]{1,2}$|^[0-9]{1,3}(?:,[0-9]{3}){2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex NoktaOndalik();

    /// <summary>Tutar; hücre boşsa deger null ve hata null. Metin tutarda nokta binlik, virgül ondalıktır; "12,500" gibi belirsiz yazım hatadır.</summary>
    private static string? ParaOku(Hucre h, out Para? para)
    {
        para = null;
        if (h.Hata)
            return "hücrede Excel hatası var";
        if (h.Bos)
            return null;
        if (h.Deger is double or int)
        {
            var d = Convert.ToDouble(h.Deger, CultureInfo.InvariantCulture);
            if (double.IsNaN(d) || Math.Abs(d) > 999_999_999_999.99)
                return $"\"{h.Metin}\" izin verilen sınırda değil";
            var yuvarlak = Math.Round((decimal)d, 2);
            if (Math.Abs((double)yuvarlak - d) > 0.000_001)
                return $"\"{h.Metin}\" kuruştan küçük hane içeriyor";
            para = new(yuvarlak, d < 0, null);
            return null;
        }
        if (h.Deger is not string)
            return $"\"{h.Metin}\" tutar değil";
        var metin = Sade(h.Metin);
        var m = ParaMetni().Match(metin);
        if (!m.Success || m.Groups["bas"].Success && m.Groups["son"].Success || metin.Contains('(') != metin.Contains(')'))
            return $"\"{h.Metin}\" tutar olarak okunamadı";
        var sayi = m.Groups["sayi"].Value;
        string? sade = VirgulOndalik().IsMatch(sayi) ? sayi.Replace(".", "").Replace(',', '.') : NoktaOndalik().IsMatch(sayi) ? sayi.Replace(",", "") : null;
        if (sade is null || !decimal.TryParse(sade, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var deger) || deger > 999_999_999_999.99m)
            return $"\"{h.Metin}\" tutar olarak okunamadı (1234,56 ya da 1.234,56 gibi yazın)";
        var isaret = m.Groups["bas"].Success ? m.Groups["bas"].Value : m.Groups["son"].Value;
        var eksi = isaret is "-" or "−" || metin.Contains('(');
        para = new(eksi ? -deger : deger, isaret.Length > 0 || metin.Contains('('), m.Groups["ek"].Value.ToUpperInvariant() switch { "B" => "Cikis", "A" => "Giris", _ => null });
        return null;
    }

    /// <summary>Boşluk ve para birimi yazısı atılmış tutar metni.</summary>
    private static string Sade(string metin)
    {
        var s = new StringBuilder(metin.Length);
        foreach (var c in metin)
            if (!char.IsWhiteSpace(c) && c != '₺')
                s.Append(c);
        var t = s.ToString();
        foreach (var ek in new[] { "TL", "TRY" })
            t = t.EndsWith(ek, StringComparison.OrdinalIgnoreCase) ? t[..^ek.Length] : t.StartsWith(ek, StringComparison.OrdinalIgnoreCase) ? t[ek.Length..] : t;
        return t;
    }
}
