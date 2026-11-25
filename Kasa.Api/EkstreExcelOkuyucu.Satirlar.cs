using System.Globalization;
using System.Text.RegularExpressions;
using Kasa.Core.Kodlar;

namespace Kasa.Api;

// Excel tablosunun satırları: doğrulama (hata varsa dosya reddedilir), yön kanıtları, bakiye doğrulaması ve PDF okuyucusuyla aynı
// sınıflandırma (docs/specs/2026-11-25-excel-yukleme.md).
public static partial class EkstreExcelOkuyucu
{
    [GeneratedRegex(@"\b(?:TOPLAM|TOPLAMI|DEVIR|DEVREDEN|BAKIYE|BAKIYESI|GENEL TOPLAM|ARA TOPLAM)\b", RegexOptions.CultureInvariant)]
    private static partial Regex OzetSozcugu();
    [GeneratedRegex(@"^(?:DEVIR|DEVREDEN|ONCEKI|ACILIS|BASLANGIC|KAPANIS|SON)(?: DONEM)? ?(?:BAKIYE|BAKIYESI)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DevirAciklamasi();
    [GeneratedRegex(@"^(?:ORNEK)\b", RegexOptions.CultureInvariant)]
    private static partial Regex OrnekSatir();
    [GeneratedRegex(@"^([0-9]{1,2})\s*/\s*([0-9]{1,2})$", RegexOptions.CultureInvariant)]
    private static partial Regex TaksitOrani();

    private sealed class Hareket
    {
        public required Satir Satir { get; init; }
        public required DateOnly Tarih { get; init; }
        public required string Aciklama { get; init; }
        public required decimal Tutar { get; init; }
        public required string Para { get; init; }
        /// <summary>Sütundan ya da B/A ekinden okunan kesin yön; yoksa "Belirsiz".</summary>
        public string Yon { get; set; } = "Belirsiz";
        /// <summary>Tek tutar sütunundaki yazılı işaret (+1/-1), yoksa 0.</summary>
        public int Isaret { get; set; }
        public decimal? Bakiye { get; init; }
    }

    private sealed class Okuma(Sayfa sayfa, Duzen duzen, string kaynak)
    {
        private readonly List<string> _hatalar = [];
        private readonly List<EkstreAtlananSatir> _atlanan = [];
        private readonly List<string> _uyarilar = [];
        private readonly List<Hareket> _hareketler = [];
        private readonly List<BakiyeDugumu> _dugumler = [];
        private bool Kart => kaynak == EkstreKaynaklari.Kart;

        public EkstreOkumaSonucu Sonuc()
        {
            foreach (var satir in sayfa.Satirlar.Where(s => s.No > duzen.BaslikNo))
                SatiriOku(satir);
            if (_hatalar.Count > 0)
                throw new ExcelOkumaException(string.Join("\n", _hatalar.Take(EnCokHata).Prepend($"Excel dosyasında {_hatalar.Count} hata var; düzeltilmeden işleme alınmadı."))
                    + (_hatalar.Count > EnCokHata ? $"\n… ve {_hatalar.Count - EnCokHata} hata daha." : ""));
            if (_hareketler.Count == 0)
                throw new ExcelOkumaException("Başlığın altında hareket satırı yok.");
            IsaretleriYorumla();
            var cozum = BakiyeyiDogrula();
            _uyarilar.Insert(0, $"Excel: '{sayfa.Ad}' sayfası okundu. Sütunlar: {string.Join(", ", duzen.Sutunlar.OrderBy(s => s.Value).Select(s => $"{AlanAdi(s.Key)} {SutunAdi(s.Value)}"))}.");
            _uyarilar.Add(Kart ? "Yalnız TL hareketleri kaydedin. Kart ödemesini ve iadeyi harcama olarak kaydetmeyin."
                : "Yalnız TL hareketlerini kaydedin. Hesaplar arası transfer aynı parayı ikinci kez gelir/gider yapmamalı.");
            if (_atlanan.Count > 0)
                _uyarilar.Add($"{_atlanan.Count} toplam, devir ya da bakiye satırı hareket olarak alınmadı.");
            var alacakIsareti = Kart ? AlacakIsareti() : 0;
            var satirlar = _hareketler.Select((h, i) => Cevir(h, i, alacakIsareti, cozum)).ToList();
            return new(satirlar, _uyarilar.Select(GirdiDogrulama.Temizle).ToList(), _atlanan);
        }

        private static string AlanAdi(Alan a) => a switch
        {
            Alan.Aciklama => "Açıklama",
            Alan.Islem => "İşlem türü",
            Alan.Giris => "Giriş",
            Alan.Cikis => "Çıkış",
            Alan.DovizTutar => "Döviz tutarı",
            Alan.Yon => "Yön",
            Alan.ParaBirimi => "Para birimi",
            Alan.Valor => "Valör",
            _ => a.ToString(),
        };

        private void Hata(Satir satir, string ileti, Alan? alan = null) => _hatalar.Add(GirdiDogrulama.Temizle($"Satır {satir.No}{(alan is { } a && duzen[a] is { } s ? $", {AlanAdi(a)} ({SutunAdi(s)}{satir.No})" : "")}: {ileti}."));
        private void Atla(Satir satir, string neden) => _atlanan.Add(new(1, Kisalt(GirdiDogrulama.Temizle($"Excel satır {satir.No}: {satir.Metin}"), 500), neden));
        private static string Kisalt(string s, int n) => s.Length <= n ? s : s[..n];

        private void SatiriOku(Satir satir)
        {
            // Sayfa içinde yinelenen başlık.
            if (satir.Hucreler.Select(BaslikAlani).Count(b => b is not null) >= 3)
                return;
            var tutarAlanlari = new[] { Alan.Giris, Alan.Cikis, Alan.Tutar, Alan.DovizTutar }.Where(a => duzen[a] is not null).ToList();
            var tutarVar = tutarAlanlari.Any(a => !satir[duzen[a]].Bos);
            var tarihAlani = duzen[Alan.Tarih] is not null ? Alan.Tarih : Alan.Valor;
            var tarihHucresi = satir[duzen[tarihAlani]];
            var tarih = Tarih(tarihHucresi);
            if (tarih is null && duzen[Alan.Tarih] is not null && tarihHucresi.Bos)
                tarih = Tarih(satir[duzen[Alan.Valor]]);
            if (tarih is null)
            {
                var ozet = OzetSozcugu().IsMatch(satir.Normal);
                if (!tutarVar && (satir[duzen[Alan.Bakiye]].Bos || ozet))
                {
                    if (ozet && satir[duzen[Alan.Bakiye]] is { Bos: false } bakiyeHucresi)
                        DevirEkle(bakiyeHucresi);
                    return; // tarihi ve tutarı olmayan açıklama/dipnot satırı
                }
                if (ozet)
                {
                    Atla(satir, "Özet satırı");
                    if (satir[duzen[Alan.Bakiye]] is { Bos: false } bakiyeHucresi)
                        DevirEkle(bakiyeHucresi);
                    else
                        _dugumler.Add(BakiyeDugumu.Kopma);
                    return;
                }
                Hata(satir, tarihHucresi.Bos ? "tarih boş" : $"\"{Kisalt(tarihHucresi.Metin, 40)}\" tarih olarak okunamadı (31.01.2027 gibi yazın)", tarihAlani);
                return;
            }
            var aciklama = Aciklama(satir);
            if (DevirAciklamasi().IsMatch(EkstreKuralServisi.Normalize(aciklama)))
            {
                Atla(satir, "Devir satırı");
                DevirEkle(satir[duzen[Alan.Bakiye]]);
                return;
            }
            var hataSayisi = _hatalar.Count;
            if (OrnekSatir().IsMatch(EkstreKuralServisi.Normalize(aciklama)))
                Hata(satir, "örnek satır; silip yeniden yükleyin");
            if (aciklama.Length == 0)
                Hata(satir, "açıklama boş", duzen[Alan.Aciklama] is not null ? Alan.Aciklama : Alan.Islem);
            foreach (var metinAlani in new[] { Alan.Aciklama, Alan.Islem, Alan.ParaBirimi, Alan.Yon, Alan.Taksit })
                if (satir[duzen[metinAlani]].Hata)
                    Hata(satir, "hücrede Excel hatası var", metinAlani);
            var para = ParaBirimi(satir);
            decimal? tutar = null;
            var yon = "Belirsiz";
            var isaret = 0;
            // Giriş/Çıkış sütunu doluysa yön oradan kesindir; yanındaki Tutar sütunu yalnız aynı tutarı doğrular.
            if (duzen[Alan.Tutar] is null || IkiSutunDolu(satir))
            {
                (tutar, yon) = IkiSutun(satir);
                if (tutar is { } okunan)
                    TutarlaKarsilastir(satir, okunan, yon);
            }
            else
                (tutar, yon, isaret, para) = TekTutar(satir, para);
            decimal? bakiye = null;
            if (duzen[Alan.Bakiye] is { } b)
            {
                if (ParaOku(satir[b], out var okunan) is { } bakiyeHatasi)
                    Hata(satir, bakiyeHatasi, Alan.Bakiye);
                else
                    bakiye = okunan is { } p ? p.EkYonu == "Cikis" ? -Math.Abs(p.Deger) : p.Deger : null;
            }
            if (_hatalar.Count > hataSayisi || tutar is null)
                return;
            _hareketler.Add(new() { Satir = satir, Tarih = tarih.Value, Aciklama = aciklama, Tutar = tutar.Value, Para = para, Yon = yon, Isaret = isaret, Bakiye = bakiye });
            _dugumler.Add(new(_hareketler.Count - 1, bakiye, 0, [new(tutar.Value, yon switch { "Giris" => 1, "Cikis" => -1, _ => isaret }, yon != "Belirsiz")], true,
                tarih.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        private void DevirEkle(Hucre h) => _dugumler.Add(ParaOku(h, out var p) is null && p is { } bakiye ? new(null, bakiye.Deger, 0, [], true) : BakiyeDugumu.Kopma);

        /// <summary>Açıklama; ayrı "İşlem türü" sütunu varsa (ve açıklamada zaten yoksa) başına eklenir: "GELEN FAST AHMET YILMAZ".</summary>
        private string Aciklama(Satir satir)
        {
            var ana = duzen[Alan.Aciklama] is { } a ? satir[a].Metin : "";
            var islem = duzen[Alan.Islem] is { } i ? satir[i].Metin : "";
            var metin = ana.Length == 0 ? islem : islem.Length == 0 || EkstreKuralServisi.Normalize(ana).Contains(EkstreKuralServisi.Normalize(islem), StringComparison.Ordinal) ? ana : $"{islem} {ana}";
            return GirdiDogrulama.Temizle(metin).Trim();
        }

        private string ParaBirimi(Satir satir)
        {
            if (duzen[Alan.ParaBirimi] is { } s && EkstreKuralServisi.Normalize(satir[s].Metin) is { Length: > 0 } yazan)
                return yazan is "TL" or "TRY" or "TURK LIRASI" or "YTL" ? "TRY" : ParaKodu(yazan);
            return duzen.Para ?? "Belirsiz";
        }

        private (decimal? Tutar, string Yon, int Isaret, string Para) TekTutar(Satir satir, string paraBirimi)
        {
            if (ParaOku(satir[duzen[Alan.Tutar]], out var p) is { } hata)
            {
                Hata(satir, hata, Alan.Tutar);
                return (null, "Belirsiz", 0, paraBirimi);
            }
            if (p is null && duzen[Alan.DovizTutar] is { } d && ParaOku(satir[d], out var doviz) is null && doviz is { } yabanci && yabanci.Deger != 0)
                (p, paraBirimi) = (yabanci, duzen.TutarDovizi!);
            if (p is not { } tutar)
            {
                Hata(satir, "tutar boş", Alan.Tutar);
                return (null, "Belirsiz", 0, paraBirimi);
            }
            if (tutar.Deger == 0)
            {
                Hata(satir, "tutar sıfır", Alan.Tutar);
                return (null, "Belirsiz", 0, paraBirimi);
            }
            var yon = tutar.EkYonu ?? "Belirsiz";
            if (YonSutunu(satir) is { } sutunYonu)
            {
                if (yon != "Belirsiz" && yon != sutunYonu)
                    Hata(satir, "tutarın B/A eki ile yön sütunu çelişiyor", Alan.Yon);
                else
                    yon = sutunYonu;
            }
            return (Math.Abs(tutar.Deger), yon, tutar.Deger < 0 ? -1 : tutar.Isaretli ? 1 : 0, paraBirimi);
        }

        /// <summary>Yön sütununda yazan yön; sütun yoksa ya da okunamıyorsa (hata yazılır) null.</summary>
        private string? YonSutunu(Satir satir)
        {
            if (duzen[Alan.Yon] is not { } s || satir[s].Hata)
                return null;
            var yazan = satir[s].Metin;
            var yon = yazan is "-" or "−" ? "Cikis" : yazan == "+" ? "Giris" : EkstreKuralServisi.Normalize(yazan) switch
            {
                "B" or "BORC" or "CIKIS" or "GIDEN" or "CEKILEN" => "Cikis",
                "A" or "ALACAK" or "GIRIS" or "GELEN" or "YATAN" => "Giris",
                _ => null,
            };
            if (yon is null)
                Hata(satir, yazan.Length == 0 ? "yön boş" : $"\"{Kisalt(yazan, 30)}\" yön olarak okunamadı (Giriş/Çıkış ya da A/B yazın)", Alan.Yon);
            return yon;
        }

        /// <summary>Tutar sütununun yanındaki Giriş ya da Çıkış hücresi dolu (sıfırdan farklı) mı.</summary>
        private bool IkiSutunDolu(Satir satir) => new[] { Alan.Giris, Alan.Cikis }.Any(a => duzen[a] is { } s && !satir[s].Bos && (ParaOku(satir[s], out var p) is not null || p is { Deger: not 0 }));

        /// <summary>Giriş/Çıkış ile okunan satırda Tutar ve Yön sütunları da varsa aynı şeyi söylemelidir; çelişen satır hatadır.</summary>
        private void TutarlaKarsilastir(Satir satir, decimal okunan, string yon)
        {
            if (duzen[Alan.Tutar] is { } t && !satir[t].Bos)
            {
                if (ParaOku(satir[t], out var p) is { } hata)
                    Hata(satir, hata, Alan.Tutar);
                else if (p is { } tutar && Math.Abs(tutar.Deger) != okunan)
                    Hata(satir, "Tutar sütunu Giriş/Çıkış tutarıyla aynı değil", Alan.Tutar);
                else if (p?.EkYonu is { } ek && ek != yon)
                    Hata(satir, "tutarın B/A eki Giriş/Çıkış sütunuyla çelişiyor", Alan.Tutar);
            }
            if (YonSutunu(satir) is { } sutunYonu && sutunYonu != yon)
                Hata(satir, "yön sütunu Giriş/Çıkış sütunuyla çelişiyor", Alan.Yon);
        }

        private (decimal? Tutar, string Yon) IkiSutun(Satir satir)
        {
            decimal? Oku(Alan alan)
            {
                if (duzen[alan] is not { } s)
                    return null;
                if (ParaOku(satir[s], out var p) is { } hata)
                {
                    Hata(satir, hata, alan);
                    return null;
                }
                return p is { Deger: not 0 } para ? para.Deger : null;
            }
            var hataSayisi = _hatalar.Count;
            var giris = Oku(Alan.Giris);
            var cikis = Oku(Alan.Cikis);
            if (_hatalar.Count > hataSayisi)
                return (null, "Belirsiz");
            if (giris is not null && cikis is not null)
                Hata(satir, "Giriş ve Çıkış ikisi birden dolu; yalnız biri yazılmalı");
            else if (giris is null && cikis is null)
                Hata(satir, duzen[Alan.Giris] is null ? "Çıkış tutarı boş ya da sıfır" : duzen[Alan.Cikis] is null ? "Giriş tutarı boş ya da sıfır" : "Giriş ve Çıkış ikisi de boş ya da sıfır");
            else if (giris < 0)
                Hata(satir, "Giriş eksi olamaz", Alan.Giris);
            else
                // Bankalar borç sütununu eksi de yazar: -100 çıkıştır.
                return (Math.Abs((giris ?? cikis)!.Value), giris is not null ? "Giris" : "Cikis");
            return (null, "Belirsiz");
        }

        /// <summary>Tek tutar sütununda eksi varsa eksisiz sayılar da işaretlidir (artı); hepsi eksisizse işaret kanıt değildir.</summary>
        private void IsaretleriYorumla()
        {
            if (duzen[Alan.Tutar] is null || !_hareketler.Any(h => h.Isaret < 0))
                return;
            foreach (var h in _hareketler.Where(h => h.Isaret == 0))
                h.Isaret = 1;
        }

        private BakiyeCozumu BakiyeyiDogrula()
        {
            if (Kart || duzen[Alan.Bakiye] is null)
                return BakiyeCozumu.Bos;
            var cozum = EkstreBakiyeZinciri.Coz(_dugumler);
            string? Yazilan(Hareket h) => h.Yon != "Belirsiz" ? h.Yon : h.Isaret < 0 ? "Cikis" : h.Isaret > 0 ? "Giris" : null;
            var kanitli = cozum.Baglar.Where(b => Yazilan(_hareketler[b.Key]) is not null).ToList();
            var celisen = kanitli.Where(b => Yazilan(_hareketler[b.Key]) != (b.Value.Giris ? "Giris" : "Cikis")).ToList();
            if (celisen.Count >= 2 && celisen.Count * 2 > kanitli.Count)
                throw new ExcelOkumaException($"Giriş/çıkış bakiyeyle çelişiyor: {kanitli.Count} satırın {celisen.Count} tanesinde bakiye ters yönde değişiyor "
                    + $"(Excel satırı {string.Join(", ", celisen.Take(5).Select(b => _hareketler[b.Key].Satir.No))}{(celisen.Count > 5 ? "…" : "")}). Sütun başlıklarını kontrol edip yeniden yükleyin.");
            if (cozum.Kopuk.Count > 0)
                _uyarilar.Add($"{cozum.Kopuk.Count} satırda bakiye farkı tutarla tutmuyor (Excel satırı {string.Join(", ", cozum.Kopuk.Take(10).Select(i => _hareketler[i].Satir.No))}{(cozum.Kopuk.Count > 10 ? "…" : "")}). "
                    + "Eksik ya da fazladan satır olabilir; bankadan yeniden indirip karşılaştırın.");
            return cozum;
        }

        /// <summary>Kart tutarlarında alacağın işareti: ödeme ve iade satırları alacak, faiz/ücret/vergi borçtur; kanıtlar birleşmezse 0.</summary>
        private int AlacakIsareti()
        {
            var kanit = new HashSet<int>();
            foreach (var h in _hareketler.Where(h => h.Yon == "Belirsiz" && h.Isaret != 0))
            {
                var normal = EkstreMetinOkuyucu.Normalize(h.Aciklama);
                var sinif = EkstreMetinOkuyucu.Classify(normal);
                var anahtar = EkstreMetinOkuyucu.CardKey(normal, sinif);
                var masraf = EkstreMetinOkuyucu.IsFee(sinif);
                if (anahtar == "Odeme" || anahtar == "Iade" && !masraf)
                    kanit.Add(h.Isaret);
                else if (anahtar is null && masraf)
                    kanit.Add(-h.Isaret);
            }
            return kanit.Count == 1 ? kanit.Single() : 0;
        }

        private EkstreOkunanSatir Cevir(Hareket h, int sira, int alacakIsareti, BakiyeCozumu cozum)
        {
            var uyarilar = new List<string>();
            var normal = EkstreMetinOkuyucu.Normalize(h.Aciklama);
            var sinif = EkstreMetinOkuyucu.Classify(normal);
            var yon = h.Yon;
            var dayanak = yon == "Belirsiz" ? null : YonKaynaklari.Sutun;
            string oneri;
            if (h.Para == "Belirsiz")
                uyarilar.Add("Para birimi yazmıyor; bu satırın TL olduğunu doğrulayın.");
            else if (h.Para != "TRY")
                uyarilar.Add("Bu satır farklı para biriminde; TL içe aktarmaya uygun değil.");
            if (Kart)
                (yon, dayanak, oneri) = EkstreMetinOkuyucu.KartKarari(normal, sinif, yon, dayanak, h.Isaret, alacakIsareti, uyarilar);
            else
            {
                if (yon == "Belirsiz" && h.Isaret != 0)
                    (yon, dayanak) = (h.Isaret < 0 ? "Cikis" : "Giris", YonKaynaklari.Isaret);
                if (cozum.Baglar.TryGetValue(sira, out var bag))
                {
                    var bakiyeYonu = bag.Giris ? "Giris" : "Cikis";
                    if (yon == "Belirsiz")
                        (yon, dayanak) = (bakiyeYonu, YonKaynaklari.Bakiye);
                    else if (yon != bakiyeYonu)
                    {
                        (yon, dayanak) = ("Belirsiz", null);
                        uyarilar.Add($"Yazan yön bakiyeyle çelişiyor (bakiye {(bag.Giris ? "artmış" : "azalmış")}); yön tahmin edilmedi.");
                    }
                }
                else if (cozum.Kopuk.Contains(sira))
                    uyarilar.Add("Bakiye farkı bu satırın tutarıyla tutmuyor; Excel'den kontrol edin.");
                if (yon == "Belirsiz" && uyarilar.All(u => !u.StartsWith("Yazan yön", StringComparison.Ordinal)) && EkstreMetinOkuyucu.DescribedDirection(normal) is { } soylenen)
                    (yon, dayanak) = (soylenen, YonKaynaklari.Sozcuk);
                oneri = EkstreMetinOkuyucu.BankaOnerisi(normal, sinif, yon);
            }
            EkstreMetinOkuyucu.OrtakUyarilar(normal, sinif, uyarilar);
            if (Kart && (TaksitSutunu(h.Satir) ?? EkstreMetinOkuyucu.TaksitSirasi(normal)) is (var no, var adet))
            {
                oneri = EkstreIslemTurleri.Atla;
                uyarilar.Add($"Taksitli işlemin {no}/{adet}. taksidi. Harcama kartta taksitli girildiyse kaydetmeyin; ilk kez giriyorsanız Kartlar bölümünden toplam tutar ve {adet} taksitle girin.");
            }
            if (yon == "Belirsiz")
                uyarilar.Add("Giriş/çıkış yönü kesin okunamadı; işlem türünü seçin.");
            return new(sira + 1, 1, Kisalt(GirdiDogrulama.Temizle($"Excel satır {h.Satir.No}: {h.Satir.Metin}"), 2000), h.Tarih, Kisalt(h.Aciklama, 500).TrimEnd(), h.Tutar, yon, oneri, sinif, h.Para,
                uyarilar.Select(u => u.Replace("PDF", "Excel", StringComparison.Ordinal)).ToList(), dayanak);
        }

        private (int, int)? TaksitSutunu(Satir satir) => duzen[Alan.Taksit] is { } s && TaksitOrani().Match(satir[s].Metin) is { Success: true } m
            && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is var no && int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) is var adet
            && adet is >= 2 and <= 60 && no >= 1 && no <= adet ? (no, adet) : null;
    }
}
