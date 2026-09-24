using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Kasa Sayımı sayfası: editör kasadaki nakdi sayıp girer; seçili günün sonundaki defter kasası ve
/// fark kaydetmeden önce canlı gösterilir. Geçmiş her iki rolde görünür. Sayım hiçbir kasa
/// rakamını değiştirmez.
/// </summary>
public partial class KasaSayimiViewModel : TemelViewModel
{
    private readonly IKasaApi _api;

    public KasaSayimiViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        _varsayilanGun = Bugun;
        _formTarih = _varsayilanGun;
    }

    public const string IleriTarihMesaji = "Sayım tarihi ileri bir gün olamaz.";

    public ObservableCollection<KasaSayimSatiri> Sayimlar { get; } = new();

    [ObservableProperty] private bool _editorMu;

    // ---- Form ----
    [ObservableProperty] private DateTime _formTarih;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fark), nameof(FarkGorunur), nameof(FarkRenkDegeri), nameof(FarkYazi), nameof(FarkMetni))]
    private decimal _sayilanTutar;

    [ObservableProperty] private string? _not;

    /// <summary>Seçili günün sonundaki defter kasası (sunucudan; yüklenene ya da tarih geçersizken null).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefterYazi), nameof(Fark), nameof(FarkGorunur), nameof(FarkRenkDegeri), nameof(FarkYazi), nameof(FarkMetni))]
    private decimal? _defterTutari;

    /// <summary>Sayılan − defter (defter bilinmiyorsa null). Kaydetmeden önce canlı güncellenir.</summary>
    public decimal? Fark => DefterTutari is { } d ? SayilanTutar - d : null;
    public bool FarkGorunur => Fark is not null;
    /// <summary>ParaRenk dönüştürücüsü için: fark 0 → yeşil; fazla da eksik de → kırmızı.</summary>
    public decimal FarkRenkDegeri => RenkDegeri(Fark ?? 0m);
    public string FarkYazi => Fark is { } f ? FarkBicimi(f) : "";
    public string FarkMetni => Fark is { } f ? FarkAciklamasi(f) : "";
    public string DefterYazi => DefterTutari is { } d ? Bicim.Tl(d) : "—";

    /// <summary>Formların "bugün" varsayılanının kurulduğu gün (gece yarısından sonra tazelenir).</summary>
    private DateTime _varsayilanGun;
    /// <summary>Defter isteği sürümü: tarih hızlı değişince geç gelen eski yanıt yenisini ezmesin.</summary>
    private int _defterSurumu;

    public static decimal RenkDegeri(decimal fark) => -Math.Abs(fark);
    public static string FarkBicimi(decimal fark) => fark == 0m ? Bicim.Tl(0m) : Bicim.ImzaliTl(fark);
    public static string FarkAciklamasi(decimal fark) => fark switch
    {
        0m => "Kasa defterle uyuşuyor.",
        > 0m => $"Kasada defterden {Bicim.Tl(fark)} ₺ fazla var.",
        _ => $"Kasada defterden {Bicim.Tl(-fark)} ₺ eksik var.",
    };

    public Task YukleAsync() => CalistirAsync(async () =>
    {
        VarsayilanTarihiTazele();
        var liste = ListeyiYukleAsync();
        var defter = DefterIcAsync();
        await Task.WhenAll(liste, defter);
    });

    private async Task ListeyiYukleAsync()
    {
        var liste = await _api.KasaSayimlariAsync();
        Sayimlar.Clear();
        foreach (var s in liste) Sayimlar.Add(new KasaSayimSatiri(s));
    }

    /// <summary>Seçili tarihin defter kasasını yeniden çeker (tarih değişince kendiliğinden çağrılır).</summary>
    public Task DefterYukleAsync() => CalistirAsync(DefterIcAsync);

    private async Task DefterIcAsync()
    {
        var surum = ++_defterSurumu;
        var tarih = DateOnly.FromDateTime(FormTarih);
        DefterTutari = null;
        Dogrula(tarih <= BugunTarih, IleriTarihMesaji);
        KasaHesapDto h;
        try { h = await _api.KasaHesaplaAsync(tarih); }
        catch when (surum != _defterSurumu) { return; }   // bu arada başka tarih seçildi
        if (surum == _defterSurumu) DefterTutari = h.HesaplananTutar;
    }

    partial void OnFormTarihChanged(DateTime value) => _ = DefterYukleAsync();

    /// <summary>Açık kalan sayfada gün değiştiyse, dokunulmamış "bugün" varsayılanını yeni güne taşır.</summary>
    private void VarsayilanTarihiTazele()
    {
        var bugun = Bugun;
        if (bugun == _varsayilanGun) return;
        var dokunulmadi = FormTarih == _varsayilanGun;
        _varsayilanGun = bugun;
        if (dokunulmadi) FormTarih = bugun;   // defter OnFormTarihChanged ile de istenir; sürüm eskisini atar
    }

    [RelayCommand]
    private Task KaydetAsync() => CalistirAsync(async () =>
    {
        var tarih = DateOnly.FromDateTime(FormTarih);
        Dogrula(tarih <= BugunTarih, IleriTarihMesaji);
        var not = string.IsNullOrWhiteSpace(Not) ? null : Not.Trim();
        await _api.KasaSayimKaydetAsync(new KasaSayimYaz(tarih, SayilanTutar, not));
        SayilanTutar = 0m;
        Not = null;
        _varsayilanGun = Bugun;
        FormTarih = _varsayilanGun;   // tarih değiştiyse bugünün defteri yeniden istenir
        await ListeyiYukleAsync();
    });

    [RelayCommand]
    private Task SilAsync(KasaSayimSatiri s) => CalistirAsync(async () =>
    {
        await _api.KasaSayimSilAsync(s.Id);
        await ListeyiYukleAsync();
    });
}

/// <summary>Sayım geçmişi satırı (görünüm için hazır metinler).</summary>
public sealed class KasaSayimSatiri
{
    public KasaSayimSatiri(KasaSayimDto d)
    {
        Id = d.Id;
        Tarih = d.Tarih;
        SayilanTutar = d.SayilanTutar;
        HesaplananTutar = d.HesaplananTutar;
        Fark = d.Fark;
        GuncelHesaplanan = d.GuncelHesaplanan;
        Not = d.Not;
    }

    public int Id { get; }
    public DateOnly Tarih { get; }
    public decimal SayilanTutar { get; }
    /// <summary>Kayıt anındaki defter kasası (değişmez).</summary>
    public decimal HesaplananTutar { get; }
    public decimal Fark { get; }
    /// <summary>Aynı günün bugünkü defter değeri (takvim dışıysa null).</summary>
    public decimal? GuncelHesaplanan { get; }
    public string? Not { get; }

    public bool NotVar => !string.IsNullOrWhiteSpace(Not);
    public decimal FarkRenkDegeri => KasaSayimiViewModel.RenkDegeri(Fark);
    public string FarkYazi => KasaSayimiViewModel.FarkBicimi(Fark);
    public string FarkMetni => KasaSayimiViewModel.FarkAciklamasi(Fark);

    /// <summary>
    /// Sayımdan sonra o güne kadarki kayıtlar değiştiyse (defter bugün farklı hesaplanıyorsa) uyarı;
    /// değişmediyse boş.
    /// </summary>
    public string DefterDegistiMetni => GuncelHesaplanan switch
    {
        null => "Bu tarih artık takip döneminin dışında; güncel defter değeri yok.",
        { } g when g != HesaplananTutar =>
            $"Defter sonradan değişti: bugünkü değer {Bicim.Tl(g)} ₺, güncel fark {KasaSayimiViewModel.FarkBicimi(SayilanTutar - g)} ₺.",
        _ => "",
    };
}
