using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// ERP12 tediye karşılaştırması (SALT OKUNUR): ERP12'den dışa aktarılan CSV ile kasadaki işlemleri
/// yan yana koyar ve üç liste verir — eşleşenler, yalnız ERP12'de olanlar, yalnız kasada olanlar.
/// Sunucuya hiçbir şey yazmaz; dosya cihazda okunur. Sütun eşleştirmesi (tarih/cari/tutar) başlık
/// adlarıyla cihazda hatırlanır. Eşleşme kuralı <see cref="Erp12Karsilastirici"/>'dadır.
/// </summary>
public partial class Erp12KarsilastirmaViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    private readonly IAyarDeposu _ayarlar;

    public Erp12KarsilastirmaViewModel(IKasaApi api, TimeProvider? zaman = null, IAyarDeposu? ayarlar = null) : base(zaman)
    {
        _api = api;
        _ayarlar = ayarlar ?? new BellekAyarDeposu();
        _baslangic = Bugun;
        _bitis = Bugun;
    }

    /// <summary>Sütun eşleştirmesinin cihazdaki anahtarı.</summary>
    public const string AyarAnahtari = "erp12.eslestirme";
    public const long EnFazlaCsvBoyutu = 20L * 1024 * 1024;

    public const string SeciciYokMesaji = "Bu cihazda dosya seçilemiyor.";
    public const string DosyaSecinMesaji = "Önce ERP12'den aldığınız CSV dosyasını seçin.";
    public const string BoyutMesaji = "Dosya 20 MB'tan büyük; ERP12'den daha kısa bir tarih aralığı alın.";
    public const string SutunSecinMesaji = "Tarih, cari ve tutar sütunlarını seçin.";
    public const string AyniSutunMesaji = "Tarih, cari ve tutar için farklı sütunlar seçin.";
    public const string AralikMesaji = "Başlangıç tarihi bitişten sonra olamaz.";
    public const string SatirYokMesaji = "Seçilen tarih aralığında okunabilen ERP12 satırı yok.";

    /// <summary>Platform dosya seçicisi (MAUI DI verir).</summary>
    public IDosyaSecici? DosyaSecici { get; set; }

    // ---------------------------------------------------------------- dosya ve sütunlar

    [ObservableProperty] private string? _dosyaAdi;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DosyaVar), nameof(DosyaBilgisi))] private CsvTablo? _tablo;
    public bool DosyaVar => Tablo is not null;

    /// <summary>"tediye.csv · 42 satır · ayırıcı ';' · Windows-1254"</summary>
    public string DosyaBilgisi => Tablo is { } t
        ? $"{DosyaAdi} · {t.Satirlar.Count} satır · ayırıcı {AyiriciAdi(t.Ayirici)} · {t.Kodlama}" + (t.BaslikVar ? "" : " · başlık satırı yok")
        : "Dosya seçilmedi.";

    public ObservableCollection<SecimCipi> TarihCipleri { get; } = new();
    public ObservableCollection<SecimCipi> CariCipleri { get; } = new();
    public ObservableCollection<SecimCipi> TutarCipleri { get; } = new();

    /// <summary>İlk birkaç satır, sütun seçimine yardım için ("a · b · c").</summary>
    public ObservableCollection<string> Onizleme { get; } = new();

    [ObservableProperty] private int? _tarihSutunu;
    [ObservableProperty] private int? _cariSutunu;
    [ObservableProperty] private int? _tutarSutunu;

    // ---------------------------------------------------------------- aralık ve seçenekler

    [ObservableProperty] private DateTime _baslangic;
    [ObservableProperty] private DateTime _bitis;

    /// <summary>Kapalı (varsayılan): yalnız "Cari" tipindeki kasa işlemleri karşılaştırılır (tediye = cariye ödeme).</summary>
    [ObservableProperty] private bool _tumTipler;

    // ---------------------------------------------------------------- sonuç

    public ObservableCollection<Erp12SonucSatiri> Eslesenler { get; } = new();
    public ObservableCollection<Erp12SonucSatiri> YalnizErp12 { get; } = new();
    public ObservableCollection<Erp12SonucSatiri> YalnizKasa { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonucVar), nameof(EslesenYok), nameof(YalnizErp12Yok), nameof(YalnizKasaYok))]
    private Erp12Sonuc? _sonuc;
    public bool SonucVar => Sonuc is not null;

    // Sonuç listeleri boşken gösterilen açıklamalar için.
    public bool EslesenYok => Sonuc is { Eslesenler.Count: 0 };
    public bool YalnizErp12Yok => Sonuc is { YalnizErp12.Count: 0 };
    public bool YalnizKasaYok => Sonuc is { YalnizKasa.Count: 0 };

    [ObservableProperty] private string _eslesenOzeti = "";
    [ObservableProperty] private string _yalnizErp12Ozeti = "";
    [ObservableProperty] private string _yalnizKasaOzeti = "";

    /// <summary>Tarihi, tutarı ya da carisi okunamayan satırlar ("3 satır okunamadı: 4, 9, 12").</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(OkunamayanVar))] private string? _okunamayanOzeti;
    public bool OkunamayanVar => OkunamayanOzeti is not null;

    private int _surum;

    partial void OnTarihSutunuChanged(int? value) => SutunDegisti(TarihCipleri, value);
    partial void OnCariSutunuChanged(int? value) => SutunDegisti(CariCipleri, value);
    partial void OnTutarSutunuChanged(int? value) => SutunDegisti(TutarCipleri, value);
    partial void OnTumTiplerChanged(bool value) => SonucuTemizle();

    private void SutunDegisti(ObservableCollection<SecimCipi> cipler, int? secili)
    {
        for (int i = 0; i < cipler.Count; i++) cipler[i].Secili = i == secili;
        SonucuTemizle();
    }

    // ---------------------------------------------------------------- komutlar

    [RelayCommand]
    private Task DosyaSecAsync() => CalistirAsync(async () =>
    {
        Dogrula(DosyaSecici is not null, SeciciYokMesaji);
        if (await DosyaSecici!.CsvSecAsync() is not { } d) return;
        DosyaYukle(d);
    });

    /// <summary>Seçilen dosyayı okur, sütunları tahmin eder (önce hatırlanan eşleştirme) ve aralığı dosyaya göre kurar.</summary>
    public void DosyaYukle(SecilenDosya d)
    {
        Dogrula(d.Boyut <= EnFazlaCsvBoyutu, BoyutMesaji);
        var tablo = Erp12Csv.Oku(d.Icerik);
        SonucuTemizle();
        DosyaAdi = d.Ad;
        Tablo = tablo;

        foreach (var l in new[] { TarihCipleri, CariCipleri, TutarCipleri })
        {
            l.Clear();
            foreach (var b in tablo.Basliklar) l.Add(new SecimCipi(b));
        }
        Onizleme.Clear();
        foreach (var s in tablo.Satirlar.Take(3)) Onizleme.Add(string.Join(" · ", s));

        var (tarih, cari, tutar) = HatirlananEslestirme(tablo) ?? Erp12SutunTahmini.Tahmin(tablo);
        TarihSutunu = tarih;
        CariSutunu = cari;
        TutarSutunu = tutar;
        // Setter aynı değerde tetiklenmez: çip vurgusunu her durumda tazele.
        SutunDegisti(TarihCipleri, TarihSutunu);
        SutunDegisti(CariCipleri, CariSutunu);
        SutunDegisti(TutarCipleri, TutarSutunu);
        AraligiDosyayaGoreKur();
    }

    [RelayCommand] private void SecTarihSutunu(SecimCipi c) { TarihSutunu = TarihCipleri.IndexOf(c); AraligiDosyayaGoreKur(); }
    [RelayCommand] private void SecCariSutunu(SecimCipi c) => CariSutunu = CariCipleri.IndexOf(c);
    [RelayCommand] private void SecTutarSutunu(SecimCipi c) => TutarSutunu = TutarCipleri.IndexOf(c);

    /// <summary>Tarih aralığını dosyadaki en eski–en yeni tarihe çeker.</summary>
    [RelayCommand]
    private void AraligiDosyayaGoreKur()
    {
        if (Tablo is null || TarihSutunu is not int ts) return;
        var tarihler = Tablo.Satirlar.Select(s => ts < s.Count ? Erp12Csv.TarihOku(s[ts]) : null).OfType<DateOnly>().ToList();
        if (tarihler.Count == 0) return;
        Baslangic = tarihler.Min().ToDateTime(TimeOnly.MinValue);
        Bitis = tarihler.Max().ToDateTime(TimeOnly.MinValue);
    }

    [RelayCommand]
    private Task KarsilastirAsync() => CalistirAsync(async () =>
    {
        Dogrula(Tablo is not null, DosyaSecinMesaji);
        Dogrula(TarihSutunu is not null && CariSutunu is not null && TutarSutunu is not null, SutunSecinMesaji);
        int ts = TarihSutunu!.Value, cs = CariSutunu!.Value, us = TutarSutunu!.Value;
        Dogrula(ts != cs && ts != us && cs != us, AyniSutunMesaji);
        var bas = DateOnly.FromDateTime(Baslangic);
        var bit = DateOnly.FromDateTime(Bitis);
        Dogrula(bas <= bit, AralikMesaji);

        var (satirlar, okunamayan) = Satirlar(Tablo!, ts, cs, us);
        var erp = satirlar.Where(s => s.Tarih >= bas && s.Tarih <= bit).ToList();
        Dogrula(erp.Count > 0, okunamayan.Count > 0 ? $"{SatirYokMesaji} {OkunamayanMetni(okunamayan)}" : SatirYokMesaji);
        EslestirmeyiHatirla(Tablo!, ts, cs, us);

        var surum = ++_surum;
        var tumTipler = TumTipler;
        // Sınırdaki ödemeler için kasa tarafı ±3 gün geniş alınır; "yalnız kasada" ise aralıkla sınırlıdır.
        var islemler = await _api.IslemlerAsync(bas.AddDays(-Erp12Karsilastirici.GunToleransi), bit.AddDays(Erp12Karsilastirici.GunToleransi));
        if (surum != _surum) return;
        var aday = islemler.Where(i => tumTipler || i.Tip == GiderTipi.Cari).ToList();
        var ham = Erp12Karsilastirici.Karsilastir(erp, aday);
        var sonuc = ham with { YalnizKasa = ham.YalnizKasa.Where(x => x.Kayit.Tarih >= bas && x.Kayit.Tarih <= bit).ToList() };

        Eslesenler.Clear();
        foreach (var e in sonuc.Eslesenler) Eslesenler.Add(Erp12SonucSatiri.Eslesen(e));
        YalnizErp12.Clear();
        foreach (var e in sonuc.YalnizErp12) YalnizErp12.Add(Erp12SonucSatiri.Erp(e));
        YalnizKasa.Clear();
        foreach (var k in sonuc.YalnizKasa) YalnizKasa.Add(Erp12SonucSatiri.Kasa(k));

        EslesenOzeti = Ozet(sonuc.Eslesenler.Count, sonuc.Eslesenler.Sum(e => e.Erp.Tutar));
        YalnizErp12Ozeti = Ozet(sonuc.YalnizErp12.Count, sonuc.YalnizErp12.Sum(e => e.Kayit.Tutar));
        YalnizKasaOzeti = Ozet(sonuc.YalnizKasa.Count, sonuc.YalnizKasa.Sum(k => Math.Abs(k.Kayit.TutarTl)));
        OkunamayanOzeti = okunamayan.Count > 0 ? OkunamayanMetni(okunamayan) : null;
        Sonuc = sonuc;
    });

    [RelayCommand]
    private void Temizle()
    {
        _surum++;
        SonucuTemizle();
        Tablo = null;
        DosyaAdi = null;
        TarihCipleri.Clear();
        CariCipleri.Clear();
        TutarCipleri.Clear();
        Onizleme.Clear();
        TarihSutunu = CariSutunu = TutarSutunu = null;
    }

    // ---------------------------------------------------------------- yardımcılar

    private void SonucuTemizle()
    {
        Sonuc = null;
        Eslesenler.Clear();
        YalnizErp12.Clear();
        YalnizKasa.Clear();
        EslesenOzeti = YalnizErp12Ozeti = YalnizKasaOzeti = "";
        OkunamayanOzeti = null;
    }

    /// <summary>Veri satırlarını ERP12 satırına çevirir; tarihi, tutarı (0 dahil) ya da carisi okunamayanların sıra numarası ayrıca döner.</summary>
    public static (List<Erp12Satir> Satirlar, List<int> Okunamayan) Satirlar(CsvTablo t, int tarih, int cari, int tutar)
    {
        var liste = new List<Erp12Satir>();
        var okunamayan = new List<int>();
        for (int i = 0; i < t.Satirlar.Count; i++)
        {
            var s = t.Satirlar[i];
            var no = i + 1;
            string Hucre(int k) => k < s.Count ? s[k] : "";
            var d = Erp12Csv.TarihOku(Hucre(tarih));
            var u = Erp12Csv.TutarOku(Hucre(tutar));
            var c = Hucre(cari).Trim();
            if (d is null || u is null || u.Value == 0m || c.Length == 0) { okunamayan.Add(no); continue; }
            liste.Add(new Erp12Satir(no, d.Value, c, decimal.Round(Math.Abs(u.Value), 2, MidpointRounding.AwayFromZero)));
        }
        return (liste, okunamayan);
    }

    private static string OkunamayanMetni(List<int> l)
        => $"{l.Count} satır okunamadı (tarih, tutar ya da cari boş/geçersiz; sıra: {string.Join(", ", l.Take(10))}{(l.Count > 10 ? ", …" : "")}).";

    private static string Ozet(int adet, decimal toplam) => $"{adet} kayıt · {Bicim.Tl(toplam)} ₺";

    private static string AyiriciAdi(char c) => c switch { '\t' => "sekme", ';' => "';'", ',' => "','", '|' => "'|'", _ => $"'{c}'" };

    private sealed record Eslestirme(string Tarih, string Cari, string Tutar);

    private (int?, int?, int?)? HatirlananEslestirme(CsvTablo t)
    {
        try
        {
            if (_ayarlar.Oku(AyarAnahtari) is not { Length: > 0 } json) return null;
            if (JsonSerializer.Deserialize<Eslestirme>(json) is not { } e) return null;
            int ta = Index(e.Tarih), ca = Index(e.Cari), ua = Index(e.Tutar);
            return ta < 0 || ca < 0 || ua < 0 ? null : (ta, ca, ua);
        }
        catch (JsonException) { return null; }

        int Index(string? ad) => ad is null ? -1 : t.Basliklar.ToList().IndexOf(ad);
    }

    private void EslestirmeyiHatirla(CsvTablo t, int tarih, int cari, int tutar)
        => _ayarlar.Yaz(AyarAnahtari, JsonSerializer.Serialize(new Eslestirme(t.Basliklar[tarih], t.Basliklar[cari], t.Basliklar[tutar])));
}

/// <summary>Karşılaştırma sonucunda tek satır (liste için hazır metinler).</summary>
public sealed record Erp12SonucSatiri(string Baslik, string Ayrinti, string Tutar, string? Ipucu)
{
    public bool IpucuVar => Ipucu is not null;

    public static Erp12SonucSatiri Eslesen(Erp12Eslesme e)
    {
        var gun = e.GunFarki switch { 0 => "aynı gün", > 0 => $"kasada {e.GunFarki} gün sonra", _ => $"kasada {-e.GunFarki} gün önce" };
        var cari = string.Equals(Erp12Karsilastirici.Normal(e.Erp.Cari), Erp12Karsilastirici.Normal(e.Islem.Cari), StringComparison.Ordinal)
            ? "" : $" · kasada \"{e.Islem.Cari}\"";
        return new(e.Erp.Cari, $"{e.Erp.Tarih:dd.MM.yyyy} (sıra {e.Erp.SatirNo}) · {e.Islem.Kanal} · {gun}{cari}", Bicim.Tl(e.Erp.Tutar) + " ₺", null);
    }

    public static Erp12SonucSatiri Erp(Erp12Tekil<Erp12Satir> e)
        => new(e.Kayit.Cari, $"{e.Kayit.Tarih:dd.MM.yyyy} · sıra {e.Kayit.SatirNo}", Bicim.Tl(e.Kayit.Tutar) + " ₺", e.Ipucu);

    public static Erp12SonucSatiri Kasa(Erp12Tekil<IslemDto> k)
        => new(k.Kayit.Cari, $"{k.Kayit.Tarih:dd.MM.yyyy} · {k.Kayit.Kanal}" + (string.IsNullOrWhiteSpace(k.Kayit.Not) ? "" : $" · {k.Kayit.Not}"),
            Bicim.Tl(Math.Abs(k.Kayit.TutarTl)) + " ₺", k.Ipucu);
}

/// <summary>
/// Sütun tahmini: önce başlık adları (Türkçe normalleştirilmiş: "Tarih", "Cari Ünvanı", "Tutar"…),
/// bulunamazsa içerik (tarih okunan, tutar okunan, harf içeren metin sütunu).
/// </summary>
public static class Erp12SutunTahmini
{
    public static (int? Tarih, int? Cari, int? Tutar) Tahmin(CsvTablo t)
    {
        var adlar = t.Basliklar.Select(Erp12Karsilastirici.Normal).ToList();
        int? tarih = t.BaslikVar ? AdlaBul(adlar, TarihPuani) : null;
        int? tutar = t.BaslikVar ? AdlaBul(adlar, TutarPuani, tarih) : null;
        int? cari = t.BaslikVar ? AdlaBul(adlar, CariPuani, tarih, tutar) : null;

        var ornek = t.Satirlar.Take(50).ToList();
        double Oran(int k, Func<string, bool> kosul) => ornek.Count == 0 ? 0
            : ornek.Count(s => k < s.Count && kosul(s[k])) / (double)ornek.Count;
        var sutunlar = Enumerable.Range(0, t.Basliklar.Count).ToList();

        tarih ??= EnIyi(sutunlar.Where(k => k != tutar && k != cari), k => Oran(k, h => Erp12Csv.TarihOku(h) is not null));
        tutar ??= EnIyi(sutunlar.Where(k => k != tarih && k != cari),
            k => Oran(k, h => Erp12Csv.TutarOku(h) is not null && Erp12Csv.TarihOku(h) is null)
                 + 0.5 * Oran(k, h => h.Contains(',') || h.Contains('.')));
        cari ??= EnIyi(sutunlar.Where(k => k != tarih && k != tutar),
            k => Oran(k, h => h.Any(char.IsLetter) && Erp12Csv.TutarOku(h) is null && Erp12Csv.TarihOku(h) is null)
                 + 0.01 * ornek.Average(s => k < s.Count ? Math.Min(s[k].Length, 40) : 0));
        return (tarih, cari, tutar);
    }

    private static int? EnIyi(IEnumerable<int> adaylar, Func<int, double> puan)
    {
        var l = adaylar.Select(k => (k, p: puan(k))).Where(x => x.p >= 0.5).OrderByDescending(x => x.p).ThenBy(x => x.k).ToList();
        return l.Count > 0 ? l[0].k : null;
    }

    private static int? AdlaBul(List<string> adlar, Func<string, int> puan, params int?[] haric)
    {
        var l = adlar.Select((a, k) => (k, p: haric.Contains(k) ? 0 : puan(a))).Where(x => x.p > 0).OrderByDescending(x => x.p).ThenBy(x => x.k).ToList();
        return l.Count > 0 ? l[0].k : null;
    }

    /// <summary>Kelime tam olarak var mı.</summary>
    private static bool W(string ad, string k) => ad.Split(' ').Contains(k);

    /// <summary>Bu önekle başlayan kelime var mı ("tarih" → "tarihi").</summary>
    private static bool P(string ad, string k) => ad.Split(' ').Any(w => w.StartsWith(k, StringComparison.Ordinal));

    private static int TarihPuani(string a)
    {
        if (a is "tarih" or "tarihi" or "date") return 10;
        if (P(a, "vade")) return 1;
        if (P(a, "tarih") || W(a, "date")) return 5;
        return 0;
    }

    private static int TutarPuani(string a)
    {
        if (P(a, "doviz") || W(a, "kur") || P(a, "bakiye") || W(a, "adet")) return 0;
        if (a is "tutar" or "tutari" or "tl tutar" or "tl tutari" or "tutar tl" or "amount") return 10;
        if (P(a, "tutar") || P(a, "meblag") || W(a, "amount")) return 6;
        if (P(a, "odeme") || P(a, "borc") || P(a, "alacak") || P(a, "tediye")) return 3;
        return 0;
    }

    private static int CariPuani(string a)
    {
        if (P(a, "kod") || W(a, "no") || P(a, "vergi") || W(a, "vkn") || W(a, "tckn") || P(a, "adres") || P(a, "tel")) return 0;
        if (P(a, "cari") && (P(a, "unvan") || W(a, "ad") || W(a, "adi") || P(a, "isim"))) return 10;
        if (P(a, "unvan")) return 8;
        if (P(a, "cari")) return 6;
        if (P(a, "firma") || P(a, "tedarikci") || P(a, "musteri") || (W(a, "hesap") && (W(a, "ad") || W(a, "adi")))) return 4;
        if (P(a, "aciklama")) return 1;
        return 0;
    }
}
