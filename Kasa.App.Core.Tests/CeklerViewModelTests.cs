using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class CeklerViewModelTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);
    private static SabitSaat Saat() => new(Bugun.AddHours(10));
    private static DateOnly G(int ay, int gun) => new(2026, ay, gun);

    private static CekDto Cek(int id, CekYonu yon, CekDurumu durum, decimal tutar, DateOnly vade, string kanal = "MEZAT",
        DateOnly? islem = null, string kisi = "Ahmet")
        => new(id, yon, "0012", "Ziraat", kisi, tutar, G(9, 1), vade, kanal, durum, islem, null);

    private static SahteApi Api() => new()
    {
        KanallarListe = new List<KanalDto>
        {
            new(1, "MEZAT", true, 0, 0m), new(2, "PERAKENDE", true, 1, 0m), new(3, "ESKI", false, 2, 0m),
        },
        CeklerListe = new List<CekDto>
        {
            Cek(1, CekYonu.Alinan, CekDurumu.Portfoyde, 1000m, G(10, 15)),
            Cek(2, CekYonu.Verilen, CekDurumu.Portfoyde, 250m, G(9, 20), kanal: "Ortak"),
            Cek(3, CekYonu.Alinan, CekDurumu.TahsilEdildi, 400m, G(9, 10), islem: G(9, 12)),
        },
        CekOzeti = new CekOzetDto(1000m, 1, 250m, 1, 30,
            new List<CekDto> { Cek(1, CekYonu.Alinan, CekDurumu.Portfoyde, 1000m, G(10, 15)) },
            new List<CekDto> { Cek(2, CekYonu.Verilen, CekDurumu.Portfoyde, 250m, G(9, 20), kanal: "Ortak") }),
    };

    private static CeklerViewModel Vm(SahteApi api) => new(api, Saat()) { EditorMu = true };

    [Fact]
    public async Task Yukle_ozeti_listeyi_ve_cipleri_doldurur()
    {
        var api = Api();
        var vm = Vm(api);

        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.Equal((1000m, 1, 250m, 1), (vm.PortfoydekiAlinanToplam, vm.PortfoydekiAlinanAdet, vm.OdenecekVerilenToplam, vm.OdenecekVerilenAdet));
        Assert.Equal(1, vm.YaklasanAdet);
        Assert.Equal("Vadesi 30 gün içinde", vm.YaklasanBaslik);
        Assert.Equal("Alınan 1.000,00 ₺ · Verilen 0,00 ₺", vm.YaklasanAyrinti);
        Assert.Equal(1, vm.VadesiGecenAdet);
        Assert.True(vm.VadesiGecenVar);
        Assert.True(vm.VadesiGecenler.Single().VadesiGecti);

        Assert.Equal([1, 2, 3], vm.Cekler.Select(c => c.Id));
        Assert.Equal("3 çek · alınan 1.400,00 ₺ · verilen 250,00 ₺", vm.ListeOzeti);
        // Alınan çek formu: yalnız aktif kanallar, "Ortak" yok.
        Assert.Equal(["MEZAT", "PERAKENDE"], vm.KanalCipleri.Select(k => k.Ad));
        Assert.Equal(["Tümü", "Alınan", "Verilen"], vm.FiltreYonleri.Select(c => c.Ad));
        Assert.True(vm.FiltreYonleri[0].Secili);
        Assert.Equal(["Portföyde", "Tahsil edildi", "Ciro edildi", "Karşılıksız", "İade edildi"], vm.DurumCipleri.Select(c => c.Ad));
    }

    [Fact]
    public async Task Satir_gorunumu_isaretli_tutar_durum_ve_ton_verir()
    {
        var vm = Vm(Api());
        await vm.YukleAsync();

        var alinan = vm.Cekler.Single(c => c.Id == 1);
        Assert.Equal("+1.000,00", alinan.ImzaliTutar);
        Assert.Equal("Portföyde", alinan.DurumAdi);
        Assert.Equal("bekliyor", alinan.Ton);
        Assert.False(alinan.VadesiGecti);
        Assert.Equal("Alınan · Ziraat · No 0012 · MEZAT", alinan.Ayrinti);

        var verilen = vm.Cekler.Single(c => c.Id == 2);
        Assert.Equal("−250,00", verilen.ImzaliTutar);
        Assert.Equal("Ödenecek", verilen.DurumAdi);
        Assert.True(verilen.VadesiGecti);                  // vade 20 Eyl < bugün, hâlâ ödenmedi

        var tahsil = vm.Cekler.Single(c => c.Id == 3);
        Assert.Equal("olumlu", tahsil.Ton);
        Assert.Equal("Vade 10 Eyl 2026 · Tahsil 12 Eyl 2026", tahsil.TarihMetni);
    }

    [Fact]
    public async Task Yon_filtresi_sunucuya_gider_durum_ciplerini_yeniden_kurar()
    {
        var api = Api();
        var vm = Vm(api);
        await vm.YukleAsync();

        await vm.SecFiltreDurumCommand.ExecuteAsync(vm.FiltreDurumlari.Single(c => c.Durum == CekDurumu.TahsilEdildi));
        Assert.Equal((null, CekDurumu.TahsilEdildi), api.SonCekFiltre);
        Assert.Equal([3], vm.Cekler.Select(c => c.Id));

        // Verilen çekte "Tahsil edildi" yok: durum filtresi "Tümü"ne döner, Portföyde "Ödenecek" olur.
        await vm.SecFiltreYonCommand.ExecuteAsync(vm.FiltreYonleri.Single(c => c.Yon == CekYonu.Verilen));
        Assert.Equal((CekYonu.Verilen, (CekDurumu?)null), api.SonCekFiltre);
        Assert.Null(vm.FiltreDurum);
        Assert.Equal(["Tümü", "Ödenecek", "Ödendi", "İade edildi"], vm.FiltreDurumlari.Select(c => c.Ad));
        Assert.True(vm.FiltreDurumlari[0].Secili);
        Assert.True(vm.FiltreYonleri.Single(c => c.Yon == CekYonu.Verilen).Secili);
        Assert.Equal([2], vm.Cekler.Select(c => c.Id));
    }

    [Fact]
    public async Task Gec_gelen_eski_liste_yeni_filtrenin_sonucunu_ezmez()
    {
        var api = Api();
        var bekleyen = new TaskCompletionSource<IReadOnlyList<CekDto>>();
        var vm = Vm(api);
        await vm.YukleAsync();

        api.CeklerUret = (yon, _) => yon is null
            ? bekleyen.Task
            : Task.FromResult<IReadOnlyList<CekDto>>(api.CeklerListe.Where(c => c.Yon == yon).ToList());
        var eski = vm.SecFiltreYonCommand.ExecuteAsync(vm.FiltreYonleri[0]);              // Tümü: bekliyor
        await vm.SecFiltreYonCommand.ExecuteAsync(vm.FiltreYonleri.Single(c => c.Yon == CekYonu.Alinan));
        bekleyen.SetResult(api.CeklerListe);
        await eski;

        Assert.Equal([1, 3], vm.Cekler.Select(c => c.Id));
    }

    [Fact]
    public async Task Tahsil_edildiye_gecince_islem_tarihi_bugune_varsayilir()
    {
        var vm = Vm(Api());
        await vm.YukleAsync();
        vm.Duzenle(vm.Cekler.Single(c => c.Id == 1));      // portföyde, işlem tarihi yok
        vm.DuzenIslemTarihi = new DateTime(2026, 1, 5);
        Assert.False(vm.IslemTarihiGorunur);

        vm.SecDurumCommand.Execute(vm.DurumCipleri.Single(c => c.Durum == CekDurumu.TahsilEdildi));

        Assert.Equal(CekDurumu.TahsilEdildi, vm.DuzenDurum);
        Assert.True(vm.IslemTarihiGorunur);
        Assert.Equal("Tahsil tarihi", vm.IslemTarihiEtiketi);
        Assert.Equal(Bugun, vm.DuzenIslemTarihi);
        Assert.True(vm.DurumCipleri.Single(c => c.Durum == CekDurumu.TahsilEdildi).Secili);
        Assert.Equal("24 Eylül 2026 günü kasaya ve MEZAT kanalına girer.", vm.KasaEtkisiMetni);

        vm.DuzenIslemTarihi = new DateTime(2026, 9, 30);
        Assert.Contains("ileri tarih", vm.KasaEtkisiMetni);
    }

    [Fact]
    public async Task Verilen_cekte_odendiye_gecince_tarih_bugun_ortak_secilebilir()
    {
        var vm = Vm(Api());
        await vm.YukleAsync();
        vm.DuzenIslemTarihi = new DateTime(2026, 2, 1);

        vm.SecYonCommand.Execute(vm.YonCipleri.Single(c => c.Yon == CekYonu.Verilen));
        Assert.Equal(["MEZAT", "PERAKENDE", "Ortak"], vm.KanalCipleri.Select(k => k.Ad));
        Assert.Equal(["Ödenecek", "Ödendi", "İade edildi"], vm.DurumCipleri.Select(c => c.Ad));
        Assert.Equal("Verildiği kişi/firma", vm.KisiEtiketi);

        vm.SecKanalCommand.Execute(vm.KanalCipleri.Single(k => k.Ad == "Ortak"));
        vm.SecDurumCommand.Execute(vm.DurumCipleri.Single(c => c.Durum == CekDurumu.Odendi));
        Assert.Equal(Bugun, vm.DuzenIslemTarihi);
        Assert.Equal("Ödeme tarihi", vm.IslemTarihiEtiketi);
        Assert.Contains("ortak gider olarak kasadan çıkar", vm.KasaEtkisiMetni);

        // Alınana dönünce "Ortak" ve "Ödendi" geçersiz: kanal temizlenir, durum portföye döner.
        vm.SecYonCommand.Execute(vm.YonCipleri.Single(c => c.Yon == CekYonu.Alinan));
        Assert.Equal("", vm.DuzenKanal);
        Assert.Equal(CekDurumu.Portfoyde, vm.DuzenDurum);
        Assert.DoesNotContain(vm.KanalCipleri, k => k.Ad == "Ortak");
    }

    [Fact]
    public async Task Pasif_kanalli_cek_duzenlenirken_kanali_secili_gorunur_yeni_formda_kaybolur()
    {
        var api = Api();
        api.CeklerListe = [.. api.CeklerListe, Cek(4, CekYonu.Alinan, CekDurumu.Portfoyde, 90m, G(10, 1), kanal: "ESKI")];
        var vm = Vm(api);
        await vm.YukleAsync();
        Assert.DoesNotContain(vm.KanalCipleri, k => k.Ad == "ESKI");

        vm.Duzenle(vm.Cekler.Single(c => c.Id == 4));
        Assert.Equal(["MEZAT", "PERAKENDE", "ESKI"], vm.KanalCipleri.Select(k => k.Ad));
        Assert.True(vm.KanalCipleri.Single(k => k.Ad == "ESKI").Secili);

        vm.SecKanalCommand.Execute(vm.KanalCipleri.Single(k => k.Ad == "MEZAT"));
        Assert.Equal("MEZAT", vm.DuzenKanal);
        Assert.False(vm.KanalCipleri.Single(k => k.Ad == "ESKI").Secili);

        vm.YeniCommand.Execute(null);
        Assert.Equal(["MEZAT", "PERAKENDE"], vm.KanalCipleri.Select(k => k.Ad));
    }

    [Fact]
    public async Task Duzenle_kaydin_islem_tarihini_korur_ve_guncelleme_gonderir()
    {
        var api = Api();
        var vm = Vm(api);
        await vm.YukleAsync();

        vm.Duzenle(vm.Cekler.Single(c => c.Id == 3));
        Assert.Equal(new DateTime(2026, 9, 12), vm.DuzenIslemTarihi);   // bugüne ezilmez
        Assert.Equal("Çeki düzenle", vm.FormBasligi);
        Assert.True(vm.KanalCipleri.Single(k => k.Ad == "MEZAT").Secili);

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        var (id, g) = api.SonCekGuncelle!.Value;
        Assert.Equal(3, id);
        Assert.Equal(CekDurumu.TahsilEdildi, g.Durum);
        Assert.Equal(G(9, 12), g.IslemTarihi);
        Assert.Equal(0, vm.DuzenId);                                      // form sıfırlandı
        Assert.Equal("Yeni çek", vm.FormBasligi);
    }

    [Fact]
    public async Task Yeni_portfoy_cekinde_islem_tarihi_gonderilmez_liste_ve_ozet_yenilenir()
    {
        var api = Api();
        var vm = Vm(api);
        await vm.YukleAsync();
        var (listeOnce, ozetOnce) = (api.CeklerCagri, api.CekOzetCagri);

        vm.DuzenKisi = "  Veli Kaya ";
        vm.DuzenCekNo = " ";
        vm.DuzenBanka = "Garanti";
        vm.DuzenTutar = 1500m;
        vm.DuzenVadeTarihi = new DateTime(2026, 11, 30);
        vm.SecKanalCommand.Execute(vm.KanalCipleri.Single(k => k.Ad == "PERAKENDE"));
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        var g = api.SonCekOlustur!;
        Assert.Equal((CekYonu.Alinan, CekDurumu.Portfoyde, "Veli Kaya", "PERAKENDE", 1500m), (g.Yon, g.Durum, g.Kisi, g.Kanal, g.Tutar));
        Assert.Null(g.IslemTarihi);
        Assert.Null(g.CekNo);
        Assert.Equal(G(9, 24), g.DuzenlemeTarihi);
        Assert.Equal(G(11, 30), g.VadeTarihi);
        Assert.Equal(listeOnce + 1, api.CeklerCagri);
        Assert.Equal(ozetOnce + 1, api.CekOzetCagri);
        Assert.Equal("", vm.DuzenKisi);
    }

    [Theory]
    [InlineData(0, "Ahmet", "MEZAT", CeklerViewModel.TutarMesaji)]
    [InlineData(10, " ", "MEZAT", "Çeki veren kişi/firmayı yazın.")]
    [InlineData(10, "Ahmet", "", CeklerViewModel.KanalMesaji)]
    public async Task Eksik_alan_kaydetmeyi_durdurur(int tutar, string kisi, string kanal, string mesaj)
    {
        var api = Api();
        var vm = Vm(api);
        await vm.YukleAsync();
        vm.DuzenTutar = tutar; vm.DuzenKisi = kisi; vm.DuzenKanal = kanal;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(mesaj, vm.Hata);
        Assert.Null(api.SonCekOlustur);
    }

    [Fact]
    public async Task Sunucu_hatasi_gosterilir_form_korunur()
    {
        var api = Api();
        api.CekYazHatasi = new KasaApiException(System.Net.HttpStatusCode.BadRequest, "Alınan çek 'Ödendi' durumunda olamaz.");
        var vm = Vm(api);
        await vm.YukleAsync();
        vm.DuzenKisi = "Ahmet"; vm.DuzenTutar = 5m; vm.DuzenKanal = "MEZAT";

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal("Alınan çek 'Ödendi' durumunda olamaz.", vm.Hata);
        Assert.Equal("Ahmet", vm.DuzenKisi);
    }

    [Fact]
    public async Task Sil_formdaki_kaydi_temizler_ve_yeniler()
    {
        var api = Api();
        var vm = Vm(api);
        await vm.YukleAsync();
        vm.Duzenle(vm.Cekler.Single(c => c.Id == 2));
        Assert.Equal(CekYonu.Verilen, vm.DuzenYon);
        Assert.Equal("Ortak", vm.DuzenKanal);

        await vm.SilCommand.ExecuteAsync(vm.Cekler.Single(c => c.Id == 2));

        Assert.Equal(2, api.SonCekSil);
        Assert.Equal(0, vm.DuzenId);
        Assert.Equal(CekYonu.Alinan, vm.DuzenYon);
    }

    [Fact]
    public async Task Yukleme_hatasi_hata_alanina_yazilir()
    {
        var api = Api();
        api.YuklemeHatasi = new HttpRequestException("yok");
        var vm = Vm(api);
        await vm.YukleAsync();
        Assert.Equal(HataMesaji.Ulasilamadi, vm.Hata);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public void Cekler_bolumu_iki_rolde_de_gorunur_kredi_kartlarindan_sonra_gelir()
    {
        foreach (var rol in new[] { Rol.Izleyici, Rol.Editor })
        {
            var b = SekmeModeli.Bolumler(rol).ToList();
            Assert.Contains(Bolum.Cekler, b);
            Assert.Equal(b.IndexOf(Bolum.KrediKartlari) + 1, b.IndexOf(Bolum.Cekler));
        }
    }
}
