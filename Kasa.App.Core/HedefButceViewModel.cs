using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Hedef/bütçe giriş satırı: kayıtlı değer, gerçekleşen ve düzenlenen metin (boş = hedef yok).</summary>
public partial class HedefGirisSatiri : ObservableObject
{
    public HedefGirisSatiri(int id, string ad, bool aktif, decimal? kayitli, decimal gerceklesen, decimal? yuzde, decimal? sablon)
    {
        Id = id;
        Ad = ad;
        Aktif = aktif;
        Kayitli = kayitli;
        Gerceklesen = gerceklesen;
        Yuzde = yuzde;
        Sablon = sablon;
        _metin = kayitli is { } k ? ParaGiris.Bicimle(k) is { Length: > 0 } m ? m : "0" : "";
    }

    public int Id { get; }
    public string Ad { get; }
    public bool Aktif { get; }
    public decimal? Kayitli { get; }
    public decimal Gerceklesen { get; }
    public decimal? Yuzde { get; }
    public decimal? Sablon { get; }

    [ObservableProperty] private string _metin;

    public string GerceklesenMetni => $"Gerçekleşen {Bicim.Tl(Gerceklesen)} ₺";
    public string YuzdeMetni => Yuzde is { } y ? "%" + y.ToString("0.0", Kultur.Turkce) : "";
    public string SablonMetni => Sablon is { } s ? $"Tekrarlayan şablon {Bicim.Tl(s)} ₺" : "";
    public bool SablonVar => Sablon is not null;
    public double Oran => Kayitli is > 0m ? Math.Clamp((double)(Gerceklesen / Kayitli.Value), 0d, 1d) : 0d;

    /// <summary>Metni çözer: boş → null (satır silinir), geçersiz → hata mesajı.</summary>
    public (bool Gecerli, decimal? Deger, string? Hata) Coz()
    {
        if (string.IsNullOrWhiteSpace(Metin)) return (true, null, null);
        var r = ParaGiris.Ayristir(Metin);
        return r.Gecerli ? (true, r.Tutar, null) : (false, null, $"{Ad}: {r.Hata}");
    }
}

/// <summary>
/// 05 · Kanal gelir hedefleri ve sabit gider kalemi bütçeleri (aylık). Gerçekleşen Aylık raporla
/// aynıdır (kanal: gelen + çek tahsilatı; kalem: kartsız sabit gider). Boş bırakılan satır hedefsizdir.
/// "Geçen aydan kopyala" yalnız bu ayda olmayan satırları doldurur.
/// </summary>
public partial class HedefButceViewModel : TemelViewModel
{
    private readonly IKasaApi _api;

    public HedefButceViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        var bugun = BugunTarih;
        _yil = bugun.Year;
        _ay = bugun.Month;
    }

    public const string DegisiklikYok = "Değişiklik yok.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AyEtiketi))]
    private int _yil;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AyEtiketi))]
    private int _ay;
    public string AyEtiketi => KasaDokumuGorunum.AyEtiketi(Yil, Ay);

    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] private string? _bilgi;

    public ObservableCollection<HedefGirisSatiri> Kanallar { get; } = new();
    public ObservableCollection<HedefGirisSatiri> Kalemler { get; } = new();

    private int _surum;

    public void AyUygula(int yil, int ay)
    {
        if (yil is < 2000 or > 2100 || ay is < 1 or > 12) return;
        Yil = yil;
        Ay = ay;
    }

    public Task YukleAsync()
    {
        Bilgi = null;
        return CalistirAsync(DoldurAsync);
    }

    private async Task DoldurAsync()
    {
        var surum = ++_surum;
        int yil = Yil, ay = Ay;
        HedefButceDto d;
        try { d = await _api.HedefButceAsync(yil, ay); }
        catch when (surum != _surum) { return; }
        if (surum == _surum) Kur(d);
    }

    /// <summary>Pasif kanal/kalem yalnız kayıtlı değeri ya da gerçekleşeni varsa gösterilir.</summary>
    private void Kur(HedefButceDto d)
    {
        Kanallar.Clear();
        foreach (var k in d.Kanallar.Where(k => k.Aktif || k.Hedef is not null || k.Gerceklesen != 0m))
            Kanallar.Add(new HedefGirisSatiri(k.KanalId, k.Kanal, k.Aktif, k.Hedef, k.Gerceklesen, k.Yuzde, null));
        Kalemler.Clear();
        foreach (var k in d.Kalemler.Where(k => k.Aktif || k.Butce is not null || k.Gerceklesen != 0m))
            Kalemler.Add(new HedefGirisSatiri(k.GiderKalemiId, k.Kalem, k.Aktif, k.Butce, k.Gerceklesen, k.Yuzde, k.Sablon));
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

    /// <summary>Yalnız değişen satırları gönderir (boş = sil). Geçersiz tutar hiçbir şey göndermez.</summary>
    [RelayCommand]
    private Task KaydetAsync() => CalistirAsync(async () =>
    {
        Bilgi = null;
        Dogrula(EditorMu, HataMesaji.Yetkisiz);
        var hedefler = new List<HedefYaz>();
        var butceler = new List<ButceYaz>();
        foreach (var s in Kanallar)
        {
            var (ok, deger, hata) = s.Coz();
            Dogrula(ok, hata!);
            if (deger != s.Kayitli) hedefler.Add(new HedefYaz(s.Id, deger));
        }
        foreach (var s in Kalemler)
        {
            var (ok, deger, hata) = s.Coz();
            Dogrula(ok, hata!);
            if (deger != s.Kayitli) butceler.Add(new ButceYaz(s.Id, deger));
        }
        if (hedefler.Count == 0 && butceler.Count == 0)
        {
            Bilgi = DegisiklikYok;
            return;
        }
        int yil = Yil, ay = Ay;
        var surum = ++_surum;
        var d = await _api.HedefButceKaydetAsync(new HedefButceYaz(new DateOnly(yil, ay, 1),
            hedefler.Count > 0 ? hedefler : null, butceler.Count > 0 ? butceler : null));
        if (surum != _surum) return;
        Kur(d);
        Bilgi = $"{KasaDokumuGorunum.AyEtiketi(yil, ay)} hedef ve bütçeleri kaydedildi ({hedefler.Count + butceler.Count} satır).";
    });

    [RelayCommand]
    private Task GecenAydanKopyalaAsync() => CalistirAsync(async () =>
    {
        Bilgi = null;
        Dogrula(EditorMu, HataMesaji.Yetkisiz);
        var r = await _api.HedefButceKopyalaAsync(new DateOnly(Yil, Ay, 1));
        await DoldurAsync();
        Bilgi = $"Geçen aydan {r.Kopyalanan} satır kopyalandı"
                + (r.Atlanan > 0 ? $"; bu ayda zaten olan {r.Atlanan} satır korundu." : ".");
    });
}
