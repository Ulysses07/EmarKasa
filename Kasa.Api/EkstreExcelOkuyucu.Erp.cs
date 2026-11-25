using System.Text.RegularExpressions;

namespace Kasa.Api;

/// <summary>ERP12'den okunan cari hareketi (docs/specs/2026-11-25-erp12-aktarimi.md). <paramref name="KasaSatiri"/> değilse (fatura, devir…)
/// yalnız cari hesapta görünür. <paramref name="Yon"/>: cari alacak = Giriş, cari borç = Çıkış; işlem türüyle çelişirse "Belirsiz".</summary>
public sealed record ErpSatir(int ExcelSatir, DateOnly Tarih, string? CariKodu, string CariAdi, string? IslemTuru, string? BelgeNo, string? Aciklama,
    decimal Borc, decimal Alacak, bool KasaSatiri, bool Nakit, string Yon, IReadOnlyList<string> Uyarilar, string Kaynak);

public sealed record ErpOkumaSonucu(IReadOnlyList<ErpSatir> Satirlar, IReadOnlyList<string> Uyarilar, IReadOnlyList<EkstreAtlananSatir> Atlananlar);

// ERP12 cari hareketleri (Excel): banka tablosuyla aynı hücre kuralları; ek olarak cari, cari kodu, belge no sütunları ve işlem türünden
// kasa hareketi / nakit / yön ayrımı.
public static partial class EkstreExcelOkuyucu
{
    private static readonly Dictionary<string, Alan> ErpBasliklar = new(StringComparer.Ordinal)
    {
        ["CARI"] = Alan.Cari,
        ["CARI ADI"] = Alan.Cari,
        ["CARI AD"] = Alan.Cari,
        ["CARI ISMI"] = Alan.Cari,
        ["CARI UNVANI"] = Alan.Cari,
        ["CARI UNVAN"] = Alan.Cari,
        ["CARI HESAP"] = Alan.Cari,
        ["CARI HESAP ADI"] = Alan.Cari,
        ["CARI HESAP UNVANI"] = Alan.Cari,
        ["HESAP ADI"] = Alan.Cari,
        ["HESAP UNVANI"] = Alan.Cari,
        ["UNVAN"] = Alan.Cari,
        ["UNVANI"] = Alan.Cari,
        ["TICARI UNVAN"] = Alan.Cari,
        ["FIRMA"] = Alan.Cari,
        ["FIRMA ADI"] = Alan.Cari,
        ["FIRMA UNVANI"] = Alan.Cari,
        ["MUSTERI"] = Alan.Cari,
        ["MUSTERI ADI"] = Alan.Cari,
        ["TEDARIKCI"] = Alan.Cari,
        ["TEDARIKCI ADI"] = Alan.Cari,
        ["CARI KODU"] = Alan.CariKodu,
        ["CARI KOD"] = Alan.CariKodu,
        ["CARI NO"] = Alan.CariKodu,
        ["CARI HESAP KODU"] = Alan.CariKodu,
        ["HESAP KODU"] = Alan.CariKodu,
        ["MUSTERI KODU"] = Alan.CariKodu,
        ["BELGE NO"] = Alan.BelgeNo,
        ["BELGE NUMARASI"] = Alan.BelgeNo,
        ["EVRAK NO"] = Alan.BelgeNo,
        ["EVRAK NUMARASI"] = Alan.BelgeNo,
        ["FIS NO"] = Alan.BelgeNo,
        ["MAKBUZ NO"] = Alan.BelgeNo,
        ["DEKONT NO"] = Alan.BelgeNo,
        ["FATURA NO"] = Alan.BelgeNo,
        ["ISLEM NO"] = Alan.BelgeNo,
        ["EVRAK TIPI"] = Alan.Islem,
        ["EVRAK TURU"] = Alan.Islem,
        ["BELGE TIPI"] = Alan.Islem,
        ["BELGE TURU"] = Alan.Islem,
        ["FIS TURU"] = Alan.Islem,
        ["FIS TIPI"] = Alan.Islem,
        ["HAREKET TURU"] = Alan.Islem,
        ["ODEME SEKLI"] = Alan.Islem,
        ["ODEME TIPI"] = Alan.Islem,
        ["ODEME TURU"] = Alan.Islem,
        ["TUR"] = Alan.Islem,
        ["TURU"] = Alan.Islem,
        ["TIP"] = Alan.Islem,
        ["BELGE TARIHI"] = Alan.Tarih,
        ["EVRAK TARIHI"] = Alan.Tarih,
        ["FIS TARIHI"] = Alan.Tarih,
    };

    // Normalize edilmiş (büyük harf, Türkçe harfsiz) metinde aranır.
    [GeneratedRegex(@"\b(?:TAHSILAT|TAHSILATI|TAHSIL|ODEME|ODEMESI|ODENDI|TEDIYE|TEDIYESI|NAKIT|NAKDEN|NAKITEN|ELDEN|KASA|KASADAN|KASAYA|HAVALE|HAVALESI|EFT|FAST|BANKA|BANKADAN|BANKAYA|POS|KREDI KARTI|CEK|CEKI|SENET|SENEDI|GELEN|GIDEN|GONDERILEN)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ErpKasaSozcugu();
    [GeneratedRegex(@"\b(?:FATURA|FATURASI|IRSALIYE|IRSALIYESI|DEVIR|DEVRI|ACILIS|VADE FARKI|KUR FARKI|MAHSUP|VIRMAN|DEKONT|DEKONTU|IADE|IADESI|ISKONTO|SIPARIS|SATIS|ALIS)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ErpKasaDisiSozcugu();
    [GeneratedRegex(@"\b(?:FATURA|FATURASI|IRSALIYE|IRSALIYESI|IADE|IADESI)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ErpFaturaSozcugu();
    [GeneratedRegex(@"\b(?:NAKIT|NAKDEN|NAKITEN|ELDEN|KASA|KASADAN|KASAYA)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ErpNakitSozcugu();
    [GeneratedRegex(@"\b(?:BANKA|BANKADAN|BANKAYA|HAVALE|HAVALESI|EFT|FAST|POS|KREDI KARTI|CEK|CEKI|SENET|SENEDI)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ErpBankaSozcugu();
    /// <summary>Çek ya da senet hareketi: kasadaki karşılığı çek kaydıdır (ERP12 günü kasa günü değildir).</summary>
    [GeneratedRegex(@"\b(?:CEK|CEKI|SENET|SENEDI)\b", RegexOptions.CultureInvariant)]
    internal static partial Regex ErpCekSozcugu();
    [GeneratedRegex(@"\b(?:TAHSILAT|TAHSILATI|TAHSIL|GELEN)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ErpGirisSozcugu();
    [GeneratedRegex(@"\b(?:ODEME|ODEMESI|TEDIYE|TEDIYESI|GIDEN|GONDERILEN)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ErpCikisSozcugu();

    public static ErpOkumaSonucu OkuErp(byte[] bytes)
    {
        var sayfalar = Sayfalar(bytes);
        var adaylar = sayfalar.Select(s => (Sayfa: s, Duzen: DuzenBul(s, erp: true))).Where(x => x.Duzen is not null).ToList();
        if (adaylar.Count == 0)
            throw new ExcelOkumaException("ERP12 cari hareket tablosu bulunamadı. Başlık satırında Tarih, Cari (ünvan) ve Borç ile Alacak (ya da Tutar) sütunları olmalı. "
                + $"Görülen başlıklar: {GorulenBasliklar(sayfalar)}.");
        if (adaylar.Count > 1)
            throw new ExcelOkumaException($"Birden çok sayfada cari hareket tablosu var: {string.Join(", ", adaylar.Select(a => a.Sayfa.Ad))}. Yalnız bir sayfa bırakıp yeniden yükleyin.");
        return new ErpOkuma(adaylar[0].Sayfa, adaylar[0].Duzen!).Sonuc();
    }

    private sealed class ErpOkuma(Sayfa sayfa, Duzen duzen)
    {
        private readonly List<string> _hatalar = [];
        private readonly List<EkstreAtlananSatir> _atlanan = [];
        private readonly List<ErpSatir> _satirlar = [];
        private int _sozcuklu, _celisen;

        public ErpOkumaSonucu Sonuc()
        {
            foreach (var satir in sayfa.Satirlar.Where(s => s.No > duzen.BaslikNo))
                Oku(satir);
            if (_hatalar.Count > 0)
                throw new ExcelOkumaException(string.Join("\n", _hatalar.Take(EnCokHata).Prepend($"ERP12 dosyasında {_hatalar.Count} hata var; düzeltilmeden işleme alınmadı."))
                    + (_hatalar.Count > EnCokHata ? $"\n… ve {_hatalar.Count - EnCokHata} hata daha." : ""));
            if (_satirlar.Count == 0)
                throw new ExcelOkumaException("Başlığın altında cari hareket yok.");
            if (_celisen >= 2 && _celisen * 2 > _sozcuklu)
                throw new ExcelOkumaException($"Borç/Alacak, işlem türüyle çelişiyor: tahsilat ya da ödeme yazan {_sozcuklu} satırın {_celisen} tanesinde yön ters. Tahsilat cari Alacak, ödeme cari Borç olmalı; sütunları kontrol edip yeniden yükleyin.");
            var uyarilar = new List<string>
            {
                $"ERP12: '{sayfa.Ad}' sayfası okundu. Sütunlar: {string.Join(", ", duzen.Sutunlar.OrderBy(s => s.Value).Select(s => $"{ErpAlanAdi(s.Key)} {SutunAdi(s.Value)}"))}.",
                $"{_satirlar.Count(s => s.KasaSatiri)} kasa hareketi bekliyor; {_satirlar.Count(s => !s.KasaSatiri)} fatura, devir ya da düzeltme yalnız Cariler'de görünür. ERP12 kasayı tek başına değiştirmez.",
            };
            return new(_satirlar, uyarilar.Select(GirdiDogrulama.Temizle).ToList(), _atlanan);
        }

        private static string ErpAlanAdi(Alan a) => a switch { Alan.Giris => "Alacak", Alan.Cikis => "Borç", _ => AlanAdi(a) };
        private void Hata(Satir satir, string ileti, Alan? alan = null) => _hatalar.Add(GirdiDogrulama.Temizle($"Satır {satir.No}{(alan is { } a && duzen[a] is { } s ? $", {ErpAlanAdi(a)} ({SutunAdi(s)}{satir.No})" : "")}: {ileti}."));
        private void Atla(Satir satir, string neden) => _atlanan.Add(new(1, Kisalt(GirdiDogrulama.Temizle($"Excel satır {satir.No}: {satir.Metin}"), 500), neden));

        /// <summary>Alanın bütün sütunlarının birleşik metni ("Tahsilat · Nakit"); hata hücresi satır hatasıdır.</summary>
        private string? Metin(Satir satir, Alan alan, int sinir)
        {
            var parcalar = new List<string>();
            foreach (var s in duzen.Hepsi(alan))
            {
                if (satir[s].Hata)
                    Hata(satir, "hücrede Excel hatası var", alan);
                else if (GirdiDogrulama.Temizle(satir[s].Metin).Trim() is { Length: > 0 } m && !parcalar.Contains(m))
                    parcalar.Add(m);
            }
            return parcalar.Count == 0 ? null : Kisalt(string.Join(" · ", parcalar), sinir).TrimEnd();
        }

        private decimal? Para(Satir satir, Alan alan)
        {
            if (duzen[alan] is not { } s)
                return null;
            if (ParaOku(satir[s], out var p) is { } hata)
                Hata(satir, hata, alan);
            return p is { Deger: not 0 } para ? para.Deger : null;
        }

        private void Oku(Satir satir)
        {
            if (satir.Hucreler.Select(h => BaslikAlani(h, erp: true)).Count(b => b is not null) >= 3)
                return;
            var hataSayisi = _hatalar.Count;
            var tutarVar = new[] { Alan.Cikis, Alan.Giris, Alan.Tutar }.Any(a => duzen[a] is { } s && !satir[s].Bos);
            var tarihHucresi = satir[duzen[Alan.Tarih] ?? duzen[Alan.Valor]];
            if (Tarih(tarihHucresi) is not { } tarih)
            {
                if (!tutarVar)
                    return;
                if (OzetSozcugu().IsMatch(satir.Normal))
                    Atla(satir, "Özet satırı");
                else
                    Hata(satir, tarihHucresi.Bos ? "tarih boş" : $"\"{Kisalt(tarihHucresi.Metin, 40)}\" tarih olarak okunamadı (31.01.2027 gibi yazın)", Alan.Tarih);
                return;
            }
            var cari = Metin(satir, Alan.Cari, 200);
            var kod = Metin(satir, Alan.CariKodu, 100);
            var tur = Metin(satir, Alan.Islem, 200);
            var belge = Metin(satir, Alan.BelgeNo, 100);
            var aciklama = Metin(satir, Alan.Aciklama, 500);
            if (cari is null)
                Hata(satir, "cari boş", Alan.Cari);
            var borc = Para(satir, Alan.Cikis);
            var alacak = Para(satir, Alan.Giris);
            var tutar = duzen[Alan.Cikis] is null && duzen[Alan.Giris] is null ? Para(satir, Alan.Tutar) : null;
            if (_hatalar.Count > hataSayisi)
                return;
            if (borc < 0 || alacak < 0 || tutar < 0)
            { Hata(satir, "tutar eksi olamaz"); return; }
            if (borc is null && alacak is null && tutar is null)
            { Atla(satir, "Tutarı sıfır ya da boş"); return; }
            var turNormal = EkstreKuralServisi.Normalize(tur ?? aciklama ?? "");
            var hepsi = EkstreKuralServisi.Normalize($"{tur} {aciklama}");
            var kasaSozcugu = ErpKasaSozcugu().IsMatch(turNormal);
            var disSozcugu = ErpKasaDisiSozcugu().IsMatch(turNormal);
            var ikisiDeDolu = borc is not null && alacak is not null;
            if (ikisiDeDolu && borc != alacak)
            { Hata(satir, "Borç ve Alacak ikisi birden dolu; yalnız biri yazılmalı"); return; }
            var kasa = !ikisiDeDolu && (kasaSozcugu || !disSozcugu);
            var sozcukYonu = (ErpGirisSozcugu().IsMatch(hepsi), ErpCikisSozcugu().IsMatch(hepsi)) switch { (true, false) => "Giris", (false, true) => "Cikis", _ => null };
            if (tutar is { } t)
            {
                var taraf = sozcukYonu ?? (turNormal.Contains("SATIS", StringComparison.Ordinal) && !turNormal.Contains("ALIS", StringComparison.Ordinal) ? "Cikis"
                    : turNormal.Contains("ALIS", StringComparison.Ordinal) && !turNormal.Contains("SATIS", StringComparison.Ordinal) ? "Giris" : null);
                if (taraf is null)
                { Hata(satir, "borç mu alacak mı belli değil; Borç ve Alacak sütunlarıyla ya da tahsilat/ödeme türüyle dışa aktarın", Alan.Tutar); return; }
                borc = taraf == "Cikis" ? t : null;
                alacak = taraf == "Cikis" ? null : t;
            }
            var uyarilar = new List<string>();
            var yon = alacak is not null ? "Giris" : "Cikis";
            if (kasa && sozcukYonu is not null)
            {
                _sozcuklu++;
                if (sozcukYonu != yon)
                {
                    _celisen++;
                    yon = "Belirsiz";
                    uyarilar.Add("Borç/Alacak, işlem türüyle çelişiyor; yönü ERP12'den kontrol edin.");
                }
            }
            if (kasa && kasaSozcugu && ErpFaturaSozcugu().IsMatch(turNormal))
                uyarilar.Add("Fatura ya da iade de olabilir; kasa hareketi değilse yok sayın.");
            if (kasa && !kasaSozcugu && turNormal.Length > 0)
                uyarilar.Add("İşlem türü tanınmadı; kasa hareketi değilse yok sayın.");
            _satirlar.Add(new(satir.No, tarih, kod, cari!, tur, belge, aciklama, borc ?? 0m, alacak ?? 0m, kasa,
                kasa && ErpNakitSozcugu().IsMatch(hepsi) && !ErpBankaSozcugu().IsMatch(hepsi), yon, uyarilar, Kisalt(GirdiDogrulama.Temizle($"Excel satır {satir.No}: {satir.Metin}"), 2000)));
        }
    }
}
