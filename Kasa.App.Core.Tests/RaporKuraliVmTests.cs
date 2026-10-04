using System.ComponentModel;
using Kasa.ApiClient;
using Kasa.App.Core;

namespace Kasa.App.Core.Tests;

/// <summary>Rapor kuralı kararlarının masaüstü yüzü: aylık sayfada kredi girişi (K2), kapatılmış ay (K4) ve veri sağlığı
/// uyarısı (K1); gider formunda yalnız takipteki kartlar ve kartsız yeni kredi kartı giderinin reddi (K3).</summary>
public class RaporKuraliVmTests
{
    private const string KartIletisi = "Kredi kartı gideri için yeni takipteki bir kart seçin. Kart eski takipteyse önce kart ekranından yeni takibe geçirin.";

    [Fact]
    public void Aylik_kural_2_raporda_kredi_girisi_ayri_ay_sonucu_kredi_haric_ve_uyari_gorunur()
    {
        var vm = new AylikViewModel(new SahteApi());
        var bildirilen = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => bildirilen.Add(e.PropertyName);
        vm.Rapor = new AylikRaporDto(2026, 9, [new KanalAylikDto("MEZAT", 80_000m, 100_000m, 0, 0, 0, -20_000m, 120_000m)],
            KrediGirisi: 170_000m, KuralSurumu: 2, VeriSagligiUyarisi: "Takip başlangıcından önce tarihli 2 kayıt");

        Assert.True(vm.KrediGirisiVar);
        Assert.Equal(170_000m, vm.KrediGirisiToplam);
        Assert.Equal(-20_000m, vm.GenelAySonucu); // kredi girişi ay sonucuna dahil değil
        Assert.Equal("AY SONUCU (KREDİ HARİÇ)", vm.AySonucuBasligi);
        Assert.True(vm.VeriSagligiUyarisiVar);
        Assert.Equal("Takip başlangıcından önce tarihli 2 kayıt", vm.VeriSagligiUyarisi);
        Assert.False(vm.Dondurulmus);
        Assert.Null(vm.DondurulmusMetni);
        foreach (var ad in new[] { nameof(vm.KrediGirisiVar), nameof(vm.KrediGirisiToplam), nameof(vm.AySonucuBasligi), nameof(vm.VeriSagligiUyarisiVar), nameof(vm.Dondurulmus) })
            Assert.Contains(ad, bildirilen);
    }

    [Fact]
    public void Aylik_kural_1_ile_dondurulmus_ay_isaretlenir_kredi_gelenin_icinde_oldugu_soylenir()
    {
        var vm = new AylikViewModel(new SahteApi())
        {
            Rapor = new AylikRaporDto(2026, 2, [new KanalAylikDto("MEZAT", 200_000m, 100_000m, 0, 0, 0, 100_000m, 120_000m)], KuralSurumu: 1, Dondurulmus: true),
        };
        Assert.True(vm.Dondurulmus);
        Assert.Contains("kapatıldı", vm.DondurulmusMetni);
        Assert.Contains("Gelen ve Ay sonucu içindedir", vm.DondurulmusMetni);
        Assert.Equal("AY SONUCU", vm.AySonucuBasligi);
        Assert.False(vm.KrediGirisiVar);
        Assert.Equal(100_000m, vm.GenelAySonucu);
        Assert.False(vm.VeriSagligiUyarisiVar);
    }

    private static SahteApi KartliApi() => new()
    {
        KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)],
        KrediKartlariListe =
        [
            new KrediKartiDto(1, "Eski kart", new(2026, 1, 10), new(2026, 1, 20), 0, 0),
            new KrediKartiDto(2, "Takipli", new(2026, 1, 10), new(2026, 1, 20), 0, 0, YeniTakip: true, Aktif: true),
            new KrediKartiDto(3, "Kapalı", new(2026, 1, 10), new(2026, 1, 20), 0, 0, YeniTakip: true, Aktif: false),
        ],
    };

    [Fact]
    public async Task Gider_formu_yalniz_takipteki_acik_kartlari_listeler_ve_kartsiz_yeni_kredi_karti_giderini_gondermez()
    {
        var api = KartliApi();
        var vm = new IslemlerViewModel(api, TestOturumu.Ac());
        await vm.YukleAsync();
        Assert.Equal(new[] { (2, "Takipli") }, vm.KartCipleri.Select(k => (k.Id, k.Ad)));

        vm.DuzenCari = "Market";
        vm.DuzenTutar = 75m;
        vm.DuzenKanal = "MEZAT";
        vm.DuzenTarih = new DateTime(2026, 9, 20);
        vm.SecTipCommand.Execute(new SecimCipi("Kredi kartı"));
        Assert.Null(vm.KartUyarisi);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(KartIletisi, vm.Hatalar[nameof(IslemlerViewModel.DuzenKrediKartiId)]);
        Assert.Null(api.SonIslemOlustur);

        vm.SecKartCommand.Execute(vm.KartCipleri.Single());
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.SonIslemOlustur!.KrediKartiId);
    }

    [Fact]
    public async Task Takipte_kart_yoksa_kredi_karti_secilince_yol_gosterilir()
    {
        var api = KartliApi();
        api.KrediKartlariListe = [api.KrediKartlariListe[0]];
        var vm = new IslemlerViewModel(api, TestOturumu.Ac());
        await vm.YukleAsync();
        Assert.Empty(vm.KartCipleri);
        Assert.Null(vm.KartUyarisi);
        vm.SecTipCommand.Execute(new SecimCipi("Kredi kartı"));
        Assert.Contains("Takipte kart yok", vm.KartUyarisi);
    }

    [Fact]
    public async Task Eski_kartsiz_ve_eski_kartli_kayit_kendi_kartiyla_duzenlenip_kaydedilir()
    {
        var api = KartliApi();
        var vm = new IslemlerViewModel(api, TestOturumu.Ac());
        await vm.YukleAsync();

        vm.Duzenle(new IslemDto(20, new DateOnly(2026, 8, 5), "Eski kartlı", 100m, "MEZAT", GiderTipi.KrediKarti, null, KrediKartiId: 1));
        Assert.Equal(new[] { (2, "Takipli"), (1, "Eski kart (eski kart)") }, vm.KartCipleri.Select(k => (k.Id, k.Ad)));
        Assert.True(vm.KartCipleri.Single(k => k.Id == 1).Secili);
        vm.DuzenTutar = 110m;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal((20, 1, 110m), (api.SonIslemGuncelle!.Value.Id, api.SonIslemGuncelle.Value.G.KrediKartiId, api.SonIslemGuncelle.Value.G.TutarTl));

        vm.Duzenle(new IslemDto(21, new DateOnly(2026, 8, 6), "Kartsız eski", 50m, "MEZAT", GiderTipi.KrediKarti, null));
        Assert.Equal(new[] { (2, "Takipli") }, vm.KartCipleri.Select(k => (k.Id, k.Ad))); // önceki kaydın eski kartı listeden kalkar
        vm.DuzenNot = "Dekont";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.False(vm.Hatalar.Var);
        Assert.Equal((21, (int?)null, "Dekont"), (api.SonIslemGuncelle!.Value.Id, api.SonIslemGuncelle.Value.G.KrediKartiId, api.SonIslemGuncelle.Value.G.Not));

        // Yeni kayda dönünce kartsız kredi kartı gideri yine reddedilir.
        vm.YeniCommand.Execute(null);
        vm.DuzenCari = "Yeni";
        vm.DuzenTutar = 10m;
        vm.DuzenKanal = "MEZAT";
        vm.SecTipCommand.Execute(new SecimCipi("Kredi kartı"));
        api.SonIslemOlustur = null;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(KartIletisi, vm.Hatalar[nameof(IslemlerViewModel.DuzenKrediKartiId)]);
        Assert.Null(api.SonIslemOlustur);
    }
}
