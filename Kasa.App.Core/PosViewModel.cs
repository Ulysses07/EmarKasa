using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// POS sayfası: POS tanımları (komisyon oranı, blokaj günü), günlük POS satışları ve özet (bugün
/// bankada bloke duran net, valör dökümü, ayın kanal başına komisyonu). YALNIZ BİLGİ: kasa,
/// devir ve kârlılık rakamlarına girmez. Okuma iki rol; yazma editör.
/// </summary>
public partial class PosViewModel : TemelViewModel
{
    private readonly IKasaApi _api;

    public PosViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        _yil = Bugun.Year;
        _ay = Bugun.Month;
        _satisTarih = Bugun;
        foreach (var s in Enum.GetValues<PosSaglayici>()) SaglayiciCipleri.Add(new SecimCipi(SaglayiciAdi(s)));
        SaglayiciVurgu();
    }

    public const string KanalsizAdi = "Kanalsız";
    public const string PosSecinMesaji = "Bir POS seçin.";
    public const string BrutMesaji = "Brüt tutar sıfırdan büyük olmalı.";
    public const string AdBosMesaji = "POS adı boş olamaz.";
    public const string OranGerekliMesaji = "Komisyon oranını girin (ör. 1,79).";

    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AyBasligi))] private int _yil;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AyBasligi))] private int _ay;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ValorYok), nameof(BlokeKanallari), nameof(BlokeKanalVar))] private PosOzetDto? _ozet;

    /// <summary>Blokajı süren satış yok (valör takvimi boş).</summary>
    public bool ValorYok => Ozet is null || Ozet.Valorler.Count == 0;

    /// <summary>Bugün bankada bloke duran netin kanal kanal dökümü (ay seçiminden bağımsız; geçen ayın satışı dahil).</summary>
    public IReadOnlyList<PosKanalBlokeDto> BlokeKanallari => Ozet?.BlokeKanallar ?? [];
    public bool BlokeKanalVar => BlokeKanallari.Count > 0;

    public string AyBasligi => new DateOnly(Yil, Ay, 1).ToString("MMMM yyyy", Kultur.Turkce);

    public ObservableCollection<PosTanimDto> Tanimlar { get; } = new();
    public ObservableCollection<PosSatisDto> Satislar { get; } = new();
    public ObservableCollection<SecimCipi> SaglayiciCipleri { get; } = new();
    public ObservableCollection<SeciliKanal> KanalCipleri { get; } = new();
    public ObservableCollection<PosCipi> PosCipleri { get; } = new();

    public bool TanimYok => Tanimlar.Count == 0;
    public bool SatisYok => Satislar.Count == 0;

    // ---------------------------------------------------------------- tanım formu
    [ObservableProperty] private int _tanimId;
    [ObservableProperty] private string? _tanimAd;
    [ObservableProperty] private PosSaglayici _tanimSaglayici;
    [ObservableProperty] private int? _tanimKanalId;
    [ObservableProperty] private string? _tanimOran;
    [ObservableProperty] private string? _tanimBlokaj;
    [ObservableProperty] private bool _tanimAktif = true;
    /// <summary>Kanal değişince POS'un kayıtlı satışları da yeni kanala geçsin mi (varsayılan: hayır, geçmiş korunur).</summary>
    [ObservableProperty] private bool _tanimEskiSatislaraUygula;
    /// <summary>Düzenlenen POS'un kayıtlı kanalı.</summary>
    private int? _tanimEskiKanalId;
    /// <summary>Düzenlemede kanal değişti: "önceki satışlar da geçsin" seçeneği görünür.</summary>
    public bool TanimKanalDegisti => TanimId != 0 && TanimKanalId != _tanimEskiKanalId;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TanimSilmeOnayiBekliyor))] private PosTanimDto? _silinecekTanim;
    public bool TanimSilmeOnayiBekliyor => SilinecekTanim is not null;
    public string TanimFormBasligi => TanimId == 0 ? "Yeni POS" : "POS düzenle";

    // ---------------------------------------------------------------- satış formu
    [ObservableProperty] private int _satisId;
    [ObservableProperty] private DateTime _satisTarih;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SatisOnizleme))] private int? _satisPosId;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SatisOnizleme))] private decimal _satisBrut;
    /// <summary>Boş = POS'un oranı.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SatisOnizleme))] private string? _satisOran;
    /// <summary>Boş = POS'un blokaj günü.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SatisOnizleme))] private string? _satisBlokaj;
    [ObservableProperty] private string? _satisNot;
    public string SatisFormBasligi => SatisId == 0 ? "POS satışı" : "POS satışını düzenle";

    /// <summary>Kaydetmeden önce: "Komisyon 17,90 ₺ · Net 982,10 ₺ · Valör 25 Eylül" (sunucu da aynı kuralla hesaplar).</summary>
    public string SatisOnizleme
    {
        get
        {
            if (SatisBrut <= 0 || SatisPosId is not int pid || Tanimlar.FirstOrDefault(t => t.Id == pid) is not { } pos) return "";
            var oran = OranGiris.Ayristir(SatisOran) is { Gecerli: true, Deger: { } o } ? o : pos.KomisyonOrani;
            var blokaj = BlokajGiris.Ayristir(SatisBlokaj) is { Gecerli: true, Deger: { } b } ? b : pos.BlokajGunu;
            var komisyon = PosOnizleme.Komisyon(SatisBrut, oran);
            var valor = DateOnly.FromDateTime(SatisTarih).AddDays(blokaj);
            return $"Komisyon {Bicim.Tl(komisyon)} ₺ · Net {Bicim.Tl(PosOnizleme.Net(SatisBrut, oran))} ₺ · Valör {valor.ToString("d MMMM yyyy", Kultur.Turkce)}";
        }
    }

    partial void OnSatisTarihChanged(DateTime value) => OnPropertyChanged(nameof(SatisOnizleme));
    partial void OnTanimIdChanged(int value)
    {
        OnPropertyChanged(nameof(TanimFormBasligi));
        KanalDegisimiBildir();
    }
    partial void OnSatisIdChanged(int value) => OnPropertyChanged(nameof(SatisFormBasligi));
    partial void OnTanimSaglayiciChanged(PosSaglayici value) => SaglayiciVurgu();
    partial void OnTanimKanalIdChanged(int? value)
    {
        KanalVurgu();
        KanalDegisimiBildir();
    }

    private void KanalDegisimiBildir()
    {
        if (!TanimKanalDegisti) TanimEskiSatislaraUygula = false;
        OnPropertyChanged(nameof(TanimKanalDegisti));
    }
    partial void OnSatisPosIdChanged(int? value)
    {
        PosVurgu();
        // Düzenlenen satışın formdaki oran/blokajı o satışın (eski POS'un) değerleridir. POS değişince elle
        // değiştirilmemiş olanlar boşaltılır: önizleme ve kayıt yeni POS'unkini kullanır (sunucu kuralı da
        // "POS değiştiyse POS tanımındaki değer"). Satışın kendi POS'una dönülünce kayıtlı değerler geri gelir.
        if (_duzenlenenSatis is not { } d || SatisId != d.Id) return;
        var (oran, blokaj) = FormDegerleri(d);
        if (value == d.PosId)
        {
            if (string.IsNullOrWhiteSpace(SatisOran)) SatisOran = oran;
            if (string.IsNullOrWhiteSpace(SatisBlokaj)) SatisBlokaj = blokaj;
        }
        else
        {
            if (SatisOran == oran) SatisOran = null;
            if (SatisBlokaj == blokaj) SatisBlokaj = null;
        }
    }

    /// <summary>Düzenlenen satış (POS değişiminde kayıtlı oran/blokajı tanımak için).</summary>
    private PosSatisDto? _duzenlenenSatis;

    private static (string Oran, string Blokaj) FormDegerleri(PosSatisDto s)
        => (s.KomisyonOrani.ToString("0.####", Kultur.Turkce), s.BlokajGunu.ToString(CultureInfo.InvariantCulture));

    private int _surum;

    public Task YukleAsync() => CalistirAsync(YenileIcAsync);

    private async Task YenileIcAsync()
    {
        var surum = ++_surum;
        int yil = Yil, ay = Ay;
        var bas = new DateOnly(yil, ay, 1);
        var tanimG = _api.PosTanimlariAsync();
        var satisG = _api.PosSatislariAsync(bas, bas.AddMonths(1).AddDays(-1));
        var ozetG = _api.PosOzetAsync(yil, ay);
        var kanalG = _api.KanallarAsync();
        try { await Task.WhenAll(tanimG, satisG, ozetG, kanalG); }
        catch when (surum != _surum) { return; }
        if (surum != _surum) return;

        Tanimlar.Clear();
        foreach (var t in tanimG.Result) Tanimlar.Add(t);
        Satislar.Clear();
        foreach (var s in satisG.Result) Satislar.Add(s);
        Ozet = ozetG.Result;

        KanalCipleri.Clear();
        KanalCipleri.Add(new SeciliKanal(null, KanalsizAdi));
        foreach (var k in kanalG.Result.Where(k => k.Aktif || k.Id == TanimKanalId).OrderBy(k => k.Sira))
            KanalCipleri.Add(new SeciliKanal(k.Id, k.Ad));
        KanalVurgu();
        PosCipleriniKur();
        OnPropertyChanged(nameof(TanimYok));
        OnPropertyChanged(nameof(SatisYok));
        OnPropertyChanged(nameof(SatisOnizleme));
    }

    /// <summary>Satış formundaki POS çipleri: aktif POS'lar (+ düzenlenen satışın pasif POS'u).</summary>
    private void PosCipleriniKur()
    {
        PosCipleri.Clear();
        foreach (var t in Tanimlar.Where(t => t.Aktif || t.Id == SatisPosId))
            PosCipleri.Add(new PosCipi(t.Id, t.Ad));
        if (SatisPosId is null && PosCipleri.Count == 1) SatisPosId = PosCipleri[0].Id;
        PosVurgu();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task OncekiAy()
    {
        if (Ay == 1) { Ay = 12; Yil--; } else Ay--;
        return YukleAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SonrakiAy()
    {
        if (Ay == 12) { Ay = 1; Yil++; } else Ay++;
        return YukleAsync();
    }

    // ---------------------------------------------------------------- tanım komutları
    [RelayCommand] private void SecSaglayici(SecimCipi s) => TanimSaglayici = SaglayiciDegeri(s.Ad);
    [RelayCommand] private void SecTanimKanal(SeciliKanal k) => TanimKanalId = k.Id;

    [RelayCommand]
    private void TanimYeni()
    {
        _tanimEskiKanalId = null;
        TanimId = 0; TanimAd = null; TanimSaglayici = PosSaglayici.BankaPosu; TanimKanalId = null;
        TanimOran = null; TanimBlokaj = null; TanimAktif = true; SilinecekTanim = null;
    }

    [RelayCommand]
    private void TanimDuzenle(PosTanimDto t)
    {
        _tanimEskiKanalId = t.KanalId;
        TanimId = t.Id; TanimAd = t.Ad; TanimSaglayici = t.Saglayici; TanimKanalId = t.KanalId;
        KanalDegisimiBildir();
        TanimOran = t.KomisyonOrani.ToString("0.####", Kultur.Turkce); TanimBlokaj = t.BlokajGunu.ToString(CultureInfo.InvariantCulture);
        TanimAktif = t.Aktif; SilinecekTanim = null;
    }

    [RelayCommand]
    private Task TanimKaydetAsync() => CalistirAsync(async () =>
    {
        var ad = TanimAd?.Trim() ?? "";
        Dogrula(ad.Length > 0, AdBosMesaji);
        var oran = OranGiris.Ayristir(TanimOran);
        Dogrula(oran.Gecerli, oran.Hata ?? OranGiris.HataGecersiz);
        Dogrula(oran.Deger is not null, OranGerekliMesaji);
        var blokaj = BlokajGiris.Ayristir(TanimBlokaj);
        Dogrula(blokaj.Gecerli, blokaj.Hata ?? BlokajGiris.HataGecersiz);
        var g = new PosTanimYaz(ad, TanimSaglayici, TanimKanalId, oran.Deger!.Value, blokaj.Deger ?? 0, TanimAktif,
            EskiSatislaraUygula: TanimKanalDegisti && TanimEskiSatislaraUygula);
        if (TanimId == 0) await _api.PosTanimOlusturAsync(g);
        else await _api.PosTanimGuncelleAsync(TanimId, g);
        TanimYeni();
        await YenileIcAsync();
    });

    [RelayCommand] private void TanimSilIste(PosTanimDto t) => SilinecekTanim = t;
    [RelayCommand] private void TanimSilVazgec() => SilinecekTanim = null;

    [RelayCommand]
    private Task TanimSilOnaylaAsync() => CalistirAsync(async () =>
    {
        if (SilinecekTanim is not { } t) return;
        SilinecekTanim = null;
        await _api.PosTanimSilAsync(t.Id);
        if (TanimId == t.Id) TanimYeni();
        if (SatisPosId == t.Id) SatisPosId = null;
        await YenileIcAsync();
    });

    // ---------------------------------------------------------------- satış komutları
    [RelayCommand] private void SecPos(PosCipi p) => SatisPosId = p.Id;

    [RelayCommand]
    private void SatisYeni()
    {
        _duzenlenenSatis = null;
        SatisId = 0; SatisTarih = Bugun; SatisBrut = 0m; SatisOran = null; SatisBlokaj = null; SatisNot = null;
        if (SatisPosId is int p && !PosCipleri.Any(c => c.Id == p)) SatisPosId = null;
        PosCipleriniKur();
    }

    [RelayCommand]
    private void SatisDuzenle(PosSatisDto s)
    {
        _duzenlenenSatis = s;
        SatisId = s.Id; SatisTarih = s.Tarih.ToDateTime(TimeOnly.MinValue); SatisPosId = s.PosId; SatisBrut = s.BrutTutar;
        (SatisOran, SatisBlokaj) = FormDegerleri(s);
        SatisNot = s.Not;
        PosCipleriniKur();
    }

    [RelayCommand]
    private Task SatisKaydetAsync() => CalistirAsync(async () =>
    {
        Dogrula(SatisPosId is not null, PosSecinMesaji);
        Dogrula(SatisBrut > 0, BrutMesaji);
        var oran = OranGiris.Ayristir(SatisOran);
        Dogrula(oran.Gecerli, oran.Hata ?? OranGiris.HataGecersiz);
        var blokaj = BlokajGiris.Ayristir(SatisBlokaj);
        Dogrula(blokaj.Gecerli, blokaj.Hata ?? BlokajGiris.HataGecersiz);
        var not = string.IsNullOrWhiteSpace(SatisNot) ? null : SatisNot.Trim();
        var g = new PosSatisYaz(DateOnly.FromDateTime(SatisTarih), SatisPosId!.Value, SatisBrut, oran.Deger, blokaj.Deger, not);
        if (SatisId == 0) await _api.PosSatisOlusturAsync(g);
        else await _api.PosSatisGuncelleAsync(SatisId, g);
        SatisYeni();
        await YenileIcAsync();
    });

    [RelayCommand]
    private Task SatisSilAsync(PosSatisDto s) => CalistirAsync(async () =>
    {
        await _api.PosSatisSilAsync(s.Id);
        if (SatisId == s.Id) SatisYeni();
        await YenileIcAsync();
    });

    // ---------------------------------------------------------------- yardımcılar
    public static string SaglayiciAdi(PosSaglayici s) => s switch
    {
        PosSaglayici.Iyzico => "iyzico",
        PosSaglayici.PayTr => "PayTR",
        PosSaglayici.Diger => "Diğer",
        _ => "Banka POS'u",
    };

    private static PosSaglayici SaglayiciDegeri(string ad)
        => Enum.GetValues<PosSaglayici>().FirstOrDefault(s => SaglayiciAdi(s) == ad);

    private void SaglayiciVurgu()
    {
        var ad = SaglayiciAdi(TanimSaglayici);
        foreach (var c in SaglayiciCipleri) c.Secili = c.Ad == ad;
    }

    private void KanalVurgu()
    {
        foreach (var k in KanalCipleri) k.Secili = k.Id == TanimKanalId;
    }

    private void PosVurgu()
    {
        foreach (var p in PosCipleri) p.Secili = p.Id == SatisPosId;
    }
}

/// <summary>Kanal çipi (Id null = "Kanalsız").</summary>
public partial class SeciliKanal(int? id, string ad) : ObservableObject
{
    public int? Id { get; } = id;
    public string Ad { get; } = ad;
    [ObservableProperty] private bool _secili;
}

/// <summary>Satış formundaki POS çipi.</summary>
public partial class PosCipi(int id, string ad) : ObservableObject
{
    public int Id { get; } = id;
    public string Ad { get; } = ad;
    [ObservableProperty] private bool _secili;
}

/// <summary>Kaydetmeden önceki POS önizlemesi — sunucudaki kuralla aynı (Kasa.Core.PosHesap).</summary>
public static class PosOnizleme
{
    private static decimal Yuvarla(decimal d) => decimal.Round(d, 2, MidpointRounding.AwayFromZero);
    public static decimal Komisyon(decimal brut, decimal oran) => Yuvarla(Yuvarla(brut) * oran / 100m);
    public static decimal Net(decimal brut, decimal oran) => Yuvarla(brut) - Komisyon(brut, oran);
}

/// <summary>Yüzde giriş sonucu (boş = null).</summary>
public readonly record struct SayiGirisSonucu<T>(bool Gecerli, T? Deger, string? Hata) where T : struct;

/// <summary>Komisyon oranı girişi: "1,79", "1.79", "%1,79"; 0–100, en fazla 4 ondalık. Boş = null.</summary>
public static class OranGiris
{
    public const string HataGecersiz = "Geçersiz oran. Örnek: 1,79";
    public const string HataAralik = "Komisyon oranı 0 ile 100 arasında olmalı.";
    public const string HataOndalik = "Komisyon oranı en fazla 4 ondalık basamak içerebilir.";

    public static SayiGirisSonucu<decimal> Ayristir(string? metin)
    {
        var s = (metin ?? "").Trim().TrimStart('%').TrimEnd('%').Trim();
        if (s.Length == 0) return new(true, null, null);
        if (s.Count(c => c is ',' or '.') > 1 || s.Any(c => !(char.IsAsciiDigit(c) || c is ',' or '.' or '-')))
            return new(false, null, HataGecersiz);
        if (!decimal.TryParse(s.Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var d))
            return new(false, null, HataGecersiz);
        if (d < 0 || d > 100) return new(false, null, HataAralik);
        if (decimal.Round(d, 4) != d) return new(false, null, HataOndalik);
        return new(true, d, null);
    }
}

/// <summary>Blokaj günü girişi: 0–365 tam sayı. Boş = null.</summary>
public static class BlokajGiris
{
    public const string HataGecersiz = "Blokaj günü 0 ile 365 arasında bir tam sayı olmalı.";

    public static SayiGirisSonucu<int> Ayristir(string? metin)
    {
        var s = (metin ?? "").Trim();
        if (s.Length == 0) return new(true, null, null);
        if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var g) || g > 365)
            return new(false, null, HataGecersiz);
        return new(true, g, null);
    }
}
