using Kasa.App.Core;
using Kasa.App.Converters;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

public sealed partial class EkstreAktarmaPage : TakipSayfasi<EkstreAktarmaViewModel>, IQueryAttributable
{
    private int? _kaynakKayitId;
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("KayitId", out var value) && int.TryParse(value.ToString(), out var id) && id > 0)
            _kaynakKayitId = id;
    }
    protected override async void OnAppearing()
    {
        if (_kaynakKayitId is { } id)
        {
            _kaynakKayitId = null;
            var oturum = Vm.OturumNesli;
            if (!Vm.VeriHazir)
                await Vm.YukleAsync();
            if (Vm.OturumNesli == oturum && Vm.VeriHazir)
                await Vm.KaynakAcAsync(id);
        }
        base.OnAppearing();
    }
    public EkstreAktarmaPage(EkstreAktarmaViewModel vm) : base(vm, "Ekstre İçe Aktar",
        "PDF'deki bütün hareketleri inceleyin; yalnız seçip onayladığınız satırlar kaydedilir. PDF yüklemek tek başına kasayı değiştirmez. Yalnız TL; metin içeren, şifresiz PDF (en fazla 10 MB / 50 sayfa).",
        vm.YukleAsync)
    {
        Govde.Add(Editor(Kart("1. PDF belgesi",
            Alan("Belge türü", Secim(nameof(vm.Kaynaklar), nameof(vm.Kaynak))),
            Alan("Banka", Secim(nameof(vm.Bankalar), nameof(vm.Banka))),
            Goster(Alan("Kısa hesap adı / son 4 hane", Girdi(nameof(vm.HesapAdi))), nameof(vm.BankaMi)),
            Goster(Alan("Kart (önce Kartlar bölümünde yeni takibi açın)", Secim(nameof(vm.Kartlar), nameof(vm.Kart))), nameof(vm.KartMi)),
            Tikla("PDF seç ve oku", PdfSecAsync))));
        Govde.Add(Editor(BelgeKarti(vm), nameof(vm.BelgeVar)));
        _kontrolKarti = Kart("3. Kontrol ve kayıt", Bagli(nameof(vm.OnizlemeMetni)),
            Goster(Onay("Benzer kayıt / belirsizlik uyarılarını kontrol ettim; ayrı hareket olarak kaydedilsin.", nameof(vm.TekrarOnay)), nameof(vm.TekrarOnayGerekli)),
            Onay("Seçilen satırları, kanal paylarını ve kasa etkisini kontrol ettim.", nameof(vm.Onay)),
            Dugme("Onayladığım satırları kaydet", nameof(vm.KaydetCommand)), FormHatasi(nameof(vm.KayitHatasi)));
        Govde.Add(Editor(_kontrolKarti, nameof(vm.OnizlemeVar)));
        Govde.Add(Editor(Kart("İçe aktarma geçmişi", Liste<EkstreGecmisSatiri>(nameof(vm.Gecmis), s => vm.BelgeAcAsync(s.Veri.Id), "Belgeyi incele"),
            Goster(Dugme("Daha eski belgeleri yükle", nameof(vm.EskiBelgeleriYukleCommand)), nameof(vm.EskiBelgeVar)))));
        KurallariKur();
        vm.SonucGosterIstendi += (_, _) => Gorunur.Yap(vm.OnizlemeHataVar ? _hataKutusu : vm.OnizlemeVar ? _kontrolKarti : _satirSonucu, KaydirmaHesabi.FormKaydirmasi);
        vm.SatirGosterIstendi += (_, s) => Gorunur.Yap(_bekleyenListe, KaydirmaHesabi.FormKaydirmasi, () => _bekleyenListe.ScrollTo(s, position: ScrollToPosition.Start, animate: false));
    }
    private readonly Border _kontrolKarti;
    // Aşağıdakiler BelgeKarti'nda kurulur (kurucudan bir kez).
    private CollectionView _bekleyenListe = null!;
    private Border _hataKutusu = null!;
    private VerticalStackLayout _satirSonucu = null!;

    private static View Dolu(Label etiket, string yol)
    {
        etiket.SetBinding(Label.TextProperty, yol);
        etiket.SetBinding(IsVisibleProperty, yol, converter: new DoluIseConverter());
        return etiket;
    }
    private static Label Etiket(string stil) => new() { Style = (Style)Application.Current!.Resources[stil] };
    private static View Sekmede(View v, EkstreSekmesi sekme)
    {
        v.SetBinding(IsVisibleProperty, nameof(EkstreAktarmaViewModel.SeciliSekme), converter: new AcikIseConverter(sekme, true, false));
        return v;
    }
    private static FlexLayout Eylemler(params View[] dugmeler)
    {
        var satir = new FlexLayout { Direction = Microsoft.Maui.Layouts.FlexDirection.Row, Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var d in dugmeler)
        {
            d.Margin = new Thickness(0, 0, 8, 8);
            satir.Add(d);
        }
        return satir;
    }
    private static Button Ikincil(Button b)
    {
        b.Style = (Style)Application.Current!.Resources["BtnSecondary"];
        return b;
    }

    /// <summary>"2. Hareket satırları": belge özeti ve yön toplamları, Bekleyen/Kapanan sekmeleri, toplu işlemler ve "etkisini göster"
    /// ile hemen altındaki hataları (docs/specs/2026-11-25-pdf-ekrani.md).</summary>
    private Border BelgeKarti(EkstreAktarmaViewModel vm)
    {
        var sekmeler = new Controls.CipGrubu();
        sekmeler.SetBinding(BindableLayout.ItemsSourceProperty, nameof(vm.Sekmeler));
        sekmeler.SetBinding(Controls.CipGrubu.SecCommandProperty, nameof(vm.SekmeSecCommand));
        _satirSonucu = new VerticalStackLayout
        {
            Spacing = 4,
            Children = { Dolu(Etiket("LblTakipMesaj"), nameof(vm.SatirMesaji)), Dolu(Etiket("LblTakipHata"), nameof(vm.SatirHatasi)) }
        };
        var atlananDugmesi = Ikincil(Dugme("", nameof(vm.AtlananAcKapatCommand)));
        atlananDugmesi.SetBinding(Button.TextProperty, nameof(vm.AtlananDugmesi));
        var yonOzeti = Bagli(nameof(vm.YonOzeti));
        yonOzeti.FontAttributes = FontAttributes.Bold;
        return Kart("2. Hareket satırları",
            Bagli(nameof(vm.BelgeOzeti)),
            Eylemler(Ikincil(Tikla("Kaynak PDF'yi indir", () => DosyaIslemleri.IndirVeKaydetAsync(this, vm.DosyaAsync))),
                Goster(Ikincil(Tikla("PDF'yi yeniden oku", YenidenOkuAsync)), nameof(vm.YenidenOkunabilir)),
                Goster(atlananDugmesi, nameof(vm.AtlananVar))),
            Dolu(Etiket("LblTakipKucuk"), nameof(vm.AtlananMetni)),
            yonOzeti, sekmeler, _satirSonucu,
            Sekmede(BekleyenBolumu(vm), EkstreSekmesi.Bekleyen), Sekmede(KapananBolumu(vm), EkstreSekmesi.Kapanan));
    }

    private View BekleyenBolumu(EkstreAktarmaViewModel vm)
    {
        _bekleyenListe = new CollectionView
        {
            AutomationId = "BekleyenSatirlar",
            HeightRequest = 520,
            SelectionMode = SelectionMode.None,
            EmptyView = "Bekleyen satır yok. Belge yeni yüklendiyse PDF'ten hareket okunamamıştır; kaynak PDF'yi ve uyarıları kontrol edin."
        };
        _bekleyenListe.SetBinding(ItemsView.ItemsSourceProperty, nameof(vm.Bekleyenler));
        _bekleyenListe.ItemTemplate = new DataTemplate(() => BekleyenSatir(vm));
        var hatalar = new VerticalStackLayout { Spacing = 6 };
        hatalar.SetBinding(BindableLayout.ItemsSourceProperty, nameof(vm.OnizlemeHatalari));
        BindableLayout.SetItemTemplate(hatalar, new DataTemplate(() =>
        {
            var satir = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
            var metin = Etiket("LblError");
            metin.VerticalOptions = LayoutOptions.Center;
            metin.SetBinding(Label.TextProperty, nameof(EkstreSatirHatasi.Metin));
            satir.Add(metin);
            var git = Ikincil(new Button { Text = "Satıra git", MinimumWidthRequest = 0, MinimumHeightRequest = 44, Padding = new Thickness(12, 6) });
            git.SetBinding(IsVisibleProperty, nameof(EkstreSatirHatasi.SatirVar));
            git.SetBinding(SemanticProperties.DescriptionProperty, new Binding(nameof(EkstreSatirHatasi.Metin), stringFormat: "Satıra git: {0}"));
            git.Clicked += (_, _) => { if (git.BindingContext is EkstreSatirHatasi h) vm.SatiraGit(h); };
            satir.Add(git, 1);
            return satir;
        }));
        _hataKutusu = new Border
        {
            Style = (Style)Application.Current!.Resources["ErrorBox"],
            Content = new VerticalStackLayout { Spacing = 8, Children = { Metin("Etkisi gösterilemedi:"), hatalar } }
        };
        _hataKutusu.SetBinding(IsVisibleProperty, nameof(vm.OnizlemeHataVar));
        var secim = Bagli(nameof(vm.SecimOzeti));
        secim.FontAttributes = FontAttributes.Bold;
        return new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                Metin("Kaydedeceğiniz satırların kutularını işaretleyin. Bir satırı inceleyerek tarih, tutar, işlem türü ve kanal dağılımını "
                    + "düzeltebilirsiniz; satırı açmak seçmek değildir. Kaydetmeyeceğiniz satırı yok sayarak kapatabilirsiniz."),
                _bekleyenListe, secim, TopluBolum(vm),
                Eylemler(Dugme("Seçilen satırların etkisini göster", nameof(vm.OnizleCommand)),
                    Goster(Ikincil(Dugme("Seçilenleri yok say", nameof(vm.SeciliyiYokSayCommand))), nameof(vm.YokSayDestekleniyor))),
                _hataKutusu
            }
        };
    }

    private static View TopluBolum(EkstreAktarmaViewModel vm)
    {
        var uygula = Ikincil(Dugme("Seçilenlere uygula", nameof(vm.TopluUygulaCommand)));
        var icerik = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Metin("Seçilenlere toplu uygula: işaretli satırların türünü ve kanal dağılımını doldurur; kaydetmez."),
                SatirAlanlari(Alan("Toplu işlem türü", Secim(nameof(vm.TopluTurler), nameof(vm.TopluTur))),
                    Alan("Toplu kanal dağılımı", Secim(nameof(vm.TopluDagilimlar), nameof(vm.TopluDagilim)))),
                Goster(KanalSecimleri(nameof(vm.TopluKanallar)), nameof(vm.TopluKanalGorunur)),
                uygula, Dolu(Etiket("LblTakipMesaj"), nameof(vm.TopluMesaji)), Dolu(Etiket("LblTakipHata"), nameof(vm.TopluHatasi))
            }
        };
        var kutu = new Border { Padding = 12, Content = icerik };
        kutu.SetDynamicResource(StyleProperty, "Card");
        return kutu;
    }

    private View KapananBolumu(EkstreAktarmaViewModel vm)
    {
        var liste = new CollectionView { AutomationId = "KapananSatirlar", HeightRequest = 420, SelectionMode = SelectionMode.None, EmptyView = "Kapanan satır yok." };
        liste.SetBinding(ItemsView.ItemsSourceProperty, nameof(vm.Kapananlar));
        liste.ItemTemplate = new DataTemplate(() =>
        {
            var satir = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(2, 8) };
            satir.Add(Bagli(nameof(EkstreSatirEditor.Ozet)));
            satir.Add(YonEtiketi());
            satir.Add(BagliMetin(nameof(EkstreSatirEditor.SonucMetni)));
            var dugme = Ikincil(new Button { HorizontalOptions = LayoutOptions.Start, MinimumHeightRequest = 44, Padding = new Thickness(12, 6) });
            dugme.BindingContextChanged += (_, _) =>
            {
                if (dugme.BindingContext is not EkstreSatirEditor s)
                    return;
                dugme.Text = s.YokSayildi ? "Yok saymayı geri al" : "Gerekçeyle iptal et";
                SemanticProperties.SetDescription(dugme, $"Satır {s.Kaynak.No}: {dugme.Text}");
            };
            dugme.Clicked += async (_, _) =>
            {
                if (dugme.BindingContext is not EkstreSatirEditor s)
                    return;
                if (s.YokSayildi)
                    await vm.YokSayAsync([s], false);
                else if (s.Kayit is { } kayit)
                    await KaydiIptalAsync(kayit);
            };
            satir.Add(dugme);
            return satir;
        });
        return new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                Metin("Kaydedilen, eşleştirilen ve yok sayılan satırlar. Kaydı iptal edilen ya da geri alınan satır yeniden bekler."),
                liste,
                Goster(Metin("İptal edilen kayıtlar"), nameof(vm.IptalEdilenVar)),
                Goster(Liste<EkstreKayitSatiri>(nameof(vm.IptalEdilenler), bosMetin: false), nameof(vm.IptalEdilenVar))
            }
        };
    }

    /// <summary>"Giriş +", "Çıkış −" ya da "Belirsiz": anlam metindedir, renk yalnız destekler.</summary>
    private static Label YonEtiketi()
    {
        var etiket = Bagli(nameof(EkstreSatirEditor.YonMetni));
        etiket.FontAttributes = FontAttributes.Bold;
        etiket.FontSize = 13;
        foreach (var (yon, renk) in new[] { ("Giris", "Pos"), ("Cikis", "Neg"), ("Belirsiz", "UyariMetin") })
        {
            var tetik = new DataTrigger(typeof(Label)) { Binding = new Binding(nameof(EkstreSatirEditor.EtkinYon)), Value = yon };
            tetik.Setters.Add(new Setter { Property = Label.TextColorProperty, Value = Application.Current!.Resources[renk] });
            etiket.Triggers.Add(tetik);
        }
        return etiket;
    }

    private View BekleyenSatir(EkstreAktarmaViewModel vm)
    {
        var satir = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(0, 4) };
        var grid = new Grid
        {
            Padding = new Thickness(2, 4),
            ColumnSpacing = 8,
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) },
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) }
        };
        var sec = new CheckBox { MinimumWidthRequest = 0 };
        sec.SetBinding(CheckBox.IsCheckedProperty, nameof(EkstreSatirEditor.Secili));
        sec.SetBinding(IsEnabledProperty, nameof(EkstreSatirEditor.Secilebilir));
        sec.SetBinding(SemanticProperties.DescriptionProperty, new Binding(nameof(EkstreSatirEditor.Ozet), stringFormat: "Kaydetmek için seç: {0}"));
        grid.Add(sec);
        var metin = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { Bagli(nameof(EkstreSatirEditor.Ozet)), YonEtiketi() }
        };
        var tiklama = new TapGestureRecognizer();
        tiklama.Tapped += (_, _) => { if (satir.BindingContext is EkstreSatirEditor s) vm.SeciliSatir = s; };
        metin.GestureRecognizers.Add(tiklama);
        grid.Add(metin, 1);
        var duzenle = new Button
        {
            Text = "İncele / düzenle",
            FontSize = 13,
            MinimumWidthRequest = 0,
            MinimumHeightRequest = 44,
            Padding = new Thickness(12, 6),
            HorizontalOptions = LayoutOptions.End
        };
        duzenle.Clicked += (_, _) => { if (duzenle.BindingContext is EkstreSatirEditor s) vm.SeciliSatir = s; };
        grid.Add(duzenle, 1, 1);
        grid.SizeChanged += (_, _) =>
        {
            var genis = grid.Width >= 600;
            if (grid.ColumnDefinitions.Count != (genis ? 3 : 2))
            {
                if (genis)
                    grid.ColumnDefinitions.Add(new(GridLength.Auto));
                else
                    grid.ColumnDefinitions.RemoveAt(2);
            }
            Grid.SetColumn(duzenle, genis ? 2 : 1);
            Grid.SetRow(duzenle, genis ? 0 : 1);
        };
        var form = new ContentView { IsVisible = false };
        form.SetBinding(IsVisibleProperty, new MultiBinding
        {
            Converter = new EsitIseConverter(),
            Bindings = { new Binding("."), new Binding(nameof(vm.SeciliSatir), source: vm) }
        });
        // Sanallaştırılan hücre yeniden başka satıra bağlanabilir. Yalnız açık satırın formunu üretiriz;
        // taslak değerler zaten satır modelindedir, aç/kapa mali seçime veya önizlemeye dokunmaz.
        void FormuYansit()
        {
            if (form.IsVisible && form.BindingContext is EkstreSatirEditor s && ReferenceEquals(vm.SeciliSatir, s))
            {
                if (!ReferenceEquals(form.Content?.BindingContext, s))
                    form.Content = SatirFormunuKur(s);
            }
            else
                form.Content = null;
        }
        form.BindingContextChanged += (_, _) => FormuYansit();
        form.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(IsVisible)) FormuYansit(); };
        satir.Add(grid);
        satir.Add(form);
        return satir;
    }
    private async Task YenidenOkuAsync()
    {
        if (await DisplayAlertAsync("PDF'yi yeniden oku", "Saklanan PDF baştan okunur. Bu belgede satırlara yazdıklarınız ve işaretleriniz silinir; kasa değişmez.", "Yeniden oku", "Vazgeç"))
            await Vm.YenidenOkuAsync();
    }
    private View SatirFormunuKur(EkstreSatirEditor s)
    {
        var baslik = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        var baslikMetni = Metin($"Satır {s.Kaynak.No}");
        baslikMetni.SetDynamicResource(StyleProperty, "LblTakipKartBaslik");
        baslik.Add(baslikMetni);
        var kapat = Tikla("Kapat", () => { if (ReferenceEquals(Vm.SeciliSatir, s)) Vm.SeciliSatir = null; return Task.CompletedTask; });
        kapat.MinimumWidthRequest = 0;
        kapat.MinimumHeightRequest = 44;
        kapat.Padding = new Thickness(12, 6);
        SemanticProperties.SetDescription(kapat, $"Satır {s.Kaynak.No} düzenleyicisini kapat");
        baslik.Add(kapat, 1);
        var eylemler = new FlexLayout { Direction = Microsoft.Maui.Layouts.FlexDirection.Row, Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        eylemler.Add(Goster(Tikla("Öneriyi uygula", () =>
        {
            if (Vm.EditorMu && !Vm.Mesgul && Vm.Satirlar.Contains(s))
                s.OneriyiUygula(false);
            return Task.CompletedTask;
        }), nameof(s.OneriUygulanabilir)));
        eylemler.Add(Tikla("Bu seçimi hatırla", () => { KuralHatirla(s); return Task.CompletedTask; }));
        eylemler.Add(Goster(Tikla("Yok say", () => Vm.YokSayAsync([s], true)), nameof(s.YokSayilabilir)));
        foreach (var eylem in eylemler.Children.OfType<View>())
            eylem.Margin = new Thickness(0, 0, 8, 4);
        var kaynak = Bagli(nameof(s.KaynakMetni));
        kaynak.FontSize = 12;
        var icerik = new VerticalStackLayout
        {
            Spacing = 8,
            Children = {
            baslik, kaynak, Bagli(nameof(s.Uyarilar)), Bagli(nameof(s.OneriMetni)), eylemler, Dolu(Etiket("LblTakipMesaj"), nameof(s.FormMesaji)),
            SatirAlanlari(Alan("Tarih (yıl-ay-gün)", Girdi(nameof(s.TarihMetni))), Alan("Tutar (pozitif TL)", Girdi(nameof(s.TutarMetni)))),
            Alan("Açıklama", Girdi(nameof(s.Aciklama))),
            SatirAlanlari(Alan("İşlem türü", Secim(nameof(s.IslemTurleri), nameof(s.IslemTuru))),
                Goster(Alan("Kanal dağılımı", Secim(nameof(s.DagilimTurleri), nameof(s.DagilimTuru))), nameof(s.DagilimGorunur))),
            Goster(Alan("Kart ödemesinde kullanılacak kart", Secim(nameof(s.Kartlar), nameof(s.Kart))), nameof(s.KartSecimiGorunur)),
            Goster(Alan("İadenin kaynak harcaması", Secim(nameof(s.KaynakHarcamalar), nameof(s.KaynakHarcama), "Baslik")), nameof(s.IadeMi)),
            // Mevcut kayıtla eşleştirme yeni kayıt üretmez: kanal dağılımı yerine bağlanacak kayıt seçilir.
            Goster(new VerticalStackLayout
            {
                Spacing = 8,
                Children = {
                Metin("Bu satır yeni kayıt oluşturmaz; aynı tutarda, en çok 3 gün farklı tarihli mevcut bir kayda bağlanır. Kasa ve kart borcu değişmez; iptali de hiçbir kaydı değiştirmez."),
                Tikla("Eşleşme adaylarını getir", () => Vm.EslesmeAdaylariniGetirCommand.ExecuteAsync(null)),
                Alan("Bağlanacak mevcut kayıt", Secim(nameof(s.EslesmeAdaylari), nameof(s.SeciliAday), "Baslik")) }
            }, nameof(s.EslesmeMi)),
            Goster(new VerticalStackLayout
            {
                Spacing = 8,
                Children = {
                Metin("Eşit: kanalları seçin. Özel: payların toplamı hareket tutarı olmalı. Kart ödemesi ve iadede paylar karttan alınır."),
                Paylar(s.Paylar, s.PayEkle) }
            }, nameof(s.DagilimGorunur)) }
        };
        var form = new Border { Padding = 12, Content = icerik };
        form.SetDynamicResource(StyleProperty, "Card");
        form.BindingContext = s;
        return form;
    }
    private static Grid SatirAlanlari(View sol, View sag)
    {
        var grid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 8,
            ColumnDefinitions = { new(GridLength.Star) },
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) }
        };
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
    // Yalnız PDF, 10 MB sınırı ve oturum koruması EkstreAktarmaViewModel.PdfSecVeYukleAsync'tedir (maui-8); sayfa yalnız dosya
    // seçiciyi açar.
    private async Task PdfSecAsync()
    {
        var secim = Vm.YuklemeSecimi();
        if (secim is null)
            return;
        await Vm.PdfSecVeYukleAsync(secim, async () =>
        {
            var dosya = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Banka veya kart PDF ekstresini seçin",
                FileTypes = DosyaSecimTurleri.Pdf
            });
            return dosya is null ? null : new SecilenDosya(dosya.FileName, dosya.OpenReadAsync);
        });
    }
    private async Task KaydiIptalAsync(EkstreKayitSatiri s)
    {
        var belgeId = Vm.Belge?.Id;
        if (belgeId is null)
            return;
        await GerekceyleAsync("Aktarılan kaydı iptal et", async (gerekce, oturum) =>
        {
            if (await DisplayAlertAsync("İptali onayla", $"{s.Baslik}\nBu kaydın mali etkisi iptal edilecek. Kaynak PDF ve işlem geçmişi korunur.", "İptal et", "Vazgeç"))
                await Vm.KayitIptalAsync(s, gerekce, oturum, belgeId.Value);
        });
    }
}
