using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Cari özetinin bir ayı (tablo satırı).</summary>
public sealed record CariOzetiSatiri(CariOzetiAyDto Dto, bool KalemMi)
{
    public string AyAdi => Kultur.Turkce.TextInfo.ToTitleCase(Kultur.Turkce.DateTimeFormat.GetMonthName(Dto.Ay));
    public string NakitMetni => Tutar(Dto.Nakit);
    public string KrediKartiMetni => Tutar(Dto.KrediKarti);
    public string CekMetni => Tutar(Dto.Cek);
    public string ToplamMetni => Tutar(Dto.Toplam);
    public string AdetMetni => Dto.Adet == 0 ? "—" : $"{Dto.Adet} kayıt";
    /// <summary>Kalem özetinde tekrarlayan gider şablonunun o ayki tutarı (yoksa boş).</summary>
    public string SablonMetni => Dto.Sablon is { } s ? Bicim.Tl(s) : "—";
    /// <summary>"Girildi" / "Atlandı" / boş (tekrarlayan gider kararı).</summary>
    public string KararMetni => Dto.Karar ?? "";
    /// <summary>Girilen tutar şablondan farklı mı (kalem özetinde uyarı rengi).</summary>
    public bool SablondanFarkli => KalemMi && Dto.Sablon is { } s && Dto.Adet > 0 && Dto.Toplam != s;
    public bool Bos => Dto.Toplam == 0m && Dto.Adet == 0;
    private static string Tutar(decimal d) => d == 0m ? "—" : Bicim.Tl(d);
}

/// <summary>
/// 07 · Cari özeti: bir cari (ya da sabit gider kalemi) için seçilen yılın ay ay toplamları. Cari özetinde
/// nakit, kredi kartı ve (kişi adı büyük/küçük harf farkı gözetmeden eşleşen) ödenmiş verilen çekler ayrı
/// sütunlardır; kalem özetinde girilen tutarın yanında tekrarlayan gider şablonu gösterilir.
/// </summary>
public partial class CariOzetiViewModel : TemelViewModel
{
    public const string CariCipi = "Cari";
    public const string KalemCipi = "Sabit gider kalemi";

    private readonly IKasaApi _api;

    public CariOzetiViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        _yil = BugunTarih.Year;
        TurCipleri.Add(new SecimCipi(CariCipi));
        TurCipleri.Add(new SecimCipi(KalemCipi));
        TurVurgu();
    }

    public ObservableCollection<SecimCipi> TurCipleri { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KalemMi), nameof(AdEtiketi))]
    private CariOzetiTuru _tur = CariOzetiTuru.Cari;
    public bool KalemMi => Tur == CariOzetiTuru.Kalem;
    public string AdEtiketi => KalemMi ? "Sabit gider kalemi" : "Cari";

    /// <summary>Seçilebilecek adlar (cariler ya da sabit gider kalemleri, aktif önce).</summary>
    public ObservableCollection<string> Adlar { get; } = new();
    [ObservableProperty] private string? _seciliAd;
    [ObservableProperty] private int _yil;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OzetVar), nameof(ToplamMetni), nameof(SablonToplamMetni))]
    private CariOzetiDto? _ozet;
    public bool OzetVar => Ozet is not null;
    public string ToplamMetni => Ozet is { } o ? $"{Bicim.Tl(o.Toplam)} ₺" : "";
    public string SablonToplamMetni => Ozet?.SablonToplam is { } s ? $"Şablon toplamı {Bicim.Tl(s)} ₺" : "";
    public ObservableCollection<CariOzetiSatiri> Aylar { get; } = new();

    private int _surum;
    private bool _adlarKuruluyor;

    public Task YukleAsync() => CalistirAsync(async () =>
    {
        await AdlariKurAsync();
        await OzetiYukleAsync();
    });

    private async Task AdlariKurAsync()
    {
        var tur = Tur;
        IEnumerable<(string Ad, bool Aktif)> kaynak = tur == CariOzetiTuru.Kalem
            ? (await _api.GiderKalemleriAsync()).Select(k => (k.Ad, k.Aktif))
            : (await _api.CarilerAsync()).Select(c => (c.Ad, c.Aktif));
        if (tur != Tur) return;
        var adlar = kaynak.OrderByDescending(x => x.Aktif).ThenBy(x => x.Ad, StringComparer.Create(Kultur.Turkce, true))
            .Select(x => x.Ad).Distinct().ToList();
        var secili = SeciliAd;
        _adlarKuruluyor = true;
        try
        {
            if (!adlar.SequenceEqual(Adlar))
            {
                Adlar.Clear();
                foreach (var a in adlar) Adlar.Add(a);
            }
            SeciliAd = secili is not null && Adlar.Contains(secili) ? secili : null;
        }
        finally { _adlarKuruluyor = false; }
    }

    private async Task OzetiYukleAsync()
    {
        var surum = ++_surum;
        var ad = SeciliAd;
        if (string.IsNullOrWhiteSpace(ad))
        {
            Ozet = null;
            Aylar.Clear();
            return;
        }
        int yil = Yil;
        var tur = Tur;
        CariOzetiDto o;
        try { o = await _api.CariOzetiAsync(ad, yil, tur); }
        catch when (surum != _surum) { return; }
        if (surum != _surum) return;
        Ozet = o;
        Aylar.Clear();
        foreach (var a in o.Aylar) Aylar.Add(new CariOzetiSatiri(a, tur == CariOzetiTuru.Kalem));
    }

    /// <summary>Son özet yüklemesi (testler bekler).</summary>
    public Task? SonYukleme { get; private set; }

    partial void OnSeciliAdChanged(string? value)
    {
        if (_adlarKuruluyor) return;
        SonYukleme = CalistirAsync(OzetiYukleAsync);
    }

    private void TurVurgu()
    {
        foreach (var c in TurCipleri) c.Secili = c.Ad == (Tur == CariOzetiTuru.Kalem ? KalemCipi : CariCipi);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SecTurAsync(SecimCipi s)
    {
        var yeni = s.Ad == KalemCipi ? CariOzetiTuru.Kalem : CariOzetiTuru.Cari;
        if (yeni == Tur) return Task.CompletedTask;
        Tur = yeni;
        TurVurgu();
        _adlarKuruluyor = true;
        try { SeciliAd = null; }
        finally { _adlarKuruluyor = false; }
        return YukleAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task OncekiYil()
    {
        Yil--;
        return CalistirAsync(OzetiYukleAsync);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SonrakiYil()
    {
        Yil++;
        return CalistirAsync(OzetiYukleAsync);
    }
}
