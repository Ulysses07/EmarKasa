using System.Collections.ObjectModel;
using Kasa.ApiClient;
using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>TakipUi (kodla yazılmış sayfaların yapı taşları) davranışı: boş durum satırları yer kaplamaz, form açan düğmenin kenarı
/// değişmez, Editor'ün ikinci koşulu, meşgul göstergesi ve hata. Eski görünümle birebir karşılaştırmalar, takip ailesi bilerek tek sisteme
/// taşındığında kalktı (docs/specs/2026-11-26-slop-temizligi.md §7).</summary>
public partial class GorunumEsdegerligiTests
{
    public sealed record ListeSatiri(string Baslik, string Ozet, bool Acik);

    public sealed class TakipBaglami
    {
        public bool Mesgul { get; set; }
        public string? Hata { get; set; }
        public string? Mesaj { get; set; }
        public DateTimeOffset? SonGuncelleme { get; set; } = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(3));
        public string SonGuncellemeMetni => SonGuncelleme is { } z ? $"Son güncelleme: {z:dd.MM.yyyy HH:mm}" : "Henüz yüklenmedi.";
        public string Ozet { get; set; } = "Kart borcu 12.500,00 ₺";
        public string Metin { get; set; } = "Açıklama metni";
        public decimal Tutar { get; set; } = 250m;
        public bool Secili { get; set; } = true;
        public bool EditorMu { get; set; } = true;
        public ObservableCollection<ListeSatiri> Satirlar { get; } = [new("Bonus", "Kesim 12, son ödeme 22", true), new("World", "Pasif", false)];
        public ObservableCollection<TakipKanalSecimi> Kanallar { get; } =
            [new(new KanalDto(1, "MEZAT", true, 1, 0)) { Secili = true }, new(new KanalDto(2, "TOPTAN", true, 2, 0))];
        public BenzerlikDurumu Benzerlik { get; } = new();
    }

    public sealed class BenzerlikDurumu
    {
        public bool UyariVar => true;
        public string Uyari => "Aynı gün aynı tutarda kayıt var.";
    }

    private static TakipBaglami TakipBaglam(bool mesgul = false, bool hata = true, bool mesaj = true) => new()
    {
        Mesgul = mesgul,
        Hata = hata ? HataMetni : null,
        Mesaj = mesaj ? MesajMetni : null,
    };

    public sealed class FormBaglami : System.ComponentModel.INotifyPropertyChanged
    {
        private KartFormu _acikForm;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public KartFormu AcikForm
        {
            get => _acikForm;
            set
            {
                _acikForm = value;
                PropertyChanged?.Invoke(this, new(nameof(AcikForm)));
            }
        }
    }

    /// <summary>Açık formun düğmesi (Kartlar ekranı): kenar kalınlığı her durumda 2'dir, açılınca yalnız kenar rengi (beyaz karta
    /// karşı belirgin, kontrastı MauiKayitTutarliligiTests'te) ve zemin değişir; düğme genişlemez, yanındakileri itmez. Kapalıyken
    /// kenar düğmenin kendi rengindedir (birincil: zemini Green, ikincil: BtnSecondary kenarı Border).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Form_dugmesi_acilinca_kenar_kalinligi_ve_ic_boslugu_degismez(bool birincil)
    {
        GorunumOrtami.Kur();
        var baglam = new FormBaglami();
        var dugme = TakipUi.FormDugmesi("Ödeme kaydet", "X", KartFormu.Odeme, nameof(FormBaglami.AcikForm), birincil);
        dugme.BindingContext = baglam;
        var kapali = (dugme.BorderWidth, dugme.Padding, dugme.WidthRequest, dugme.BorderColor, dugme.BackgroundColor);
        Assert.Equal(2, dugme.BorderWidth);
        Assert.Equal(Renk(birincil ? "Green" : "Border"), dugme.BorderColor);
        Assert.Null(SemanticProperties.GetHint(dugme));

        baglam.AcikForm = KartFormu.Odeme;
        Assert.Equal(kapali.BorderWidth, dugme.BorderWidth);
        Assert.Equal(kapali.Padding, dugme.Padding);
        Assert.Equal(kapali.WidthRequest, dugme.WidthRequest);
        Assert.Equal(Renk(TakipUi.AcikFormKenari), dugme.BorderColor);
        Assert.Equal(Renk(birincil ? "GreenDark" : "GreenSoft"), dugme.BackgroundColor);
        Assert.Equal("Açık", SemanticProperties.GetHint(dugme));

        baglam.AcikForm = KartFormu.Harcama;   // başka form açıldı: bu düğme kapalı görünüme döner
        Assert.Equal((kapali.BorderWidth, kapali.Padding, kapali.WidthRequest, kapali.BorderColor, kapali.BackgroundColor),
            (dugme.BorderWidth, dugme.Padding, dugme.WidthRequest, dugme.BorderColor, dugme.BackgroundColor));
        Assert.Null(SemanticProperties.GetHint(dugme));
    }

    private static Color Renk(string anahtar) => (Color)Application.Current!.Resources[anahtar];

    /// <summary>EK-01: "editörde ama içi gizli" durumunda sarmalayıcının kendisi de görünmez olmalı, yoksa dikey
    /// yığında (Spacing) boş bir satır kalır.</summary>
    [Fact]
    public void Editor_ikinci_parametreyle_hem_EditorMu_hem_kosula_bagliyken_gorunur()
    {
        GorunumOrtami.Kur();
        var v = TakipUi.Editor(new Label { Text = "İçerik" }, nameof(TakipBaglami.Secili));

        v.BindingContext = new TakipBaglami { EditorMu = true, Secili = true };
        Assert.True(v.IsVisible);

        v.BindingContext = new TakipBaglami { EditorMu = true, Secili = false };
        Assert.False(v.IsVisible);

        v.BindingContext = new TakipBaglami { EditorMu = false, Secili = true };
        Assert.False(v.IsVisible);
    }

    /// <summary>KS-03: Yenile satırının dışında duran, bağımsız bir yükleme göstergesi ve altındaki hata metni ikisi de
    /// boşken yer kaplamamalı (Kasalar'daki "Kart ve kredi takibi" kutusundaki ~87 px boşluk).</summary>
    [Fact]
    public void MesgulVeHata_bosken_gosterge_ve_hata_gorunmez_doluyken_gorunur()
    {
        GorunumOrtami.Kur();
        var (gosterge, hata) = TakipUi.MesgulVeHata("Mesgul", "Hata");
        var panel = new VerticalStackLayout { Children = { gosterge, hata } };

        panel.BindingContext = TakipBaglam(mesgul: false, hata: false, mesaj: false);
        Assert.False(gosterge.IsVisible);
        Assert.False(gosterge.IsRunning);
        Assert.False(hata.IsVisible);

        panel.BindingContext = TakipBaglam(mesgul: true, hata: false, mesaj: false);
        Assert.True(gosterge.IsVisible);
        Assert.True(gosterge.IsRunning);
        Assert.False(hata.IsVisible);

        panel.BindingContext = TakipBaglam(mesgul: false, hata: true, mesaj: false);
        Assert.False(gosterge.IsVisible);
        Assert.True(hata.IsVisible);
        Assert.Equal(HataMetni, hata.Text);
    }

    /// <summary>Değişiklikten önceki TakipUi ve durum başlıklarının birebir kopyası (yalnız karşılaştırma için).</summary>
}
