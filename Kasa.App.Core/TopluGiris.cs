using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Excel'den yapıştırılan (sekmeyle ayrılmış) ya da CSV dosyasından okunan metnin ayrıştırılması
/// (saf fonksiyonlar, birim testli). xlsx dosyası <see cref="TopluXlsx"/> ile aynı tabloya çevrilir.
/// </summary>
public static class TopluMetin
{
    /// <summary>Tek yüklemede en fazla satır (sunucu sınırıyla aynı).</summary>
    public const int EnFazlaSatir = 1000;

    /// <summary>Dosyadan okunacak en büyük boyut.</summary>
    public const int EnBuyukDosya = 2 * 1024 * 1024;

    /// <summary>
    /// Ayırıcı: ilk 20 dolu satırın birinde (tırnak dışında) sekme varsa sekme, yoksa noktalı virgül,
    /// yoksa virgül (Türkçe Excel CSV'si ';' kullanır; ',' ondalık ayırıcı olabildiği için en son
    /// denenir). Banka dökümlerinin başlıktan önceki tek hücreli satırları kararı bozmasın diye
    /// yalnız ilk satıra bakılmaz.
    /// </summary>
    public static char AyiriciBul(string metin)
    {
        var satirlar = metin.Split('\n').Select(s => TirnakDisi(s.TrimEnd('\r'))).Where(s => s.Trim().Length > 0).Take(20).ToList();
        foreach (var aday in new[] { '\t', ';', ',' })
            if (satirlar.Any(s => s.Contains(aday))) return aday;
        return '\t';
    }

    /// <summary>Satırın çift tırnak içindeki kısımları atılmış hali (ayırıcı aramak için).</summary>
    private static string TirnakDisi(string satir)
    {
        if (!satir.Contains('"')) return satir;
        var sb = new StringBuilder(satir.Length);
        var icinde = false;
        foreach (var c in satir)
        {
            if (c == '"') { icinde = !icinde; continue; }
            if (!icinde) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Tabloyu sekmeyle ayrılmış metne çevirir (xlsx dosyası önizlemeye yapıştırılmış gibi dökülür).
    /// Sekme, satır sonu içeren ya da tırnakla başlayan hücre tırnağa alınır.
    /// </summary>
    public static string TsvYaz(IEnumerable<IReadOnlyList<string>> tablo)
    {
        var sb = new StringBuilder();
        foreach (var satir in tablo)
        {
            for (var i = 0; i < satir.Count; i++)
            {
                if (i > 0) sb.Append('\t');
                var h = satir[i] ?? "";
                if (h.IndexOfAny(['\t', '\n', '\r']) >= 0 || h.StartsWith('"')) sb.Append('"').Append(h.Replace("\"", "\"\"")).Append('"');
                else sb.Append(h);
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Metni satır ve hücrelere böler: çift tırnaklı hücre (içinde ayırıcı, satır sonu ve "" kaçışı
    /// olabilir) desteklenir; BOM ve tamamen boş satırlar atılır; hücreler kırpılır.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Ayristir(string? metin, char? ayirici = null)
    {
        var sonuc = new List<IReadOnlyList<string>>();
        if (string.IsNullOrEmpty(metin)) return sonuc;
        if (metin[0] == '﻿') metin = metin[1..];
        var ay = ayirici ?? AyiriciBul(metin);
        var satir = new List<string>();
        var hucre = new StringBuilder();
        var tirnakta = false;
        var hucreBasi = true;

        void HucreBitir()
        {
            satir.Add(hucre.ToString().Trim());
            hucre.Clear();
            hucreBasi = true;
        }
        void SatirBitir()
        {
            HucreBitir();
            if (satir.Any(h => h.Length > 0)) sonuc.Add(satir);
            satir = new List<string>();
        }

        for (var i = 0; i < metin.Length; i++)
        {
            var c = metin[i];
            if (tirnakta)
            {
                if (c == '"')
                {
                    if (i + 1 < metin.Length && metin[i + 1] == '"') { hucre.Append('"'); i++; }
                    else tirnakta = false;
                }
                else hucre.Append(c);
                continue;
            }
            if (c == '"' && hucreBasi && hucre.ToString().Trim().Length == 0) { hucre.Clear(); tirnakta = true; hucreBasi = false; continue; }
            if (c == ay) { HucreBitir(); continue; }
            if (c == '\r') continue;
            if (c == '\n') { SatirBitir(); continue; }
            hucre.Append(c);
            if (!char.IsWhiteSpace(c)) hucreBasi = false;
        }
        SatirBitir();
        return sonuc;
    }

    /// <summary>
    /// Dosya içeriğini metne çevirir: UTF-8 (BOM'lu/BOM'suz); geçerli UTF-8 değilse Türkçe Windows
    /// kod sayfası (1254, Excel'in "CSV (virgülle ayrılmış)" varsayılanı).
    /// </summary>
    public static string Coz(byte[] icerik)
    {
        if (icerik.Length >= 3 && icerik[0] == 0xEF && icerik[1] == 0xBB && icerik[2] == 0xBF)
            return Encoding.UTF8.GetString(icerik, 3, icerik.Length - 3);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(icerik);
        }
        catch (DecoderFallbackException)
        {
            var tr = CodePagesEncodingProvider.Instance.GetEncoding(1254);
            return tr is null ? Encoding.Latin1.GetString(icerik) : tr.GetString(icerik);
        }
    }

    private static readonly string[] TarihBicimleri =
    [
        "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd",
        "dd.MM.yy", "d.M.yy",
    ];

    /// <summary>Tarih: gg.aa.yyyy (ve / - ayırıcılı), yyyy-aa-gg; Excel'in eklediği saat kısmı atılır. Geçersizse null.</summary>
    public static DateOnly? TarihCoz(string? metin)
    {
        var s = (metin ?? "").Trim();
        if (s.Length == 0) return null;
        var bosluk = s.IndexOf(' ');
        if (bosluk > 0) s = s[..bosluk];                                   // "24.09.2026 00:00:00"
        var t = s.IndexOf('T');
        if (t > 0 && s.Length > 10 && s[4] == '-') s = s[..t];            // "2026-09-24T00:00:00"
        return DateOnly.TryParseExact(s, TarihBicimleri, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    /// <summary>
    /// Tutar: Türkçe biçim (<see cref="ParaGiris"/>); eksi işaretli ya da parantezli tutarın mutlak
    /// değeri döner (işlem her zaman giderdir). İşaret kaybolmaz: <see cref="EksiMi"/> onu söyler ve
    /// tabloda iki işaret birlikte varsa doğrulama yalnız gider tarafını kabul eder
    /// (<see cref="TopluIsaret"/>).
    /// </summary>
    public static (decimal? Tutar, string? Hata) TutarCoz(string? metin)
    {
        var s = (metin ?? "").Trim();
        if (s.Length == 0) return (null, "Tutar boş.");
        if (s.StartsWith('(') && s.EndsWith(')')) s = s[1..^1].Trim();
        if (s.StartsWith('-') || s.StartsWith('−')) s = s[1..].Trim();
        if (s.EndsWith('-')) s = s[..^1].Trim();
        var p = ParaGiris.Ayristir(s);
        if (!p.Gecerli) return (null, p.Hata);
        if (p.Tutar <= 0) return (null, "Tutar sıfırdan büyük olmalı.");
        return (p.Tutar, null);
    }

    /// <summary>Tutar metni eksi mi ("-250", "−250", "250-", "(250)")? Banka dökümünde borç (çıkan para).</summary>
    public static bool EksiMi(string? metin)
    {
        var s = (metin ?? "").Trim();
        return s.StartsWith('-') || s.StartsWith('−') || s.EndsWith('-') || (s.StartsWith('(') && s.EndsWith(')'));
    }

    /// <summary>Tutarı hücreye yazılacak Türkçe biçime çevirir ("1.500,50").</summary>
    public static string Bicimle(decimal tutar) => tutar.ToString("#,##0.00", Kultur.Turkce);

    /// <summary>Tip metni: boşsa (true, null) = otomatik; tanınırsa tip; tanınmazsa (false, null).</summary>
    public static (bool Gecerli, GiderTipi? Tip) TipCoz(string? metin)
    {
        var s = CariBenzerlik.Normallestir(metin).Replace(" ", "");
        if (s.Length == 0) return (true, null);
        return s switch
        {
            "cari" or "c" => (true, GiderTipi.Cari),
            "sabit" or "sabitgider" or "sg" or "gider" or "sabitgiderler" => (true, GiderTipi.SabitGider),
            "kk" or "kredikarti" or "kart" or "kredi" => (true, GiderTipi.KrediKarti),
            _ => (false, null),
        };
    }

    /// <summary>
    /// Tutarı <paramref name="parca"/> eşit parçaya böler; kuruş artığı ilk parçaya eklenir
    /// (parçaların toplamı her zaman tutara eşittir).
    /// </summary>
    public static IReadOnlyList<decimal> Bol(decimal tutar, int parca)
    {
        if (parca < 1) throw new ArgumentOutOfRangeException(nameof(parca));
        var pay = decimal.Floor(tutar * 100m / parca) / 100m;
        var sonuc = Enumerable.Repeat(pay, parca).ToArray();
        sonuc[0] += tutar - pay * parca;
        return sonuc;
    }
}

/// <summary>
/// Toplu yükleme sütunları. Borç / Alacak / Bakiye yalnız banka dökümü başlığından gelir: borç ve
/// alacak tek bir işaretli tutara çevrilir (borç eksi), bakiye okunmaz ama dökümü tanıtır.
/// </summary>
public enum TopluSutun { Tarih, Cari, Tutar, Kanal, Tip, Not, Kart, Borc, Alacak, Bakiye }

/// <summary>Tablodaki tutar işaretinin anlamı (hangi taraf gider).</summary>
public enum TopluIsaret
{
    /// <summary>Tabloda tek işaret var: bütün tutarlar gider (eski tablolar, yalnız çıkışlı döküm).</summary>
    Yok,
    /// <summary>Eksi (borç, hesaptan çıkan) tutarlar gider; artılar (alacak, giren para) kaydedilmez.</summary>
    EksiGider,
    /// <summary>Artı tutarlar gider; eksiler (iade, düzeltme) kaydedilmez.</summary>
    ArtiGider,
}

/// <summary>Başlık satırı tanıma ve sütun eşleme.</summary>
public static class TopluBaslik
{
    /// <summary>Başlık yoksa sütun sırası: Tarih, Cari, Tutar, Kanal, Tip, Not, Kart.</summary>
    public static IReadOnlyDictionary<TopluSutun, int> Varsayilan { get; } = new Dictionary<TopluSutun, int>
    {
        [TopluSutun.Tarih] = 0, [TopluSutun.Cari] = 1, [TopluSutun.Tutar] = 2, [TopluSutun.Kanal] = 3,
        [TopluSutun.Tip] = 4, [TopluSutun.Not] = 5, [TopluSutun.Kart] = 6,
    };

    private static string Anahtar(string hucre) => CariBenzerlik.Normallestir(hucre).Replace(" ", "");

    private static TopluSutun? Sutun(string hucre) => Anahtar(hucre) switch
    {
        "tarih" or "islemtarihi" or "date" => TopluSutun.Tarih,
        "cari" or "firma" or "unvan" or "kalem" or "carikalem" or "ad" or "cariadi" or "giderkalemi" => TopluSutun.Cari,
        "tutar" or "tutartl" or "miktar" or "tl" or "tutartry" or "bedel" or "tutari" or "islemtutari"
            or "islemmiktari" or "meblag" => TopluSutun.Tutar,
        "kanal" or "sube" or "kanaladi" => TopluSutun.Kanal,
        "tip" or "gidertipi" or "tur" or "odemetipi" => TopluSutun.Tip,
        "not" or "notlar" => TopluSutun.Not,
        "kart" or "kredikarti" or "kartadi" => TopluSutun.Kart,
        "borc" or "borctl" or "borctutari" => TopluSutun.Borc,
        "alacak" or "alacaktl" or "alacaktutari" => TopluSutun.Alacak,
        "bakiye" or "bakiyetl" or "kalanbakiye" or "hesapbakiyesi" => TopluSutun.Bakiye,
        _ => null,
    };

    /// <summary>Hücre bir "Açıklama" başlığı mı (banka dökümünde işlem açıklaması)?</summary>
    public static bool AciklamaMi(string hucre) => Anahtar(hucre) is "aciklama" or "aciklamalar" or "islemaciklamasi";

    /// <summary>Başlık bir banka dökümünün mü (borç, alacak ya da bakiye sütunu var)?</summary>
    public static bool BankaDokumu(IReadOnlyDictionary<TopluSutun, int> harita)
        => harita.ContainsKey(TopluSutun.Borc) || harita.ContainsKey(TopluSutun.Alacak) || harita.ContainsKey(TopluSutun.Bakiye);

    /// <summary>
    /// Satır bir başlık mı (en az iki hücre tanınıyor ve biri Tarih, Tutar, Borç ya da Alacak)? Başlıksa
    /// sütun eşlemesi; "Açıklama" Cari sütunu yoksa Cari, varsa Not sayılır. Değilse null.
    /// </summary>
    public static IReadOnlyDictionary<TopluSutun, int>? Coz(IReadOnlyList<string> satir)
    {
        var harita = new Dictionary<TopluSutun, int>();
        var aciklamalar = new List<int>();
        for (var i = 0; i < satir.Count; i++)
        {
            if (AciklamaMi(satir[i])) { aciklamalar.Add(i); continue; }
            if (Sutun(satir[i]) is { } s && !harita.ContainsKey(s)) harita[s] = i;
        }
        foreach (var i in aciklamalar)
        {
            if (!harita.ContainsKey(TopluSutun.Cari)) harita[TopluSutun.Cari] = i;
            else if (!harita.ContainsKey(TopluSutun.Not)) harita[TopluSutun.Not] = i;
        }
        var baslik = harita.Count >= 2 && (harita.ContainsKey(TopluSutun.Tarih) || harita.ContainsKey(TopluSutun.Tutar)
                                           || harita.ContainsKey(TopluSutun.Borc) || harita.ContainsKey(TopluSutun.Alacak));
        return baslik ? harita : null;
    }

    /// <summary>Başlık aranan en fazla satır (banka dökümünde başlıktan önce hesap bilgisi satırları olur).</summary>
    public const int BaslikArama = 20;

    /// <summary>
    /// Tablonun ilk <see cref="BaslikArama"/> satırında başlık arar: bulunursa satır no'su ve eşlemesi
    /// (öncesindeki satırlar veri değildir), yoksa null (başlıksız tablo, varsayılan sütun sırası).
    /// </summary>
    public static (int Satir, IReadOnlyDictionary<TopluSutun, int> Harita)? Bul(IReadOnlyList<IReadOnlyList<string>> tablo)
    {
        for (var i = 0; i < Math.Min(BaslikArama, tablo.Count); i++)
            if (Coz(tablo[i]) is { } h) return (i, h);
        return null;
    }
}

/// <summary>Önizleme tablosunun bir satırı: hücreler düzenlenebilir, her değişiklikte yeniden doğrulanır.</summary>
public partial class TopluSatir : ObservableObject
{
    [ObservableProperty] private int _sira;
    [ObservableProperty] private string _tarihMetni = "";
    [ObservableProperty] private string _cari = "";
    [ObservableProperty] private string _tutarMetni = "";
    [ObservableProperty] private string _kanal = "";
    [ObservableProperty] private string _tipMetni = "";
    [ObservableProperty] private string _not = "";
    [ObservableProperty] private string _kartMetni = "";

    /// <summary>Satır hatası (varsa satır kaydedilemez).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Gecerli))]
    private string? _hata;

    /// <summary>Bilgi (yeni cari, ileri tarih, pasif kanal…): kaydı engellemez.</summary>
    [ObservableProperty] private string? _bilgi;

    /// <summary>Cari kayıtlı değil; "yeni carileri ekle" açıksa kayıtla birlikte eklenir.</summary>
    [ObservableProperty] private bool _yeniCari;

    /// <summary>
    /// Satırın ayrıştırmadan gelen yapı hatası (virgüllü CSV'de hücreye bölünmüş kuruş, fazladan
    /// sütun). Kullanıcı tutarı elle düzeltince kalkar; o zamana dek satır kaydedilemez.
    /// </summary>
    [ObservableProperty] private string? _yapiHatasi;

    /// <summary>Tutar gider tarafında değil (alacak / iade): kaydedilmez, toplu kaldırılabilir.</summary>
    public bool GiderDegil { get; internal set; }

    public bool Gecerli => Hata is null;

    partial void OnTutarMetniChanged(string value) => YapiHatasi = null;

    // Doğrulamanın çözdüğü değerler (kayıtlı yazımlar).
    public DateOnly? Tarih { get; internal set; }
    public decimal? Tutar { get; internal set; }
    public string? KanalAdi { get; internal set; }
    public GiderTipi? Tip { get; internal set; }
    public int? KartId { get; internal set; }
    public string? CariAdi { get; internal set; }

    /// <summary>Hücrelerin kopyası (bölme).</summary>
    public TopluSatir Kopya() => new()
    {
        TarihMetni = TarihMetni, Cari = Cari, TutarMetni = TutarMetni, Kanal = Kanal, TipMetni = TipMetni,
        Not = Not, KartMetni = KartMetni,
    };

    public const string FazlaSutunMesaji =
        "Satırda başlıktan fazla sütun var: virgülle ayrılmış dosyada ondalık virgül ya da addaki virgül hücreyi " +
        "bölmüş olabilir. Dosyayı ';' ayırıcıyla kaydedin ya da tutarı düzeltin.";
    public const string BorcAlacakBirlikteMesaji = "Borç ve alacak birlikte dolu; tutarı düzeltin.";
    public static string KurusAyrildiMesaji(string tutar, string yan) =>
        $"Tutarın kuruşu yan sütuna düşmüş olabilir ('{tutar}' ve '{yan}'). Dosyayı ';' ayırıcıyla kaydedin ya da tutarı düzeltin.";

    /// <summary>
    /// Hücreler: sütun eşlemesine göre (eksik sütun boş). Banka dökümünde borç/alacak sütunları tek
    /// işaretli tutara çevrilir (borç eksi). Ayırıcı virgülse (<paramref name="ayirici"/>) satır yapısı
    /// denetlenir: <paramref name="sutunSayisi"/>'ndan (başlık hücre sayısı; başlık yoksa varsayılan 7)
    /// fazla dolu sütun ya da tutarın yanında kuruşa benzeyen sayı yapı hatasıdır.
    /// </summary>
    public static TopluSatir Olustur(IReadOnlyList<string> hucreler, IReadOnlyDictionary<TopluSutun, int> harita,
        char ayirici = '\t', int? sutunSayisi = null)
    {
        string H(TopluSutun s) => harita.TryGetValue(s, out var i) && i < hucreler.Count ? hucreler[i] : "";
        string? yapi = null;
        var tutar = H(TopluSutun.Tutar);
        if (!harita.ContainsKey(TopluSutun.Tutar) && (harita.ContainsKey(TopluSutun.Borc) || harita.ContainsKey(TopluSutun.Alacak)))
            (tutar, yapi) = BorcAlacak(H(TopluSutun.Borc), H(TopluSutun.Alacak));
        if (ayirici == ',') yapi ??= VirgulYapisi(hucreler, harita, sutunSayisi ?? TopluBaslik.Varsayilan.Count);
        var s = new TopluSatir
        {
            TarihMetni = H(TopluSutun.Tarih), Cari = H(TopluSutun.Cari), TutarMetni = tutar,
            Kanal = H(TopluSutun.Kanal), TipMetni = H(TopluSutun.Tip), Not = H(TopluSutun.Not), KartMetni = H(TopluSutun.Kart),
        };
        s.YapiHatasi = yapi;   // TutarMetni atandıktan sonra (o atama yapı hatasını siler)
        return s;
    }

    /// <summary>Borç (çıkan) → eksi tutar, alacak (giren) → artı tutar. İkisi birden doluysa yapı hatası.</summary>
    private static (string Tutar, string? Hata) BorcAlacak(string borc, string alacak)
    {
        var (b, _) = TopluMetin.TutarCoz(borc);
        var (a, _) = TopluMetin.TutarCoz(alacak);
        if (b is { } bt && a is not null) return ("-" + TopluMetin.Bicimle(bt), BorcAlacakBirlikteMesaji);
        if (b is { } bt2) return ("-" + TopluMetin.Bicimle(bt2), null);
        if (a is { } at) return (TopluMetin.Bicimle(at), null);
        return (borc.Trim().Length > 0 ? borc : alacak, null);   // boş ya da geçersiz: satır doğrulaması söyler
    }

    private static readonly System.Text.RegularExpressions.Regex KurusGibi = new(@"^\d{1,3}(\.\d{1,2})?$");

    /// <summary>Virgülle ayrılmış satırın yapı denetimi (tırnaksız "1.500,50" iki hücreye bölünür).</summary>
    private static string? VirgulYapisi(IReadOnlyList<string> hucreler, IReadOnlyDictionary<TopluSutun, int> harita, int sutunSayisi)
    {
        var sonDolu = 0;
        for (var i = 0; i < hucreler.Count; i++) if (hucreler[i].Length > 0) sonDolu = i + 1;
        if (sonDolu > sutunSayisi) return FazlaSutunMesaji;
        if (harita.TryGetValue(TopluSutun.Tutar, out var t) && t + 1 < hucreler.Count && hucreler[t].Length > 0
            && char.IsAsciiDigit(hucreler[t][^1]) && KurusGibi.IsMatch(hucreler[t + 1])
            && !harita.Any(x => x.Value == t + 1 && x.Key is TopluSutun.Borc or TopluSutun.Alacak or TopluSutun.Bakiye))
            return KurusAyrildiMesaji(hucreler[t], hucreler[t + 1]);
        return null;
    }
}

/// <summary>Toplu satır doğrulamasının bağlamı (kayıtlı adlar ve seçenekler).</summary>
public sealed record TopluBaglam(
    IReadOnlyList<KanalDto> Kanallar,
    IReadOnlyList<string> Cariler,
    IReadOnlyList<string> Kalemler,
    IReadOnlyList<KrediKartiDto> Kartlar,
    DateOnly Bugun,
    bool YeniCarileriEkle,
    string? VarsayilanKanal,
    TopluIsaret Isaret = TopluIsaret.Yok);

/// <summary>Satır doğrulaması: sunucunun tek işlem kurallarının istemci aynası (son söz sunucunundur).</summary>
public static class TopluDogrulama
{
    public const string OrtakKanal = "Ortak";

    public const string ArtiGiderDegilMesaji =
        "Artı tutar: hesaba giren para (alacak), gider değil. Kaydedilmez; satırı kaldırın.";
    public const string EksiGiderDegilMesaji =
        "Eksi tutar: iade ya da hesaba giren para, gider değil. Kaydedilmez; satırı kaldırın.";

    public static void Dogrula(TopluSatir s, TopluBaglam b)
    {
        var hatalar = new List<string>();
        var bilgiler = new List<string>();
        s.Tarih = null; s.Tutar = null; s.KanalAdi = null; s.Tip = null; s.KartId = null; s.CariAdi = null;
        s.GiderDegil = false;
        var yeniCari = false;
        if (s.YapiHatasi is { } yapi) hatalar.Add(yapi);

        // Tarih
        if (s.TarihMetni.Trim().Length == 0) hatalar.Add("Tarih boş.");
        else if (TopluMetin.TarihCoz(s.TarihMetni) is not { } tarih) hatalar.Add("Tarih geçersiz (gg.aa.yyyy).");
        else if (tarih.Year is < 2000 or > 2100) hatalar.Add("Tarih 2000 ile 2100 arasında olmalı.");
        else
        {
            s.Tarih = tarih;
            if (tarih > b.Bugun) bilgiler.Add("İleri tarih: raporlara o gün yansır");
        }

        // Tutar (iki işaretli tabloda yalnız gider tarafı kaydedilir)
        var (tutar, tutarHatasi) = TopluMetin.TutarCoz(s.TutarMetni);
        var eksi = TopluMetin.EksiMi(s.TutarMetni);
        if (tutarHatasi is not null) hatalar.Add(tutarHatasi);
        else if (b.Isaret == TopluIsaret.EksiGider && !eksi) { hatalar.Add(ArtiGiderDegilMesaji); s.GiderDegil = true; }
        else if (b.Isaret == TopluIsaret.ArtiGider && eksi) { hatalar.Add(EksiGiderDegilMesaji); s.GiderDegil = true; }
        else s.Tutar = tutar;

        // Kanal
        var kanal = s.Kanal.Trim();
        if (kanal.Length == 0) kanal = b.VarsayilanKanal ?? "";
        if (kanal.Length == 0) hatalar.Add("Kanal boş.");
        else if (Esit(kanal, OrtakKanal)) s.KanalAdi = OrtakKanal;
        else if (b.Kanallar.FirstOrDefault(k => Esit(k.Ad, kanal)) is { } k)
        {
            s.KanalAdi = k.Ad;
            if (!k.Aktif) bilgiler.Add("Pasif kanal");
        }
        else hatalar.Add($"'{Kisalt(kanal)}' adında bir kanal yok.");

        // Tip ve kart
        var cari = s.Cari.Trim();
        var (tipGecerli, tip) = TopluMetin.TipCoz(s.TipMetni);
        var kartMetni = s.KartMetni.Trim();
        if (!tipGecerli) hatalar.Add("Tip geçersiz (Cari, Sabit gider ya da K.K).");
        else
        {
            if (kartMetni.Length > 0) tip = GiderTipi.KrediKarti;   // karta bağlı harcama K.K'dır
            tip ??= b.Kalemler.Any(a => Esit(a, cari)) && !b.Cariler.Any(a => Esit(a, cari)) ? GiderTipi.SabitGider : GiderTipi.Cari;
            s.Tip = tip;
            if (tip == GiderTipi.KrediKarti)
            {
                if (kartMetni.Length > 0)
                {
                    if (b.Kartlar.FirstOrDefault(x => Esit(x.Ad, kartMetni)) is { } kart) s.KartId = kart.Id;
                    else hatalar.Add($"'{Kisalt(kartMetni)}' adında bir kart yok.");
                }
                else if (b.Kartlar.Count == 1) s.KartId = b.Kartlar[0].Id;
                else hatalar.Add(b.Kartlar.Count == 0 ? "Önce Kredi Kartları sayfasından kart ekleyin." : "Kart adını yazın (K.K satırı).");
            }
        }

        // Cari / kalem
        if (cari.Length == 0) hatalar.Add("Cari boş.");
        else if (cari.Length > 200) hatalar.Add("Cari adı en fazla 200 karakter olabilir.");
        else if (s.Tip == GiderTipi.SabitGider)
        {
            if (b.Kalemler.FirstOrDefault(a => Esit(a, cari)) is { } kalem) s.CariAdi = kalem;
            else hatalar.Add($"'{Kisalt(cari)}' adında bir sabit gider kalemi yok.");
        }
        else if (s.Tip is not null)
        {
            if (b.Cariler.FirstOrDefault(a => Esit(a, cari)) is { } kayitli) s.CariAdi = kayitli;
            else if (b.YeniCarileriEkle)
            {
                s.CariAdi = cari;
                yeniCari = true;
                var benzer = CariBenzerlik.Benzerler(cari, b.Cariler, 1);
                bilgiler.Add(benzer.Count > 0 ? $"Yeni cari (benzer: {benzer[0]})" : "Yeni cari eklenecek");
            }
            else
            {
                var benzer = CariBenzerlik.Benzerler(cari, b.Cariler, 1);
                hatalar.Add($"'{Kisalt(cari)}' adında bir cari yok." + (benzer.Count > 0 ? $" Benzer: {benzer[0]}." : ""));
            }
        }

        if (s.Not.Length > 1000) hatalar.Add("Not en fazla 1000 karakter olabilir.");

        s.YeniCari = yeniCari && hatalar.Count == 0;
        s.Hata = hatalar.Count > 0 ? string.Join(" ", hatalar) : null;
        s.Bilgi = bilgiler.Count > 0 ? string.Join(" · ", bilgiler) : null;
    }

    /// <summary>Geçerli satırın gönderilecek işlemi.</summary>
    public static IslemYaz Islem(TopluSatir s)
        => new(s.Tarih!.Value, s.CariAdi!, s.Tutar!.Value, s.KanalAdi!, s.Tip!.Value,
            string.IsNullOrWhiteSpace(s.Not) ? null : s.Not.Trim(), s.KartId);

    private static bool Esit(string a, string b) => string.Compare(a.Trim(), b.Trim(), Kultur.Turkce, CompareOptions.IgnoreCase) == 0;

    private static string Kisalt(string s) => s.Length <= 40 ? s : s[..40] + "…";
}
