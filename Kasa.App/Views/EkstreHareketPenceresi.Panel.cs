using System.ComponentModel;
using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

// Sağ panel (docs/specs/2026-11-26-ekstre-hareket-penceresi.md §2): açık satırın formu, toplu tür/kanal ya da kayıt özeti. Pencere 900 px'ten darsa
// panel tablonun yerini alır ve "Geri" ile kapanır. Esc önce paneli, sonra pencereyi kapatır.
public sealed partial class EkstreHareketPenceresi
{
    private enum PanelKipi { Kapali, Satir, Toplu, KayitOzeti }
    private const double DarGenislik = 900, PanelGenisligi = 360;
    private PanelKipi _kip;
    private Border _panel = null!;
    private ContentView _panelIcerigi = null!, _panelAlti = null!;
    private Label _panelBasligi = null!;
    private Button _panelKapat = null!;
    private View? _toplu, _kayitOzeti, _kayitEylemleri;
    private bool PanelAcik => _kip != PanelKipi.Kapali;

    private View PaneliKur()
    {
        _panelBasligi = Etiket("LblTakipKartBaslik");
        _panelKapat = Ikincil(Tikla("Kapat", () => { PaneliKapat(); return Task.CompletedTask; }));
        _panelKapat.HorizontalOptions = LayoutOptions.End;
        var ust = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        ust.Add(_panelBasligi);
        ust.Add(_panelKapat, 1);
        _panelIcerigi = new ContentView();
        // Kaydetme eylemleri uzun özetin altında kaybolmasın: panelin altında sabit durur.
        _panelAlti = new ContentView { Padding = new Thickness(0, 10, 0, 0) };
        var yerlesim = new Grid { RowSpacing = 10, RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        yerlesim.Add(ust);
        yerlesim.Add(new ScrollView { Content = _panelIcerigi }, 0, 1);
        yerlesim.Add(_panelAlti, 0, 2);
        _panel = new Border
        {
            IsVisible = false,
            WidthRequest = PanelGenisligi,
            Margin = new Thickness(12, 0, 0, 0),
            Padding = 14,
            Content = yerlesim,
        };
        _panel.SetDynamicResource(StyleProperty, "Card");
        return _panel;
    }

    private void ModelDegisti(object? sender, PropertyChangedEventArgs e)
    {
        // Oturum kapandı ya da belge silindi: pencere boş belgeyle açık kalmaz.
        if (e.PropertyName == nameof(_vm.Belge) && _vm.Belge is null)
        {
            _ = KapatAsync();
            return;
        }
        if (e.PropertyName != nameof(_vm.SeciliSatir))
            return;
        if (_vm.SeciliSatir is { } satir)
            PaneliAc(PanelKipi.Satir, satir);
        else if (_kip == PanelKipi.Satir)
            PaneliKapat();
    }

    private void SatiriAcKapat(EkstreSatirEditor satir) => _vm.SatiriAcKapat(satir);

    private void PaneliAc(PanelKipi kip, EkstreSatirEditor? satir = null)
    {
        if (kip != PanelKipi.Satir && _vm.SeciliSatir is not null)
        {
            _kip = kip;
            _vm.SeciliSatir = null;
        }
        _kip = kip;
        (_panelBasligi.Text, _panelIcerigi.Content) = kip switch
        {
            PanelKipi.Satir when satir is not null => ($"Satır {satir.Kaynak.No}", satir.Kapali ? KapananSatirFormu(satir) : SatirFormu(satir)),
            PanelKipi.Toplu => ("Toplu tür / kanal", _toplu ??= TopluBolum()),
            PanelKipi.KayitOzeti => ("Kayıt özeti", _kayitOzeti ??= KayitOzeti()),
            _ => ("", null),
        };
        _panelAlti.Content = kip == PanelKipi.KayitOzeti ? _kayitEylemleri ??= KayitEylemleri() : null;
        EtkiDugmesiniAyarla();
        _panel.IsVisible = true;
        DarDuzeniUygula();
    }

    private void PaneliKapat()
    {
        var kip = _kip;
        _kip = PanelKipi.Kapali;
        _panel.IsVisible = false;
        _panelIcerigi.Content = null;
        _panelAlti.Content = null;
        EtkiDugmesiniAyarla();
        if (kip == PanelKipi.Satir)
            _vm.SeciliSatir = null;
        DarDuzeniUygula();
    }

    /// <summary>Dar pencerede açık panel tablonun yerini alır.</summary>
    private void DarDuzeniUygula()
    {
        if (_panel is null || _tablo?.Parent?.Parent is not View tabloKarti)
            return;
        var dar = Width > 0 && Width < DarGenislik;
        tabloKarti.IsVisible = !(dar && PanelAcik);
        Grid.SetColumn(_panel, dar ? 0 : 1);
        _panel.WidthRequest = dar ? -1 : PanelGenisligi;
        _panel.Margin = dar ? 0 : new Thickness(12, 0, 0, 0);
        _panelKapat.Text = dar ? "Geri" : "Kapat";
    }

    private void SatiraGit(object? sender, EkstreSatirEditor satir)
    {
        if (!_vm.Gorunenler.Contains(satir))
            _vm.Suzgec = EkstreSuzgec.Tumu;
        _tablo.ScrollTo(satir, position: ScrollToPosition.Start, animate: false);
        PaneliAc(PanelKipi.Satir, satir);
    }

    private void SonucuGoster(object? sender, EkstreSonucu sonuc)
    {
        if (sonuc == EkstreSonucu.Onizleme)
            PaneliAc(PanelKipi.KayitOzeti);
        else if (_kip == PanelKipi.KayitOzeti)
            PaneliKapat();
    }

    private View KayitOzeti()
    {
        var hatalar = new VerticalStackLayout { Spacing = 6 };
        hatalar.SetBinding(BindableLayout.ItemsSourceProperty, nameof(_vm.OnizlemeHatalari));
        BindableLayout.SetItemTemplate(hatalar, new DataTemplate(() =>
        {
            var metin = Etiket("LblError");
            metin.SetBinding(Label.TextProperty, nameof(EkstreSatirHatasi.Metin));
            var git = Ikincil(new Button { Text = "Satıra git", HorizontalOptions = LayoutOptions.Start, MinimumHeightRequest = 36, Padding = new Thickness(10, 4) });
            git.SetBinding(IsVisibleProperty, nameof(EkstreSatirHatasi.SatirVar));
            git.Clicked += (_, _) => { if (git.BindingContext is EkstreSatirHatasi h) _vm.SatiraGit(h); };
            return new VerticalStackLayout { Spacing = 4, Children = { metin, git } };
        }));
        return new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                Goster(hatalar, nameof(_vm.OnizlemeHataVar)),
                Goster(Bagli(nameof(_vm.OnizlemeMetni)), nameof(_vm.OnizlemeVar)),
                // Seçim ya da satır değişince özet düşer: boş panel yerine ne yapılacağı.
                Goster(Metin("Seçim değişti. Güncel etkiyi görmek için alttaki \"Etkisini göster ve kaydet\"e basın."), nameof(_vm.OnizlemeYenilenmeli)),
            }
        };
    }

    private void EtkiDugmesiniAyarla()
    {
        if (_kip == PanelKipi.KayitOzeti)
            _etkiDugmesi.Style = Stil("BtnSecondary");
        else
            _etkiDugmesi.ClearValue(StyleProperty);
    }

    private View KayitEylemleri() => Goster(new VerticalStackLayout
    {
        Spacing = 8,
        Children =
        {
            Goster(Onay("Benzer kayıt / belirsizlik uyarılarını kontrol ettim; ayrı hareket olarak kaydedilsin.", nameof(_vm.TekrarOnay)), nameof(_vm.TekrarOnayGerekli)),
            Onay("Seçilen satırları, kanal paylarını ve kasa etkisini kontrol ettim.", nameof(_vm.Onay)),
            Dugme("Kaydet", nameof(_vm.KaydetCommand)),
            FormHatasi(nameof(_vm.KayitHatasi)),
        }
    }, nameof(_vm.OnizlemeVar));

    private View KapananSatirFormu(EkstreSatirEditor s)
    {
        var kaynak = new Label { Text = s.KaynakMetni, FontSize = 12, Style = Stil("LblTakipKucuk") };
        var dugme = Ikincil(Tikla(s.YokSayildi ? "Yok saymayı geri al" : "Gerekçeyle iptal et", async () =>
        {
            if (s.YokSayildi)
                await _vm.YokSayAsync([s], false);
            else if (s.Kayit is { } kayit)
                await KaydiIptalAsync(kayit);
        }));
        dugme.HorizontalOptions = LayoutOptions.Start;
        return new VerticalStackLayout { Spacing = 8, Children = { Metin(s.KapananBaslik), Metin(s.SonucMetni), kaynak, dugme } };
    }

    private View SatirFormu(EkstreSatirEditor s)
    {
        var eylemler = new FlexLayout { Direction = Microsoft.Maui.Layouts.FlexDirection.Row, Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        eylemler.Add(Goster(Ikincil(Tikla("Öneriyi uygula", () =>
        {
            if (_vm.EditorMu && !_vm.Mesgul && _vm.Satirlar.Contains(s))
                s.OneriyiUygula(false);
            return Task.CompletedTask;
        })), nameof(s.OneriUygulanabilir)));
        eylemler.Add(Ikincil(Tikla("Bu seçimi hatırla", async () => { await KapatAsync(); _sayfa.KuralHatirla(s); })));
        eylemler.Add(Goster(Ikincil(Tikla("Bu hafızayı unut", () => SatirHafizasiniUnutAsync(s))), nameof(s.HafizaVar)));
        eylemler.Add(Goster(Ikincil(Tikla("Yok say", () => _vm.YokSayAsync([s], true))), nameof(s.YokSayilabilir)));
        foreach (var eylem in eylemler.Children.OfType<View>())
            eylem.Margin = new Thickness(0, 0, 8, 6);
        var kaynak = Bagli(nameof(s.KaynakMetni));
        kaynak.FontSize = 12;
        kaynak.Style = Stil("LblTakipKucuk");
        var icerik = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                kaynak, Bagli(nameof(s.Uyarilar)), Bagli(nameof(s.OneriMetni)), eylemler, Dolu(Etiket("LblTakipMesaj"), nameof(s.FormMesaji)),
                SatirAlanlari(Alan("Tarih (yıl-ay-gün)", Girdi(nameof(s.TarihMetni))), Alan("Tutar (pozitif TL)", Girdi(nameof(s.TutarMetni)))),
                Alan("Açıklama", Girdi(nameof(s.Aciklama))),
                Alan("İşlem türü", Secim(nameof(s.IslemTurleri), nameof(s.IslemTuru))),
                Goster(Alan("Kanal dağılımı", Secim(nameof(s.DagilimTurleri), nameof(s.DagilimTuru))), nameof(s.DagilimGorunur)),
                Goster(Alan("Kart ödemesinde kullanılacak kart", Secim(nameof(s.Kartlar), nameof(s.Kart))), nameof(s.KartSecimiGorunur)),
                Goster(Alan("İadenin kaynak harcaması", Secim(nameof(s.KaynakHarcamalar), nameof(s.KaynakHarcama), "Baslik")), nameof(s.IadeMi)),
                // Mevcut kayıtla eşleştirme yeni kayıt üretmez: kanal dağılımı yerine bağlanacak kayıt seçilir.
                Goster(new VerticalStackLayout
                {
                    Spacing = 8,
                    Children = {
                    Metin("Bu satır yeni kayıt oluşturmaz; aynı tutarda, en çok 3 gün farklı tarihli mevcut bir kayda bağlanır. Kasa ve kart borcu değişmez."),
                    Ikincil(Tikla("Eşleşme adaylarını getir", () => _vm.EslesmeAdaylariniGetirCommand.ExecuteAsync(null))),
                    Alan("Bağlanacak mevcut kayıt", Secim(nameof(s.EslesmeAdaylari), nameof(s.SeciliAday), "Baslik")) }
                }, nameof(s.EslesmeMi)),
                Goster(new VerticalStackLayout
                {
                    Spacing = 8,
                    Children = {
                    Metin("Eşit: kanalları seçin. Özel: payların toplamı hareket tutarı olmalı."),
                    IkincilDugmeler(Paylar(s.Paylar, s.PayEkle)) }
                }, nameof(s.DagilimGorunur)),
            }
        };
        icerik.BindingContext = s;
        return icerik;
    }

    /// <summary>İşaretli satırlara toplu tür ve kanal dağılımı: doldurur, kaydetmez.</summary>
    private View TopluBolum() => new VerticalStackLayout
    {
        Spacing = 8,
        Children =
        {
            Metin("İşaretli satırların türünü ve kanal dağılımını doldurur; kaydetmez. Süzgecin ya da aramanın gizlediği satırlar değişmez."),
            Bagli(nameof(_vm.TopluHedefMetni)),
            Alan("Toplu işlem türü", Secim(nameof(_vm.TopluTurler), nameof(_vm.TopluTur))),
            Alan("Toplu kanal dağılımı", Secim(nameof(_vm.TopluDagilimlar), nameof(_vm.TopluDagilim))),
            Goster(KanalSecimleri(nameof(_vm.TopluKanallar)), nameof(_vm.TopluKanalGorunur)),
            Ikincil(Dugme("Seçilenlere uygula", nameof(_vm.TopluUygulaCommand))),
            Dolu(Etiket("LblTakipMesaj"), nameof(_vm.TopluMesaji)), Dolu(Etiket("LblTakipHata"), nameof(_vm.TopluHatasi)),
        }
    };

    /// <summary>Paneldeki yardımcı düğmeler ikincildir: ekranda tek birincil düğme olur.</summary>
    private static View IkincilDugmeler(View gorunum)
    {
        foreach (var dugme in gorunum.GetVisualTreeDescendants().OfType<Button>())
            dugme.Style = Stil("BtnSecondary");
        return gorunum;
    }

    /// <summary>İki alan: panel genişse yan yana, darsa alt alta.</summary>
    private static Grid SatirAlanlari(View sol, View sag)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8, ColumnDefinitions = { new(GridLength.Star) }, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) } };
        grid.Add(sol);
        grid.Add(sag, 0, 1);
        grid.SizeChanged += (_, _) =>
        {
            var genis = grid.Width >= 540;
            if (grid.ColumnDefinitions.Count != (genis ? 2 : 1))
            {
                if (genis)
                    grid.ColumnDefinitions.Add(new(GridLength.Star));
                else
                    grid.ColumnDefinitions.RemoveAt(1);
            }
            Grid.SetColumn(sag, genis ? 1 : 0);
            Grid.SetRow(sag, genis ? 0 : 1);
        };
        return grid;
    }

    private Task SatirHafizasiniUnutAsync(EkstreSatirEditor s) => _vm.Belge is { } belge && s.Oneri?.Hafiza is { } hafiza
        ? HafizaUnutAsync(belge.Kaynak, hafiza.Anahtar, s.Kaynak.Yon) : Task.CompletedTask;

    private async Task HafizaUnutAsync(string kaynak, string anahtar, string yon)
    {
        if (await DisplayAlertAsync("Hafızayı unut", $"{anahtar} · {EkstreSatirEditor.YonAdi(yon)}\nBu hareket bundan sonra kendiliğinden doldurulmaz. Kayıtlar ve raporlar "
            + "değişmez; yeniden kaydettiğinizde yeniden öğrenilir.", "Unut", "Vazgeç"))
            await _vm.HafizaUnutAsync(kaynak, anahtar, yon);
    }

    private async Task KaydiIptalAsync(EkstreKayitSatiri s)
    {
        if (_vm.Belge?.Id is not { } belgeId)
            return;
        await _vm.GerekceyleAsync(() => DisplayPromptAsync("Aktarılan kaydı iptal et", "İşlemin nedenini yazın. Geçmiş kayıtlar korunur.", "Devam", "Vazgeç", maxLength: 1000),
            async (gerekce, oturum) =>
            {
                if (await DisplayAlertAsync("İptali onayla", $"{s.Baslik}\nBu kaydın mali etkisi iptal edilecek. Kaynak dosya ve işlem geçmişi korunur.", "İptal et", "Vazgeç"))
                    await _vm.KayitIptalAsync(s, gerekce, oturum, belgeId);
            });
    }

#if WINDOWS
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler?.PlatformView is Microsoft.UI.Xaml.UIElement eleman)
            eleman.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Escape)
                    return;
                e.Handled = true;
                OnBackButtonPressed();
            };
    }
#endif
}
