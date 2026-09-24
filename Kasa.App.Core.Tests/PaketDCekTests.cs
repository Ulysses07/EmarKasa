using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket D — Çekler: tek dokunuş (30), senet/konum/ciro carisi, çoklu seçim ve risk (43).</summary>
public class PaketDCekTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);
    private static DateOnly G(int ay, int gun) => new(2026, ay, gun);

    private static CekDto Cek(int id, CekYonu yon, CekDurumu durum, decimal tutar, DateOnly vade, string kanal = "MEZAT",
        CekTuru tur = CekTuru.Cek, CekKonumu konum = CekKonumu.Elde, string? ciro = null, string kisi = "Ahmet", string? banka = "Ziraat")
        => new(id, yon, "0012", banka, kisi, tutar, G(9, 1), vade, kanal, durum, null, null, tur, konum, ciro);

    private static SahteApi Api() => new()
    {
        KanallarListe = new List<KanalDto> { new(1, "MEZAT", true, 0, 0m), new(2, "PERAKENDE", true, 1, 0m) },
        CarilerListe = new List<CariDto> { new(1, "Yılmaz Gıda", true), new(2, "Yıldız Ltd", true), new(3, "Eski Cari", false) },
        CeklerListe = new List<CekDto>
        {
            Cek(1, CekYonu.Alinan, CekDurumu.Portfoyde, 1000m, G(10, 24)),                                   // +30 gün
            Cek(2, CekYonu.Verilen, CekDurumu.Portfoyde, 250m, G(9, 20), kanal: "Ortak"),
            Cek(3, CekYonu.Alinan, CekDurumu.Portfoyde, 3000m, G(9, 29), tur: CekTuru.Senet, konum: CekKonumu.BankadaTahsilde, kisi: "Veli"),
            Cek(4, CekYonu.Alinan, CekDurumu.CiroEdildi, 400m, G(9, 10), ciro: "Yılmaz Gıda"),
        },
        CekRiski = new CekRiskDto(4000m, 2,
            new List<CekRiskKalemiDto> { new("Veli", 3000m, 1, 0.75m), new("Ahmet", 1000m, 1, 0.25m) },
            new List<CekRiskKalemiDto> { new("Ziraat", 4000m, 2, 1m) }),
    };

    private static CeklerViewModel Vm(SahteApi api, bool editor = true) => new(api, new SabitSaat(Bugun.AddHours(10))) { EditorMu = editor };

    private static async Task<(SahteApi api, CeklerViewModel vm)> Yuklu(bool editor = true)
    {
        var api = Api();
        var vm = Vm(api, editor);
        await vm.YukleAsync();
        Assert.Null(vm.Hata);
        return (api, vm);
    }

    // ---------------------------------------------------------------- tek dokunuş

    [Fact]
    public async Task Tek_dokunus_dugmeleri_yalniz_gecerli_gecislerde_gorunur()
    {
        var (_, vm) = await Yuklu();
        var alinan = vm.Cekler.Single(c => c.Id == 1);
        var verilen = vm.Cekler.Single(c => c.Id == 2);
        var cirolu = vm.Cekler.Single(c => c.Id == 4);

        Assert.True(alinan.AlinanTekDokunus);
        Assert.False(alinan.VerilenTekDokunus);
        Assert.False(verilen.AlinanTekDokunus);
        Assert.True(verilen.VerilenTekDokunus);
        Assert.False(cirolu.AlinanTekDokunus);        // portföyde değil
        Assert.False(cirolu.VerilenTekDokunus);
    }

    [Fact]
    public async Task Tahsil_et_tarihsiz_istek_atar_liste_ve_ozeti_tazeler()
    {
        var (api, vm) = await Yuklu();
        var ozet = api.CekOzetCagri;
        var liste = api.CeklerCagri;

        await vm.TahsilEtCommand.ExecuteAsync(vm.Cekler.Single(c => c.Id == 1));

        Assert.Null(vm.Hata);
        var (id, g) = Assert.Single(api.CekDurumCagrilari);
        Assert.Equal(1, id);
        Assert.Equal(new CekDurumYaz(CekDurumu.TahsilEdildi, null, null), g);   // tarih: sunucunun bugünü
        Assert.Equal(ozet + 1, api.CekOzetCagri);
        Assert.Equal(liste + 1, api.CeklerCagri);
        Assert.Equal(CekDurumu.TahsilEdildi, vm.Cekler.Single(c => c.Id == 1).Durum);
        Assert.Equal("Ahmet çeki bugün tahsil edildi; kasaya ve MEZAT kanalına girdi.", vm.EvrakBilgi);
    }

    [Fact]
    public async Task Ode_ve_karsiliksiz_dogru_durumu_gonderir()
    {
        var (api, vm) = await Yuklu();

        await vm.OdeCommand.ExecuteAsync(vm.Cekler.Single(c => c.Id == 2));
        await vm.KarsiliksizCommand.ExecuteAsync(vm.Cekler.Single(c => c.Id == 3));

        Assert.Equal([(2, CekDurumu.Odendi), (3, CekDurumu.Karsiliksiz)], api.CekDurumCagrilari.Select(c => (c.Id, c.G.Durum)));
        Assert.Equal("Veli seneti karşılıksız olarak işaretlendi; kasayı etkilemez.", vm.EvrakBilgi);
    }

    [Fact]
    public async Task Ciro_cari_ister_onerileri_aktif_carilerden_suzer_ve_cariyle_kaydeder()
    {
        var (api, vm) = await Yuklu();
        var cek = vm.Cekler.Single(c => c.Id == 1);

        await vm.CiroAcCommand.ExecuteAsync(cek);
        Assert.True(cek.CiroAcik);
        Assert.Equal(["Yılmaz Gıda", "Yıldız Ltd"], vm.CiroOnerileri.Select(c => c.Ad));   // pasif cari yok
        vm.CiroCari = "yılmaz";
        Assert.Equal(["Yılmaz Gıda"], vm.CiroOnerileri.Select(c => c.Ad));

        vm.CiroCari = "  ";
        await vm.CiroOnaylaCommand.ExecuteAsync(cek);
        Assert.Equal(CeklerViewModel.CiroCariMesaji, vm.Hata);
        Assert.Empty(api.CekDurumCagrilari);

        vm.SecCiroCariCommand.Execute(vm.CiroOnerileri[0]);
        Assert.Equal("Yılmaz Gıda", vm.CiroCari);
        await vm.CiroOnaylaCommand.ExecuteAsync(cek);

        Assert.Null(vm.Hata);
        Assert.Equal(new CekDurumYaz(CekDurumu.CiroEdildi, null, "Yılmaz Gıda"), api.CekDurumCagrilari.Single().G);
        var yeni = vm.Cekler.Single(c => c.Id == 1);
        Assert.Equal("Ciro: Yılmaz Gıda", yeni.EvrakEtiketi);
        Assert.Equal("", vm.CiroCari);
    }

    [Fact]
    public async Task Izleyici_tek_dokunus_yapamaz()
    {
        var (api, vm) = await Yuklu(editor: false);

        await vm.TahsilEtCommand.ExecuteAsync(vm.Cekler[0]);

        Assert.Equal(HataMesaji.Yetkisiz, vm.Hata);
        Assert.Empty(api.CekDurumCagrilari);
    }

    [Fact]
    public async Task Cakisma_listeyi_tazeler_ve_sunucu_mesajini_gosterir()
    {
        var (api, vm) = await Yuklu();
        api.CekDurumHatasi = new KasaApiException(HttpStatusCode.Conflict, "Yalnız portföydeki evrak tek dokunuşla işlenir.");
        var liste = api.CeklerCagri;

        await vm.TahsilEtCommand.ExecuteAsync(vm.Cekler[0]);

        Assert.Equal("Yalnız portföydeki evrak tek dokunuşla işlenir.", vm.Hata);
        Assert.Equal(liste + 1, api.CeklerCagri);
    }

    [Fact]
    public async Task Formda_acik_evrak_tek_dokunusla_islenince_form_temizlenir()
    {
        var (_, vm) = await Yuklu();
        vm.Duzenle(vm.Cekler.Single(c => c.Id == 1));
        Assert.Equal(1, vm.DuzenId);

        await vm.TahsilEtCommand.ExecuteAsync(vm.Cekler.Single(c => c.Id == 1));

        Assert.Equal(0, vm.DuzenId);   // eski durumla üzerine yazılmasın
    }

    // ---------------------------------------------------------------- form: tür, konum, ciro carisi

    [Fact]
    public async Task Duzenle_tur_konum_ve_ciro_carisini_forma_alir_kaydet_geri_gonderir()
    {
        var (api, vm) = await Yuklu();

        vm.Duzenle(vm.Cekler.Single(c => c.Id == 3));
        Assert.Equal(CekTuru.Senet, vm.DuzenTur);
        Assert.Equal(CekKonumu.BankadaTahsilde, vm.DuzenKonum);
        Assert.True(vm.KonumGorunur);
        Assert.True(vm.TurCipleri.Single(c => c.Tur == CekTuru.Senet).Secili);
        await vm.KaydetCommand.ExecuteAsync(null);

        var g = api.SonCekGuncelle!.Value.G;
        Assert.Equal((CekTuru.Senet, CekKonumu.BankadaTahsilde, (string?)null), (g.Tur, g.Konum, g.CiroEdilenCari));

        vm.Duzenle(vm.Cekler.Single(c => c.Id == 4));
        Assert.True(vm.CiroCariGorunur);
        Assert.Equal("Yılmaz Gıda", vm.DuzenCiroEdilenCari);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal("Yılmaz Gıda", api.SonCekGuncelle!.Value.G.CiroEdilenCari);
    }

    [Fact]
    public async Task Verilen_evrak_her_zaman_elde_ciro_carisi_yalniz_ciroda_kaydedilir()
    {
        var api = Api();
        var vm = Vm(api);
        vm.DuzenKisi = "Tedarikçi"; vm.DuzenTutar = 500m; vm.DuzenKanal = "MEZAT";
        vm.SecKonumCommand.Execute(vm.KonumCipleri.Single(c => c.Konum == CekKonumu.Teminatta));
        vm.DuzenCiroEdilenCari = "Birisi";
        vm.DuzenYon = CekYonu.Verilen;
        Assert.False(vm.KonumGorunur);
        Assert.False(vm.CiroCariGorunur);

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        var g = api.SonCekOlustur!;
        Assert.Equal(CekKonumu.Elde, g.Konum);
        Assert.Null(g.CiroEdilenCari);
    }

    [Fact]
    public async Task Yeni_form_cek_ve_elde_ile_baslar_eski_cek_kaydi_ayni_kalir()
    {
        var (api, vm) = await Yuklu();
        vm.Duzenle(vm.Cekler.Single(c => c.Id == 3));
        vm.YeniCommand.Execute(null);
        Assert.Equal((CekTuru.Cek, CekKonumu.Elde, (string?)null), (vm.DuzenTur, vm.DuzenKonum, vm.DuzenCiroEdilenCari));

        // Paket D öncesi çek: kayıt eski alanlarla birebir aynı (tür/konum varsayılan).
        vm.DuzenKisi = "Ahmet"; vm.DuzenTutar = 100m; vm.DuzenKanal = "MEZAT";
        await vm.KaydetCommand.ExecuteAsync(null);
        var g = api.SonCekOlustur!;
        Assert.Equal(new CekYaz(CekYonu.Alinan, null, null, "Ahmet", 100m, G(9, 24), G(9, 24), "MEZAT", CekDurumu.Portfoyde, null, null), g);
    }

    // ---------------------------------------------------------------- filtre

    [Fact]
    public async Task Tur_ve_konum_filtresi_istemcide_uygulanir()
    {
        var (api, vm) = await Yuklu();

        await vm.SecFiltreTurCommand.ExecuteAsync(vm.FiltreTurleri.Single(c => c.Tur == CekTuru.Senet));
        Assert.Equal([3], vm.Cekler.Select(c => c.Id));
        Assert.Equal(CekTuru.Senet, api.CekRiskCagrilari[^1]);   // risk de türe göre

        await vm.SecFiltreTurCommand.ExecuteAsync(vm.FiltreTurleri[0]);
        await vm.SecFiltreKonumCommand.ExecuteAsync(vm.FiltreKonumlari.Single(c => c.Konum == CekKonumu.Elde));
        Assert.Equal([1, 4], vm.Cekler.Select(c => c.Id));        // verilen evrak konum filtresinde yok
        Assert.True(vm.FiltreKonumlari.Single(c => c.Konum == CekKonumu.Elde).Secili);
    }

    // ---------------------------------------------------------------- çoklu seçim + ortalama vade

    [Fact]
    public void Ortalama_vade_tutar_agirlikli()
    {
        var bugun = G(9, 24);
        var l = new[]
        {
            Cek(1, CekYonu.Alinan, CekDurumu.Portfoyde, 1000m, bugun.AddDays(30)),
            Cek(2, CekYonu.Alinan, CekDurumu.Portfoyde, 3000m, bugun.AddDays(10)),
        };
        Assert.Equal(15, CekEvrakMetin.OrtalamaVadeGunu(l, bugun));    // (1000·30 + 3000·10) / 4000
        Assert.Null(CekEvrakMetin.OrtalamaVadeGunu([], bugun));
        Assert.Equal("9 Eki 2026 (15 gün sonra)", CekEvrakMetin.VadeMetni(15, bugun));
        Assert.Equal("21 Eyl 2026 (3 gün önce)", CekEvrakMetin.VadeMetni(-3, bugun));
    }

    [Fact]
    public async Task Secim_adet_toplam_ve_ortalama_vadeyi_gosterir_yenilemede_korunur()
    {
        var (_, vm) = await Yuklu();
        Assert.False(vm.SecimVar);

        vm.Cekler.Single(c => c.Id == 1).Secili = true;   // 1000, +30 gün
        vm.Cekler.Single(c => c.Id == 3).Secili = true;   // 3000, +5 gün

        Assert.Equal(2, vm.SecimAdet);
        Assert.Equal("2 evrak seçili · toplam 4.000,00 ₺", vm.SecimOzeti);
        Assert.Equal("Tutar ağırlıklı ortalama vade: 5 Eki 2026 (11 gün sonra)", vm.SecimVadeMetni);   // (30000+15000)/4000 = 11,25

        await vm.SecFiltreYonCommand.ExecuteAsync(vm.FiltreYonleri[0]);   // liste yeniden yüklenir
        Assert.Equal(2, vm.SecimAdet);
        Assert.True(vm.Cekler.Single(c => c.Id == 3).Secili);

        vm.Cekler.Single(c => c.Id == 2).Secili = true;
        Assert.Equal("3 evrak seçili · alınan 4.000,00 ₺ · verilen 250,00 ₺", vm.SecimOzeti);

        vm.SecimiTemizleCommand.Execute(null);
        Assert.Equal(0, vm.SecimAdet);
        Assert.Equal("", vm.SecimOzeti);
        vm.TumunuSecCommand.Execute(null);
        Assert.Equal(4, vm.SecimAdet);
    }

    // ---------------------------------------------------------------- risk + etiketler

    [Fact]
    public async Task Risk_dagilimi_kesideci_ve_bankaya_gore_yuklenir()
    {
        var (_, vm) = await Yuklu();

        Assert.True(vm.RiskVar);
        Assert.Equal("Portföydeki alınan evrak: 2 adet · 4.000,00 ₺", vm.RiskOzeti);
        Assert.Equal(["Veli", "Ahmet"], vm.RiskKesideciler.Select(r => r.Ad));
        Assert.Equal("3.000,00 ₺ · 1 evrak · %75", vm.RiskKesideciler[0].Metin);
        Assert.Equal(0.75, vm.RiskKesideciler[0].Oran);
        Assert.Equal("Ziraat", vm.RiskBankalar.Single().Ad);
    }

    [Fact]
    public async Task Risk_ucu_yoksa_kart_gizlenir_sayfa_yuklenir()
    {
        var api = Api();
        api.CekRiskHatasi = new KasaApiException(HttpStatusCode.NotFound);   // sunucu henüz güncellenmedi
        var vm = Vm(api);

        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.False(vm.RiskVar);
        Assert.Empty(vm.RiskKesideciler);
        Assert.Equal(4, vm.Cekler.Count);
    }

    [Fact]
    public async Task Evrak_etiketi_yalniz_varsayilandan_farkliysa_dolu()
    {
        var (_, vm) = await Yuklu();
        Assert.Equal("", vm.Cekler.Single(c => c.Id == 1).EvrakEtiketi);
        Assert.False(vm.Cekler.Single(c => c.Id == 1).EvrakEtiketiVar);
        Assert.Equal("Senet · Bankada tahsilde", vm.Cekler.Single(c => c.Id == 3).EvrakEtiketi);
        Assert.Equal("Ciro: Yılmaz Gıda", vm.Cekler.Single(c => c.Id == 4).EvrakEtiketi);
        Assert.Equal("İcrada", CekEvrakMetin.KonumAdi(CekKonumu.Icrada));
    }
}
