using System.Diagnostics;
using System.Globalization;
using Kasa.App.Controls;
using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

/// <summary>
/// Çekler ve senetler (docs/specs/2026-10-01-cekler.md "Masaüstü ekranı"). Üst şerit dört hazır süzgeçtir; altında yön ve durum
/// çipleri ile arama. Liste vadeye göre sıralıdır; açılan çekin ayrıntısı satırının hemen altında görünür (OncekiSatirlar, ayrıntı,
/// SonrakiSatirlar). Hareket düğmeleri sunucunun izin verdiği türlerdir. Bildirimden //cekler?CekId={id}, panel kutusundan
/// //cekler?Suzgec={CekHazirSuzgec} ile açılır; sayfa açıkken gelen istek hemen, değilse sayfa belirirken uygulanır (SorguSecimi).
/// </summary>
public sealed class CekTakipPage : TakipSayfasi<CekTakipViewModel>, IQueryAttributable
{
    private readonly SorguSecimi _secim = new("CekId");
    private CekHazirSuzgec? _bekleyenSuzgec;
    private bool _gorunuyor;
    private readonly View _ayrinti;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Suzgec", out var deger) && Enum.TryParse<CekHazirSuzgec>(Convert.ToString(deger, CultureInfo.InvariantCulture), out var suzgec))
        {
            _bekleyenSuzgec = suzgec;
            if (_gorunuyor)
                _ = SuzgeciUygulaAsync();
        }
        if (_secim.Iste(query))
            _ = SecimiUygulaAsync();
    }

    protected override async void OnAppearing()
    {
        _gorunuyor = true;
        _secim.Gorunuyor = true;
        await SuzgeciUygulaAsync();
        await SecimiUygulaAsync();
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _gorunuyor = false;
        _secim.Gorunuyor = false;
        base.OnDisappearing();
    }

    /// <summary>Panel kutusundan gelen hazır süzgeç; hata günlüğe yazılır (OnAppearing async void'dir).</summary>
    private async Task SuzgeciUygulaAsync()
    {
        try
        {
            if (_bekleyenSuzgec is not { } suzgec)
                return;
            _bekleyenSuzgec = null;
            await Vm.HazirSuzgecAsync(suzgec);
        }
        catch (Exception ex) { Debug.WriteLine($"Çek süzgeci uygulanamadı: {ex}"); }
    }

    /// <summary>Bildirimden gelen çek: çekin yönü ve "Hepsi" süzgeciyle yüklenir, seçilir ve ayrıntısı görünür yere kaydırılır.</summary>
    private async Task SecimiUygulaAsync()
    {
        try
        {
            if (_secim.Istenen is not { } id)
                return;
            if (await _secim.UygulaAsync(() => Vm.CekIcinYukleAsync(id), () => Vm.VeriHazir && Vm.Hata is null, Vm.IdIleSec))
                await Kaydirici.ScrollToAsync(_ayrinti, ScrollToPosition.Start, true);
        }
        catch (Exception ex) { Debug.WriteLine($"Çek bildirimi seçimi başarısız: {ex}"); }
    }

    public CekTakipPage(CekTakipViewModel vm) : base(vm, "Çekler ve senetler",
        "Çek kasayı yalnız tahsil, ödeme, ciro, kırdırma ya da dönüş gününde etkiler; kayıt ve vade günü kasayı değiştirmez.", vm.YukleAsync)
    {
        _ayrinti = Ayrinti(vm);
        Govde.Add(Serit(vm));
        Govde.Add(Kart("Süzgeçler", Cipler(nameof(vm.YonCipleri), nameof(vm.SecYonCommand)), Cipler(nameof(vm.DurumCipleri), nameof(vm.SecDurumCommand)),
            Alan("Kişi, banka ya da numara", Girdi(nameof(vm.Ara))), Dugme("Ara", nameof(vm.AraCommand)),
            Goster(new VerticalStackLayout { Spacing = 6, Children = { Bagli(nameof(vm.VadeSuzgeci)), Dugme("Vade süzgecini kaldır", nameof(vm.VadeSuzgeciniKaldirCommand)) } },
                nameof(vm.VadeSuzgeciVar))));
        Govde.Add(Editor(Dugme("Yeni çek / senet", nameof(vm.YeniCekCommand))));
        Govde.Add(Editor(Goster(Form(vm), nameof(vm.FormAcik))));
        Govde.Add(Kart("Liste", SatirListesi(vm, nameof(vm.OncekiSatirlar)), _ayrinti, SatirListesi(vm, nameof(vm.SonrakiSatirlar))));
    }

    private static View Serit(CekTakipViewModel vm)
    {
        var serit = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var (yol, suzgec) in new[]
        {
            (nameof(vm.PortfoyMetni), CekHazirSuzgec.Portfoy), (nameof(vm.Alinan30Metni), CekHazirSuzgec.Alinan30),
            (nameof(vm.Verilen30Metni), CekHazirSuzgec.Verilen30), (nameof(vm.GecmisMetni), CekHazirSuzgec.VadesiGecmis),
        })
        {
            var kutu = new Button { Style = (Style)Application.Current!.Resources["BtnSecondary"], Margin = new Thickness(0, 0, 8, 8) };
            kutu.SetBinding(Button.TextProperty, yol);
            kutu.Clicked += async (_, _) => await vm.HazirSuzgecAsync(suzgec);
            serit.Add(kutu);
        }
        return serit;
    }

    private static CipGrubu Cipler(string kaynak, string komut)
    {
        var grup = new CipGrubu();
        grup.SetBinding(BindableLayout.ItemsSourceProperty, kaynak);
        grup.SetBinding(CipGrubu.SecCommandProperty, komut);
        return grup;
    }

    private static View SatirListesi(CekTakipViewModel vm, string yol) => Liste<CekSatiri>(yol, s =>
    {
        vm.SecCommand.Execute(s);
        return Task.CompletedTask;
    }, "Aç / kapat");

    private View Ayrinti(CekTakipViewModel vm)
    {
        var hareketFormu = Goster(new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                Alan("Tarih", Tarih(nameof(vm.HareketTarihi))), Alan("Tutar", Girdi(nameof(vm.HareketTutari), para: true)),
                Goster(Alan("Kasa (kanal)", Secim(nameof(vm.KasaSecenekleri), nameof(vm.HareketKasasi), ".")), nameof(vm.KasaGerekli)),
                Goster(Alan("Ciro edilen kişi / banka ya da faktoring", Girdi(nameof(vm.Karsi))), nameof(vm.KarsiGerekli)),
                Goster(Alan("Hesaba geçen tutar", Girdi(nameof(vm.NetTutar), para: true)), nameof(vm.NetGerekli)),
                Bagli(nameof(vm.MasrafMetni)), Dugme("Hareketi kaydet", nameof(vm.HareketKaydetCommand)),
            },
        }, nameof(vm.HareketFormuAcik));
        var duzenleme = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                Cipler(nameof(vm.HareketCipleri), nameof(vm.SecHareketCommand)), hareketFormu,
                Goster(Tikla("Son hareketi geri al", () => OnayliAsync("Son hareketi geri al", "Çekin son hareketi silinir; kasa etkisi de kalkar.", vm.GeriAlAsync)),
                    nameof(vm.GeriAlinabilir)),
                Dugme("Çeki düzelt", nameof(vm.DuzeltCommand)),
            },
        };
        var sil = new Button { Text = "Çeki sil", HorizontalOptions = LayoutOptions.Start, Style = (Style)Application.Current!.Resources["BtnSecondary"] };
        sil.Clicked += async (_, _) => await SilmeOnayi.GosterAsync(this, sil, vm.AcikOzet, vm.SilAsync);
        duzenleme.Add(sil);
        return Goster(new Border
        {
            Style = (Style)Application.Current!.Resources["CardForm"],
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children = { BagliBuyuk(nameof(vm.AcikOzet)), Liste<CekHareketSatiri>(nameof(vm.Hareketler)), Editor(duzenleme) },
            },
        }, nameof(vm.CekAcik));
    }

    private async Task OnayliAsync(string baslik, string metin, Func<Task> islem)
    {
        if (await DisplayAlertAsync(baslik, metin, "Devam", "Vazgeç"))
            await islem();
    }

    private static View Form(CekTakipViewModel vm)
    {
        var konum = Alan("Konum", Secim(nameof(vm.KonumSecenekleri), nameof(vm.Konum)));
        konum.SetBinding(IsVisibleProperty, nameof(vm.FormVerilen), converter: new Converters.TersIseConverter());
        var ayni = Goster(new VerticalStackLayout
        {
            Spacing = 8,
            Children = { BagliHata(nameof(vm.AyniCekUyarisi)), Dugme("Yine de kaydet", nameof(vm.YineDeKaydetCommand)) },
        }, nameof(vm.AyniCekVar));
        var kart = Kart("Çek / senet bilgileri",
            Bagli(nameof(vm.FormBasligi)),
            Alan("Tür", Secim(nameof(vm.TurSecenekleri), nameof(vm.FormTur))), Alan("Yön", Secim(nameof(vm.YonSecenekleri), nameof(vm.FormYon))),
            Alan("Çek / senet numarası", Girdi(nameof(vm.No))), Alan("Banka (senette boş olabilir)", Girdi(nameof(vm.Banka))),
            Alan("Kişi (alınanda kimden, verilende kime)", Girdi(nameof(vm.Kisi))), Alan("Tutar", Girdi(nameof(vm.Tutar), para: true)),
            Alan("Vade", Tarih(nameof(vm.Vade))),
            Goster(Alan("Ödeneceği kasa", Secim(nameof(vm.CekKasaSecenekleri), nameof(vm.CekKasasi), ".")), nameof(vm.FormVerilen)),
            konum, Onay("Teminat çeki (bildirim çıkmaz, panel toplamlarına girmez)", nameof(vm.Teminat)), Alan("Not", Girdi(nameof(vm.Not))),
            ayni, Dugme("Kaydet", nameof(vm.KaydetCommand)), Dugme("Vazgeç", nameof(vm.FormuKapatCommand)));
        return kart;
    }
}
