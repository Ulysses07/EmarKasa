using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Panel → "Girilmesi bekleyen giderler" kartı.</summary>
public class BekleyenGiderPanelTests
{
    private static readonly DateTime Bugun = new(2026, 3, 10);
    private static readonly DateOnly Subat = new(2026, 2, 1);
    private static readonly DateOnly Mart = new(2026, 3, 1);

    private static PanelDto PanelOrnek(decimal kasa = 1000m) => new(kasa, new List<KanalBakiyeDto>(), 0m, 0m);

    private static (SahteApi api, PanelViewModel vm) Kur(bool editor = true)
    {
        var api = new SahteApi
        {
            Panel = PanelOrnek(),
            BekleyenListe = new List<BekleyenGiderDto>
            {
                new(3, "Kira", "MEZAT", 25000m, Subat, new DateOnly(2026, 2, 28)),
                new(4, "SGK", "Ortak", 8000m, Mart, new DateOnly(2026, 3, 5)),
            },
        };
        var vm = new PanelViewModel(api, new SabitSaat(Bugun.AddHours(10))) { EditorMu = editor };
        return (api, vm);
    }

    [Fact]
    public async Task Editor_bekleyenleri_gorur_satir_bilgileri_dogru()
    {
        var (api, vm) = Kur();

        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.True(vm.BekleyenVar);
        Assert.Equal(2, vm.BekleyenGiderler.Count);
        var kira = vm.BekleyenGiderler[0];
        Assert.Equal("Kira", kira.Kalem);
        Assert.Equal(25000m, kira.Tutar);                      // varsayılan: kayıttaki tutar
        Assert.Equal("MEZAT · Vade 28 Şubat 2026", kira.Aciklama);
        Assert.Equal(1000m, vm.GuncelKasa);
        Assert.Equal(1, api.BekleyenCagri);
    }

    [Fact]
    public async Task Izleyici_icin_bekleyen_istenmez_kart_gizli()
    {
        var (api, vm) = Kur(editor: false);

        await vm.YukleAsync();

        Assert.Equal(0, api.BekleyenCagri);
        Assert.Empty(vm.BekleyenGiderler);
        Assert.False(vm.BekleyenVar);
        Assert.Equal(1000m, vm.GuncelKasa);
    }

    [Fact]
    public async Task Liste_bossa_kart_gizli()
    {
        var (api, vm) = Kur();
        api.BekleyenListe = new List<BekleyenGiderDto>();

        await vm.YukleAsync();

        Assert.False(vm.BekleyenVar);
    }

    [Fact]
    public async Task BekleyenVar_degisikligi_bildirilir()
    {
        var (_, vm) = Kur();
        var degisen = new List<string?>();
        vm.PropertyChanged += (_, e) => degisen.Add(e.PropertyName);

        await vm.YukleAsync();

        Assert.Contains(nameof(PanelViewModel.BekleyenVar), degisen);
    }

    [Fact]
    public async Task Kaydet_vade_tarihiyle_ve_duzenlenen_tutarla_onaylar_satir_duser_panel_tazelenir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        var kira = vm.BekleyenGiderler[0];
        kira.Tutar = 26500m;
        api.Panel = PanelOrnek(-25500m);

        await vm.BekleyenKaydetCommand.ExecuteAsync(kira);

        Assert.Null(vm.Hata);
        var (id, g) = api.SonOnay!.Value;
        Assert.Equal(3, id);
        Assert.Equal(Subat, g.Ay);
        Assert.Equal(new DateOnly(2026, 2, 28), g.Tarih);
        Assert.Equal(26500m, g.Tutar);
        Assert.DoesNotContain(vm.BekleyenGiderler, b => b.TekrarlayanGiderId == 3);
        Assert.Single(vm.BekleyenGiderler);
        Assert.Equal(-25500m, vm.GuncelKasa);                 // kasa özeti yeniden yüklendi
    }

    [Fact]
    public async Task Kaydet_ileri_tarih_gondermez()
    {
        var (api, vm) = Kur();
        // Sunucu vadesi gelmemiş ay döndürmez; yine de istemci ileri tarihle işlem girmemeli.
        api.BekleyenListe = new List<BekleyenGiderDto> { new(9, "Kira", "MEZAT", 100m, Mart, new DateOnly(2026, 3, 31)) };
        await vm.YukleAsync();

        await vm.BekleyenKaydetCommand.ExecuteAsync(vm.BekleyenGiderler[0]);

        Assert.Equal(DateOnly.FromDateTime(Bugun), api.SonOnay!.Value.G.Tarih);
    }

    [Fact]
    public async Task Kaydet_sifir_tutarla_durur()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        vm.BekleyenGiderler[0].Tutar = 0m;

        await vm.BekleyenKaydetCommand.ExecuteAsync(vm.BekleyenGiderler[0]);

        Assert.Equal("Tutar sıfırdan büyük olmalı.", vm.Hata);
        Assert.Null(api.SonOnay);
        Assert.Equal(2, vm.BekleyenGiderler.Count);
    }

    [Fact]
    public async Task Bu_ay_atla_ayi_gonderir_satir_duser()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();

        await vm.BekleyenAtlaCommand.ExecuteAsync(vm.BekleyenGiderler[1]);

        Assert.Null(vm.Hata);
        Assert.Equal((4, Mart), api.SonAtla!.Value);
        Assert.Null(api.SonOnay);
        Assert.Single(vm.BekleyenGiderler);
        Assert.Equal("Kira", vm.BekleyenGiderler[0].Kalem);

        await vm.BekleyenAtlaCommand.ExecuteAsync(vm.BekleyenGiderler[0]);
        Assert.False(vm.BekleyenVar);
    }

    [Fact]
    public async Task Baska_cihazda_karar_verilmisse_mesaj_gosterilir_liste_tazelenir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        // Başka cihaz şubat kirasını girdi: sunucu listesi değişti ve onay 409 döner.
        api.BekleyenListe = api.BekleyenListe.Where(b => b.TekrarlayanGiderId != 3).ToList();
        api.TekrarlayanYazHatasi = new KasaApiException(HttpStatusCode.Conflict, "Bu gider şubat 2026 için zaten girildi.");

        await vm.BekleyenKaydetCommand.ExecuteAsync(vm.BekleyenGiderler[0]);

        Assert.Equal("Bu gider şubat 2026 için zaten girildi.", vm.Hata);
        Assert.Single(vm.BekleyenGiderler);
        Assert.Equal("SGK", vm.BekleyenGiderler[0].Kalem);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Yeniden_yuklemede_duzenlenen_tutar_korunur()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        vm.BekleyenGiderler[0].Tutar = 30000m;               // kullanıcı değiştirdi
        // Ayarlar'da SGK tutarı değişti (kullanıcı SGK satırına dokunmadı).
        api.BekleyenListe = api.BekleyenListe.Select(b => b.TekrarlayanGiderId == 4 ? b with { Tutar = 8500m } : b).ToList();

        await vm.YukleAsync();

        Assert.Equal(30000m, vm.BekleyenGiderler[0].Tutar);
        Assert.Equal(8500m, vm.BekleyenGiderler[1].Tutar);
    }

    [Fact]
    public async Task Eski_sunucu_404_donerse_panel_yuklenir_kart_gizli()
    {
        var (api, vm) = Kur();
        api.BekleyenHatasi = new KasaApiException(HttpStatusCode.NotFound);

        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.Equal(1000m, vm.GuncelKasa);
        Assert.False(vm.BekleyenVar);
    }

    [Fact]
    public async Task Bekleyen_okumasi_basarisizsa_hata_gosterilir()
    {
        var (api, vm) = Kur();
        api.BekleyenHatasi = new KasaApiException(HttpStatusCode.ServiceUnavailable);

        await vm.YukleAsync();

        Assert.Equal(HataMesaji.Ulasilamadi, vm.Hata);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Rol_izleyiciye_donerse_liste_temizlenir()
    {
        var (_, vm) = Kur();
        await vm.YukleAsync();
        Assert.True(vm.BekleyenVar);

        vm.EditorMu = false;
        Assert.False(vm.BekleyenVar);
        await vm.YukleAsync();

        Assert.Empty(vm.BekleyenGiderler);
    }
}

/// <summary>Ayarlar → "Tekrarlayan giderler" bölümü.</summary>
public class TekrarlayanGiderAyarlarTests
{
    private static (SahteApi api, AyarlarViewModel vm) Kur()
    {
        var api = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false),
            KanallarListe = new List<KanalDto>
            {
                new(1, "TOPTAN", true, 2, 0m), new(2, "MEZAT", true, 1, 0m), new(3, "Eski", false, 3, 0m),
            },
            GiderKalemleriListe = new List<GiderKalemiDto>
            {
                new(801, "Kira", true), new(802, "SGK", true), new(803, "Eski kalem", false),
            },
            TekrarlayanListe = new List<TekrarlayanGiderDto>
            {
                new(3, "Kira", "MEZAT", 25000m, 31, true, new DateOnly(2026, 1, 1)),
                new(4, "Eski kalem", "Eski", 500m, 5, false, new DateOnly(2026, 2, 1)),
            },
        };
        return (api, new AyarlarViewModel(api, new SabitSaat(new DateTime(2026, 3, 10, 10, 0, 0))));
    }

    [Fact]
    public async Task Yukle_liste_ve_cipleri_kurar()
    {
        var (_, vm) = Kur();

        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.Equal(2, vm.TekrarlayanGiderler.Count);
        Assert.Equal("MEZAT · ayın 31. günü (kısa ayda son gün) · 25.000,00", vm.TekrarlayanGiderler[0].Aciklama);
        Assert.False(vm.TekrarlayanGiderler[1].Aktif);
        // Pasif kalem/kanal önerilmez; kanallar sıraya göre, sonda Ortak.
        Assert.Equal(new[] { "Kira", "SGK" }, vm.TekrarKalemCipleri.Select(c => c.Ad));
        Assert.Equal(new[] { "MEZAT", "TOPTAN", "Ortak" }, vm.TekrarKanalCipleri.Select(c => c.Ad));
        Assert.False(vm.TekrarKalemYok);
    }

    [Fact]
    public async Task Kalem_yoksa_ipucu_gosterilir()
    {
        var (api, vm) = Kur();
        api.GiderKalemleriListe = new List<GiderKalemiDto>();

        await vm.YukleAsync();

        Assert.True(vm.TekrarKalemYok);
    }

    [Fact]
    public async Task Yeni_kayit_cip_secimiyle_olusturulur_form_sifirlanir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();

        vm.SecTekrarKalemCommand.Execute(vm.TekrarKalemCipleri.Single(c => c.Ad == "SGK"));
        vm.SecTekrarKanalCommand.Execute(vm.TekrarKanalCipleri.Single(c => c.Ad == "Ortak"));
        Assert.True(vm.TekrarKalemCipleri.Single(c => c.Ad == "SGK").Secili);
        Assert.False(vm.TekrarKalemCipleri.Single(c => c.Ad == "Kira").Secili);
        Assert.True(vm.TekrarKanalCipleri.Single(c => c.Ad == "Ortak").Secili);
        vm.DuzenTekrarTutar = 8000m;
        vm.DuzenTekrarGun = 15;

        await vm.TekrarKaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        var g = api.SonTekrarOlustur!;
        Assert.Equal(("SGK", "Ortak", 8000m, 15, true), (g.Kalem, g.Kanal, g.Tutar, g.AyinGunu, g.Aktif));
        Assert.Null(g.BaslangicAyi);                        // başlangıç ayını sunucu koyar (bu ay)
        Assert.Null(api.SonTekrarGuncelle);
        Assert.Equal("", vm.DuzenTekrarKalem);
        Assert.Equal(0m, vm.DuzenTekrarTutar);
        Assert.All(vm.TekrarKalemCipleri, c => Assert.False(c.Secili));
        Assert.Equal(3, vm.TekrarlayanGiderler.Count);       // liste yeniden yüklendi
    }

    [Theory]
    [InlineData("", "MEZAT", 1, 5, AyarlarViewModel.KalemSecinMesaji)]
    [InlineData("Kira", "", 1, 5, AyarlarViewModel.KanalSecinMesaji)]
    [InlineData("Kira", "MEZAT", 0, 5, AyarlarViewModel.TutarMesaji)]
    [InlineData("Kira", "MEZAT", 1, 0, AyarlarViewModel.GunMesaji)]
    [InlineData("Kira", "MEZAT", 1, 32, AyarlarViewModel.GunMesaji)]
    public async Task Eksik_ya_da_gecersiz_alan_kaydetmez(string kalem, string kanal, int tutar, int gun, string mesaj)
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        vm.DuzenTekrarKalem = kalem; vm.DuzenTekrarKanal = kanal;
        vm.DuzenTekrarTutar = tutar; vm.DuzenTekrarGun = gun;

        await vm.TekrarKaydetCommand.ExecuteAsync(null);

        Assert.Equal(mesaj, vm.Hata);
        Assert.Null(api.SonTekrarOlustur);
    }

    [Fact]
    public async Task Duzenle_formu_doldurur_guncelle_put_gonderir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();

        vm.TekrarDuzenle(vm.TekrarlayanGiderler[0]);
        Assert.Equal(3, vm.DuzenTekrarId);
        Assert.Equal(31, vm.DuzenTekrarGun);
        Assert.True(vm.TekrarKalemCipleri.Single(c => c.Ad == "Kira").Secili);
        Assert.True(vm.TekrarKanalCipleri.Single(c => c.Ad == "MEZAT").Secili);
        vm.DuzenTekrarTutar = 27000m;
        vm.DuzenTekrarAktif = false;

        await vm.TekrarKaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        var (id, g) = api.SonTekrarGuncelle!.Value;
        Assert.Equal(3, id);
        Assert.Equal(27000m, g.Tutar);
        Assert.False(g.Aktif);
        Assert.Null(g.BaslangicAyi);                        // güncellemede başlangıç ayı korunur
        Assert.Null(api.SonTekrarOlustur);
        Assert.Equal(0, vm.DuzenTekrarId);
    }

    [Fact]
    public async Task Pasif_kalem_ve_kanalli_kayit_duzenlenirken_cipleri_gorunur_yeni_denince_kalkar()
    {
        var (_, vm) = Kur();
        await vm.YukleAsync();

        vm.TekrarDuzenle(vm.TekrarlayanGiderler[1]);

        Assert.True(vm.TekrarKalemCipleri.Single(c => c.Ad == "Eski kalem").Secili);
        Assert.True(vm.TekrarKanalCipleri.Single(c => c.Ad == "Eski").Secili);

        vm.YeniTekrarCommand.Execute(null);

        Assert.DoesNotContain(vm.TekrarKalemCipleri, c => c.Ad == "Eski kalem");
        Assert.DoesNotContain(vm.TekrarKanalCipleri, c => c.Ad == "Eski");
    }

    [Fact]
    public async Task Sil_cagirir_duzenlenen_kayitsa_form_sifirlanir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        vm.TekrarDuzenle(vm.TekrarlayanGiderler[0]);

        await vm.TekrarSilCommand.ExecuteAsync(vm.TekrarlayanGiderler[0]);

        Assert.Equal(3, api.SonTekrarSil);
        Assert.Equal(0, vm.DuzenTekrarId);
        Assert.Single(vm.TekrarlayanGiderler);
    }

    [Fact]
    public async Task Sunucu_reddederse_mesaj_gosterilir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        api.TekrarlayanYazHatasi = new KasaApiException(HttpStatusCode.BadRequest, "'Elektrik' adında bir sabit gider kalemi yok. Önce kalemi ekleyin.");
        vm.DuzenTekrarKalem = "Elektrik"; vm.DuzenTekrarKanal = "MEZAT"; vm.DuzenTekrarTutar = 5m;

        await vm.TekrarKaydetCommand.ExecuteAsync(null);

        Assert.Equal("'Elektrik' adında bir sabit gider kalemi yok. Önce kalemi ekleyin.", vm.Hata);
        Assert.Equal("Elektrik", vm.DuzenTekrarKalem);       // form korunur
    }

    [Fact]
    public async Task Kalem_kaydedilince_tekrarlayan_liste_de_tazelenir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        Assert.Equal(1, api.TekrarlayanCagri);

        vm.KalemDuzenle(new GiderKalemiDto(801, "Kira", true));
        vm.DuzenKalemAd = "Dükkan kirası";
        await vm.KalemKaydetCommand.ExecuteAsync(null);

        Assert.Equal(2, api.TekrarlayanCagri);               // sunucu yeniden adlandırmayı kayıtlara taşır
    }

    [Fact]
    public async Task Eski_sunucu_404_donerse_ayarlar_yine_yuklenir()
    {
        var (api, vm) = Kur();
        api.TekrarlayanOkumaHatasi = new KasaApiException(HttpStatusCode.NotFound);

        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.Equal(3, vm.Kanallar.Count);
        Assert.Empty(vm.TekrarlayanGiderler);
    }

    [Theory]
    [InlineData(1, "ayın 1. günü")]
    [InlineData(28, "ayın 28. günü")]
    [InlineData(29, "ayın 29. günü (kısa ayda son gün)")]
    public void Gun_metni(int gun, string beklenen) => Assert.Equal(beklenen, TekrarlayanGiderSatiri.GunMetni(gun));
}
