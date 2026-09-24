using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Kasa.App.Core;

// Paket C · 29: kaydedilmemiş girişler kaybolmasın.
//  - Başka haftaya geçerken (Önceki / Sonraki / Bu hafta / "O haftaya git") yazılmış ama kaydedilmemiş
//    satır varsa önce sorulur: "Kaydetmeden geç" ya da "Vazgeç".
//  - Sayfaya geri dönünce (OnAppearing → YukleAsync) aynı dönem yeniden yüklenir; girişler korunur.
//  - "O haftaya git" tablo sayfanın başında olduğu için sayfayı yukarı kaydırır (YukariKaydirIstendi).
public partial class GelenlerViewModel
{
    /// <summary>Sayfa en üste kaydırılmalı (eksik listesinden haftaya gidildi ya da soru açıldı).</summary>
    public event EventHandler? YukariKaydirIstendi;

    public static string GecisSorusuMetni(IReadOnlyList<string> kanallar)
        => $"Kaydedilmemiş giriş var: {string.Join(", ", kanallar)}. Başka haftaya geçerseniz bu girişler silinir.";

    /// <summary>Hafta değişimi onayı; soru yoksa null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GecisSorusuVar))]
    private string? _gecisSorusu;

    public bool GecisSorusuVar => GecisSorusu is not null;

    private (DateOnly? Hedef, bool Kaydir)? _bekleyenGecis;

    /// <summary>Yazılmış ama kaydedilmemiş girişi olan satırlar (kanal adına göre).</summary>
    private Dictionary<string, GelenSatiri> KaydedilmemisGirisler()
    {
        var d = new Dictionary<string, GelenSatiri>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in Satirlar)
            if (s.GirisVar) d.TryAdd(s.Kanal, s);
        return d;
    }

    /// <summary>
    /// Hafta değişimi: hedef zaten açık haftaysa yeniden yüklemez (girişler kalır). Kaydedilmemiş giriş
    /// varsa soru açar; yoksa hemen geçer.
    /// </summary>
    private Task GecisIsteAsync(DateOnly? hedef, bool kaydir)
    {
        if (hedef is { } h && h == DonemStart && Satirlar.Count > 0)
        {
            GecisVazgec();
            if (kaydir) YukariKaydirIstendi?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
        var bekleyen = Satirlar.Where(s => s.GirisVar).Select(s => s.Kanal).ToList();
        if (bekleyen.Count == 0 || !EditorMu) return GecAsync(hedef, kaydir);
        _bekleyenGecis = (hedef, kaydir);
        GecisSorusu = GecisSorusuMetni(bekleyen);
        if (kaydir) YukariKaydirIstendi?.Invoke(this, EventArgs.Empty);   // soru sayfanın başında
        return Task.CompletedTask;
    }

    private Task GecAsync(DateOnly? hedef, bool kaydir) => CalistirAsync(async () =>
    {
        GecisVazgec();
        Sonuc = null;
        await TabloYukleAsync(hedef);
        if (kaydir) YukariKaydirIstendi?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>Sorudaki "Kaydetmeden geç": girişler bırakılır, bekleyen haftaya geçilir.</summary>
    [RelayCommand]
    private Task KaydetmedenGecAsync()
        => _bekleyenGecis is { } g ? GecAsync(g.Hedef, g.Kaydir) : Task.CompletedTask;

    /// <summary>Sorudaki "Vazgeç": hafta değişmez, girişler kalır.</summary>
    [RelayCommand]
    private void GecisVazgec()
    {
        _bekleyenGecis = null;
        GecisSorusu = null;
    }
}
