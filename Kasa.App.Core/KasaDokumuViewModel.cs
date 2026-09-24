using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// 21 · "Kasa neden değişti?" sayfası: seçilen hafta ya da ay için açılış kasasından kapanışa adım
/// adım döküm (kanal gelirleri ve tahsil edilen çekler eklenir; Cari, sabit, Ortak gider, kart ödemesi
/// ve ödenen çekler düşülür). Açılış + Σ adımlar = kapanış; rakamlar haftalık raporun kasa devriyle aynıdır.
/// </summary>
public partial class KasaDokumuViewModel : TemelViewModel
{
    public const string HaftaKipi = "Hafta";
    public const string AyKipi = "Ay";

    private readonly IKasaApi _api;
    private readonly IDosyaKaydedici? _kaydedici;

    public KasaDokumuViewModel(IKasaApi api, TimeProvider? zaman = null, IDosyaKaydedici? kaydedici = null, IGezinti? gezinti = null)
        : base(zaman)
    {
        _api = api;
        _kaydedici = kaydedici;
        Gezinti = gezinti;
        var bugun = BugunTarih;
        _yil = bugun.Year;
        _ay = bugun.Month;
        KipCipleri.Add(new SecimCipi(HaftaKipi));
        KipCipleri.Add(new SecimCipi(AyKipi));
        KipVurgu();
    }

    public IGezinti? Gezinti { get; set; }

    public ObservableCollection<SecimCipi> KipCipleri { get; } = new();

    /// <summary>true: ay seçilir (‹ ›); false: hafta (dönem) seçilir.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaftaKipinde))]
    private bool _ayKipinde = true;
    public bool HaftaKipinde => !AyKipinde;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AyEtiketi))]
    private int _yil;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AyEtiketi))]
    private int _ay;
    public string AyEtiketi => KasaDokumuGorunum.AyEtiketi(Yil, Ay);

    /// <summary>Dönem (hafta) seçici kaynağı, en yeni üstte.</summary>
    public ObservableCollection<DonemDto> Donemler { get; } = new();
    [ObservableProperty] private DonemDto? _seciliDonem;
    private bool _donemlerKuruluyor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DokumVar))]
    private KasaDokumuDto? _dokum;
    public bool DokumVar => Dokum is not null;
    public ObservableCollection<KasaDokumSatiri> Satirlar { get; } = new();

    /// <summary>Gerçek aralık (döküm, aralıkla çakışan dönemlerin tamamını kapsar).</summary>
    [ObservableProperty] private string? _aralikMetni;
    [ObservableProperty] private string? _ozet;
    /// <summary>Aralıkta takip dönemi yoksa sunucunun açıklaması.</summary>
    [ObservableProperty] private string? _bosMesaji;
    [ObservableProperty] private string? _aktarilanDosya;

    /// <summary>Rotadan gelen aralık: ay başı–ay sonu ise ay kipi, değilse o tarihi içeren hafta.</summary>
    private DateOnly? _istenenHafta;
    private int _surum;

    /// <summary>Sorgudan (Haftalık/Aylık'tan gelinince) aralığı uygular; yükleme <see cref="YukleAsync"/>'tadır.</summary>
    public void AralikUygula(DateOnly baslangic, DateOnly bitis)
    {
        var (ab, asn) = KasaDokumuGorunum.AyAraligi(baslangic.Year, baslangic.Month);
        if (baslangic == ab && bitis == asn)
        {
            AyKipinde = true;
            Yil = baslangic.Year;
            Ay = baslangic.Month;
            _istenenHafta = null;
        }
        else
        {
            AyKipinde = false;
            _istenenHafta = baslangic;
        }
        KipVurgu();
    }

    public Task YukleAsync() => CalistirAsync(async () =>
    {
        var donemler = await _api.DonemlerAsync();
        DonemleriKur(donemler);
        await DokumuYukleAsync();
    });

    private void DonemleriKur(IReadOnlyList<DonemDto> donemler)
    {
        var sirali = donemler.OrderByDescending(d => d.Start).ToList();
        var onceki = SeciliDonem;
        _donemlerKuruluyor = true;
        try
        {
            if (!sirali.SequenceEqual(Donemler))
            {
                Donemler.Clear();
                foreach (var d in sirali) Donemler.Add(d);
            }
            var hedef = _istenenHafta ?? onceki?.Start ?? BugunTarih;
            SeciliDonem = Donemler.FirstOrDefault(d => d.Start <= hedef && hedef <= d.End)
                          ?? (onceki is not null && Donemler.Contains(onceki) ? onceki : Donemler.FirstOrDefault());
        }
        finally { _donemlerKuruluyor = false; }
        _istenenHafta = null;
    }

    private (DateOnly Bas, DateOnly Bit)? Aralik()
    {
        if (AyKipinde) return KasaDokumuGorunum.AyAraligi(Yil, Ay);
        return SeciliDonem is { } d ? (d.Start, d.End) : null;
    }

    private async Task DokumuYukleAsync()
    {
        var surum = ++_surum;
        if (Aralik() is not { } a)
        {
            Dokum = null;
            Satirlar.Clear();
            Ozet = null;
            AralikMetni = null;
            BosMesaji = "Seçilecek takip dönemi yok.";
            return;
        }
        KasaDokumuDto d;
        try { d = await _api.KasaDokumuAsync(a.Bas, a.Bit); }
        catch (KasaApiException ex) when (ex.DurumKodu == HttpStatusCode.BadRequest && surum == _surum)
        {
            Dokum = null;
            Satirlar.Clear();
            Ozet = null;
            AralikMetni = KasaDokumuGorunum.AralikMetni(a.Bas, a.Bit);
            BosMesaji = string.IsNullOrWhiteSpace(ex.SunucuMesaji) ? "Bu aralıkta takip dönemi yok." : ex.SunucuMesaji;
            return;
        }
        catch when (surum != _surum) { return; }
        if (surum != _surum) return;
        Dokum = d;
        BosMesaji = null;
        Satirlar.Clear();
        foreach (var s in KasaDokumuGorunum.Satirlar(d)) Satirlar.Add(s);
        Ozet = KasaDokumuGorunum.Ozet(d);
        AralikMetni = KasaDokumuGorunum.AralikMetni(d.Baslangic, d.Bitis);
    }

    private Task Yenile() => CalistirAsync(DokumuYukleAsync);

    private void KipVurgu()
    {
        foreach (var k in KipCipleri) k.Secili = k.Ad == (AyKipinde ? AyKipi : HaftaKipi);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SecKipAsync(SecimCipi s)
    {
        AyKipinde = s.Ad == AyKipi;
        KipVurgu();
        return Yenile();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task OncekiAsync()
    {
        if (AyKipinde)
        {
            if (Ay == 1) { Ay = 12; Yil--; } else Ay--;
            return Yenile();
        }
        var i = SeciliDonem is { } d ? Donemler.IndexOf(d) : -1;
        if (i >= 0 && i + 1 < Donemler.Count) SeciliDonem = Donemler[i + 1];   // liste en yeni üstte
        return Task.CompletedTask;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SonrakiAsync()
    {
        if (AyKipinde)
        {
            if (Ay == 12) { Ay = 1; Yil++; } else Ay++;
            return Yenile();
        }
        var i = SeciliDonem is { } d ? Donemler.IndexOf(d) : -1;
        if (i > 0) SeciliDonem = Donemler[i - 1];
        return Task.CompletedTask;
    }

    /// <summary>Son dökümü yeniden yükleme görevi (testler bekler).</summary>
    public Task? SonYukleme { get; private set; }

    partial void OnSeciliDonemChanged(DonemDto? value)
    {
        if (_donemlerKuruluyor || value is null || AyKipinde) return;
        SonYukleme = Yenile();
    }

    [RelayCommand]
    private Task ExceleAktarAsync() => CalistirAsync(async () =>
    {
        AktarilanDosya = null;
        if (Aralik() is not { } a) throw new DogrulamaHatasi("Önce bir hafta ya da ay seçin.");
        AktarilanDosya = await ExcelAktarma.AktarAsync(_kaydedici, () => _api.KasaDokumuCsvAsync(a.Bas, a.Bit));
    });

    [RelayCommand]
    private Task IslemlereGitAsync(KasaDokumSatiri? s) => CalistirAsync(async () =>
    {
        if (s?.Suzgec is not { } z || Gezinti is null) return;
        await Gezinti.GitAsync(z.Rota());
    });
}
