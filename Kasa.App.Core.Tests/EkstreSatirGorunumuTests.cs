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

    private static int BirincilDugmeSayisi(Page pencere)
    {
        var ikincil = (Style)Application.Current!.Resources["BtnSecondary"];
        var cip = (Style)Application.Current!.Resources["ChipButton"];
        return pencere.GetVisualTreeDescendants().OfType<Button>().Count(b => Gorunur(b) && !ReferenceEquals(b.Style, ikincil) && !ReferenceEquals(b.Style, cip));
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
        ((IButtonController)PanelDugmesi(pencere, "Toplu tür / kanal")).SendClicked();
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
