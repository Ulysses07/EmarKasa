using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket C · 06: gelişmiş işlem arama (not içinde metin, tip, kart, tutar aralığı). Süzgeç
// "Uygula" ile etkinleşir; etkin değilken liste eski istekle (IslemSayfasiAsync) yüklenir.
public partial class IslemlerViewModel
{
    private const string TumTipler = "Tümü";

    [ObservableProperty] private bool _gelismisAramaAcik;
    [ObservableProperty] private string _aramaNot = "";
    [ObservableProperty] private string _aramaMin = "";
    [ObservableProperty] private string _aramaMax = "";
    [ObservableProperty] private GiderTipi? _aramaTip;
    [ObservableProperty] private int? _aramaKartId;

    /// <summary>Etkin gelişmiş süzgecin okunur özeti; süzgeç yoksa null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AramaEtkin))]
    private string? _aramaOzeti;

    public bool AramaEtkin => AramaOzeti is not null;

    /// <summary>Tip süzgeci çipleri: Tümü · Cari · Sabit gider · Kredi kartı.</summary>
    public ObservableCollection<SecimCipi> AramaTipCipleri { get; } = new();

    /// <summary>Kart süzgeci çipleri (listedeki kartlar).</summary>
    public ObservableCollection<KartCipi> AramaKartCipleri { get; } = new();

    /// <summary>Uygulanmış süzgeç (liste, "daha eski" ve Excel'e aktar bunu kullanır).</summary>
    private IslemAramasi _uygulanan = new();

    [RelayCommand] private void GelismisAramaAcKapat()
    {
        GelismisAramaAcik = !GelismisAramaAcik;
        if (GelismisAramaAcik) AramaCipleriniKur();
    }

    private void AramaCipleriniKur()
    {
        if (AramaTipCipleri.Count == 0)
            foreach (var ad in new[] { TumTipler, TipAdi(GiderTipi.Cari), TipAdi(GiderTipi.SabitGider), TipAdi(GiderTipi.KrediKarti) })
                AramaTipCipleri.Add(new SecimCipi(ad));
        AramaKartCipleri.Clear();
        foreach (var k in KartCipleri) AramaKartCipleri.Add(new KartCipi(k.Id, k.Ad));
        AramaVurgu();
    }

    private void AramaVurgu()
    {
        var tipAdi = AramaTip is { } t ? TipAdi(t) : TumTipler;
        foreach (var c in AramaTipCipleri) c.Secili = c.Ad == tipAdi;
        foreach (var k in AramaKartCipleri) k.Secili = k.Id == AramaKartId;
    }

    [RelayCommand]
    private void SecAramaTip(SecimCipi s)
    {
        AramaTip = s.Ad == TumTipler ? null : TipDegeri(s.Ad);
        AramaVurgu();
    }

    [RelayCommand]
    private void SecAramaKart(KartCipi k)
    {
        AramaKartId = AramaKartId == k.Id ? null : k.Id;
        AramaVurgu();
    }

    /// <summary>Formdaki gelişmiş süzgeci doğrular, uygular ve listeyi yeniler.</summary>
    [RelayCommand]
    private Task AramayiUygulaAsync() => CalistirAsync(async () =>
    {
        decimal? Tutar(string metin, string alan)
        {
            if (metin.Trim().Length == 0) return null;
            var p = ParaGiris.Ayristir(metin);
            Dogrula(p.Gecerli, $"{alan}: {p.Hata}");
            return p.Tutar;
        }
        var min = Tutar(AramaMin, "En az tutar");
        var max = Tutar(AramaMax, "En çok tutar");
        Dogrula(min is null || max is null || min <= max, "En az tutar en çok tutardan büyük olamaz.");
        var not = AramaNot.Trim();
        Dogrula(not.Length <= 200, "Not araması en fazla 200 karakter olabilir.");

        _uygulanan = new IslemAramasi(NotAra: not.Length == 0 ? null : not, Tip: AramaTip, KartId: AramaKartId,
            MinTutar: min, MaxTutar: max);
        AramaOzeti = AramaOzetiOlustur(_uygulanan);
        await IslemleriYukleAsync();
    });

    [RelayCommand]
    private Task AramayiTemizleAsync()
    {
        AramaNot = ""; AramaMin = ""; AramaMax = ""; AramaTip = null; AramaKartId = null;
        AramaVurgu();
        _uygulanan = new IslemAramasi();
        AramaOzeti = null;
        return YenidenListele();
    }

    private string? AramaOzetiOlustur(IslemAramasi a)
    {
        if (!a.GelismisVar) return null;
        var p = new List<string>();
        if (a.NotAra is { } n) p.Add($"Not: \"{n}\"");
        if (a.Tip is { } t) p.Add($"Tip: {TipAdi(t)}");
        if (a.KartId is { } k) p.Add($"Kart: {KartCipleri.FirstOrDefault(x => x.Id == k)?.Ad ?? k.ToString()}");
        if (a.MinTutar is { } en && a.MaxTutar is { } ec) p.Add($"{Bicim.Tl(en)} – {Bicim.Tl(ec)} ₺");
        else if (a.MinTutar is { } en2) p.Add($"≥ {Bicim.Tl(en2)} ₺");
        else if (a.MaxTutar is { } ec2) p.Add($"≤ {Bicim.Tl(ec2)} ₺");
        return "Gelişmiş süzgeç: " + string.Join(" · ", p);
    }

    /// <summary>Liste sayfası: gelişmiş süzgeç yoksa eski istek (<see cref="IKasaApi.IslemSayfasiAsync"/>) aynen.</summary>
    private Task<IslemSayfasi> SayfaGetirAsync(DateOnly? bas, DateOnly? bit, string? kanal, int limit, int offset)
        => _uygulanan.GelismisVar
            ? _api.IslemAraAsync(_uygulanan with { Baslangic = bas, Bitis = bit, Kanal = kanal }, limit, offset)
            : _api.IslemSayfasiAsync(bas, bit, kanal, null, limit, offset);

    /// <summary>Excel'e aktar: listedeki süzgeçle aynı.</summary>
    private Task<IndirilenDosya> CsvGetirAsync(DateOnly? bas, DateOnly? bit, string? kanal)
        => _uygulanan.GelismisVar
            ? _api.IslemAramaCsvAsync(_uygulanan with { Baslangic = bas, Bitis = bit, Kanal = kanal })
            : _api.IslemlerCsvAsync(bas, bit, kanal);
}
