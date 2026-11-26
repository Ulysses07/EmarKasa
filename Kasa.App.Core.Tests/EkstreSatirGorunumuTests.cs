using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

// Hareket penceresi gerçek MAUI bağlarıyla (docs/specs/2026-11-26-ekstre-hareket-penceresi.md §2): tablo satırında düğme yok, kutu yalnız işaret,
// satıra dokunmak sağ paneli açar ya da kapatır; panel kapanıp açılınca taslak, onay ve önizleme korunur.
public partial class EkstreAktarmaTests
{
    private static EkstreHareketPenceresi Pencere(EkstreAktarmaViewModel vm) => new(vm, new EkstreAktarmaPage(vm));
    private static CollectionView Tablo(Page pencere) => pencere.GetVisualTreeDescendants().OfType<CollectionView>().Single(l => l.AutomationId == "HareketTablosu");
    private static View SatirGorunumu(CollectionView liste, EkstreSatirEditor satir)
    {
        var gorunum = (View)liste.ItemTemplate.CreateContent();
        gorunum.BindingContext = satir;
        return gorunum;
    }
    private static bool Gorunur(Element e) => e is not VisualElement { IsVisible: false } && (e.Parent is null || Gorunur(e.Parent));
    private static Entry[] PanelGirdileri(Page pencere) => pencere.GetVisualTreeDescendants().OfType<Entry>().Where(Gorunur).Where(e => e.Placeholder != "Açıklamada ara").ToArray();
    private static Button PanelDugmesi(Page pencere, string metin) => pencere.GetVisualTreeDescendants().OfType<Button>().Single(b => b.Text == metin && Gorunur(b));

    [Fact]
    public async Task Satira_dokunmak_paneli_acar_ikinci_dokunus_kapatir_kutu_yalniz_isaretler()
    {
        GorunumOrtami.Kur();
        var (vm, api, _) = await Hazir();
        var pencere = Pencere(vm);
        var ilk = SatirGorunumu(Tablo(pencere), vm.Satirlar[0]);
        Assert.Empty(ilk.GetVisualTreeDescendants().OfType<Button>());
        Assert.Empty(PanelGirdileri(pencere));

        // Kutunun anlamı mali seçimdir; paneli açmaz.
        ilk.GetVisualTreeDescendants().OfType<CheckBox>().Single().IsChecked = true;
        Assert.Equal((true, (EkstreSatirEditor?)null), (vm.Satirlar[0].Secili, vm.SeciliSatir));
        Assert.Empty(PanelGirdileri(pencere));

        vm.SatiriAcKapat(vm.Satirlar[0]);
        var aciklama = PanelGirdileri(pencere).Single(e => e.Text == vm.Satirlar[0].Aciklama);
        aciklama.Text = "Panelde düzeltilen açıklama";
        Assert.Equal("Panelde düzeltilen açıklama", vm.Satirlar[0].Aciklama);
        vm.SatiriAcKapat(vm.Satirlar[1]);
        Assert.Contains(PanelGirdileri(pencere), e => e.Text == vm.Satirlar[1].Aciklama);
        Assert.DoesNotContain(PanelGirdileri(pencere), e => e.Text == "Panelde düzeltilen açıklama");
        // Aynı satıra ikinci dokunuş paneli kapatır; mali seçim ve taslak korunur.
        vm.SatiriAcKapat(vm.Satirlar[1]);
        Assert.Null(vm.SeciliSatir);
        Assert.Empty(PanelGirdileri(pencere));
        Assert.Equal(("Panelde düzeltilen açıklama", true, false), (vm.Satirlar[0].Aciklama, vm.Satirlar[0].Secili, vm.Satirlar[1].Secili));
        Assert.Empty(api.KaydetIstekleri);
        Assert.Equal(0, api.OnizlemeSayisi);
    }

    private static List<string> Etiketler(View gorunum) => [.. gorunum.GetVisualTreeDescendants().OfType<Label>().Select(l => l.Text)];

    /// <summary>Tarih hücresi bekleyen satırda düzenlenen tarihi, kapanan satırda kaydedilen tarihi gösterir (PDF'ten okunanı değil).</summary>
    [Fact]
    public async Task Tablonun_tarihi_duzenlenen_ve_kaydedilen_tarihtir()
    {
        GorunumOrtami.Kur();
        var belge = DurumBelgesi(Satir(1), Satir(2)) with { Kayitlar = [Kayit(7, 1) with { Tarih = new(2026, 9, 25) }] };
        var (vm, _, _) = await Hazir(new() { Veri = belge });
        var tablo = Tablo(Pencere(vm));
        var bekleyen = vm.Satirlar.Single(s => !s.Kapali);
        var gorunum = SatirGorunumu(tablo, bekleyen);
        bekleyen.TarihMetni = "2026-09-28";
        Assert.Contains("28.09", Etiketler(gorunum));
        Assert.Contains("25.09", Etiketler(SatirGorunumu(tablo, vm.Satirlar.Single(s => s.Kayitli))));
        bekleyen.TarihMetni = "28 eylül";
        Assert.DoesNotContain("27.09", Etiketler(gorunum));
    }

    /// <summary>Dar tabloda (telefon, paneli açık dar pencere) tür · kanal açıklamanın altına iner; açıklama ve tutar ekrana sığar.</summary>
    [Fact]
    public async Task Dar_tabloda_tur_kanal_aciklamanin_altina_iner()
    {
        GorunumOrtami.Kur();
        var (vm, _, _) = await Hazir();
        var pencere = Pencere(vm);
        var satir = (Grid)((Layout)SatirGorunumu(Tablo(pencere), vm.Satirlar[0])).Children[0];
        var tur = satir.Children.OfType<VerticalStackLayout>().Single();
        (int, int, double) Yer() => (Grid.GetColumn(tur), Grid.GetRow(tur), satir.ColumnDefinitions[4].Width.Value);
        ((IView)satir).Arrange(new Rect(0, 0, 1000, 60));
        Assert.Equal((4, 0, 230d), Yer());
        ((IView)satir).Arrange(new Rect(0, 0, 360, 80));
        Assert.Equal((3, 1, 0d), Yer());
        // Sabit sütunlar ve aralıklar 360'ta açıklamaya en az 100 bırakır (telefonda açıklama ve tutar görünür).
        var sabit = satir.ColumnDefinitions.Where(c => c.Width.IsAbsolute).Sum(c => c.Width.Value) + satir.ColumnSpacing * (satir.ColumnDefinitions.Count - 1)
            + satir.Padding.HorizontalThickness;
        Assert.True(360 - sabit >= 100, $"{sabit}");
        var baslik = pencere.GetVisualTreeDescendants().OfType<Grid>().Single(g => g.Children.OfType<Label>().Any(l => l.Text == "Tür · kanal"));
        ((IView)baslik).Arrange(new Rect(0, 0, 360, 30));
        Assert.Equal((false, 0d), (baslik.Children.OfType<Label>().Single(l => l.Text == "Tür · kanal").IsVisible, baslik.ColumnDefinitions[4].Width.Value));
        ((IView)satir).Arrange(new Rect(0, 0, 1000, 60));
        Assert.Equal((4, 0, 230d), Yer());
    }

    /// <summary>Kapanan satırın açıklaması kaydedilen (düzeltilmiş olabilir) açıklamadır; arama onu da bulur.</summary>
    [Fact]
    public async Task Kapanan_satirin_aciklamasi_kaydedilen_aciklamadir()
    {
        GorunumOrtami.Kur();
        var belge = DurumBelgesi(Satir(1), Satir(2)) with { Kayitlar = [Kayit(7, 1) with { Aciklama = "Düzeltilmiş kira ödemesi" }] };
        var (vm, _, _) = await Hazir(new() { Veri = belge });
        var tablo = Tablo(Pencere(vm));
        var kapanan = vm.Satirlar.Single(s => s.Kayitli);
        var etiketler = Etiketler(SatirGorunumu(tablo, kapanan));
        Assert.Contains("Düzeltilmiş kira ödemesi", etiketler);
        Assert.DoesNotContain("Hareket 1", etiketler);
        vm.Arama = "kira";
        Assert.Equal([kapanan], vm.Gorunenler);
    }

    /// <summary>Dar pencerede (telefon) alt çubuk alt alta dizilir: "Etkisini göster ve kaydet" tam genişlikte, ikincil eylemler altında; genişte tek satır.</summary>
    [Fact]
    public async Task Dar_pencerede_alt_cubuk_alt_alta_dizilir()
    {
        GorunumOrtami.Kur();
        var (vm, _, _) = await Hazir();
        var pencere = Pencere(vm);
        var kaydet = pencere.GetVisualTreeDescendants().OfType<Button>().Single(b => b.Text == "Etkisini göster ve kaydet");
        var toplu = pencere.GetVisualTreeDescendants().OfType<Button>().Single(b => b.Text == "Toplu düzenle");
        var cubuk = (Grid)kaydet.Parent;
        (int, int, int, int) Yer(View v) => (Grid.GetColumn(v), Grid.GetRow(v), Grid.GetColumnSpan(v), cubuk.ColumnDefinitions.Count);
        ((IView)cubuk).Arrange(new Rect(0, 0, 360, 160));
        Assert.Equal((0, 1, 2, 2), Yer(kaydet));
        Assert.Equal((0, 2, 1, 2), Yer(toplu));
        Assert.Equal(LayoutOptions.Fill, kaydet.HorizontalOptions);
        ((IView)cubuk).Arrange(new Rect(0, 0, 1000, 60));
        Assert.Equal((3, 0, 1, 4), Yer(kaydet));
        Assert.Equal((1, 0, 1, 4), Yer(toplu));
    }

    /// <summary>Dar pencerede (telefon) arama süzgeç çiplerinin altına iner: çipler tüm genişliği kullanır; genişte yan yana.</summary>
    [Fact]
    public async Task Dar_pencerede_arama_ciplerin_altina_iner()
    {
        GorunumOrtami.Kur();
        var (vm, _, _) = await Hazir();
        var pencere = Pencere(vm);
        var ara = pencere.GetVisualTreeDescendants().OfType<Entry>().Single(e => e.Placeholder == "Açıklamada ara");
        var kutu = (Border)ara.Parent;   // arama kutusu FieldBorder çerçevesinde (tek sistem §7)
        var satir = (Grid)kutu.Parent;
        (int, int, double, int) Yer() => (Grid.GetColumn(kutu), Grid.GetRow(kutu), kutu.WidthRequest, satir.ColumnDefinitions.Count);
        ((IView)satir).Arrange(new Rect(0, 0, 312, 120));
        Assert.Equal((0, 1, -1d, 1), Yer());
        ((IView)satir).Arrange(new Rect(0, 0, 1000, 40));
        Assert.Equal((1, 0, 240d, 2), Yer());
    }

    /// <summary>İptal edilen kayıtlar (satır yeniden bekliyor ya da yeniden kaydedildi) "⋯" menüsünden görülür: kaydedilen değerler, gerekçe, zaman.</summary>
    [Fact]
    public async Task Iptal_edilen_kayitlar_menuden_gorulur()
    {
        GorunumOrtami.Kur();
        var belge = DurumBelgesi(Satir(1), Satir(2)) with { Kayitlar = [Kayit(7, 1), Kayit(8, 2, iptal: true) with { Aciklama = "İptal edilen kira" }] };
        var (vm, _, _) = await Hazir(new() { Veri = belge });
        Assert.Contains("İptal edilen kayıtlar (1)", Pencere(vm).MenuSecenekleri().Select(s => s.Ad));
        Assert.Contains("İptal edilen kira", vm.IptalEdilenlerMetni);
        Assert.Contains("İptal gerekçesi: Yanlış", vm.IptalEdilenlerMetni);
        Assert.DoesNotContain("Satır 1 ", vm.IptalEdilenlerMetni);

        var (bos, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1)) });
        Assert.DoesNotContain(Pencere(bos).MenuSecenekleri(), s => s.Ad.StartsWith("İptal edilen", StringComparison.Ordinal));
    }

    /// <summary>"Bu seçimi hatırla" kural olamayan seçimde pencereyi kapatmaz; nedeni panelde görünür.</summary>
    [Fact]
    public async Task Hatirlanamayan_secimde_pencere_acik_kalir_nedeni_gorunur()
    {
        GorunumOrtami.Kur();
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1)) });
        var pencere = Pencere(vm);
        vm.SeciliSatir = vm.Satirlar[0];
        ((IButtonController)PanelDugmesi(pencere, "Bu seçimi hatırla")).SendClicked();
        Assert.Same(vm.Satirlar[0], vm.SeciliSatir);
        Assert.Contains("Hatırlamadan önce genel kasa veya eşit kanal dağılımını seçin.",
            pencere.GetVisualTreeDescendants().OfType<Label>().Where(Gorunur).Select(l => l.Text));
    }

    /// <summary>Pencerede (arama ve açık satır paneli) görünen her girdi alan çerçevesindedir (tek sistem §7).</summary>
    [Fact]
    public async Task Pencerede_gorunen_her_girdi_alan_cercevesindedir()
    {
        GorunumOrtami.Kur();
        var (vm, _, _) = await Hazir();
        var pencere = Pencere(vm);
        vm.SeciliSatir = vm.Satirlar[0];
        var girdiler = pencere.GetVisualTreeDescendants().OfType<View>().Where(v => TakipUi.GirdiMi(v) && Gorunur(v)).ToList();
        Assert.True(girdiler.Count >= 3);
        Assert.All(girdiler, g => Assert.True(TekSistemTests.CercevesiVar(g), g.GetType().Name));
    }

    private static int BirincilDugmeSayisi(Page pencere)
    {
        var ana = (Style)Application.Current!.Resources["BtnPrimary"];
        return pencere.GetVisualTreeDescendants().OfType<Button>().Count(b => Gorunur(b) && ReferenceEquals(b.Style, ana));
    }

    /// <summary>Ekranda tek birincil düğme: satır ve toplu panelde alt çubuğun "Etkisini göster ve kaydet"i, kayıt özetinde "Kaydet".</summary>
    [Fact]
    public async Task Pencerede_tek_birincil_dugme_vardir()
    {
        GorunumOrtami.Kur();
        var (vm, _, _) = await Hazir();
        var pencere = Pencere(vm);
        vm.SeciliSatir = vm.Satirlar[0];
        Assert.Equal(1, BirincilDugmeSayisi(pencere));
        ((IButtonController)PanelDugmesi(pencere, "Toplu düzenle")).SendClicked();
        Assert.Equal(1, BirincilDugmeSayisi(pencere));
    }

    /// <summary>Kapanan pencere görünüm modelini dinlemeyi bırakır (uzun ömürlü modelde birikmez).</summary>
    [Fact]
    public async Task Kapanan_pencere_modeli_dinlemez()
    {
        GorunumOrtami.Kur();
        var (vm, _, _) = await Hazir();
        var pencere = Pencere(vm);
        vm.SeciliSatir = vm.Satirlar[1];
        Assert.NotEmpty(PanelGirdileri(pencere));
        await pencere.KapatAsync();
        Assert.Null(vm.SeciliSatir);
        vm.SeciliSatir = vm.Satirlar[0];
        Assert.Empty(PanelGirdileri(pencere));
    }

    /// <summary>Pencerede işlemlerin iletisi ve elle "yeniden sor"un sonucu görünür.</summary>
    [Fact]
    public async Task Pencerede_ileti_ve_yapay_zeka_sonucu_gorunur()
    {
        GorunumOrtami.Kur();
        var (vm, _, _) = await Hazir();
        var pencere = Pencere(vm);
        vm.Mesaj = "Hafızadan çıkarıldı.";
        vm.YapayZekaMesaji = "1 satır soruldu, 1 satıra öneri geldi (0,0025 $).";
        var etiketler = pencere.GetVisualTreeDescendants().OfType<Label>().Where(Gorunur).Select(l => l.Text).ToList();
        Assert.Contains("Hafızadan çıkarıldı.", etiketler);
        Assert.Contains("1 satır soruldu, 1 satıra öneri geldi (0,0025 $).", etiketler);
    }

    [Fact]
    public async Task Panelin_satir_alanlari_genis_ekranda_yanyana_dar_ekranda_altalta_yerlesir()
    {
        GorunumOrtami.Kur();
        var (vm, _, _) = await Hazir();
        var pencere = Pencere(vm);
        vm.SeciliSatir = vm.Satirlar[0];
        var alanlar = pencere.GetVisualTreeDescendants().OfType<Grid>().Single(g => g.GetVisualTreeDescendants().OfType<Entry>().Count() == 2);
        var tarih = alanlar.Children[0];
        var tutar = alanlar.Children[1];
        ((IView)alanlar).Arrange(new Rect(0, 0, 800, 160));
        Assert.True(tutar.Frame.Left >= tarih.Frame.Right);
        Assert.Equal(tarih.Frame.Top, tutar.Frame.Top);
        ((IView)alanlar).Arrange(new Rect(0, 0, 280, 160));
        Assert.Equal(tarih.Frame.Left, tutar.Frame.Left);
        Assert.True(tutar.Frame.Top >= tarih.Frame.Bottom);
        Assert.True(tutar.Frame.Right <= 280);
    }

    [Fact]
    public async Task Panel_kapanip_acilinca_taslak_onay_ve_onizleme_korunur()
    {
        GorunumOrtami.Kur();
        var (vm, api, _) = await Hazir();
        Sec(vm.Satirlar[0]);
        vm.Satirlar[0].Aciklama = "Düzenlenmiş taslak";
        await vm.OnizleCommand.ExecuteAsync(null);
        vm.Onay = true;
        var onizleme = vm.OnizlemeMetni;
        var pencere = Pencere(vm);
        vm.SeciliSatir = vm.Satirlar[0];
        Assert.Contains(PanelGirdileri(pencere), e => e.Text == "Düzenlenmiş taslak");

        ((IButtonController)PanelDugmesi(pencere, "Kapat")).SendClicked();
        Assert.Null(vm.SeciliSatir);
        Assert.Empty(PanelGirdileri(pencere));
        vm.SeciliSatir = vm.Satirlar[1];
        Assert.Contains(PanelGirdileri(pencere), e => e.Text == vm.Satirlar[1].Aciklama);
        Assert.Equal("Düzenlenmiş taslak", vm.Satirlar[0].Aciklama);
        Assert.Equal((true, true, onizleme, 1), (vm.OnizlemeVar, vm.Onay, vm.OnizlemeMetni, api.OnizlemeSayisi));
        Assert.Empty(api.KaydetIstekleri);
    }

    [Fact]
    public async Task Pencerede_suzgec_kayit_ozeti_ve_kapanan_satir_dugmesi_calisir()
    {
        GorunumOrtami.Kur();
        var belge = DurumBelgesi(Satir(1), Satir(2), Satir(3)) with { Kayitlar = [Kayit(7, 1)], YokSayilanlar = [2] };
        var (vm, api, _) = await Hazir(new() { Veri = belge });
        var pencere = Pencere(vm);
        var tablo = Tablo(pencere);
        Assert.Equal(3, tablo.ItemsSource.Cast<object>().Count());
        vm.SuzgecSecCommand.Execute(vm.SuzgecCipleri[3]);
        Assert.Equal(2, tablo.ItemsSource.Cast<object>().Count());

        vm.SatiriAcKapat(vm.Satirlar[0]);
        Assert.True(Gorunur(PanelDugmesi(pencere, "Gerekçeyle iptal et")));
        vm.SatiriAcKapat(vm.Satirlar[1]);
        ((IButtonController)PanelDugmesi(pencere, "Yok saymayı geri al")).SendClicked();
        Assert.False(Assert.Single(api.YokSayIstekleri).YokSay);

        // Etki gösterilemeyince kayıt özeti hataları satıra götürür.
        vm.SeciliSatir = null;
        vm.Satirlar[2].Secili = true;
        await vm.OnizleCommand.ExecuteAsync(null);
        ((IButtonController)PanelDugmesi(pencere, "Satıra git")).SendClicked();
        Assert.Same(vm.Satirlar[2], vm.SeciliSatir);
        Assert.Contains(tablo.ItemsSource.Cast<object>(), s => ReferenceEquals(s, vm.Satirlar[2]));
        Assert.Contains(PanelGirdileri(pencere), e => e.Text == vm.Satirlar[2].Aciklama);
        // Satırın "Yok say"ı panelde, işaretlilerinki alt çubukta.
        Assert.Equal(2, pencere.GetVisualTreeDescendants().OfType<Button>().Count(b => b.Text == "Yok say" && Gorunur(b)));
    }
}
