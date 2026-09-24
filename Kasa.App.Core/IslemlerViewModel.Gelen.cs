using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket C · 29: İşlemler sayfasındaki gelen formu da kayıtlı geleni sessizce ezmez: o dönemde bu
// kanalın geleni varsa "Üzerine yaz / Üstüne ekle" sorulur; yazma, görülen tutarla korumalıdır (409).
public partial class IslemlerViewModel
{
    /// <summary>Kayıtlı gelen varken sorulan soru; yoksa null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GelenCakismaVar))]
    private string? _gelenCakismaSorusu;

    public bool GelenCakismaVar => GelenCakismaSorusu is not null;

    /// <summary>Son gelen kaydının özeti (form altında).</summary>
    [ObservableProperty] private string? _gelenSonuc;

    private GelenYaz? _bekleyenGelen;
    private decimal _gelenMevcut;

    /// <summary>Hizalanmış dönem için korumalı gelen yazımı (ana dosyadaki GelenKaydetAsync çağırır).</summary>
    private async Task GelenKorumaliYazAsync(GelenYaz g)
    {
        GelenSonuc = null;
        var mevcut = (await _api.GelenlerAsync(g.DonemStart)).FirstOrDefault(x => x.Kanal == g.Kanal)?.TutarTl ?? 0m;
        if (mevcut != 0m && mevcut != g.TutarTl)
        {
            GelenSor(g, mevcut, $"{g.Kanal} için bu dönemde ({g.DonemStart.ToString("d MMM", Kultur.Turkce)} haftası) " +
                                $"kayıtlı gelen var: {Bicim.Tl(mevcut)} ₺. Girdiğiniz {Bicim.Tl(g.TutarTl)} ₺ ne olsun?");
            return;
        }
        await GelenGonderAsync(g, mevcut);
    }

    private async Task GelenGonderAsync(GelenYaz g, decimal beklenen)
    {
        var r = await _api.GelenKorumaliKaydetAsync(g, beklenen);
        if (!r.Kaydedildi)
        {
            GelenSor(g, r.MevcutTutar, $"{g.Kanal} geleni bu arada başka bir yerden değişti: kayıtlı {Bicim.Tl(r.MevcutTutar)} ₺. " +
                                       $"Girdiğiniz {Bicim.Tl(g.TutarTl)} ₺ ne olsun?");
            return;
        }
        GelenCakismaKapat();
        GelenSonuc = $"Gelen kaydedildi: {g.Kanal} · {Bicim.Tl(r.Gelen?.TutarTl ?? g.TutarTl)} ₺";
        GelenKanal = "";
        GelenTutar = 0;
    }

    private void GelenSor(GelenYaz g, decimal mevcut, string soru)
    {
        _bekleyenGelen = g;
        _gelenMevcut = mevcut;
        GelenCakismaSorusu = soru;
    }

    /// <summary>Kayıtlı tutarın yerine girilen tutar yazılır.</summary>
    [RelayCommand]
    private Task GelenUzerineYazAsync() => CalistirAsync(async () =>
    {
        if (_bekleyenGelen is not { } g) return;
        await GelenGonderAsync(g, _gelenMevcut);
    });

    /// <summary>Girilen tutar kayıtlı tutara eklenir.</summary>
    [RelayCommand]
    private Task GelenUstuneEkleAsync() => CalistirAsync(async () =>
    {
        if (_bekleyenGelen is not { } g) return;
        await GelenGonderAsync(g with { TutarTl = _gelenMevcut + g.TutarTl }, _gelenMevcut);
    });

    [RelayCommand]
    private void GelenCakismaKapat()
    {
        GelenCakismaSorusu = null;
        _bekleyenGelen = null;
    }

    // Gelen formu değişince bekleyen soru geçersizdir.
    partial void OnGelenTarihChanged(DateTime oldValue, DateTime newValue) => GelenCakismaKapat();
    partial void OnGelenKanalChanged(string? oldValue, string newValue) => GelenCakismaKapat();
    partial void OnGelenTutarChanged(decimal oldValue, decimal newValue) => GelenCakismaKapat();
}
