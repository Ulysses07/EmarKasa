using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

/// <summary>Ekstre İçe Aktar: belge yükleme, içe aktarma geçmişi, kişisel kurallar, işlem hafızası ve yapay zekâ. Bir belge açılınca hareketler
/// tam pencere açılan <see cref="EkstreHareketPenceresi"/>'nde incelenir (docs/specs/2026-11-26-ekstre-hareket-penceresi.md §1).</summary>
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
        "Banka, kart ya da ERP12 dosyasını yükleyin; hareketler ayrı pencerede incelenir ve yalnız onayladığınız satırlar kaydedilir.", vm.YukleAsync)
    {
        Govde.Add(Editor(Kart("Yeni belge yükle",
            Alan("Belge türü", Secim(nameof(vm.Kaynaklar), nameof(vm.Kaynak))),
            Goster(Alan("Banka", Secim(nameof(vm.Bankalar), nameof(vm.Banka))), nameof(vm.BankaSecilir)),
            Goster(Alan("Kısa hesap adı / son 4 hane", Girdi(nameof(vm.HesapAdi))), nameof(vm.BankaMi)),
            Goster(Alan("Kart (önce Kartlar bölümünde yeni takibi açın)", Secim(nameof(vm.Kartlar), nameof(vm.Kart))), nameof(vm.KartMi)),
            Goster(Metin("Metin içeren şifresiz PDF (en çok 50 sayfa) ya da bankanın Excel'i; en fazla 10 MB, yalnız TL."), nameof(vm.BankaSecilir)),
            Goster(Metin("ERP12'den cari hareketlerini Excel olarak dışa aktarın; hareketler kasadaki kayıtlarla eşleşir, önceden aktarılanlar ikinci kez alınmaz."),
                nameof(vm.ErpMi)),
            Eylemler(Birincil(Tikla("PDF ya da Excel seç ve oku", PdfSecAsync)),
                Goster(Ikincil(Tikla("Örnek Excel'i indir", () => DosyaIslemleri.IndirVeKaydetAsync(this, vm.SablonAsync))), nameof(vm.BankaSecilir))),
            FormHatasi(nameof(vm.YuklemeHatasi)))));
        Govde.Add(Editor(Kart("İçe aktarma geçmişi", Liste<EkstreGecmisSatiri>(nameof(vm.Gecmis), s => vm.BelgeAcAsync(s.Veri.Id), "Belgeyi incele"),
            Goster(Dugme("Daha eski belgeleri yükle", nameof(vm.EskiBelgeleriYukleCommand)), nameof(vm.EskiBelgeVar)))));
        KurallariKur();
        vm.PencereAcIstendi += (_, _) => _ = EkstreHareketPenceresi.AcAsync(Navigation, vm, this);
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

    // Dosya türü, 10 MB sınırı ve oturum koruması EkstreAktarmaViewModel.PdfSecVeYukleAsync'tedir (maui-8); sayfa yalnız dosya
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
                PickerTitle = "Banka veya kart ekstresini seçin (PDF ya da Excel)",
                FileTypes = DosyaSecimTurleri.Ekstre
            });
            return dosya is null ? null : new SecilenDosya(dosya.FileName, dosya.OpenReadAsync);
        });
    }
}
