using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class DisariAktarViewModel(IYonetimApi api, IKasaApi finans, AuthViewModel auth) : OturumluViewModel(auth)
{
    public ObservableCollection<KanalDto> Kanallar { get; } = new();
    [ObservableProperty] private DateTime _baslangic = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _bitis = DateTime.Today;
    [ObservableProperty] private KanalDto? _kanal;
    public Task YukleAsync() => YurutAsync(async n => { var v = await finans.KanallarAsync(); if (!Gecerli(n)) return; Kanallar.Clear(); foreach (var k in v) Kanallar.Add(k); Tamamlandi(); });
    /// <summary>Raporu belleğe almadan <paramref name="hedef"/>'e yazar; hata ya da eski oturumda null.</summary>
    public async Task<IndirmeBilgisi?> IndirAsync(string bicim, Stream hedef)
    {
        IndirmeBilgisi? dosya = null;
        await YurutAsync(async n =>
        {
            if (Bitis < Baslangic) { Hata = "Bitiş tarihi başlangıçtan önce olamaz."; return; }
            var d = await api.DisariAktarAsync(DateOnly.FromDateTime(Baslangic), DateOnly.FromDateTime(Bitis), Kanal?.Ad, bicim, hedef);
            if (Gecerli(n)) dosya = d;
        });
        return dosya;
    }
    protected override void OturumTemizle() { Kanallar.Clear(); Kanal = null; }
}
