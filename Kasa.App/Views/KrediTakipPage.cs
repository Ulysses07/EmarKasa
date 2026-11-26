using System.Diagnostics;
using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

/// <summary>Krediler. Kredi bildirimine tıklanınca //krediler?KrediId={id} ile açılır: liste yüklenir, kredi seçilir ve ayrıntısı
/// görünür yere kaydırılır. Sayfa zaten açıkken gelen istek hemen, değilse sayfa belirirken uygulanır (SorguSecimi; KartTakipPage
/// ile aynı desen).</summary>
public sealed class KrediTakipPage : TakipSayfasi<KrediTakipViewModel>, IQueryAttributable
{
    private readonly View _ozet;
    /// <summary>Yeni kredi formu ve genel hata kutusu: kaydetme başarısız olunca ilk hatalı alana kaydırılır (tasarım 2026-10-02 §1).</summary>
    private readonly View _yeniKredi, _hataKutusu;
    private readonly SorguSecimi _secim = new("KrediId");

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (_secim.Iste(query))
            _ = SecimiUygulaAsync();
    }

    protected override async void OnAppearing()
    {
        _secim.Gorunuyor = true;
        await SecimiUygulaAsync();
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _secim.Gorunuyor = false;
        base.OnDisappearing();
    }

    /// <summary>Bekleyen kredi seçimi; yükleme ya da kaydırma hatası günlüğe yazılır, sayfa yine açılır (OnAppearing async void'dir,
    /// yakalanmayan istisna WinUI sürecini çökertir).</summary>
    private async Task SecimiUygulaAsync()
    {
        try
        {
            if (await _secim.UygulaAsync(Vm.YukleAsync, () => Vm.VeriHazir && Vm.Hata is null, Vm.IdIleSec))
                await Kaydirici.ScrollToAsync(_ozet, ScrollToPosition.Start, true);
        }
        catch (Exception ex) { Debug.WriteLine($"Kredi bildirimi seçimi başarısız: {ex}"); }
    }

    public KrediTakipPage(KrediTakipViewModel vm) : base(vm, "Krediler", vm.YukleAsync)
    {
        var ozet = Kart("Kredi ayrıntısı", BagliBuyuk(nameof(vm.KrediOzeti)));
        _ozet = ozet;
        Govde.Add(Kart("Krediler",
            Liste<KrediTakipSatiri>(nameof(vm.Krediler), async s =>
            {
                await vm.SecCommand.ExecuteAsync(s);
                // "Forma dön" ile seçim değişmediyse kaydırma yapılmaz (görev 14-19 incelemesi).
                if (vm.Secili?.Id == s.Veri.Id)
                    await Kaydirici.ScrollToAsync(ozet, ScrollToPosition.Start, true);
            }),
            Editor(Dugme("Kredi ekle", nameof(vm.YeniCommand)))));
        Govde.Add(ozet);
        const string h = nameof(vm.Hatalar);
        _hataKutusu = FormHatasi(h + ".Genel");
        _yeniKredi = Kart("Yeni kredi", _hataKutusu,
            Alan("Kredi adı", Girdi(nameof(vm.Ad)), h, nameof(vm.Ad)), Onay("Önceden çekilmiş kredi, kasaya girmesin", nameof(vm.MevcutKredi)),
            Alan("Çekilen tutar", Girdi(nameof(vm.CekilenTutar), true), h, nameof(vm.CekilenTutar)), Alan("Çekim tarihi", Tarih(nameof(vm.CekimTarihi))),
            Alan("İlk taksit tarihi", Tarih(nameof(vm.IlkTaksitTarihi))),
            Alan("Taksit sayısı", Girdi(nameof(vm.TaksitSayisi), sayi: true), h, nameof(vm.TaksitSayisi)),
            Alan("Aylık taksit tutarı", Girdi(nameof(vm.AylikOdeme), true), h, nameof(vm.AylikOdeme)),
            Yardim("Tek kanal seçerseniz tutarın tamamı o kanala gider. Birden fazla kanalda kredi girişi ve her taksit eşit, kuruşları korunarak "
                + "bölünür; seçili kanallar sonradan değişmez. Yeni kredi genel kasaya bir kez girer."),
            Alan("Kanallar", KanalSecimleri(nameof(vm.Kanallar)), h, nameof(vm.Kanallar)), Dugme("Tüm aktif kanalları seç", nameof(vm.TumKanallariSecCommand)),
            Birincil(Dugme("Krediyi kaydet", nameof(vm.KaydetCommand))));
        vm.Hatalar.GosterIstendi += (_, _) => Gorunur.HatayaGit(_yeniKredi, vm.Hatalar, _hataKutusu);
        Govde.Add(Editor(Goster(_yeniKredi, nameof(vm.YeniKredi))));
        Govde.Add(Editor(Goster(Kart("Eski krediyi yeni takibe al",
            Yardim("Eski kredi çekimi ikinci kez genel kasaya girmez. Geçmiş kanal bakiyeleri değişmez; ileri taksitler seçtiğiniz kanallara bölünür."),
            Alan("Geçiş tarihi", Tarih(nameof(vm.GecisTarihi))), KanalSecimleri(nameof(vm.Kanallar)),
            Dugme("Tüm aktif kanalları seç", nameof(vm.TumKanallariSecCommand)), Alan("Geçiş açıklaması", Girdi(nameof(vm.GecisAciklama))),
            Dugme("Geçiş farkını göster", nameof(vm.GecisOnizleCommand)), Bagli(nameof(vm.GecisOnizleme)),
            Onay("Farkı inceledim, geçişi onaylıyorum.", nameof(vm.GecisOnay)),
            Birincil(Dugme("Yeni takibi aç", nameof(vm.GecisiOnaylaCommand)))),
            nameof(vm.EskiTakip))));
        Govde.Add(Goster(Kart("Taksit planı", Yardim("Kalan planlı ödeme kalan anapara değildir: bankadan anapara ve faiz ayrımı girilmediği için anapara tahmin edilmez."),
            Liste<TaksitSatiri>(nameof(vm.Taksitler), s => { vm.TaksitSecCommand.Execute(s); return Task.CompletedTask; }, "Düzenle", _ => vm.EditorMu && vm.YeniTakip)),
            nameof(vm.KrediSecili)));
        var tarihTutar = new VerticalStackLayout
        {
            Spacing = 12,
            Children = { Alan("Tarih", Tarih(nameof(vm.TaksitTarihi))), Alan("Tutar", Girdi(nameof(vm.TaksitTutari), true)),
            Onay("Bu taksidi plan değişikliğiyle iptal et", nameof(vm.TaksitIptal)) }
        };
        tarihTutar.SetBinding(IsEnabledProperty, nameof(vm.TaksitDuzenlenebilir));
        Govde.Add(Editor(Goster(Kart("Taksit bilgisi", Metin("İşlenmiş taksidin tarihi ve tutarı değiştirilemez."), tarihTutar,
            Alan("Not", Girdi(nameof(vm.TaksitNotu))), Alan("Değişiklik açıklaması", Girdi(nameof(vm.Gerekce))), Birincil(Dugme("Taksiti kaydet", nameof(vm.TaksitKaydetCommand)))),
            nameof(vm.DuzenlenenTaksit), true)));
        Govde.Add(Editor(Goster(Kart("Erken kapama", Alan("Kapama tarihi", Tarih(nameof(vm.KapatmaTarihi))), Alan("Bankanın kapama tutarı", Girdi(nameof(vm.KapatmaTutari), true)),
            Alan("Kapama açıklaması", Girdi(nameof(vm.Gerekce))), Bagli(nameof(vm.KapatmaOzeti)),
            Onay("Kapama tutarını ve kalan taksitlerin iptalini onaylıyorum.", nameof(vm.KapatmaOnay)),
            Dugme("Erken kapamayı kaydet", nameof(vm.KapatCommand))),
            nameof(vm.YeniTakip))));
        Govde.Add(Editor(Goster(Kart("Arşiv",
            Tikla("Arşiv durumunu değiştir", () => GerekceyleAsync("Kredi arşiv durumunu değiştir", (gerekce, _) => { vm.Gerekce = gerekce; return vm.DurumDegistirAsync(); }))),
            nameof(vm.KrediSecili))));
    }
}
