using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket D — Panel: onayda tarih/kanal/not, atlamayı geri alma (33).</summary>
public class PaketDBekleyenTests
{
    private static readonly DateTime Bugun = new(2026, 3, 10);
    private static readonly DateOnly Subat = new(2026, 2, 1);
    private static readonly DateOnly Mart = new(2026, 3, 1);

    private static (SahteApi api, PanelViewModel vm) Kur(bool editor = true)
    {
        var api = new SahteApi
        {
            Panel = new PanelDto(1000m, new List<KanalBakiyeDto> { new("MEZAT", 0m), new("TOPTAN", 0m) }, 0m, 0m),
            BekleyenListe = new List<BekleyenGiderDto>
            {
                new(3, "Kira", "MEZAT", 25000m, Subat, new DateOnly(2026, 2, 28)),
                new(4, "KDV", "Ortak", 0m, Mart, new DateOnly(2026, 3, 28), TutarDegisken: true, Siklik: TekrarSikligi.Aylik),
                new(5, "Yazılım", "TOPTAN", 900m, Mart, new DateOnly(2026, 3, 5), KrediKartiId: 7, Siklik: TekrarSikligi.Yillik),
            },
            AtlananListe = new List<TekrarlayanAtlananDto> { new(6, "Aidat", "MEZAT", Mart, new DateOnly(2026, 3, 3)) },
        };
        return (api, new PanelViewModel(api, new SabitSaat(Bugun.AddHours(10))) { EditorMu = editor });
    }

    [Fact]
    public async Task Varsayilan_onay_eski_istekle_birebir_ayni()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        var kira = vm.BekleyenGiderler[0];
        Assert.Equal(new DateTime(2026, 2, 28), kira.TarihGiris);
        Assert.Equal("MEZAT", kira.KanalGiris);

        await vm.BekleyenKaydetCommand.ExecuteAsync(kira);

        Assert.Equal((3, new TekrarlayanOnayYaz(Subat, new DateOnly(2026, 2, 28), 25000m)), api.SonOnay);
    }

    [Fact]
    public async Task Onayda_tarih_kanal_ve_not_degistirilebilir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        var kira = vm.BekleyenGiderler[0];

        kira.TarihGiris = new DateTime(2026, 3, 2);
        kira.KanalGiris = "TOPTAN";
        kira.NotGiris = "  Şubat kirası geç ödendi ";
        await vm.BekleyenKaydetCommand.ExecuteAsync(kira);

        Assert.Equal((3, new TekrarlayanOnayYaz(Subat, new DateOnly(2026, 3, 2), 25000m, "TOPTAN", "Şubat kirası geç ödendi")), api.SonOnay);
    }

    [Fact]
    public async Task Ileri_tarih_bugune_cekilir_degisken_tutar_bos_gecilemez()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        var kdv = vm.BekleyenGiderler.Single(b => b.Kalem == "KDV");
        Assert.True(kdv.TutarDegisken);
        Assert.Equal("tutar her seferinde girilir", kdv.EkBilgi);

        await vm.BekleyenKaydetCommand.ExecuteAsync(kdv);
        Assert.Equal("Tutar sıfırdan büyük olmalı.", vm.Hata);
        Assert.Null(api.SonOnay);

        kdv.Tutar = 12_345m;
        await vm.BekleyenKaydetCommand.ExecuteAsync(kdv);
        Assert.Equal(new DateOnly(2026, 3, 10), api.SonOnay!.Value.G.Tarih);   // vade 28 Mart → bugün
        Assert.Null(api.SonOnay!.Value.G.Kanal);
    }

    [Fact]
    public async Task Kanal_secenekleri_panel_kanallari_ve_ortak_karta_bagli_bilgi()
    {
        var (_, vm) = Kur();
        await vm.YukleAsync();

        Assert.Equal(["MEZAT", "TOPTAN", "Ortak"], vm.BekleyenKanallar);
        var yaz = vm.BekleyenGiderler.Single(b => b.Kalem == "Yazılım");
        Assert.Equal("Yılda bir · karta bağlı: kart harcaması olarak girilir", yaz.EkBilgi);
        Assert.True(yaz.EkBilgiVar);
        Assert.False(vm.BekleyenGiderler[0].EkBilgiVar);
    }

    [Fact]
    public async Task Yeniden_yuklemede_degistirilen_tarih_kanal_not_korunur()
    {
        var (_, vm) = Kur();
        await vm.YukleAsync();
        vm.BekleyenGiderler[0].KanalGiris = "TOPTAN";
        vm.BekleyenGiderler[0].NotGiris = "not";
        vm.BekleyenGiderler[0].TarihGiris = new DateTime(2026, 3, 1);

        await vm.YukleAsync();

        var k = vm.BekleyenGiderler[0];
        Assert.Equal(("TOPTAN", "not", new DateTime(2026, 3, 1)), (k.KanalGiris, k.NotGiris, k.TarihGiris));
    }

    [Fact]
    public async Task Atlananlar_editore_gelir_geri_al_ayi_bekleyene_dondurur()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();

        Assert.True(vm.AtlananVar);
        var a = vm.AtlananGiderler.Single();
        Assert.Equal("Mart 2026 atlandı · MEZAT · Vade 3 Mart", a.Aciklama);

        await vm.AtlamayiGeriAlCommand.ExecuteAsync(a);

        Assert.Null(vm.Hata);
        Assert.Equal((6, Mart), api.SonAtlamaGeriAl);
        Assert.False(vm.AtlananVar);
        Assert.Contains(vm.BekleyenGiderler, b => b.TekrarlayanGiderId == 6);
    }

    [Fact]
    public async Task Izleyiciye_atlananlar_istenmez()
    {
        var (api, vm) = Kur(editor: false);
        await vm.YukleAsync();

        Assert.Equal(0, api.AtlananCagri);
        Assert.False(vm.AtlananVar);
    }

    [Fact]
    public async Task Atlananlar_ucu_yoksa_panel_yine_yuklenir()
    {
        var (api, vm) = Kur();
        api.AtlananHatasi = new KasaApiException(System.Net.HttpStatusCode.NotFound);   // sunucu henüz güncellenmedi

        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.False(vm.AtlananVar);
        Assert.Equal(3, vm.BekleyenGiderler.Count);
    }
}

/// <summary>Paket D — Ayarlar: sıklık, karta bağlı şablon, değişken tutar, hazır şablonlar (33).</summary>
public class PaketDTekrarlayanAyarlarTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    private static (SahteApi api, AyarlarViewModel vm) Kur()
    {
        var api = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false),
            KanallarListe = new List<KanalDto> { new(1, "MEZAT", true, 0, 0m) },
            KrediKartlariListe = new List<KrediKartiDto> { new(7, "Bonus", new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 25), 50_000m, 0m) },
            CarilerListe = new List<CariDto> { new(1, "Adobe", true), new(2, "Pasif", false) },
            TekrarlayanListe = new List<TekrarlayanGiderDto>
            {
                new(1, "Kira", "MEZAT", 25000m, 5, true, new DateOnly(2026, 1, 1)),
                new(2, "Adobe", "MEZAT", 900m, 12, true, new DateOnly(2026, 3, 1), TekrarSikligi.Yillik, 7, false),
                new(3, "KDV", "Ortak", 0m, 28, true, new DateOnly(2026, 9, 1), TekrarSikligi.Aylik, null, true),
            },
            HazirListe = new List<TekrarlayanHazirDto>
            {
                new("kdv", "KDV", "Her ay 28'i", true),
                new("mtv", "MTV", "31 Ocak, 31 Temmuz", false),
            },
        };
        return (api, new AyarlarViewModel(api, new SabitSaat(Bugun.AddHours(10))));
    }

    [Fact]
    public async Task Liste_aciklamasi_aylik_sabit_tutarda_eskisiyle_ayni_digerlerinde_ek_bilgi()
    {
        var (_, vm) = Kur();
        await vm.YukleAsync();

        Assert.Equal("MEZAT · ayın 5. günü · 25.000,00", vm.TekrarlayanGiderler[0].Aciklama);
        Assert.Equal("MEZAT · yılda bir, Mart 2026 başlar · ayın 12. günü · 900,00 · karta bağlı", vm.TekrarlayanGiderler[1].Aciklama);
        Assert.Equal("Ortak · ayın 28. günü · tutar her seferinde girilir", vm.TekrarlayanGiderler[2].Aciklama);
    }

    [Fact]
    public async Task Varsayilan_form_aylik_kartsiz_ve_kayit_eski_istekle_ayni()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        Assert.Equal(TekrarSikligi.Aylik, vm.DuzenTekrarSiklik);
        Assert.False(vm.BaslangicGorunur);
        Assert.Equal(["Kartsız", "Bonus"], vm.TekrarKartCipleri.Select(c => c.Ad));
        Assert.True(vm.TekrarKartCipleri[0].Secili);
        Assert.Equal(["Her ay", "3 ayda bir", "6 ayda bir", "Yılda bir"], vm.SiklikCipleri.Select(c => c.Ad));

        vm.DuzenTekrarKalem = "Kira"; vm.DuzenTekrarKanal = "MEZAT"; vm.DuzenTekrarTutar = 100m; vm.DuzenTekrarGun = 3;
        await vm.TekrarKaydetCommand.ExecuteAsync(null);

        Assert.Equal(new TekrarlayanGiderYaz("Kira", "MEZAT", 100m, 3, true), api.SonTekrarOlustur);
    }

    [Fact]
    public async Task Aylik_olmayan_siklik_baslangic_ayiyla_kaydedilir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();

        vm.SecTekrarSiklikCommand.Execute(vm.SiklikCipleri.Single(c => c.Siklik == TekrarSikligi.UcAylik));
        Assert.True(vm.BaslangicGorunur);
        Assert.True(vm.SiklikCipleri.Single(c => c.Siklik == TekrarSikligi.UcAylik).Secili);
        vm.DuzenTekrarBaslangic = new DateTime(2026, 11, 17);
        vm.DuzenTekrarKalem = "Kira"; vm.DuzenTekrarKanal = "MEZAT"; vm.DuzenTekrarTutar = 100m; vm.DuzenTekrarGun = 17;
        await vm.TekrarKaydetCommand.ExecuteAsync(null);

        var g = api.SonTekrarOlustur!;
        Assert.Equal((TekrarSikligi.UcAylik, (DateOnly?)new DateOnly(2026, 11, 1)), (g.Siklik, g.BaslangicAyi));
        Assert.Equal(TekrarSikligi.Aylik, vm.DuzenTekrarSiklik);   // form sıfırlandı
    }

    [Fact]
    public async Task Degisken_tutarda_sifir_tutar_kaydedilir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        vm.DuzenTekrarKalem = "Kira"; vm.DuzenTekrarKanal = "Ortak"; vm.DuzenTekrarGun = 28;

        await vm.TekrarKaydetCommand.ExecuteAsync(null);
        Assert.Equal(AyarlarViewModel.TutarMesaji, vm.Hata);

        vm.DuzenTekrarTutarDegisken = true;
        Assert.Equal("Önerilen tutar (isteğe bağlı)", vm.TekrarTutarEtiketi);
        await vm.TekrarKaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal((0m, true), (api.SonTekrarOlustur!.Tutar, api.SonTekrarOlustur.TutarDegisken));
    }

    [Fact]
    public async Task Kart_secilince_kalem_cipleri_aktif_carilere_doner_kayit_kartla_gider()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        vm.DuzenTekrarKalem = "Kira";

        vm.SecTekrarKartCommand.Execute(vm.TekrarKartCipleri.Single(c => c.Ad == "Bonus"));

        Assert.True(vm.KartliMi);
        Assert.Equal("", vm.DuzenTekrarKalem);                    // kalem ile cari farklı listeler
        Assert.Equal(["Adobe"], vm.TekrarKalemCipleri.Select(c => c.Ad));
        Assert.Equal("Cari (kartın harcama yaptığı yer)", vm.TekrarKalemEtiketi);
        Assert.True(vm.TekrarKartCipleri.Single(c => c.Ad == "Bonus").Secili);

        vm.SecTekrarKalemCommand.Execute(vm.TekrarKalemCipleri[0]);
        vm.DuzenTekrarKanal = "MEZAT"; vm.DuzenTekrarTutar = 900m; vm.DuzenTekrarGun = 12;
        await vm.TekrarKaydetCommand.ExecuteAsync(null);
        Assert.Equal(("Adobe", (int?)7), (api.SonTekrarOlustur!.Kalem, api.SonTekrarOlustur.KrediKartiId));

        vm.SecTekrarKartCommand.Execute(vm.TekrarKartCipleri[0]);
        Assert.False(vm.KartliMi);
        Assert.Contains(vm.TekrarKalemCipleri, c => c.Ad == "Kira");
    }

    [Fact]
    public async Task Duzenle_yeni_alanlari_forma_alir_ve_geri_gonderir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();

        vm.TekrarDuzenleCommand.Execute(vm.TekrarlayanGiderler[1]);

        Assert.Equal((TekrarSikligi.Yillik, (int?)7, false), (vm.DuzenTekrarSiklik, vm.DuzenTekrarKartId, vm.DuzenTekrarTutarDegisken));
        Assert.Equal(new DateTime(2026, 3, 1), vm.DuzenTekrarBaslangic);
        Assert.True(vm.TekrarKalemCipleri.Single(c => c.Ad == "Adobe").Secili);
        await vm.TekrarKaydetCommand.ExecuteAsync(null);

        var (id, g) = api.SonTekrarGuncelle!.Value;
        Assert.Equal(2, id);
        Assert.Equal(new TekrarlayanGiderYaz("Adobe", "MEZAT", 900m, 12, true, new DateOnly(2026, 3, 1), TekrarSikligi.Yillik, 7, false), g);
    }

    [Fact]
    public async Task Hazir_sablon_tek_dokunusla_eklenir_uyari_gosterilir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        Assert.True(vm.HazirVar);
        Assert.True(vm.HazirSablonlar.Single(h => h.Kod == "kdv").Eklendi);

        await vm.HazirEkleCommand.ExecuteAsync(vm.HazirSablonlar.Single(h => h.Kod == "mtv"));

        Assert.Null(vm.Hata);
        Assert.Equal("mtv", api.SonHazirEkle);
        Assert.Equal("MTV eklendi (1 şablon, tutar her seferinde girilir). Tarihleri muhasebecinizle doğrulayın.", vm.TekrarBilgi);
        Assert.True(vm.HazirSablonlar.Single(h => h.Kod == "mtv").Eklendi);
        Assert.Equal(4, vm.TekrarlayanGiderler.Count);
    }
}

/// <summary>Paket D — Geçmiş: "Önceki haline döndür" (36).</summary>
public class PaketDGecmisTests
{
    private static DegisiklikDto Satir(int id, string eylem, bool geriAlinabilir = true)
        => new(id, new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc), "editor", "İşlem", 5, eylem, "özet", "{}", "{}", false, null, geriAlinabilir);

    [Fact]
    public async Task Guncelleme_satirinda_onceki_haline_dondur_metni_ve_mesaji()
    {
        var api = new SahteApi { GecmisListe = new[] { Satir(2, "Güncellendi"), Satir(1, "Silindi") } };
        var vm = new GecmisViewModel(api, new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0))) { EditorMu = true };
        await vm.YukleAsync();

        Assert.Equal("Önceki haline döndür", vm.Kayitlar[0].GeriAlMetni);
        Assert.True(vm.Kayitlar[0].GuncellemeGeriAlmasi);
        Assert.Equal("Geri al", vm.Kayitlar[1].GeriAlMetni);

        await vm.GeriAlCommand.ExecuteAsync(vm.Kayitlar[0]);
        Assert.Equal(2, api.SonGeriAl);
        Assert.Equal("İşlem önceki haline döndürüldü; geçmişe \"Güncellendi (geri alındı)\" olarak yazıldı.", vm.Bilgi);

        await vm.GeriAlCommand.ExecuteAsync(vm.Kayitlar[1]);
        Assert.Equal(GecmisViewModel.GeriAlindiMesaji("İşlem"), vm.Bilgi);
    }

    [Fact]
    public async Task Geri_alinmis_guncelleme_satiri_guncelleme_rozetinde_dugmesiz()
    {
        var api = new SahteApi { GecmisListe = new[] { Satir(3, "Güncellendi (geri alındı)", geriAlinabilir: false) } };
        var vm = new GecmisViewModel(api, new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0))) { EditorMu = true };
        await vm.YukleAsync();

        var s = vm.Kayitlar.Single();
        Assert.True(s.GuncellemeMi);
        Assert.False(s.GuncellemeGeriAlmasi);
        Assert.False(s.GeriAlGorunur);
    }
}
