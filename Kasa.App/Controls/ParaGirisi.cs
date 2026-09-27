using Kasa.App.Core;
using Microsoft.Maui.Controls;   // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Graphics;

namespace Kasa.App.Controls;

/// <summary>Para girişi. Metin ParaAyristirici kuralıyla (web cents() ile aynı) okunur; '25.000' gibi gruplanmış
/// ya da ikiden fazla ondalıklı yazım tahmin edilmez: Tutar = ParaAyristirici.Gecersiz olur, alan kırmızıya döner ve
/// VM kaydı API'ye gitmeden reddeder. Odaktayken kullanıcının metni asla yeniden biçimlenmez; biçim yalnız odak
/// kaybında ya da Tutar dışarıdan (VM) değiştiğinde yazılır.</summary>
public class ParaGirisi : Entry
{
    public static readonly BindableProperty TutarProperty = BindableProperty.Create(
        nameof(Tutar), typeof(decimal), typeof(ParaGirisi), 0m, BindingMode.TwoWay, propertyChanged: TutarDegisti);

    public decimal Tutar
    {
        get => (decimal)GetValue(TutarProperty);
        set => SetValue(TutarProperty, value);
    }

    // Kontrolün kendi yazdığı Tutar (ve TwoWay bağlamanın aynı değeri geri göndermesi) metne dokunmaz.
    private bool _icerden;
    private bool _gecersizGorunum;

    public ParaGirisi()
    {
        TextChanged += (_, e) => MetniOku(e.NewTextValue);
        Unfocused += (_, _) => { if (ParaAyristirici.Coz(Text, out var d, out _)) MetniYaz(ParaAyristirici.Bicimle(d)); };
    }

    private static void TutarDegisti(BindableObject nesne, object eski, object yeni)
    {
        var giris = (ParaGirisi)nesne;
        if (giris._icerden || yeni is not decimal d || !ParaAyristirici.GecerliMi(d)) return;
        // Metin zaten bu tutarı gösteriyorsa (ör. '1500.50' ↔ 1500,5) dokunma.
        if (ParaAyristirici.Coz(giris.Text, out var mevcut, out _) && mevcut == d) return;
        giris.MetniYaz(ParaAyristirici.Bicimle(d));
    }

    private void MetniYaz(string metin)
    {
        if (Text != metin) Text = metin; // TextChanged → MetniOku aynı tutarı üretir, görünümü günceller
    }

    private void MetniOku(string? metin)
    {
        var gecerli = ParaAyristirici.Coz(metin, out var d, out var hata);
        _icerden = true;
        try { Tutar = gecerli ? d : ParaAyristirici.Gecersiz; }
        finally { _icerden = false; }
        HataGoster(gecerli ? null : hata);
    }

    private void HataGoster(string? hata)
    {
        if (hata is null)
        {
            if (!_gecersizGorunum) return;
            _gecersizGorunum = false;
            ClearValue(TextColorProperty); // stil rengine dön
            ClearValue(ToolTipProperties.TextProperty);
            ClearValue(SemanticProperties.HintProperty);
            return;
        }
        _gecersizGorunum = true;
        TextColor = Application.Current?.Resources.TryGetValue("Neg", out var renk) == true && renk is Color c ? c : Colors.DarkRed;
        ToolTipProperties.SetText(this, hata);
        SemanticProperties.SetHint(this, hata);
    }
}
