using Kasa.ApiClient;
using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Arayüz sadeleştirmesi, düğme ve renk (docs/specs/2026-11-26-slop-temizligi.md §1, §4): yeşil yalnız ana düğme, marka ve
/// gelir içindir. Düğme varsayılan olarak ikincildir, ana düğme adıyla (BtnPrimary) seçilir ve kodla yazılmış her sayfada en çok
/// bir tane görünür. Yıkıcı eylem kırmızı yazılı hafif düğmedir. Seçili çip açık yeşildir (ana düğmeyle karışmaz). Para rengi
/// tek kuraldır: artı yeşil, eksi kırmızı, sıfır gri; gider listesi tutarı eksi işaretli ve kırmızıdır.</summary>
public class DugmeVeRenkKurallariTests
{
    // "Card" hem renk hem kart stilinin adıdır (stil sonra tanımlanır): kart zemini Colors.White ile sınanır.
    private static Color Renk(string anahtar) => (Color)Application.Current!.Resources[anahtar];
    private static Style Stil(string anahtar) => (Style)Application.Current!.Resources[anahtar];
    private static AuthViewModel Editor() => new(new SahteApi()) { AktifRol = Rol.Editor, GirisYapildi = true };
    private static SahteApi Finans() => new() { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0), new KanalDto(2, "PERAKENDE", true, 1, 0)] };

    [Fact]
    public void Ortuk_dugme_ikincil_ana_dugme_yesil_tehlikeli_dugme_kirmizi_yazili()
    {
        var kok = GorunumOrtami.Yukle("""
            <VerticalStackLayout>
              <Button Text="Yenile" />
              <Button Text="Kaydet" Style="{StaticResource BtnPrimary}" />
              <Button Text="Sil" Style="{StaticResource BtnDanger}" />
            </VerticalStackLayout>
            """);
        var d = kok.GetVisualTreeDescendants().OfType<Button>().ToArray();
        Assert.Equal((Colors.White, Renk("Ink"), Renk("Border")), (d[0].BackgroundColor, d[0].TextColor, d[0].BorderColor));
        Assert.Equal((Renk("Green"), Colors.White, 0d), (d[1].BackgroundColor, d[1].TextColor, d[1].BorderWidth));
        Assert.Equal((Colors.White, Renk("Neg")), (d[2].BackgroundColor, d[2].TextColor));
    }

    /// <summary>Örtük stilin kenarı açık anahtarlı stillerle birleşir (MAUI): satır düğmeleri ve şeffaf düğme kenarsız kalır.</summary>
    [Fact]
    public void Satir_dugmeleri_ve_seffaf_dugme_ortuk_kenari_almaz()
    {
        var kok = GorunumOrtami.Yukle("""
            <VerticalStackLayout>
              <Button Text="Düzenle" Style="{StaticResource BtnRowEdit}" />
              <Button Text="Sil" Style="{StaticResource BtnRowDelete}" />
              <Button Style="{StaticResource SeffafDugme}" />
            </VerticalStackLayout>
            """);
        Assert.All(kok.GetVisualTreeDescendants().OfType<Button>(), b => Assert.Equal(0d, b.BorderWidth));
    }

    [Fact]
    public void Takip_ana_dugmesi_yardimcisi_ana_stili_verir()
    {
        GorunumOrtami.Kur();
        Assert.Same(Stil("BtnPrimary"), TakipUi.Birincil(TakipUi.Dugme("Kaydet", "KaydetCommand")).Style);
        Assert.Same(Stil("BtnDanger"), TakipUi.Tehlikeli(TakipUi.Tikla("Sil", () => Task.CompletedTask)).Style);
        Assert.Null(TakipUi.Dugme("Yenile", "YenileCommand").Style);
    }

    [Fact]
    public void Secili_cip_acik_yesil_zemin_ve_yesil_yazili_secilmeyen_beyaz()
    {
        GorunumOrtami.Kur();
        var grup = new Kasa.App.Controls.CipGrubu();
        BindableLayout.SetItemsSource(grup, new[] { new SecimCipi("MEZAT") { Secili = true }, new SecimCipi("PERAKENDE") });
        var cipler = grup.Children.OfType<Button>().ToArray();
        Assert.Equal((Renk("GreenSoft"), Renk("Green"), Renk("Green")), (cipler[0].BackgroundColor, cipler[0].TextColor, cipler[0].BorderColor));
        Assert.Equal((Colors.White, Renk("Ink")), (cipler[1].BackgroundColor, cipler[1].TextColor));

        var xaml = GorunumOrtami.Yukle("""
            <Border Style="{StaticResource Chip}"><Label Style="{StaticResource ChipText}" Text="MEZAT" /></Border>
            """, new SecimCipi("MEZAT") { Secili = true });
        var kutu = xaml.GetVisualTreeDescendants().OfType<Border>().Single();
        Assert.Equal(Renk("GreenSoft"), ((SolidColorBrush)kutu.Background).Color);
        Assert.Equal(Renk("Green"), xaml.GetVisualTreeDescendants().OfType<Label>().Single().TextColor);
    }

    /// <summary>Takip sayfalarının hata ve ileti renkleri paletten gelir (MAUI adlı DarkRed / DarkGreen yerine).</summary>
    [Fact]
    public void Takip_hata_ve_ileti_renkleri_paletten_gelir()
    {
        GorunumOrtami.Kur();
        Assert.False(Application.Current!.Resources.ContainsKey("KoyuKirmizi"));
        Assert.False(Application.Current!.Resources.ContainsKey("KoyuYesil"));
        Assert.Equal(Renk("Neg"), TakipUi.BagliHata("Hata").TextColor);
        Assert.Equal(Renk("Pos"), new Label { Style = Stil("LblTakipMesaj") }.TextColor);
    }

    [Fact]
    public void Onay_kutusu_yesildir()
    {
        var kok = GorunumOrtami.Yukle("<CheckBox />");
        Assert.Equal(Renk("Green"), kok.GetVisualTreeDescendants().OfType<CheckBox>().Single().Color);
    }

    [Fact]
    public void Ozet_kutusunun_golgesi_hafiftir()
    {
        GorunumOrtami.Kur();
        var golge = (Shadow)Stil("CardHero").Setters.Single(s => s.Property == VisualElement.ShadowProperty).Value;
        Assert.InRange(golge.Opacity, 0, 0.1f);
    }

    /// <summary>Sayfa (ve durumu) ile görünmesi beklenen ana düğme sayısı: form açıkken formun kaydı, form yokken hiç (liste sayfası sakin
    /// kalır; formlar yalnız istenince açılır, §8) ya da sayfanın tek asıl işi (Rapor'da dışa aktarma, Bildirimler'de ayarların kaydı).</summary>
    public static TheoryData<string, int> TakipSayfalari => new()
    {
        { "Kartlar", 0 }, { "Kartlar · ödeme formu", 0 }, { "Kartlar · kart bilgisi", 1 }, { "Krediler", 0 }, { "Krediler · seçili", 0 },
        { "Krediler · seçili · erken kapama", 1 }, { "Krediler · yeni", 1 }, { "Çekler", 0 }, { "Çekler · yeni", 1 }, { "Aylık giderler", 0 },
        { "Aylık giderler · yeni şablon", 1 },
        { "Cariler", 0 }, { "Bildirimler", 1 }, { "Rapor", 1 },
        { "Kasa kontrolü", 0 }, { "Kasa kontrolü · yeni", 1 }, { "Kasa kontrolü · değişenler", 0 },
    };

    /// <summary>Kodla yazılmış her sayfada, yüklenmiş ve form açılmış hâlde, en çok bir görünür ana düğme (yeşil leke tek işe ayrılır).</summary>
    [Theory]
    [MemberData(nameof(TakipSayfalari))]
    public async Task Takip_sayfasinda_tek_gorunur_ana_dugme_vardir(string ad, int beklenen)
    {
        GorunumOrtami.Kur();
        var sayfa = await SayfaKur(ad);
        var ana = sayfa.GetVisualTreeDescendants().OfType<Button>().Where(b => Gorunur(b) && ReferenceEquals(b.Style, Stil("BtnPrimary"))).Select(b => b.Text).ToList();
        Assert.True(ana.Count == beklenen, $"{ad}: {ana.Count} ana düğme ({string.Join(", ", ana)}); beklenen {beklenen}");
    }

    internal static bool Gorunur(Element e) => e is not VisualElement { IsVisible: false } && (e.Parent is null || Gorunur(e.Parent));

    internal static async Task<Page> SayfaKur(string ad)
    {
        var bugun = new DateOnly(2026, 9, 25);
        switch (ad)
        {
            case "Kartlar" or "Kartlar · ödeme formu" or "Kartlar · kart bilgisi":
                {
                    var vm = new KartTakipViewModel(new FinansTakipTests.Sahte(), Finans(), Editor());
                    await vm.YukleAsync();
                    var sayfa = new KartTakipPage(vm);
                    if (ad != "Kartlar")
                    {
                        vm.Sec(vm.Kartlar[0]);
                        vm.FormAcCommand.Execute(ad.EndsWith("formu", StringComparison.Ordinal) ? KartFormu.Odeme : KartFormu.KartBilgisi);
                    }
                    return sayfa;
                }
            case "Krediler" or "Krediler · seçili" or "Krediler · seçili · erken kapama" or "Krediler · yeni":
                {
                    var vm = new KrediTakipViewModel(new FinansTakipTests.Sahte(), Finans(), Editor());
                    await vm.YukleAsync();
                    var sayfa = new KrediTakipPage(vm);
                    if (ad.Contains("seçili", StringComparison.Ordinal))
                        await vm.SecCommand.ExecuteAsync(vm.Krediler[0]);
                    if (ad.EndsWith("kapama", StringComparison.Ordinal))
                        vm.KapamaAcCommand.Execute(null);
                    if (ad.EndsWith("yeni", StringComparison.Ordinal))
                        await vm.YeniCommand.ExecuteAsync(null);
                    return sayfa;
                }
            case "Çekler" or "Çekler · yeni":
                {
                    var vm = new CekTakipViewModel(new CekTakipViewModelTests.Sahte(), Finans(), Editor(), new IslemEditorTests.SabitZaman(bugun));
                    await vm.YukleAsync();
                    var sayfa = new CekTakipPage(vm);
                    if (ad.EndsWith("yeni", StringComparison.Ordinal))
                        vm.YeniCekCommand.Execute(null);
                    return sayfa;
                }
            case "Aylık giderler" or "Aylık giderler · yeni şablon":
                {
                    var vm = new AylikGiderViewModel(new KasaKontrolVeAylikGiderTests.Sahte(), Finans(), Editor());
                    await vm.YukleAsync();
                    var sayfa = new AylikGiderPage(vm);
                    if (ad.EndsWith("şablon", StringComparison.Ordinal))
                        vm.YeniCommand.Execute(null);
                    return sayfa;
                }
            case "Cariler":
                {
                    var vm = new CarilerViewModel(new CarilerViewModelTests.Sahte(), Editor());
                    await vm.YukleAsync();
                    return new CarilerPage(vm);
                }
            case "Bildirimler":
                {
                    var o = new BildirimOrtami();
                    var vm = new BildirimViewModel(o.Api, o.Auth, o.Nobetci);
                    await vm.YukleAsync();
                    return new BildirimPage(vm);
                }
            case "Rapor":
                {
                    var vm = new DisariAktarViewModel(new YeniAkisTests.Sahte(), Finans(), Editor());
                    await vm.YukleAsync();
                    return new DisariAktarPage(vm);
                }
            case "Kasa kontrolü" or "Kasa kontrolü · yeni" or "Kasa kontrolü · değişenler":
                {
                    var api = new KasaKontrolVeAylikGiderTests.Sahte { Gecmis = [new KasaKontrolDto(1, new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(3)), 100, 90, -10, "Sayım")] };
                    var vm = new KasaKontrolViewModel(api, Editor());
                    await vm.YukleAsync();
                    var sayfa = new KasaKontrolPage(vm);
                    if (ad.EndsWith("yeni", StringComparison.Ordinal))
                        vm.FormAcCommand.Execute(null);
                    if (ad.EndsWith("değişenler", StringComparison.Ordinal))
                        await vm.IncelemeAsync(vm.Gecmis[0]);
                    return sayfa;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(ad), ad, null);
        }
    }
}
