using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Paket D (özellik 33) — tekrarlayan gider şablonunun ikinci adımı: 3 ayda / 6 ayda / yılda bir
/// sıklık (başlangıç ayıyla), karta bağlı şablon (onaylanınca o kartın harcaması olur; kalem kayıtlı
/// bir caridir), her seferinde girilen tutar ve tek dokunuşla eklenen hazır vergi/prim şablonları.
/// Varsayılanlar (aylık, kartsız, sabit tutar) bugünkü şablonlarla aynıdır.
/// </summary>
public partial class AyarlarViewModel
{
    public const string HazirUyari = "Tarihleri muhasebecinizle doğrulayın.";
    public const string KartliKalemMesaji = "Karta bağlı giderde kalem, kartın harcama yaptığı kayıtlı bir cari olmalı (Cariler sayfası).";

    // ---------------------------------------------------------------- form alanları

    [ObservableProperty] private TekrarSikligi _duzenTekrarSiklik = TekrarSikligi.Aylik;
    /// <summary>Başlangıç ayı (aylık olmayan sıklıkta ilk vadenin ayı; yalnız ayı kullanılır).</summary>
    [ObservableProperty] private DateTime _duzenTekrarBaslangic;
    [ObservableProperty] private int? _duzenTekrarKartId;
    [ObservableProperty] private bool _duzenTekrarTutarDegisken;

    /// <summary>Sıklık çipleri: Her ay · 3 ayda bir · 6 ayda bir · Yılda bir.</summary>
    public ObservableCollection<SiklikCipi> SiklikCipleri { get; } = new(
        new[] { TekrarSikligi.Aylik, TekrarSikligi.UcAylik, TekrarSikligi.AltiAylik, TekrarSikligi.Yillik }
            .Select(s => new SiklikCipi(TekrarlayanMetin.SiklikAdi(s), s) { Secili = s == TekrarSikligi.Aylik }));

    /// <summary>Kart çipleri: "Kartsız" (Id 0) + kayıtlı kartlar.</summary>
    public ObservableCollection<KartCipi> TekrarKartCipleri { get; } = new() { new KartCipi(0, KartsizAd) { Secili = true } };
    public const string KartsizAd = "Kartsız";

    public bool BaslangicGorunur => DuzenTekrarSiklik != TekrarSikligi.Aylik;
    public bool KartliMi => DuzenTekrarKartId is not null;
    public string TekrarKalemEtiketi => KartliMi ? "Cari (kartın harcama yaptığı yer)" : "Gider kalemi";
    public string TekrarTutarEtiketi => DuzenTekrarTutarDegisken ? "Önerilen tutar (isteğe bağlı)" : "Tutar";

    private List<string> _cariler = new();

    [RelayCommand]
    private void SecTekrarSiklik(SiklikCipi c) => DuzenTekrarSiklik = c.Siklik;

    [RelayCommand]
    private void SecTekrarKart(KartCipi c)
    {
        var yeni = c.Id == 0 ? (int?)null : c.Id;
        if (yeni == DuzenTekrarKartId) return;
        var kartlilikDegisti = (yeni is null) != (DuzenTekrarKartId is null);
        DuzenTekrarKartId = yeni;
        if (kartlilikDegisti) DuzenTekrarKalem = "";   // kalem ile cari farklı listelerden gelir
        TekrarCipleriniKur();
    }

    partial void OnDuzenTekrarSiklikChanged(TekrarSikligi value)
    {
        foreach (var c in SiklikCipleri) c.Secili = c.Siklik == value;
        OnPropertyChanged(nameof(BaslangicGorunur));
    }

    partial void OnDuzenTekrarKartIdChanged(int? value)
    {
        foreach (var c in TekrarKartCipleri) c.Secili = c.Id == (value ?? 0);
        OnPropertyChanged(nameof(KartliMi));
        OnPropertyChanged(nameof(TekrarKalemEtiketi));
    }

    partial void OnDuzenTekrarTutarDegiskenChanged(bool value) => OnPropertyChanged(nameof(TekrarTutarEtiketi));

    /// <summary>Kaydedilecek başlangıç ayı: aylıkta null (yeni kayıtta bu ay, güncellemede değişmez).</summary>
    private DateOnly? TekrarBaslangicAyi()
    {
        if (DuzenTekrarSiklik == TekrarSikligi.Aylik) return null;
        var t = DuzenTekrarBaslangic == default ? Bugun : DuzenTekrarBaslangic;
        return new DateOnly(t.Year, t.Month, 1);
    }

    /// <summary>Düzenlenen şablonun Paket D alanlarını forma alır (<see cref="TekrarDuzenle"/> çağırır).</summary>
    private void TekrarEkDuzenle(TekrarlayanGiderDto g)
    {
        DuzenTekrarSiklik = g.Siklik;
        DuzenTekrarBaslangic = g.BaslangicAyi.ToDateTime(TimeOnly.MinValue);
        DuzenTekrarKartId = g.KrediKartiId;
        DuzenTekrarTutarDegisken = g.TutarDegisken;
    }

    /// <summary>Yeni şablon: aylık, bu aydan, kartsız, sabit tutar (<see cref="YeniTekrar"/> çağırır).</summary>
    private void TekrarEkYeni()
    {
        DuzenTekrarSiklik = TekrarSikligi.Aylik;
        DuzenTekrarBaslangic = new DateTime(Bugun.Year, Bugun.Month, 1);
        DuzenTekrarKartId = null;
        DuzenTekrarTutarDegisken = false;
    }

    /// <summary>
    /// Karta bağlı şablonda kalem çipleri kayıtlı carilerdir (<see cref="TekrarCipleriniKur"/> sonunda).
    /// Kart çipleri de burada kurulur (düzenlenen şablonun kartı listede yoksa bile seçili görünür).
    /// </summary>
    private void KartliKalemCipleriniKur()
    {
        foreach (var c in TekrarKartCipleri) c.Secili = c.Id == (DuzenTekrarKartId ?? 0);
        if (DuzenTekrarKartId is null) return;
        var adlar = _cariler.ToList();
        if (DuzenTekrarKalem.Length > 0 && !adlar.Contains(DuzenTekrarKalem)) adlar.Add(DuzenTekrarKalem);
        TekrarKalemCipleri.Clear();
        foreach (var ad in adlar) TekrarKalemCipleri.Add(new SecimCipi(ad) { Secili = ad == DuzenTekrarKalem });
        OnPropertyChanged(nameof(TekrarKalemYok));
    }

    // ---------------------------------------------------------------- hazır şablonlar

    public ObservableCollection<TekrarlayanHazirDto> HazirSablonlar { get; } = new();
    public bool HazirVar => HazirSablonlar.Count > 0;
    /// <summary>Hazır şablon eklendikten sonra bilgi satırı.</summary>
    [ObservableProperty] private string? _tekrarBilgi;

    [RelayCommand]
    private Task HazirEkleAsync(TekrarlayanHazirDto h) => CalistirAsync(async () =>
    {
        TekrarBilgi = null;
        var eklenen = await _api.TekrarlayanHazirEkleAsync(h.Kod);
        TekrarBilgi = $"{h.Ad} eklendi ({eklenen.Count} şablon, tutar her seferinde girilir). {HazirUyari}";
        await DoldurAsync();
    });

    /// <summary>Kartlar, cariler ve hazır şablonlar (<see cref="DoldurAsync"/> sonunda; uç yoksa boş).</summary>
    private async Task TekrarEkleriniYukleAsync()
    {
        if (DuzenTekrarBaslangic == default) DuzenTekrarBaslangic = new DateTime(Bugun.Year, Bugun.Month, 1);
        var kartGorevi = TekrarlayanYukleme.Oku(_api.KrediKartlariAsync);
        var cariGorevi = TekrarlayanYukleme.Oku(() => _api.CarilerAsync());
        var hazirGorevi = TekrarlayanYukleme.Oku(_api.TekrarlayanHazirlarAsync);
        await Task.WhenAll(kartGorevi, cariGorevi, hazirGorevi);

        var kartlar = kartGorevi.Result;
        TekrarKartCipleri.Clear();
        TekrarKartCipleri.Add(new KartCipi(0, KartsizAd));
        foreach (var k in kartlar) TekrarKartCipleri.Add(new KartCipi(k.Id, k.Ad));
        if (DuzenTekrarKartId is { } id && kartlar.All(k => k.Id != id)) TekrarKartCipleri.Add(new KartCipi(id, $"Kart #{id}"));

        _cariler = cariGorevi.Result.Where(c => c.Aktif).Select(c => c.Ad).ToList();

        HazirSablonlar.Clear();
        foreach (var h in hazirGorevi.Result) HazirSablonlar.Add(h);
        OnPropertyChanged(nameof(HazirVar));

        TekrarCipleriniKur();
    }
}

/// <summary>Sıklık seçim çipi.</summary>
public sealed class SiklikCipi : SecimCipi
{
    public TekrarSikligi Siklik { get; }
    public SiklikCipi(string ad, TekrarSikligi siklik) : base(ad) => Siklik = siklik;
}

/// <summary>Tekrarlayan gider metinleri (Ayarlar listesi ve Panel).</summary>
public static class TekrarlayanMetin
{
    public static string SiklikAdi(TekrarSikligi s) => s switch
    {
        TekrarSikligi.UcAylik => "3 ayda bir",
        TekrarSikligi.AltiAylik => "6 ayda bir",
        TekrarSikligi.Yillik => "Yılda bir",
        _ => "Her ay",
    };

    /// <summary>
    /// Ayarlar listesinin ikinci satırı. Aylık, kartsız, sabit tutarlı şablonda eskisiyle birebir aynı:
    /// "MEZAT · ayın 5. günü · 25.000,00". Diğerlerinde sıklık/başlangıç, kart ve değişken tutar eklenir.
    /// </summary>
    public static string SatirAciklamasi(TekrarlayanGiderDto g)
    {
        var gun = TekrarlayanGiderSatiri.GunMetni(g.AyinGunu);
        if (g.Siklik != TekrarSikligi.Aylik)
            gun = $"{SiklikAdi(g.Siklik).ToLower(Kultur.Turkce)}, {g.BaslangicAyi.ToString("MMMM yyyy", Kultur.Turkce)} başlar · {gun}";
        var tutar = g.TutarDegisken
            ? g.Tutar > 0m ? $"tutar her seferinde (öneri {Bicim.Tl(g.Tutar)})" : "tutar her seferinde girilir"
            : Bicim.Tl(g.Tutar);
        var kart = g.KrediKartiId is not null ? " · karta bağlı" : "";
        return $"{g.Kanal} · {gun} · {tutar}{kart}";
    }
}
