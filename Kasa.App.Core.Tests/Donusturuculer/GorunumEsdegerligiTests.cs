using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui;
using Microsoft.Maui.Graphics.Converters;
using Microsoft.Maui.Layouts;

namespace Kasa.App.Core.Tests;

/// <summary>
/// MAUI görsel birleştirmesinin (Aşama 3) görünümü değiştirmediğinin kanıtı; MAUI'de ekran görüntüsü testi yoktur. Her
/// durumda ESKİ biçimlendirme (birleştirmeden önceki sayfa parçası, aşağıda olduğu gibi; 1efdcb4) ile sayfanın BUGÜNKÜ
/// dosyasındaki bileşen kullanımı aynı uygulama kaynaklarıyla (Colors.xaml, Styles.xaml) yüklenir, aynı bağlam verilir ve
/// çözümlenmiş görsel özellik ağaçları (<see cref="GorunumOrtami.Dok"/>) birebir karşılaştırılır:
/// <list type="bullet">
/// <item>DurumSeridi: Kasalar, Haftalık, Aylık (eski RaporDurumu), İşlemler ve Alışlar durum başlıkları; yükleniyor, hata,
/// ileti ve dağılım uyarısının bütün birleşimleri.</item>
/// <item>CipGrubu: İşlemler'deki altı çip listesi; eski görünüme yakın düğme ölçüleri, komut ve erişilebilir seçim bilgisi.</item>
/// <item>BosDurum: dört boş liste görünümü.</item>
/// <item>Renk anahtarları: sayfalardaki doğrudan onaltılık renklerin yerine geçen anahtarlar aynı rengi verir.</item>
/// </list>
/// </summary>
public partial class GorunumEsdegerligiTests
{
    private const string Guncelleme = "Son güncelleme: 30.09.2026 10:00";
    private const string HataMetni = "Sunucuya ulaşılamadı. Yeniden deneyin.";
    private const string MesajMetni = "Kayıt silindi.";

    /// <summary>Sayfa bağlamının bu testlerde okunan özellikleri (eski ve yeni biçimlendirme aynı bağlamı görür).</summary>
    public sealed class SahteSayfa
    {
        public bool Mesgul { get; set; }
        public bool ListeYukleniyor { get; set; }
        public string? Hata { get; set; }
        public string? YuklemeHatasi { get; set; }
        public string? Mesaj { get; set; }
        public string SonGuncellemeMetni { get; set; } = Guncelleme;
        public ICommand YenileCommand { get; } = new RelayCommand(() => { });
        public bool DagilimBekliyor { get; set; }
        public decimal DagilimBekleyenTutar { get; set; } = 1234.5m;
        public bool VeriVar { get; set; }
        public string BosListeBasligi { get; set; } = "Bu hafta işlem yok";
        public string BosListeAciklamasi { get; set; } = "Süzgeci değiştirin ya da yeni işlem girin.";
        public ObservableCollection<SecimCipi> GiderKanallari { get; } = Cipler();
        public ObservableCollection<SecimCipi> TipCipleri { get; } = Cipler();
        public ObservableCollection<KartCipi> KartCipleri { get; } = [new(1, "Bonus"), new(2, "World") { Secili = true }, new(3, "Axess")];
        public ObservableCollection<SecimCipi> GelenKanallari { get; } = Cipler();
        public ObservableCollection<SecimCipi> FiltreKanallari { get; } = Cipler();
        public ObservableCollection<SecimCipi> FiltreZamanlar { get; } = Cipler();
        public ICommand SecGiderKanalCommand { get; } = new RelayCommand(() => { });
        public ICommand SecTipCommand { get; } = new RelayCommand(() => { });
        public ICommand SecKartCommand { get; } = new RelayCommand(() => { });
        public ICommand SecGelenKanalCommand { get; } = new RelayCommand(() => { });
        public ICommand SecFiltreKanalCommand { get; } = new RelayCommand(() => { });
        public ICommand SecFiltreZamanCommand { get; } = new RelayCommand(() => { });
        private static ObservableCollection<SecimCipi> Cipler() => [new("MEZAT"), new("PERAKENDE") { Secili = true }, new("TOPTAN"), new("Ortak")];
    }

    /// <summary>Durumların bütün birleşimleri (yükleniyor × hata × ileti × dağılım uyarısı).</summary>
    public static TheoryData<bool, bool, bool, bool> Durumlar()
    {
        var veri = new TheoryData<bool, bool, bool, bool>();
        foreach (var mesgul in new[] { false, true })
            foreach (var hata in new[] { false, true })
                foreach (var mesaj in new[] { false, true })
                    foreach (var dagilim in new[] { false, true })
                        veri.Add(mesgul, hata, mesaj, dagilim);
        return veri;
    }

    private static SahteSayfa Baglam(bool mesgul, bool hata, bool mesaj, bool dagilim) => new()
    {
        Mesgul = mesgul,
        ListeYukleniyor = mesgul,
        Hata = hata ? HataMetni : null,
        YuklemeHatasi = hata ? HataMetni : null,
        Mesaj = mesaj ? MesajMetni : null,
        DagilimBekliyor = dagilim,
    };

    private static void Esit(string eski, string yeni, object? baglam)
    {
        var once = GorunumOrtami.Dok(GorunumOrtami.Yukle(eski, baglam));
        var sonra = GorunumOrtami.Dok(GorunumOrtami.Yukle(yeni, baglam));
        Assert.True(once.Length > 40, "Eski biçimlendirme boş döküldü:\n" + once);
        Assert.True(once == sonra, $"Görünüm farklı.\nÖNCE:\n{once}\nSONRA:\n{sonra}");
    }

    private static string SayfaParcasi(string sayfa, string desen)
    {
        var m = Regex.Match(GorunumOrtami.Oku("Views/" + sayfa), desen, RegexOptions.Singleline);
        Assert.True(m.Success, $"{sayfa}: '{desen}' bulunamadı.");
        return m.Value;
    }

    // ---------------------------------------------------------------- DurumSeridi

    /// <summary>Eski RaporDurumu.xaml (Kasalar, Haftalık, Aylık); sayfada ContentView içinde, 20 aralıklı yığında.</summary>
    private const string EskiRaporDurumu = """
        <VerticalStackLayout Spacing="8">
            <HorizontalStackLayout Spacing="8" IsVisible="{Binding Mesgul}">
                <ActivityIndicator IsRunning="{Binding Mesgul}" WidthRequest="20" HeightRequest="20" />
                <Label Text="Veriler yükleniyor…" Style="{StaticResource LblPageSub}" VerticalOptions="Center" />
            </HorizontalStackLayout>
            <Label Text="{Binding Hata}" Style="{StaticResource LblError}"
                   IsVisible="{Binding Hata, Converter={StaticResource DoluIse}}" />
            <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
                <Label Text="{Binding SonGuncellemeMetni}" Style="{StaticResource LblPageSub}" VerticalOptions="Center" />
                <Button Grid.Column="1" Text="Yenile / tekrar dene" Style="{StaticResource BtnSecondary}"
                        Command="{Binding YenileCommand}" IsEnabled="{Binding Mesgul, Converter={StaticResource TersIse}}" />
            </Grid>
        </VerticalStackLayout>
        """;

    [Theory]
    [MemberData(nameof(Durumlar))]
    public void Rapor_sayfalarinin_durum_seridi_eski_RaporDurumu_ile_ayni(bool mesgul, bool hata, bool mesaj, bool dagilim)
    {
        foreach (var sayfa in new[] { "PanelPage.xaml", "HaftalikPage.xaml", "AylikPage.xaml" })
        {
            string Sar(string orta) => $"""
                <VerticalStackLayout Spacing="20">
                    <Label Text="Başlık" Style="{"{StaticResource LblPageTitle}"}" />
                    {orta}
                    <Label Text="Kart" />
                </VerticalStackLayout>
                """;
            var yeni = SayfaParcasi(sayfa, @"<ctl:DurumSeridi\b[^>]*/>");
            Esit(Sar("<ContentView>" + EskiRaporDurumu + "</ContentView>"), Sar(yeni), Baglam(mesgul, hata, mesaj, dagilim));
        }
    }

    /// <summary>Eski İşlemler liste başlığı: şerit öğeleri bölüm etiketi ve süzgeçlerle aynı 10 aralıklı yığındaydı.</summary>
    [Theory]
    [MemberData(nameof(Durumlar))]
    public void Islemler_durum_seridi_eski_duz_yerlesimle_ayni(bool mesgul, bool hata, bool mesaj, bool dagilim)
    {
        const string eski = """
            <HorizontalStackLayout Spacing="8" IsVisible="{Binding ListeYukleniyor}">
                <ActivityIndicator IsRunning="{Binding ListeYukleniyor}" WidthRequest="20" HeightRequest="20" />
                <Label Text="İşlemler yükleniyor…" Style="{StaticResource LblPageSub}" VerticalOptions="Center" />
            </HorizontalStackLayout>
            <Border Style="{StaticResource ErrorBox}"
                    IsVisible="{Binding YuklemeHatasi, Converter={StaticResource DoluIse}}">
                <Label Text="{Binding YuklemeHatasi}" Style="{StaticResource LblError}" />
            </Border>
            <Label Text="{Binding Mesaj}" Style="{StaticResource LblPageSub}" TextColor="{StaticResource Green}"
                   IsVisible="{Binding Mesaj, Converter={StaticResource DoluIse}}" />
            <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
                <Label Text="{Binding SonGuncellemeMetni}" Style="{StaticResource LblPageSub}" VerticalOptions="Center" />
                <Button Grid.Column="1" Text="Yenile / tekrar dene" Style="{StaticResource BtnSecondary}"
                        Command="{Binding YenileCommand}"
                        IsEnabled="{Binding ListeYukleniyor, Converter={StaticResource TersIse}}" />
            </Grid>
            """;
        static string Sar(string orta) => $"""
            <VerticalStackLayout Padding="18,16,18,12" Spacing="10">
                <Label Text="Son İşlemler" Style="{"{StaticResource LblSection}"}" />
                {orta}
                <Label Text="Süzgeç" />
            </VerticalStackLayout>
            """;
        var yeni = SayfaParcasi("IslemlerPage.xaml", @"<ctl:DurumSeridi\b[^>]*/>");
        Esit(Sar(eski), Sar(yeni), Baglam(mesgul, hata, mesaj, dagilim));
    }

    /// <summary>Eski Alışlar durum satırı (yığının tamamı; renkler o zamanki onaltılık değerleriyle). Şerit boşken gizlenir:
    /// ileti ve dağılım kutusu eskisi gibi yığının başına kayar.</summary>
    [Theory]
    [MemberData(nameof(Durumlar))]
    public void Alislar_durum_satiri_eski_yerlesimle_ayni(bool mesgul, bool hata, bool mesaj, bool dagilim)
    {
        const string eski = """
            <VerticalStackLayout Grid.Row="1" Spacing="8">
                <HorizontalStackLayout Spacing="8" IsVisible="{Binding Mesgul}">
                    <ActivityIndicator IsRunning="{Binding Mesgul}" WidthRequest="20" HeightRequest="20" />
                    <Label Text="İşlem tamamlanıyor…" Style="{StaticResource LblPageSub}" VerticalOptions="Center" />
                </HorizontalStackLayout>
                <Border Style="{StaticResource ErrorBox}" IsVisible="{Binding Hata, Converter={StaticResource DoluIse}}">
                    <Label Text="{Binding Hata}" Style="{StaticResource LblError}" />
                </Border>
                <Label Text="{Binding Mesaj}" TextColor="{StaticResource Green}" IsVisible="{Binding Mesaj, Converter={StaticResource DoluIse}}" />
                <Border BackgroundColor="#FFF2D9" Stroke="#E9C880" StrokeShape="RoundRectangle 10" Padding="14,10" IsVisible="{Binding DagilimBekliyor}">
                    <Label TextColor="#765100">
                        <Label.FormattedText><FormattedString>
                            <Span Text="DAĞILIM BEKLİYOR  ·  " FontAttributes="Bold" />
                            <Span Text="{Binding DagilimBekleyenTutar, Converter={StaticResource ParaBicim}}" FontAttributes="Bold" />
                            <Span Text="  Ödemeler kaydedildi; onay tamamlanana kadar kanal raporları bu tutarı içermez." />
                        </FormattedString></Label.FormattedText>
                    </Label>
                </Border>
            </VerticalStackLayout>
            """;
        var yeni = SayfaParcasi("AlislarPage.xaml", @"<VerticalStackLayout Grid\.Row=""1"" Spacing=""8"">.*?</VerticalStackLayout>");
        Assert.Contains("<ctl:DurumSeridi", yeni);
        Esit(eski, yeni, Baglam(mesgul, hata, mesaj, dagilim));
    }

    // ---------------------------------------------------------------- CipGrubu

    [Theory]
    [InlineData("GiderKanallari", "SecGiderKanalCommand", 8)]
    [InlineData("TipCipleri", "SecTipCommand", 8)]
    [InlineData("KartCipleri", "SecKartCommand", 8)]
    [InlineData("GelenKanallari", "SecGelenKanalCommand", 8)]
    [InlineData("FiltreKanallari", "SecFiltreKanalCommand", 8)]
    [InlineData("FiltreZamanlar", "SecFiltreZamanCommand", 0)]
    public void Islemler_cipleri_klavye_ve_secim_semantigi_olan_dugmelerdir(string kaynak, string komut, int altKenar)
    {
        var yeni = SayfaParcasi("IslemlerPage.xaml", $@"<ctl:CipGrubu BindableLayout\.ItemsSource=""\{{Binding {kaynak}\}}""[^>]*/>");
        Assert.Contains($"SecCommand=\"{{Binding {komut}}}\"", yeni);
        var grup = Assert.IsType<Kasa.App.Controls.CipGrubu>(GorunumOrtami.Yukle(yeni, new SahteSayfa()).Content);
        Assert.Equal(FlexWrap.Wrap, grup.Wrap);
        Assert.Equal(FlexAlignItems.Center, grup.AlignItems);
        Assert.NotEmpty(grup.Children);
        foreach (var (og, sira) in grup.Children.Select((og, sira) => (og, sira)))
        {
            var dugme = Assert.IsType<Button>(og);
            Assert.NotNull(dugme.Command);
            Assert.Same(dugme.BindingContext, dugme.CommandParameter);
            Assert.Equal(new Thickness(0, 0, 8, altKenar), dugme.Margin);
            Assert.Equal(38, dugme.MinimumHeightRequest);
            Assert.Equal(-1, dugme.HeightRequest);
            Assert.Equal(new Thickness(14, 8), dugme.Padding);
            Assert.Equal(999, dugme.CornerRadius);
            Assert.Equal(13, dugme.FontSize);
            Assert.Equal(FontAttributes.None, dugme.FontAttributes);
            Assert.Equal(1, dugme.BorderWidth);
            Assert.Equal(dugme.Text, SemanticProperties.GetDescription(dugme));
            Assert.Equal(sira == 1 ? "Seçili" : "Seçili değil", SemanticProperties.GetHint(dugme));
            Assert.Equal(sira == 1 ? (Color)Application.Current!.Resources["Green"] : Colors.White, dugme.BackgroundColor);
            Assert.Equal((Color)Application.Current!.Resources[sira == 1 ? "Green" : "Border"], dugme.BorderColor);
            Assert.Equal(sira == 1 ? Colors.White : (Color)Application.Current!.Resources["Ink"], dugme.TextColor);
        }
    }

    /// <summary>Çip düğmesi grubun komutunu seçilen öğeyle çalıştırır; modeldeki seçili değişimi ipucuna yansır.</summary>
    [Fact]
    public void Cip_dugmesi_komutu_ogeyle_calistirir_ve_secimi_duyurur()
    {
        GorunumOrtami.Kur();
        object? secilen = null;
        var cipler = new ObservableCollection<SecimCipi> { new("MEZAT"), new("TOPTAN") };
        var grup = new Kasa.App.Controls.CipGrubu { SecCommand = new RelayCommand<object>(o => secilen = o) };
        BindableLayout.SetItemsSource(grup, cipler);
        var dugme = Assert.IsType<Button>(grup.Children[1]);
        Assert.Empty(dugme.GestureRecognizers);
        dugme.Command!.Execute(dugme.CommandParameter);
        Assert.Same(cipler[1], secilen);
        Assert.Equal("Seçili değil", SemanticProperties.GetHint(dugme));
        cipler[1].Secili = true;
        Assert.Equal("Seçili", SemanticProperties.GetHint(dugme));
        VisualStateManager.GoToState(dugme, "Focused");
        Assert.Equal(2, dugme.BorderWidth);
        Assert.Equal((Color)Application.Current!.Resources["Ink"], dugme.BorderColor);
    }

    [Fact]
    public void Takip_alaninin_gorsel_etiketi_girisin_erisilebilir_adidir()
    {
        GorunumOrtami.Kur();
        foreach (View giris in new View[] { new Entry(), new Kasa.App.Controls.ParaGirisi(), new DatePicker(), new Picker() })
        {
            var alan = Assert.IsType<VerticalStackLayout>(Kasa.App.Views.TakipUi.Alan("Tutar", giris));
            Assert.Equal("Tutar", Assert.IsType<Label>(alan.Children[0]).Text);
            Assert.Same(giris, alan.Children[1]);
            Assert.Equal("Tutar", SemanticProperties.GetDescription(giris));
        }
        var onay = Assert.IsType<Grid>(Kasa.App.Views.TakipUi.Onay("Şablon aktif", "Aktif"));
        Assert.Equal("Şablon aktif", SemanticProperties.GetDescription(Assert.IsType<CheckBox>(onay.Children[0])));
    }

    // ---------------------------------------------------------------- BosDurum

    private static string EskiBosDurum(string baslik, string aciklama, string ek = "") => $$"""
        <VerticalStackLayout Padding="20,36,20,40" Spacing="6"{{ek}}>
            <Border Style="{StaticResource AvatarChip}" WidthRequest="40" HeightRequest="40"
                    StrokeShape="RoundRectangle 12" HorizontalOptions="Center">
                <Label Text="₺" Style="{StaticResource LblAvatar}" FontSize="17" />
            </Border>
            <Label Text="{{baslik}}" Style="{StaticResource LblEmptyTitle}" Margin="0,4,0,0" />
            <Label Text="{{aciklama}}"
                   Style="{StaticResource LblEmptySub}" />
        </VerticalStackLayout>
        """;

    [Theory]
    [InlineData("PanelPage.xaml", "Henüz kanal yok", "Ayarlar → Kanallar bölümünden ilk kanalınızı ekleyin.")]
    [InlineData("HaftalikPage.xaml", "Kayıtlı dönem yok", "İşlem girdikçe haftalık sonuçlar burada birikir.")]
    [InlineData("AylikPage.xaml", "Bu ay için kayıt yok", "İşlem girdikçe ay sonucu burada oluşur.")]
    public void Rapor_sayfalarinin_bos_liste_gorunumu_eskisiyle_ayni(string sayfa, string baslik, string aciklama)
        => Esit(EskiBosDurum(baslik, aciklama), SayfaParcasi(sayfa, @"<ctl:BosDurum\b[^>]*/>"), new SahteSayfa());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Islemler_bos_liste_gorunumu_eskisiyle_ayni(bool veriVar)
    {
        var eski = EskiBosDurum("{Binding BindingContext.BosListeBasligi, Source={x:Reference Sayfa}}",
            "{Binding BindingContext.BosListeAciklamasi, Source={x:Reference Sayfa}}",
            "\n                                 IsVisible=\"{Binding BindingContext.VeriVar, Source={x:Reference Sayfa}}\"");
        var baglam = new SahteSayfa { VeriVar = veriVar };
        var yeni = SayfaParcasi("IslemlerPage.xaml", @"<ctl:BosDurum\b[^>]*/>");
        if (veriVar)
            Esit(eski, yeni, baglam);
        else
            Assert.Equal(GorunumOrtami.Dok(GorunumOrtami.Yukle(eski, baglam)), GorunumOrtami.Dok(GorunumOrtami.Yukle(yeni, baglam)));
    }

    // ---------------------------------------------------------------- Renk anahtarları

    /// <summary>Sayfalardaki doğrudan onaltılık renkler (maui-lint tabanı) anahtara taşındı: anahtar, yerine geçtiği değerle
    /// aynı rengi verir (XAML'in değer dönüştürücüsüyle çözülür; fırçalar aynı renkte düz fırçadır).</summary>
    [Theory]
    [InlineData("UyariMetin", "#765100")]
    [InlineData("UyariZemin", "#FFF2D9")]
    [InlineData("UyariKenar", "#E9C880")]
    [InlineData("KilitZemin", "#E3EDDF")]
    [InlineData("KilitMetin", "#366341")]
    [InlineData("HeroAcik", "#CBE6D5")]
    [InlineData("HeroUyari", "#FFDA91")]
    [InlineData("KalemZemin", "#F8F8F3")]
    [InlineData("PayZemin", "#F1F5EE")]
    [InlineData("Border", "#E3E0D6")]   // alış kalemi kenarlığı ve takip listesi ayırıcısı (TakipUi: Color.FromArgb("E3E0D6"))
    [InlineData("Green", "#1E5F46")]    // giriş logosu gölgesi
    public void Renk_anahtari_yerine_gectigi_onaltilik_rengi_verir(string anahtar, string onaltilik)
    {
        GorunumOrtami.Kur();
        var beklenen = (Color)new ColorTypeConverter().ConvertFromInvariantString(onaltilik)!;
        Assert.Equal(beklenen, (Color)Application.Current!.Resources[anahtar]);
        var firca = (SolidColorBrush)new BrushTypeConverter().ConvertFromInvariantString(onaltilik)!;
        Assert.Equal(firca.Color, (Color)Application.Current!.Resources[anahtar]);
    }

    [Theory]
    [InlineData("BrushUyariKenar", "#E9C880")]
    [InlineData("BrushBorder", "#E3E0D6")]
    [InlineData("BrushGreen", "#1E5F46")]
    public void Firca_anahtari_yerine_gectigi_onaltilik_fircayla_ayni(string anahtar, string onaltilik)
    {
        GorunumOrtami.Kur();
        var beklenen = (SolidColorBrush)new BrushTypeConverter().ConvertFromInvariantString(onaltilik)!;
        Assert.Equal(beklenen.Color, ((SolidColorBrush)Application.Current!.Resources[anahtar]).Color);
    }

    [Fact]
    public void Koyu_renk_anahtarlari_MAUI_adli_renkleriyle_ayni()
    {
        GorunumOrtami.Kur();
        Assert.Equal(Colors.DarkRed, (Color)Application.Current!.Resources["KoyuKirmizi"]);
        Assert.Equal(Colors.DarkGreen, (Color)Application.Current!.Resources["KoyuYesil"]);
    }
}
