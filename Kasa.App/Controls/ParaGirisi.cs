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

    /// <summary>VM'de geçersiz tutar durup yazım görünmediğinde (ör. BindableLayout satırı yeniden üretildi) ipucu.</summary>
    public const string YenidenYazin = "Bu alandaki tutar geçersiz; binlik ayırıcı kullanmadan, en çok iki ondalıkla yeniden yazın (ör. 25000 veya 25000,50).";

    // Kontrolün kendi yazdığı Tutar (ve TwoWay bağlamanın aynı değeri geri göndermesi) metne dokunmaz.
    private bool _icerden;
    private bool _gecersizGorunum;
    // Bağlı geçersiz tutar hatası: boş alanda kırmızı metin görünmediğinden yer tutucu geçici olarak hatayı söyler.
    private bool _yerTutucuHatasi;
    private string? _eskiYerTutucu;

    public ParaGirisi()
    {
        TextChanged += (_, e) => MetniOku(e.NewTextValue);
        Unfocused += (_, _) => OdakKaybedildi();
    }

    /// <summary>Odak kaybında geçerli metni biçimler. Bağlı geçersiz tutar hatasında boş metin 0'a çevrilmez:
    /// VM'deki geçersiz değer kullanıcı yeniden yazana kadar kalır.</summary>
    internal void OdakKaybedildi()
    {
        if (!_yerTutucuHatasi && ParaAyristirici.Coz(Text, out var d, out _))
            MetniYaz(ParaAyristirici.Bicimle(d));
    }

    private static void TutarDegisti(BindableObject nesne, object eski, object yeni)
    {
        var giris = (ParaGirisi)nesne;
        if (giris._icerden || yeni is not decimal d)
            return;
        if (!ParaAyristirici.GecerliMi(d))
        {
            // Metin geçersiz yazımı zaten gösteriyorsa görünüm hazırdır; göstermiyorsa (yeni üretilen satır) alan
            // hata durumunda açılır. Metin değiştirilmez: '0' yazmak VM'deki geçersiz tutarı sessizce ezerdi.
            if (ParaAyristirici.Coz(giris.Text, out _, out _))
                giris.HataGoster(YenidenYazin, yerTutucu: true);
            return;
        }
        if (giris._yerTutucuHatasi)
            giris.HataGoster(null);   // VM geçerli bir tutar yazdı
        // Metin zaten bu tutarı gösteriyorsa (ör. '1500.50' ↔ 1500,5) dokunma.
        if (ParaAyristirici.Coz(giris.Text, out var mevcut, out _) && mevcut == d)
            return;
        giris.MetniYaz(ParaAyristirici.Bicimle(d));
    }

    private void MetniYaz(string metin)
    {
        if (Text != metin)
            Text = metin; // TextChanged → MetniOku aynı tutarı üretir, görünümü günceller
    }

    private void MetniOku(string? metin)
    {
        var gecerli = ParaAyristirici.Coz(metin, out var d, out var hata);
        _icerden = true;
        try
        { Tutar = gecerli ? d : ParaAyristirici.Gecersiz; }
        finally { _icerden = false; }
        HataGoster(gecerli ? null : hata);
    }

    private void HataGoster(string? hata, bool yerTutucu = false)
    {
        if (!yerTutucu)
            YerTutucuyuGeriAl();   // yazılan metin hatası kendini gösterir
        if (hata is null)
        {
            if (!_gecersizGorunum)
                return;
            _gecersizGorunum = false;
            ClearValue(TextColorProperty); // stil rengine dön
            ClearValue(ToolTipProperties.TextProperty);
            ClearValue(SemanticProperties.HintProperty);
            return;
        }
        _gecersizGorunum = true;
        var renk = Application.Current?.Resources.TryGetValue("Neg", out var neg) == true && neg is Color c ? c : Colors.DarkRed;
        TextColor = renk;
        ToolTipProperties.SetText(this, hata);
        SemanticProperties.SetHint(this, hata);
        if (yerTutucu && !_yerTutucuHatasi)
        {
            _yerTutucuHatasi = true;
            _eskiYerTutucu = Placeholder;
            Placeholder = ParaAyristirici.GecersizGosterim;
            PlaceholderColor = renk;
        }
    }

    private void YerTutucuyuGeriAl()
    {
        if (!_yerTutucuHatasi)
            return;
        _yerTutucuHatasi = false;
        if (Placeholder == ParaAyristirici.GecersizGosterim)
        {
            if (_eskiYerTutucu is null)
                ClearValue(PlaceholderProperty);
            else
                Placeholder = _eskiYerTutucu;
        }
        ClearValue(PlaceholderColorProperty);
        _eskiYerTutucu = null;
    }
}
