using System.Diagnostics;
using Kasa.App.Core;
using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Accessibility;
using Microsoft.Maui.Controls;

namespace Kasa.App.Controls;

/// <summary>
/// Kaydırıcının içindeki öğeyi görünür yapan ortak yardımcı (eski KartTakipPage.GorunurYap; tasarım 2026-10-02 §1 "Hataya
/// kaydırma"). Karar <see cref="KaydirmaHesabi"/>'ndadır (hedef görünüyorsa kaydırılmaz). Zamanlama: hedefin bir sonraki
/// SizeChanged'i (yerleşim turunda üst öğeleri de yerleşmiş olur) ya da, boyutu değişmeden yalnız yeri değişirse, 100 ms aralıklı
/// yoklama; yoklama hedef yerleşmiş (genişliği olan) bulunca ya da 1 sn sonra biter. Kaydırma yerleşim turunun bitimine
/// (Dispatch) bırakılır. Yeni istek eskisini geçersiz kılar; istek bir kez kaydırır. <see cref="HatayaGit"/> kaydetme başarısız
/// olunca ilk hatalı alana (yoksa formun genel hata kutusuna) kaydırır ve alana odaklanır.
/// </summary>
public sealed class GorunurYapici(ScrollView kaydirici)
{
    /// <summary>Hedefin (üst, yükseklik) ve kaydırıcının (kaydırma konumu, görünür yükseklik) değerlerinden yeni kaydırma konumu;
    /// null: kaydırılmaz (KaydirmaHesabi).</summary>
    public delegate double? KaydirmaKarari(double ust, double yukseklik, double kaydirmaY, double gorunurYukseklik);

    /// <summary>Son kaydırma isteğinin sırası ve kaydırması yapılmış istek: yalnız en son istek, bir kez kaydırır.</summary>
    private int _istek, _kaydirilan;

    /// <summary>Hedefi görünür yapar; <paramref name="sonra"/> kaydırma denendikten sonra çalışır (ör. odaklanma).</summary>
    public void Yap(View hedef, KaydirmaKarari karar, Action? sonra = null)
    {
        var istek = ++_istek;
        var deneme = 0;
        void Boyutlandi(object? sender, EventArgs e) => Yerlesti();
        void Yerlesti()
        {
            hedef.SizeChanged -= Boyutlandi;
            kaydirici.Dispatcher.Dispatch(() => Kaydir(hedef, karar, istek, sonra));
        }
        void Yokla()
        {
            if (istek != _istek || istek == _kaydirilan)
            {
                hedef.SizeChanged -= Boyutlandi;
                return;
            }
            if (hedef.Width > 0)
                Yerlesti();
            else if (++deneme < 10)
                kaydirici.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), Yokla);
            else
                hedef.SizeChanged -= Boyutlandi;
        }
        hedef.SizeChanged += Boyutlandi;
        kaydirici.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), Yokla);
    }

    /// <summary>Formun ilk hatalı alanına (yoksa genel hata kutusuna) kaydırır ve alana odaklanır; odaklanamayan alanda (ör.
    /// çip grubu) ya da genel hatada ileti ekran okuyucuya duyurulur (Ö-2). Duyuru genel hatada hemen yapılır (kaydırmanın
    /// sonucunu beklemez); alan hatasında odaklanma denendikten sonra (yalnız odaklanamazsa) yapılır.</summary>
    /// <param name="form">Formun alanlarını içeren görünüm (FormAlani'ler bunun altındadır).</param>
    /// <param name="genelKutu">Formun en üstündeki genel hata kutusu.</param>
    /// <param name="duyur">Testler içindir; üretimde <see cref="SemanticScreenReader"/> kullanılır.</param>
    public void HatayaGit(View form, AlanHatalari hatalar, View genelKutu, Action<string>? duyur = null)
    {
        duyur ??= s => SemanticScreenReader.Default.Announce(s);
        if (IlkHataliAlan(form, hatalar) is { } alan)
            Yap(alan, KaydirmaHesabi.FormKaydirmasi, () => OdaklanVeyaDuyur(alan, duyur));
        else if (hatalar.Genel is { } genel)
        {
            duyur(genel);
            Yap(genelKutu, KaydirmaHesabi.FormKaydirmasi);
        }
    }

    /// <summary>Alana odaklanılamazsa (girdi olmayan içerik, ör. çip grubu) hatası ekran okuyucuya duyurulur (Ö-2).</summary>
    internal static void OdaklanVeyaDuyur(FormAlani alan, Action<string> duyur)
    {
        if (!alan.Odaklan())
            duyur(alan.Hata ?? "");
    }

    /// <summary>Hataların konulduğu sırayla ilk hatası olan, görünür (kendisi ve bütün ataları) form alanı; yoksa null.</summary>
    public static FormAlani? IlkHataliAlan(View form, AlanHatalari hatalar)
    {
        var alanlar = form.GetVisualTreeDescendants().OfType<FormAlani>().Where(GorunurMu).ToList();
        return hatalar.Alanlar.Select(ad => alanlar.FirstOrDefault(f => f.Alan == ad)).FirstOrDefault(f => f is not null);
    }

    private static bool GorunurMu(VisualElement v)
    {
        for (Element? e = v; e is not null; e = e.Parent)
            if (e is VisualElement { IsVisible: false })
                return false;
        return true;
    }

    private async void Kaydir(View hedef, KaydirmaKarari karar, int istek, Action? sonra)
    {
        if (istek != _istek || istek == _kaydirilan || !hedef.IsVisible)
            return;
        _kaydirilan = istek;
        try
        {
            if (karar(KaydiriciyaGoreY(hedef), hedef.Height, kaydirici.ScrollY, kaydirici.Height) is { } y)
                await kaydirici.ScrollToAsync(kaydirici.ScrollX, y, true);
            sonra?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ekran kaydırılamadı: {ex}");
        }
    }

    /// <summary>Hedefin kaydırılan içeriğe göre üstü: ata zinciri boyunca her öğenin üst öğesine göre yeri (Frame.Y) toplanır;
    /// hedef kaydırıcının içinde değilse NaN (kaydırılmaz).</summary>
    private double KaydiriciyaGoreY(VisualElement hedef)
    {
        var y = 0d;
        for (Element? e = hedef; e is not null; e = e.Parent)
        {
            if (ReferenceEquals(e, kaydirici))
                return y;
            if (e is VisualElement v)
                y += v.Frame.Y;
        }
        return double.NaN;
    }
}
