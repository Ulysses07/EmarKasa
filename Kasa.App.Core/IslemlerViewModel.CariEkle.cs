using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket C · 27: formdan cari ekleme ("Cari olarak ekle") ve benzer ad sorusu.
public partial class IslemlerViewModel
{
    public const string CariUzunMesaji = "Cari adı en fazla 200 karakter olabilir.";

    /// <summary>Yazılan ad kayıtlı bir cari değil: "Cari olarak ekle" düğmesi görünür (sabit giderde kalem düğmesi var).</summary>
    public bool CariEklenebilir
    {
        get
        {
            var ad = DuzenCari?.Trim() ?? "";
            return EditorMu && !SabitGiderMi && ad.Length > 0 && KayitliCari(ad) is null;
        }
    }

    /// <summary>"Bunu mu demek istediniz?" seçenekleri.</summary>
    public ObservableCollection<string> BenzerCariler { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BenzerCariSoruluyor))]
    private string? _benzerCariSorusu;

    public bool BenzerCariSoruluyor => BenzerCariSorusu is not null;

    /// <summary>
    /// Yazılan adı cari olarak ekler. Önce kayıtlı adlara benzerliğe bakar: benzer varsa eklemez,
    /// "Bunu mu demek istediniz: …?" sorar (seçilirse o ad, "Yine de ekle" ile yeni cari).
    /// </summary>
    [RelayCommand]
    private Task CariEkleAsync() => CalistirAsync(async () =>
    {
        var ad = DuzenCari?.Trim() ?? "";
        Dogrula(ad.Length > 0, CariBosMesaji);
        Dogrula(ad.Length <= 200, CariUzunMesaji);
        if (KayitliCari(ad) is { } kayitli)
        {
            DuzenCari = kayitli;
            return;
        }
        var benzer = CariBenzerlik.Benzerler(ad, _cariAdlari);
        if (benzer.Count > 0)
        {
            BenzerCariler.Clear();
            foreach (var b in benzer) BenzerCariler.Add(b);
            BenzerCariSorusu = $"Bunu mu demek istediniz: {string.Join(" / ", benzer)}?";
            return;
        }
        await CariyiOlusturAsync(ad);
    });

    /// <summary>Benzer adlardan birini seçer (yeni cari eklenmez).</summary>
    [RelayCommand]
    private void BenzerCariSec(string ad)
    {
        DuzenCari = ad;
        BenzerCariKapat();
        OdakIste(OdakTutar);
    }

    /// <summary>Benzerlere rağmen yazılan adı yeni cari olarak ekler.</summary>
    [RelayCommand]
    private Task CariYineDeEkleAsync() => CalistirAsync(async () =>
    {
        var ad = DuzenCari?.Trim() ?? "";
        Dogrula(ad.Length > 0, CariBosMesaji);
        BenzerCariKapat();
        await CariyiOlusturAsync(ad);
    });

    [RelayCommand]
    private void BenzerCariKapat()
    {
        BenzerCariSorusu = null;
        BenzerCariler.Clear();
    }

    /// <summary>Mevcut cari ekleme çağrısıyla ekler, listeyi tazeler ve kayıtlı yazımı seçer.</summary>
    private async Task CariyiOlusturAsync(string ad)
    {
        await _api.CariOlusturAsync(new CariYaz(ad, true));
        CarileriKur(await _api.CarilerAsync());
        DuzenCari = KayitliCari(ad) ?? ad;
        OnPropertyChanged(nameof(CariEklenebilir));
        OdakIste(OdakTutar);
    }
}
