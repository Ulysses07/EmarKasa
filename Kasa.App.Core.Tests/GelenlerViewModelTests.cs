using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket C · 29: gelen tablosu (hafta × kanal), korumalı kayıt ve eksik gelen listesi.</summary>
public class GelenlerViewModelTests
{
    private static readonly DateOnly Hafta = new(2026, 9, 21);
    private static readonly DateOnly OncekiHafta = new(2026, 9, 14);

    private static GelenTablosuDto Tablo(DateOnly bas) => bas == OncekiHafta
        ? new GelenTablosuDto(OncekiHafta, new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 7), Hafta,
            [new GelenHucreDto("MEZAT", true, null, null), new GelenHucreDto("TOPTAN", true, 300m, 5)])
        : new GelenTablosuDto(Hafta, new DateOnly(2026, 9, 27), OncekiHafta, null,
            [
                new GelenHucreDto("MEZAT", true, 1_000m, 1),
                new GelenHucreDto("TOPTAN", true, null, null),
                new GelenHucreDto("ESKİ", false, 50m, 2),
            ]);

    private static (SahteApi api, GelenlerViewModel vm) Kur(Action<SahteApi>? ayar = null)
    {
        var api = new SahteApi
        {
            GelenTablosuUret = d => Tablo(d ?? Hafta),
            EksikGelenler = new EksikGelenSayfasi([new EksikGelenSatiriDto(OncekiHafta, new DateOnly(2026, 9, 20), "MEZAT")], 1),
        };
        api.GelenDeposu[(Hafta, "MEZAT")] = 1_000m;
        api.GelenDeposu[(Hafta, "ESKİ")] = 50m;
        ayar?.Invoke(api);
        return (api, new GelenlerViewModel(api, new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0))) { EditorMu = true });
    }

    private static GelenSatiri Satir(GelenlerViewModel vm, string kanal) => vm.Satirlar.Single(s => s.Kanal == kanal);

    [Fact]
    public async Task Yukleme_bu_haftayi_ve_eksikleri_getirir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.Equal([(DateOnly?)null], api.GelenTablosuCagrilari);
        Assert.Equal(Hafta, vm.DonemStart);
        Assert.Equal("21 Eyl – 27 Eyl 2026", vm.DonemMetni);
        Assert.Equal(["MEZAT", "TOPTAN", "ESKİ"], vm.Satirlar.Select(s => s.Kanal));
        Assert.Equal(("1.000,00 ₺", "—"), (Satir(vm, "MEZAT").KayitliMetni, Satir(vm, "TOPTAN").KayitliMetni));
        Assert.False(Satir(vm, "ESKİ").Aktif);
        Assert.Equal(1_050m, vm.Toplam);
        Assert.True(vm.OncekiVar);
        Assert.False(vm.SonrakiVar);

        var e = Assert.Single(vm.Eksikler);
        Assert.Equal("MEZAT geleni girilmedi · 14 Eyl – 20 Eyl 2026", e.Metin);
        Assert.Equal("1 eksik gelen", vm.EksikOzeti);
    }

    [Theory]
    [InlineData(0, 0, "Geçmiş haftalarda eksik gelen yok.")]
    [InlineData(2, 2, "2 eksik gelen")]
    [InlineData(2, 250, "250 eksik gelen (en yeni 2 gösteriliyor)")]
    public async Task Eksik_ozeti(int gosterilen, int toplam, string beklenen)
    {
        var (_, vm) = Kur(a => a.EksikGelenler = new EksikGelenSayfasi(
            Enumerable.Range(0, gosterilen).Select(i => new EksikGelenSatiriDto(OncekiHafta.AddDays(-7 * i), OncekiHafta.AddDays(-7 * i + 6), "MEZAT")).ToList(),
            toplam));
        await vm.YukleAsync();
        Assert.Equal(beklenen, vm.EksikOzeti);
        Assert.Equal(gosterilen, vm.Eksikler.Count);
    }

    [Fact]
    public async Task Hafta_gezinme_ve_eksige_gitme()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();

        await vm.SonrakiCommand.ExecuteAsync(null);                  // sonraki yok: istek atılmaz
        Assert.Single(api.GelenTablosuCagrilari);

        await vm.OncekiCommand.ExecuteAsync(null);
        Assert.Equal(OncekiHafta, api.GelenTablosuCagrilari.Last());
        Assert.Equal(OncekiHafta, vm.DonemStart);
        Assert.Equal(["MEZAT", "TOPTAN"], vm.Satirlar.Select(s => s.Kanal));
        Assert.Equal(300m, vm.Toplam);
        Assert.True(vm.SonrakiVar);

        await vm.BuHaftaCommand.ExecuteAsync(null);
        Assert.Null(api.GelenTablosuCagrilari.Last());
        Assert.Equal(Hafta, vm.DonemStart);

        await vm.EksigeGitCommand.ExecuteAsync(vm.Eksikler[0]);
        Assert.Equal(OncekiHafta, api.GelenTablosuCagrilari.Last());
        Assert.Equal(OncekiHafta, vm.DonemStart);
    }

    [Fact]
    public async Task Yukleme_hatasi_mesaj_olur()
    {
        var (_, vm) = Kur(a => a.YuklemeHatasi = new HttpRequestException("yok"));
        await vm.YukleAsync();
        Assert.NotNull(vm.Hata);
        Assert.Empty(vm.Satirlar);
    }

    [Fact]
    public async Task Yalniz_degisen_satir_korumali_gonderilir_eksikler_tazelenir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        Satir(vm, "TOPTAN").GirisMetni = "2.500,50";
        Assert.False(Satir(vm, "TOPTAN").ModGerekli);                // kayıt yok: seçim gerekmez
        Assert.Equal("→ 2.500,50 ₺", Satir(vm, "TOPTAN").Onizleme);

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        var (g, beklenen) = Assert.Single(api.KorumaliGelenCagrilari);
        Assert.Equal((new GelenYaz(Hafta, "TOPTAN", 2_500.50m), 0m), (g, beklenen));
        var t = Satir(vm, "TOPTAN");
        Assert.Equal((2_500.50m, "", "Kaydedildi"), (t.Kayitli!.Value, t.GirisMetni, t.Durum));
        Assert.Equal(3_550.50m, vm.Toplam);
        Assert.Equal("1 kanal kaydedildi.", vm.Sonuc);
        Assert.Equal(2, api.EksikGelenListesiCagri);

        t.GirisMetni = "1";                                          // yeni giriş durumu siler
        Assert.Null(t.Durum);
    }

    [Fact]
    public async Task Kayitli_satirda_uzerine_yaz_ya_da_ustune_ekle_secilmeli()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        var m = Satir(vm, "MEZAT");
        m.GirisMetni = "500";
        Assert.True(m.ModGerekli);
        Assert.Null(m.YeniTutar);
        Assert.Null(m.Onizleme);

        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(GelenlerViewModel.ModSecMesaji("MEZAT"), vm.Hata);
        Assert.Empty(api.KorumaliGelenCagrilari);

        vm.SecUzerineYazCommand.Execute(m);
        Assert.True(m.UzerineYazSecili);
        Assert.Equal(500m, m.YeniTutar);
        vm.SecUstuneEkleCommand.Execute(m);
        Assert.True(m.UstuneEkleSecili);
        Assert.False(m.UzerineYazSecili);
        Assert.Equal("→ 1.500,00 ₺", m.Onizleme);

        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal((1_500m, 1_000m), (api.KorumaliGelenCagrilari.Single().G.TutarTl, api.KorumaliGelenCagrilari.Single().Beklenen));
        Assert.Equal((1_500m, GelenYazmaModu.Secilmedi), (m.Kayitli!.Value, m.Mod));
        Assert.Equal(1_500m, api.GelenDeposu[(Hafta, "MEZAT")]);
    }

    [Fact]
    public async Task Degisiklik_yoksa_ya_da_ayni_tutar_yazilirsa_istek_atilmaz()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(GelenlerViewModel.DegisiklikYokMesaji, vm.Hata);

        var m = Satir(vm, "MEZAT");
        m.GirisMetni = "1.000";
        m.Mod = GelenYazmaModu.UzerineYaz;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(GelenlerViewModel.DegisiklikYokMesaji, vm.Hata);
        Assert.Equal("", m.GirisMetni);                              // aynı tutar: giriş temizlenir
        Assert.Empty(api.KorumaliGelenCagrilari);
    }

    [Fact]
    public async Task Gecersiz_giriste_kaydedilmez_sifir_girilebilir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        var t = Satir(vm, "TOPTAN");
        t.GirisMetni = "abc";
        Assert.Equal(ParaGiris.HataGecersiz, t.GirisHatasi);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal($"TOPTAN: {ParaGiris.HataGecersiz}", vm.Hata);
        Assert.Empty(api.KorumaliGelenCagrilari);

        t.GirisMetni = "0";
        Assert.Null(t.GirisHatasi);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal(0m, api.KorumaliGelenCagrilari.Single().G.TutarTl);
        Assert.Equal("0,00 ₺", t.KayitliMetni);
    }

    [Fact]
    public async Task Bu_arada_degisen_satir_cakisir_guncel_tutarla_yeniden_sorar_digerleri_kaydedilir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        api.GelenDeposu[(Hafta, "MEZAT")] = 1_200m;                  // başka biri bu arada değiştirdi
        var m = Satir(vm, "MEZAT");
        m.GirisMetni = "500";
        m.Mod = GelenYazmaModu.UstuneEkle;
        Satir(vm, "TOPTAN").GirisMetni = "100";

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal("1 kanal kaydedildi. 1 kanalda çakışma var: seçim yapıp tekrar kaydedin.", vm.Sonuc);
        Assert.True(m.Cakisma);
        Assert.Equal((1_200m, GelenYazmaModu.Secilmedi, "500"), (m.Kayitli!.Value, m.Mod, m.GirisMetni));
        Assert.Contains("1.200,00 ₺", m.Durum);
        Assert.Equal(1_200m, api.GelenDeposu[(Hafta, "MEZAT")]);     // ezilmedi
        Assert.Equal(100m, api.GelenDeposu[(Hafta, "TOPTAN")]);

        m.Mod = GelenYazmaModu.UstuneEkle;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(1_700m, api.GelenDeposu[(Hafta, "MEZAT")]);
        Assert.False(m.Cakisma);
        Assert.Equal("1 kanal kaydedildi.", vm.Sonuc);
    }

    [Fact]
    public async Task Yalniz_cakisma_olursa_eksikler_tazelenmez()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        api.GelenDeposu[(Hafta, "MEZAT")] = 900m;
        var m = Satir(vm, "MEZAT");
        m.GirisMetni = "10";
        m.Mod = GelenYazmaModu.UzerineYaz;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal("1 kanalda çakışma var: seçim yapıp tekrar kaydedin.", vm.Sonuc);
        Assert.Equal(1, api.EksikGelenListesiCagri);
    }

    // ---- Kaydedilmemiş girişler ve "O haftaya git" ----

    [Fact]
    public async Task Kaydedilmemis_giris_varken_hafta_degisimi_sorulur_vazgec_girisi_korur()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        Satir(vm, "TOPTAN").GirisMetni = "250";

        await vm.OncekiCommand.ExecuteAsync(null);
        Assert.Single(api.GelenTablosuCagrilari);                    // soru açık: yüklenmedi
        Assert.True(vm.GecisSorusuVar);
        Assert.Equal(GelenlerViewModel.GecisSorusuMetni(["TOPTAN"]), vm.GecisSorusu);

        vm.GecisVazgecCommand.Execute(null);
        Assert.False(vm.GecisSorusuVar);
        Assert.Equal(Hafta, vm.DonemStart);
        Assert.Equal("250", Satir(vm, "TOPTAN").GirisMetni);

        await vm.KaydetmedenGecCommand.ExecuteAsync(null);           // bekleyen geçiş yok: bir şey olmaz
        Assert.Single(api.GelenTablosuCagrilari);

        await vm.BuHaftaCommand.ExecuteAsync(null);
        Assert.True(vm.GecisSorusuVar);
        await vm.KaydetCommand.ExecuteAsync(null);                   // kaydetmeyi seçti: soru kapanır
        Assert.False(vm.GecisSorusuVar);
        Assert.Equal(250m, api.GelenDeposu[(Hafta, "TOPTAN")]);

        await vm.OncekiCommand.ExecuteAsync(null);                   // girişler kaydedildi: sormadan geçer
        Assert.False(vm.GecisSorusuVar);
        Assert.Equal(OncekiHafta, vm.DonemStart);

        Satir(vm, "MEZAT").GirisMetni = "10";
        await vm.SonrakiCommand.ExecuteAsync(null);
        Assert.Equal(OncekiHafta, vm.DonemStart);
        await vm.KaydetmedenGecCommand.ExecuteAsync(null);
        Assert.False(vm.GecisSorusuVar);
        Assert.Equal(Hafta, vm.DonemStart);
        Assert.Equal(Hafta, api.GelenTablosuCagrilari.Last());
        Assert.All(vm.Satirlar, s => Assert.False(s.GirisVar));
        Assert.DoesNotContain(api.KorumaliGelenCagrilari, c => c.G.DonemStart == OncekiHafta);
    }

    [Fact]
    public async Task Eksige_gitmek_sayfayi_yukari_kaydirir_soru_acilinca_da_kaydirir()
    {
        var (api, vm) = Kur();
        var kaydirma = 0;
        vm.YukariKaydirIstendi += (_, _) => kaydirma++;
        await vm.YukleAsync();
        await vm.OncekiCommand.ExecuteAsync(null);
        Assert.Equal(0, kaydirma);                                   // üstteki düğmeler kaydırmaz

        await vm.BuHaftaCommand.ExecuteAsync(null);
        await vm.EksigeGitCommand.ExecuteAsync(vm.Eksikler[0]);
        Assert.Equal(OncekiHafta, vm.DonemStart);
        Assert.Equal(1, kaydirma);

        // Aynı haftadayken: yeniden yüklemez, girişler kalır, yalnız yukarı kaydırır.
        Satir(vm, "MEZAT").GirisMetni = "75";
        var cagri = api.GelenTablosuCagrilari.Count;
        await vm.EksigeGitCommand.ExecuteAsync(vm.Eksikler[0]);
        Assert.Equal(cagri, api.GelenTablosuCagrilari.Count);
        Assert.False(vm.GecisSorusuVar);
        Assert.Equal("75", Satir(vm, "MEZAT").GirisMetni);
        Assert.Equal(2, kaydirma);

        // Başka haftadayken girişle: soru açılır ve görünsün diye yukarı kaydırır; onayla geçer.
        await vm.SonrakiCommand.ExecuteAsync(null);
        await vm.KaydetmedenGecCommand.ExecuteAsync(null);
        Assert.Equal(Hafta, vm.DonemStart);
        Satir(vm, "TOPTAN").GirisMetni = "5";
        await vm.EksigeGitCommand.ExecuteAsync(vm.Eksikler[0]);
        Assert.True(vm.GecisSorusuVar);
        Assert.Equal(Hafta, vm.DonemStart);
        Assert.Equal(3, kaydirma);
        await vm.KaydetmedenGecCommand.ExecuteAsync(null);
        Assert.Equal(OncekiHafta, vm.DonemStart);
        Assert.Equal(4, kaydirma);
    }

    [Fact]
    public async Task Sayfaya_donunce_ayni_hafta_yuklenir_girisler_korunur_kayitli_degistiyse_secim_sifirlanir()
    {
        var mezat = 1_000m;
        var (api, vm) = Kur(a => a.GelenTablosuUret = d => (d ?? Hafta) == Hafta
            ? new GelenTablosuDto(Hafta, new DateOnly(2026, 9, 27), OncekiHafta, null,
                [new GelenHucreDto("MEZAT", true, mezat, 1), new GelenHucreDto("TOPTAN", true, null, null),
                 new GelenHucreDto("PERAKENDE", true, 40m, 3)])
            : Tablo(d!.Value));
        await vm.YukleAsync();
        Satir(vm, "TOPTAN").GirisMetni = "250";
        var m = Satir(vm, "MEZAT");
        m.GirisMetni = "500";
        m.Mod = GelenYazmaModu.UstuneEkle;
        var p = Satir(vm, "PERAKENDE");
        p.GirisMetni = "60";
        p.Mod = GelenYazmaModu.UzerineYaz;

        mezat = 1_200m;                                              // başka yerden (İşlemler) değişti
        await vm.YukleAsync();                                       // sayfaya geri dönüş (OnAppearing)

        Assert.Equal([null, Hafta], api.GelenTablosuCagrilari);
        Assert.Equal("250", Satir(vm, "TOPTAN").GirisMetni);
        Assert.Equal(250m, Satir(vm, "TOPTAN").YeniTutar);
        var p2 = Satir(vm, "PERAKENDE");
        Assert.Equal(("60", GelenYazmaModu.UzerineYaz, false), (p2.GirisMetni, p2.Mod, p2.Cakisma));
        var m2 = Satir(vm, "MEZAT");
        Assert.Equal(("500", GelenYazmaModu.Secilmedi, true), (m2.GirisMetni, m2.Mod, m2.Cakisma));
        Assert.Equal(GelenSatiri.KayitliDegistiMesaji(1_000m, 1_200m), m2.Durum);
        Assert.Null(m2.YeniTutar);                                   // yeniden seçilmeden kaydedilmez
        Assert.Equal(1_240m, vm.Toplam);

        await vm.OncekiCommand.ExecuteAsync(null);                   // korunan girişler de soruyu açar
        Assert.Equal(GelenlerViewModel.GecisSorusuMetni(["MEZAT", "TOPTAN", "PERAKENDE"]), vm.GecisSorusu);
    }
}
