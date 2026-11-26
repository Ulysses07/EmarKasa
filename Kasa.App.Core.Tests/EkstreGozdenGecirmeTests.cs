using System.Collections.Specialized;
using System.Diagnostics;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core.Tests;

// Son gözden geçirmenin bulguları (docs/specs/2026-11-26-ekstre-hareket-penceresi.md): toplu işlemler yalnız görünen işaretli satırlara, pencere kapanınca
// yapay zekâ durur, hazırlığı kalkan kendiliğinden işaretli satırın işareti kalkar, büyük listede süzgeç tek bildirimle değişir, yapay zekâ durumu
// belgeye aittir.
public partial class EkstreAktarmaTests
{
    private static async Task<EkstreAktarmaViewModel> KontrolSuzgecinde3Satir(Sahte api)
    {
        api.Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3));
        api.Oneriler = [YzHazir(1), YzHazir(2), YzHazir(3, dagilim: null, hazir: false, neden: "kanal seçin")];
        var (vm, _, _) = await Hazir(api);
        vm.Suzgec = EkstreSuzgec.Kontrol;
        vm.Satirlar[2].Secili = true;
        return vm;
    }

    [Fact]
    public async Task Toplu_tur_kanal_yalniz_gorunen_isaretli_satirlara_uygulanir()
    {
        var api = new Sahte();
        var vm = await KontrolSuzgecinde3Satir(api);
        vm.TopluDagilim = vm.TopluDagilimlar.Single(d => d.Kod == DagilimBicimleri.Genel);
        vm.TopluUygulaCommand.Execute(null);
        Assert.Equal([false, false, true], vm.Satirlar.Select(s => s.ElleDegisti));
        Assert.Equal("1 satıra uygulandı; henüz kaydedilmedi.", vm.TopluMesaji);
        Assert.Equal("Bu listede işaretli 1 satıra uygulanır.", vm.TopluHedefMetni);
    }

    [Fact]
    public async Task Alt_cubugun_yok_sayi_yalniz_gorunen_isaretli_satirlari_kapatir()
    {
        var api = new Sahte();
        var vm = await KontrolSuzgecinde3Satir(api);
        await vm.SeciliyiYokSayCommand.ExecuteAsync(null);
        Assert.Equal([3], Assert.Single(api.YokSayIstekleri).SatirNolar);
    }

    [Fact]
    public async Task Pencere_kapaninca_yapay_zeka_dongusu_durur()
    {
        var api = YzBelgesi(Yok(1));
        EkstreAktarmaViewModel? vm = null;
        api.YzSorYaniti = _ => { vm!.YapayZekaDurdur(); return new(1, 1, 5, 0.0001m, null); };
        vm = new EkstreAktarmaViewModel(api, new SahteApi { KanallarListe = [new(1, "MEZAT", true, 0, 0)] }, new FinansTakipTests.Sahte(),
            new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        await vm.BelgeAcAsync(1);
        Assert.Single(api.YzSorulari);
        // Durdurma o belge açılışına özgüdür: yeniden açılınca yine sorar.
        api.YzSorYaniti = _ => new(1, 1, 0, 0.0001m, null);
        await vm.BelgeAcAsync(1);
        Assert.Equal(2, api.YzSorulari.Count);
    }

    [Fact]
    public async Task Hazirligi_kalkan_kendiliginden_isaretli_satirin_isareti_kalkar()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1)), Oneriler = [HafizaOnerisi(1)] });
        Assert.True(vm.Satirlar[0].Secili);
        api.Oneriler = [];
        await vm.KurallariYenileAsync();
        // Satır PDF okumasına döner (gider, kanalsız): işaret kalkar, nedeni yazar.
        Assert.Equal((false, "kanal seçin"), (vm.Satirlar[0].Secili, vm.Satirlar[0].KontrolNedeni));
    }

    [Fact]
    public async Task Kullanicinin_dokundugu_isaret_hazirlik_kalksa_da_korunur()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1)), Oneriler = [HafizaOnerisi(1)] });
        vm.Satirlar[0].Secili = false;
        vm.Satirlar[0].Secili = true;
        api.Oneriler = [];
        await vm.KurallariYenileAsync();
        Assert.True(vm.Satirlar[0].Secili);
    }

    /// <summary>Hazırlanmadan önce kullanıcının işaretleyip kaldırdığı satır, sonradan hazırlansa da kendiliğinden işaretlenmez.</summary>
    [Fact]
    public async Task Hazirlanmadan_once_elle_kaldirilan_isaret_hazirlaninca_geri_gelmez()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1)) });
        vm.Satirlar[0].Secili = true;
        vm.Satirlar[0].Secili = false;
        api.Oneriler = [HafizaOnerisi(1)];
        await vm.KurallariYenileAsync();
        Assert.Equal((true, false), (vm.Satirlar[0].HazirOneri, vm.Satirlar[0].Secili));
    }

    /// <summary>Pencere, belge açılışının kural isteği sürerken kapanırsa yapay zekâya hiç soru gitmez; belge yeniden açılınca sorar.</summary>
    [Fact]
    public async Task Kurallar_beklenirken_kapanan_pencere_yapay_zekayi_baslatmaz()
    {
        var api = YzBelgesi(Yok(1));
        var vm = new EkstreAktarmaViewModel(api, new SahteApi { KanallarListe = [new(1, "MEZAT", true, 0, 0)] }, new FinansTakipTests.Sahte(),
            new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        var kurallar = new TaskCompletionSource<IReadOnlyList<EkstreKuralOnerisi>>();
        api.OneriYaniti = kurallar.Task;
        var acildi = false;
        vm.PencereAcIstendi += (_, _) => acildi = true;
        var acilis = vm.BelgeAcAsync(1);
        Assert.True(acildi);
        vm.YapayZekaDurdur();
        kurallar.SetResult(api.Oneriler);
        await acilis;
        Assert.Equal((0, 0), (api.YzSorulari.Count, api.YzOnizlemeSayisi));
        await vm.BelgeAcAsync(1);
        Assert.Single(api.YzSorulari);
    }

    /// <summary>Elle "yeniden sor"un onaylanan metni de gönderilenlere eklenir: "gönderilen metni göster" son gönderimi de gösterir.</summary>
    [Fact]
    public async Task Elle_yeniden_sorulan_metin_gonderilenlere_eklenir()
    {
        var api = YzBelgesi(Yok(1));
        api.YzSorYaniti = _ => new(1, 0, 0, 0.0001m, null);
        var (vm, _, _) = await Hazir(api);
        List<string> otomatik = [.. vm.SonGonderilenler];
        Assert.NotEmpty(otomatik);
        api.YzOnizleme = new(1, 0, 0, ["1 | CIKIS | 100-1000 | [1] KIRA"], 0.0091m);
        Assert.NotNull(await vm.YapayZekaOnizleAsync());
        await vm.YanitsizlariSorAsync();
        Assert.Equal([.. otomatik, "1 | CIKIS | 100-1000 | [1] KIRA"], vm.SonGonderilenler);
        await vm.GonderilecekAcKapatCommand.ExecuteAsync(null);
        Assert.Contains("1 | CIKIS | 100-1000 | [1] KIRA", vm.GonderilecekSatirlar);
    }

    /// <summary>1500 satırda Tümü → Kontrol edin geçişi satır satır değil tek bildirimle olur (WinUI listesine 1460 ayrı güncelleme gitmez); arama
    /// ve süzgeç geçişleri hızlıdır.</summary>
    [Fact]
    public async Task Buyuk_listede_suzgec_tek_bildirimle_degisir()
    {
        var satirlar = Enumerable.Range(1, 1500).Select(i => Satir(i) with { Aciklama = $"HAREKET {i}" }).ToArray();
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(satirlar), Oneriler = [.. satirlar.Select(r => r.No <= 1460 ? YzHazir(r.No) : Yok(r.No))] });
        Assert.Equal((1460, 40), (vm.HazirSayisi, vm.KontrolSayisi));
        var bildirim = 0;
        vm.Gorunenler.CollectionChanged += (_, _) => bildirim++;
        vm.PropertyChanged += (_, e) => bildirim += e.PropertyName == nameof(vm.Gorunenler) ? 1 : 0;
        vm.Suzgec = EkstreSuzgec.Kontrol;
        Assert.InRange(bildirim, 1, 2);
        Assert.Equal(40, vm.Gorunenler.Count);
        var sure = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
        {
            vm.Suzgec = i % 2 == 0 ? EkstreSuzgec.Tumu : EkstreSuzgec.Hazir;
            vm.Arama = $"hareket {i}";
            vm.Arama = "";
        }
        Assert.True(sure.ElapsedMilliseconds < 1500, $"{sure.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Yapay_zeka_durumu_baska_belge_acilinca_temizlenir_gonderilen_yalniz_basarili_sorudan()
    {
        var api = YzBelgesi(Yok(1));
        api.YzSorYaniti = _ => new(1, 1, 0, 0.0001m, null);
        var (vm, _, _) = await Hazir(api);
        Assert.Equal(("Yapay zekâ 1 satırı doldurdu", 1), (vm.YapayZekaIlerleme, vm.SonGonderilenler.Count));
        api.Veri = DurumBelgesi(Satir(1)) with { Id = 2 };
        api.Oneriler = [HafizaOnerisi(1)];
        await vm.BelgeAcAsync(2);
        Assert.Equal(((string?)null, 0), (vm.YapayZekaIlerleme, vm.SonGonderilenler.Count));

        var hatali = YzBelgesi(Yok(1));
        hatali.YzSorYaniti = _ => throw new KasaApiException(System.Net.HttpStatusCode.ServiceUnavailable, "Yapay zekâ hizmetine ulaşılamadı.");
        var (vm2, _, _) = await Hazir(hatali);
        Assert.Empty(vm2.SonGonderilenler);
    }
}
