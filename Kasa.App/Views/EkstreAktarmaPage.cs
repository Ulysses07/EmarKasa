using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

public sealed class EkstreAktarmaPage : TakipSayfasi<EkstreAktarmaViewModel>, IQueryAttributable
{
    private int? _kaynakKayitId;
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("KayitId", out var value) && int.TryParse(value.ToString(), out var id) && id > 0) _kaynakKayitId = id;
    }
    protected override async void OnAppearing()
    {
        if (_kaynakKayitId is { } id)
        {
            _kaynakKayitId = null; var oturum = Vm.OturumNesli;
            if (!Vm.VeriHazir) await Vm.YukleAsync();
            if (Vm.OturumNesli == oturum && Vm.VeriHazir) await Vm.KaynakAcAsync(id);
        }
        base.OnAppearing();
    }
    private readonly ContentView _satirFormu = new();
    public EkstreAktarmaPage(EkstreAktarmaViewModel vm) : base(vm, "Ekstre İçe Aktar", "PDF'deki bütün hareketleri inceleyin; yalnız seçip onayladığınız satırlar kaydedilir. PDF yüklemek tek başına kasayı değiştirmez. Yalnız TL; metin içeren, şifresiz PDF (en fazla 10 MB / 50 sayfa).", vm.YukleAsync)
    {
        Govde.Add(Editor(Kart("1. PDF belgesi",
            Alan("Belge türü", Secim(nameof(vm.Kaynaklar), nameof(vm.Kaynak))),
            Alan("Banka", Secim(nameof(vm.Bankalar), nameof(vm.Banka))),
            Goster(Alan("Kısa hesap adı / son 4 hane", Girdi(nameof(vm.HesapAdi))), nameof(vm.BankaMi)),
            Goster(Alan("Kart (önce Kartlar bölümünde yeni takibi açın)", Secim(nameof(vm.Kartlar), nameof(vm.Kart))), nameof(vm.KartMi)),
            Tikla("PDF seç ve oku", PdfSecAsync))));
        var liste = new CollectionView { HeightRequest = 440, SelectionMode = SelectionMode.Single, EmptyView = "Bu PDF'den hareket okunamadı. Kaynak PDF ve uyarıları kontrol edin." };
        liste.SetBinding(ItemsView.ItemsSourceProperty, nameof(vm.Satirlar));
        liste.SetBinding(SelectableItemsView.SelectedItemProperty, nameof(vm.SeciliSatir), BindingMode.TwoWay);
        liste.ItemTemplate = new DataTemplate(() =>
        {
            var grid = new Grid { Padding = new Thickness(2, 10), ColumnSpacing = 10, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
            var sec = new CheckBox(); sec.SetBinding(CheckBox.IsCheckedProperty, nameof(EkstreSatirEditor.Secili)); sec.SetBinding(IsEnabledProperty, nameof(EkstreSatirEditor.Secilebilir)); grid.Add(sec);
            var metin = Bagli(nameof(EkstreSatirEditor.Ozet)); metin.VerticalOptions = LayoutOptions.Center; grid.Add(metin, 1);
            var duzenle = new Button { Text = "İncele / düzenle" }; duzenle.Clicked += (_, _) => { if (duzenle.BindingContext is EkstreSatirEditor s) vm.SeciliSatir = s; }; grid.Add(duzenle, 2);
            return grid;
        });
        Govde.Add(Editor(Goster(Kart("2. Hareket satırları",
            Bagli(nameof(vm.BelgeOzeti)),
            Tikla("Kaynak PDF'yi indir", async () => { if (await vm.DosyaAsync() is { } d) await DosyaIslemleri.KaydetAsync(this, d); }),
            Metin("Kutuları tek tek işaretleyin. Bir satırı inceleyerek tarih, tutar, işlem türü ve kanal dağılımını düzeltebilirsiniz. İptal edilmiş satırlar yeniden seçilebilir."), liste, _satirFormu,
            Dugme("Seçilen satırların etkisini göster", nameof(vm.OnizleCommand))), nameof(vm.BelgeVar))));
        Govde.Add(Editor(Goster(Kart("3. Kontrol ve kayıt", Bagli(nameof(vm.OnizlemeMetni)),
            Goster(Onay("Benzer kayıt / belirsizlik uyarılarını kontrol ettim; ayrı hareket olarak kaydedilsin.", nameof(vm.TekrarOnay)), nameof(vm.TekrarOnayGerekli)),
            Onay("Seçilen satırları, kanal paylarını ve kasa etkisini kontrol ettim.", nameof(vm.Onay)),
            Dugme("Onayladığım satırları kaydet", nameof(vm.KaydetCommand))), nameof(vm.OnizlemeVar))));
        Govde.Add(Editor(Goster(Kart("Bu belgeden kaydedilenler", Liste<EkstreKayitSatiri>(nameof(vm.Kayitlar), KaydiIptalAsync, "Gerekçeyle iptal et", s => !s.Veri.Iptal)), nameof(vm.BelgeVar))));
        Govde.Add(Editor(Kart("İçe aktarma geçmişi", Liste<EkstreGecmisSatiri>(nameof(vm.Gecmis), s => vm.BelgeAcAsync(s.Veri.Id), "Belgeyi incele"), Goster(Dugme("Daha eski belgeleri yükle", nameof(vm.EskiBelgeleriYukleCommand)), nameof(vm.EskiBelgeVar)))));
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.SeciliSatir)) SatirFormunuKur(); };
    }
    private void SatirFormunuKur()
    {
        if (Vm.SeciliSatir is not { } s) { _satirFormu.Content = null; return; }
        var form = Kart($"Satır {s.Kaynak.No}", Bagli(nameof(s.KaynakMetni)), Bagli(nameof(s.Uyarilar)),
            Alan("Tarih (yıl-ay-gün)", Girdi(nameof(s.TarihMetni))),
            Alan("Açıklama", Girdi(nameof(s.Aciklama))),
            Alan("Tutar (pozitif TL; ör. 1234,56)", Girdi(nameof(s.TutarMetni))),
            Alan("İşlem türü", Secim(nameof(s.IslemTurleri), nameof(s.IslemTuru))),
            Goster(Alan("Kart ödemesinde kullanılacak kart", Secim(nameof(s.Kartlar), nameof(s.Kart))), nameof(s.KartSecimiGorunur)),
            Goster(Alan("İadenin kaynak harcaması", Secim(nameof(s.KaynakHarcamalar), nameof(s.KaynakHarcama), "Baslik")), nameof(s.IadeMi)),
            Alan("Kanal dağılımı", Secim(nameof(s.DagilimTurleri), nameof(s.DagilimTuru))),
            Metin("Eşit dağılımda kanalları seçin; tutarlar kullanılmaz. Özel dağılımda toplam hareket tutarına eşit olmalı. Kart ödemesi ve iadede paylar karttan otomatik alınır."), Paylar(s.Paylar, s.PayEkle));
        form.BindingContext = s; _satirFormu.Content = form;
    }
    private async Task PdfSecAsync()
    {
        var secim = Vm.YuklemeSecimi(); if (secim is null) return;
        try
        {
            var dosya = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Banka veya kart PDF ekstresini seçin", FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> { [DevicePlatform.WinUI] = new[] { ".pdf" } }) });
            if (dosya is null || Vm.OturumNesli != secim.Oturum) return;
            if (!dosya.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) { Vm.Hata = "Yalnız PDF dosyası seçin."; return; }
            await using var akis = await dosya.OpenReadAsync(); using var bellek = new MemoryStream(); var buffer = new byte[81920]; int count;
            while ((count = await akis.ReadAsync(buffer)) > 0)
            {
                if (Vm.OturumNesli != secim.Oturum) return;
                if (bellek.Length + count > 10 * 1024 * 1024) { Vm.Hata = "PDF en fazla 10 MB olabilir."; return; }
                bellek.Write(buffer, 0, count);
            }
            await Vm.PdfYukleAsync(bellek.ToArray(), dosya.FileName, secim);
        }
        catch (Exception) { if (Vm.OturumNesli == secim.Oturum) Vm.Hata = "PDF okunamadı. Dosyayı kontrol edip yeniden seçin."; }
    }
    private async Task KaydiIptalAsync(EkstreKayitSatiri s)
    {
        var oturum = Vm.OturumNesli; var belgeId = Vm.Belge?.Id; if (belgeId is null) return;
        var gerekce = await GerekceAsync("Aktarılan kaydı iptal et"); if (string.IsNullOrWhiteSpace(gerekce)) return;
        if (!await DisplayAlertAsync("İptali onayla", $"{s.Baslik}\nBu kaydın mali etkisi iptal edilecek. Kaynak PDF ve işlem geçmişi korunur.", "İptal et", "Vazgeç")) return;
        await Vm.KayitIptalAsync(s, gerekce, oturum, belgeId.Value);
    }
}
