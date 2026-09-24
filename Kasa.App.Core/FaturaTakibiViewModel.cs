using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Fatura takibi sayfası: faturası beklenen ödemeler (cari cari, en eski önce), seçilen ayın belge
/// türü dökümü (belgesiz toplam dahil) ve ay sonu muhasebeci listesi (CSV). Her iki rol görür;
/// "Fatura geldi" (gelen faturanın türü, no'su ve isteğe bağlı fotoğrafı/PDF'i) yalnız editörde.
/// Para hesabına dokunmaz.
/// </summary>
public partial class FaturaTakibiViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    private readonly IDosyaKaydedici? _kaydedici;

    public FaturaTakibiViewModel(IKasaApi api, TimeProvider? zaman = null, IDosyaKaydedici? kaydedici = null) : base(zaman)
    {
        _api = api;
        _kaydedici = kaydedici;
        _yil = Bugun.Year;
        _ay = Bugun.Month;
    }

    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AyBasligi))] private int _yil;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AyBasligi))] private int _ay;
    [ObservableProperty] private FaturaTakibiDto? _veri;
    [ObservableProperty] private string? _aktarilanDosya;

    /// <summary>Eki gösterilen işlem ve ekleri (satırdaki "Ekler" düğmesi).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EkPaneliGorunur))] private FaturaSatiri? _ekliSatir;
    public ObservableCollection<EkDto> SeciliEkler { get; } = new();
    public bool EkPaneliGorunur => EkliSatir is not null;

    /// <summary>Eki cihazda açan servis (MAUI DI verir).</summary>
    public IEkAcici? EkAcici { get; set; }

    public ObservableCollection<FaturaCariGrubu> Bekleyenler { get; } = new();
    public ObservableCollection<BelgeTuruToplamDto> AyOzeti { get; } = new();

    public string AyBasligi => new DateOnly(Yil, Ay, 1).ToString("MMMM yyyy", Kultur.Turkce);
    public bool BekleyenYok => Bekleyenler.Count == 0;
    public string BekleyenOzeti => Veri is { BekleyenAdet: > 0 } v
        ? $"{v.BekleyenAdet} ödeme · {Bicim.Tl(v.BekleyenToplam)} ₺ faturası bekleniyor"
        : "Faturası beklenen ödeme yok.";
    public string BelgesizOzeti => Veri is { } v
        ? $"{Bicim.Tl(v.AyBelgesizToplam)} ₺ ({v.AyBelgesizAdet} işlem)"
        : "—";

    private int _surum;

    public Task YukleAsync() => CalistirAsync(YenileIcAsync);

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

    /// <summary>Seçili ayın muhasebeci listesini CSV olarak kaydeder (Belgeler\Emar Kasa).</summary>
    [RelayCommand]
    private Task MuhasebeciListesiAsync() => CalistirAsync(async () =>
    {
        int yil = Yil, ay = Ay;
        AktarilanDosya = null;
        AktarilanDosya = await ExcelAktarma.AktarAsync(_kaydedici, () => _api.MuhasebeciCsvAsync(yil, ay));
    });

    // ---------------------------------------------------------------- "Fatura geldi" formu (yalnız editör)

    /// <summary>Platform dosya/fotoğraf seçicisi (gelen faturayı eklemek için; MAUI DI verir).</summary>
    public IDosyaSecici? DosyaSecici { get; set; }

    /// <summary>Faturası gelen ödeme (dolu = "Fatura geldi" formu açık).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GelenFormuGorunur), nameof(GelenBasligi))]
    private FaturaSatiri? _gelenSatir;

    /// <summary>Gelen faturanın türü (varsayılan e-Fatura; "Belgesiz" seçilemez: fatura geldi).</summary>
    [ObservableProperty] private BelgeTuru _gelenTur = BelgeTuru.EFatura;
    [ObservableProperty] private string? _gelenNo;

    /// <summary>Gelen fatura türü çipleri: e-Fatura · e-Arşiv · Fiş · Makbuz.</summary>
    public ObservableCollection<SecimCipi> GelenTurCipleri { get; } =
        new(GelenTurleri.Select(t => new SecimCipi(BelgeMetin.TurAdi(t))));

    /// <summary>Gelen faturanın fotoğrafı/PDF'i: kaydette yüklenir.</summary>
    public ObservableCollection<BekleyenEk> GelenEkler { get; } = new();

    public bool GelenFormuGorunur => GelenSatir is not null;
    public bool GelenEkVar => GelenEkler.Count > 0;
    public string GelenBasligi => GelenSatir is { } s
        ? $"Fatura geldi · {s.Islem.Cari} · {Bicim.Tl(s.Tutar)} ₺ · {s.Tarih.ToString("dd.MM.yyyy", Kultur.Turkce)}"
        : "";

    public static readonly IReadOnlyList<BelgeTuru> GelenTurleri = [BelgeTuru.EFatura, BelgeTuru.EArsiv, BelgeTuru.Fis, BelgeTuru.Makbuz];
    public const string BelgeNoUzunMesaji = "Belge no en fazla 50 karakter olabilir.";

    partial void OnGelenTurChanged(BelgeTuru value) => GelenTurVurgu();

    private void GelenTurVurgu()
    {
        var ad = BelgeMetin.TurAdi(GelenTur);
        foreach (var c in GelenTurCipleri) c.Secili = c.Ad == ad;
    }

    /// <summary>
    /// "Fatura geldi": satırın formunu açar. Tür, ödemede gerçek bir fatura türü varsa odur, yoksa (boş ya da
    /// Belgesiz) e-Fatura; belge no ödemedekidir. Kaydet'e basılınca bekleniyor işareti kalkar.
    /// </summary>
    [RelayCommand]
    private void FaturaGeldi(FaturaSatiri s)
    {
        if (!EditorMu) { Hata = HataMesaji.Yetkisiz; return; }
        Hata = null;
        GelenSatir = s;
        GelenTur = s.Islem.BelgeTuru is { } t && GelenTurleri.Contains(t) ? t : BelgeTuru.EFatura;
        GelenTurVurgu();
        GelenNo = s.Islem.BelgeNo;
        GelenEkler.Clear();
        OnPropertyChanged(nameof(GelenEkVar));
    }

    [RelayCommand] private void SecGelenTur(SecimCipi c) => GelenTur = GelenTurleri.FirstOrDefault(t => BelgeMetin.TurAdi(t) == c.Ad, GelenTur);

    [RelayCommand]
    private void GelenVazgec()
    {
        GelenSatir = null;
        GelenNo = null;
        GelenEkler.Clear();
        OnPropertyChanged(nameof(GelenEkVar));
    }

    [RelayCommand]
    private Task GelenEkEkleAsync() => CalistirAsync(async () =>
    {
        Dogrula(DosyaSecici is not null, IslemlerViewModel.SeciciYokMesaji);
        if (GelenSatir is not { } s) return;
        var hatalar = new List<string>();
        foreach (var d in await DosyaSecici!.BelgeSecAsync())
        {
            if (EkKurallari.Hata(d) is string h) { hatalar.Add(h); continue; }
            if (s.Islem.EkSayisi + GelenEkler.Count >= EkKurallari.IslemBasinaEnFazla) { hatalar.Add(EkKurallari.SayiMesaji); break; }
            GelenEkler.Add(new BekleyenEk(d.Ad, d.Icerik));
        }
        OnPropertyChanged(nameof(GelenEkVar));
        if (hatalar.Count > 0) throw new DogrulamaHatasi(string.Join(" ", hatalar.Distinct()));
    });

    [RelayCommand]
    private void GelenEkKaldir(BekleyenEk e)
    {
        GelenEkler.Remove(e);
        OnPropertyChanged(nameof(GelenEkVar));
    }

    /// <summary>
    /// Gelen faturayı kaydeder: tür + no yazılır, "fatura bekleniyor" kalkar, seçilen dosyalar yüklenir.
    /// Bir dosya yüklenemezse fatura bilgisi yine kayıtlıdır: form açık kalır, yüklenemeyenler bekler ve
    /// Kaydet'e yeniden basınca tekrar denenir.
    /// </summary>
    [RelayCommand]
    private Task GelenKaydetAsync() => CalistirAsync(async () =>
    {
        Dogrula(EditorMu, HataMesaji.Yetkisiz);
        if (GelenSatir is not { } s) return;
        var no = string.IsNullOrWhiteSpace(GelenNo) ? null : GelenNo.Trim();
        Dogrula(no is null || no.Length <= 50, BelgeNoUzunMesaji);
        await _api.BelgeGuncelleAsync(s.Islem.Id, new BelgeBilgisi(GelenTur, no, false));

        var hatalar = new List<string>();
        foreach (var e in GelenEkler.ToList())
        {
            try
            {
                await _api.EkYukleAsync(s.Islem.Id, e.Ad, e.Icerik);
                GelenEkler.Remove(e);
            }
            catch (Exception ex) { hatalar.Add($"{e.Ad}: {HataMesaji.Coz(ex)}"); }
        }
        OnPropertyChanged(nameof(GelenEkVar));
        if (hatalar.Count == 0) GelenVazgec();
        await YenileIcAsync();
        if (hatalar.Count > 0)
            throw new DogrulamaHatasi(
                $"Fatura bilgisi kaydedildi ama {hatalar.Count} dosya yüklenemedi ({string.Join("; ", hatalar)}). Tekrar denemek için Kaydet'e basın.");
    });

    /// <summary>Seçili ayı yükler; hızlı ay değişiminde geç gelen eski yanıt yenisini ezmez.</summary>
    private async Task YenileIcAsync()
    {
        var surum = ++_surum;
        int yil = Yil, ay = Ay;
        FaturaTakibiDto v;
        try { v = await _api.FaturaTakibiAsync(yil, ay); }
        catch when (surum != _surum) { return; }
        if (surum != _surum) return;
        Veri = v;
        Bekleyenler.Clear();
        foreach (var g in v.Bekleyenler) Bekleyenler.Add(new FaturaCariGrubu(g));
        AyOzeti.Clear();
        foreach (var o in v.AyOzeti) AyOzeti.Add(o);
        OnPropertyChanged(nameof(BekleyenYok));
        OnPropertyChanged(nameof(BekleyenOzeti));
        OnPropertyChanged(nameof(BelgesizOzeti));
    }

    [RelayCommand]
    private Task EkleriGosterAsync(FaturaSatiri s) => CalistirAsync(async () =>
    {
        EkliSatir = s;
        SeciliEkler.Clear();
        foreach (var e in await _api.EklerAsync(s.Islem.Id)) SeciliEkler.Add(e);
    });

    [RelayCommand]
    private void EkleriKapat()
    {
        EkliSatir = null;
        SeciliEkler.Clear();
    }

    [RelayCommand]
    private Task EkAcAsync(EkDto e) => CalistirAsync(async () =>
    {
        Dogrula(EkAcici is not null, IslemlerViewModel.AciciYokMesaji);
        var d = await _api.EkIndirAsync(e.Id);
        await EkAcici!.AcAsync(d.DosyaAdi, d.Icerik);
    });
}

/// <summary>Bir carinin faturası beklenen ödemeleri.</summary>
public sealed class FaturaCariGrubu(FaturaBekleyenCariDto g)
{
    public string Cari { get; } = g.Cari;
    public string Ozet { get; } = $"{g.Adet} ödeme · en eski {g.EnEskiTarih.ToString("d MMMM yyyy", Kultur.Turkce)}";
    public decimal Toplam { get; } = g.Toplam;
    public IReadOnlyList<FaturaSatiri> Islemler { get; } = g.Islemler.Select(i => new FaturaSatiri(i)).ToList();
}

/// <summary>Fatura takibinde tek ödeme satırı.</summary>
public sealed class FaturaSatiri(FaturaIslemDto i)
{
    public FaturaIslemDto Islem { get; } = i;
    public DateOnly Tarih => Islem.Tarih;
    public decimal Tutar => Islem.TutarTl;
    public string Aciklama { get; } = string.Join(" · ", new[]
    {
        i.Kanal,
        BelgeMetin.Ozet(new BelgeBilgisi(i.BelgeTuru, i.BelgeNo, false)),
        i.EkSayisi > 0 ? $"{i.EkSayisi} ek" : "",
        i.Not ?? "",
    }.Where(p => p.Length > 0));
    public bool EkVar => Islem.EkSayisi > 0;
}
