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
        var liste = new CollectionView { HeightRequest = 520, SelectionMode = SelectionMode.None, EmptyView = "Bu PDF'den hareket okunamadı. Kaynak PDF ve uyarıları kontrol edin." };
        liste.SetBinding(ItemsView.ItemsSourceProperty, nameof(vm.Satirlar));
        liste.ItemTemplate = new DataTemplate(() =>
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
            var metin = Bagli(nameof(EkstreSatirEditor.Ozet));
            metin.VerticalOptions = LayoutOptions.Center;
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
        });
        Govde.Add(Editor(Kart("2. Hareket satırları",
            Bagli(nameof(vm.BelgeOzeti)),
            Tikla("Kaynak PDF'yi indir", () => DosyaIslemleri.IndirVeKaydetAsync(this, vm.DosyaAsync)),
            Metin("Kutuları tek tek işaretleyin. Bir satırı inceleyerek tarih, tutar, işlem türü ve kanal dağılımını düzeltebilirsiniz. İptal edilmiş satırlar yeniden seçilebilir."),
            liste,
            Dugme("Seçilen satırların etkisini göster", nameof(vm.OnizleCommand))), nameof(vm.BelgeVar)));
        Govde.Add(Editor(Kart("3. Kontrol ve kayıt", Bagli(nameof(vm.OnizlemeMetni)),
            Goster(Onay("Benzer kayıt / belirsizlik uyarılarını kontrol ettim; ayrı hareket olarak kaydedilsin.", nameof(vm.TekrarOnay)), nameof(vm.TekrarOnayGerekli)),
            Onay("Seçilen satırları, kanal paylarını ve kasa etkisini kontrol ettim.", nameof(vm.Onay)),
            Dugme("Onayladığım satırları kaydet", nameof(vm.KaydetCommand))), nameof(vm.OnizlemeVar)));
        Govde.Add(Editor(Kart("Bu belgeden kaydedilenler", Liste<EkstreKayitSatiri>(nameof(vm.Kayitlar), KaydiIptalAsync, "Gerekçeyle iptal et", s => !s.Veri.Iptal)), nameof(vm.BelgeVar)));
        Govde.Add(Editor(Kart("İçe aktarma geçmişi", Liste<EkstreGecmisSatiri>(nameof(vm.Gecmis), s => vm.BelgeAcAsync(s.Veri.Id), "Belgeyi incele"),
            Goster(Dugme("Daha eski belgeleri yükle", nameof(vm.EskiBelgeleriYukleCommand)), nameof(vm.EskiBelgeVar)))));
        KurallariKur();
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
        foreach (var eylem in eylemler.Children.OfType<View>())
            eylem.Margin = new Thickness(0, 0, 8, 4);
        var kaynak = Bagli(nameof(s.KaynakMetni));
        kaynak.FontSize = 12;
        var icerik = new VerticalStackLayout
        {
            Spacing = 8,
            Children = {
            baslik, kaynak, Bagli(nameof(s.Uyarilar)), Bagli(nameof(s.OneriMetni)), eylemler,
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
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> { [DevicePlatform.WinUI] = new[] { ".pdf" } })
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
