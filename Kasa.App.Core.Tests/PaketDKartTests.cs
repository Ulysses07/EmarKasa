using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket D — Kredi Kartları: "Ekstreyi öde" / "Tamamını öde" (30) ve ekstre mutabakatı (34).</summary>
public class PaketDKartOdemeTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    private static (SahteApi api, KrediKartlariViewModel vm) Kur()
    {
        var api = new SahteApi
        {
        KrediKartlariListe = new List<KrediKartiDto>
        {
            new(7, "Bonus", new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 25), 50_000m, 0m,
                GuncelBorc: 6_200m, AcilisBorc: 0m, HarcamaToplam: 6_200m, OdemeToplam: 0m, EkstreBorc: 4_250m),
            new(8, "Boş", new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 25), 10_000m, 0m),
        },
        };
        return (api, new KrediKartlariViewModel(api, new SabitSaat(Bugun.AddHours(10))) { EditorMu = true });
    }

    [Fact]
    public async Task Ekstreyi_ode_ve_tamamini_ode_formu_sayfadaki_borcla_ve_bugunle_doldurur()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        var k = vm.Kartlar.Single(x => x.Id == 7);
        k.OdemeTarihGiris = Bugun.AddDays(-3);

        vm.EkstreyiOdeCommand.Execute(k);
        Assert.Equal(4_250m, k.OdemeTutarGiris);
        Assert.Equal(Bugun, k.OdemeTarihGiris);
        Assert.Null(api.SonKartOdemeKaydet);                    // kullanıcı yine "Ekle" ile onaylar

        vm.TamaminiOdeCommand.Execute(k);
        Assert.Equal(6_200m, k.OdemeTutarGiris);

        await vm.OdemeEkleCommand.ExecuteAsync(k);
        // Mevcut kart ödemesi kaydı: elle girilenle birebir aynı.
        Assert.Equal(new KartOdemeYaz(7, new DateOnly(2026, 9, 24), 6_200m, null), api.SonKartOdemeKaydet);
    }

    [Fact]
    public async Task Borcu_olmayan_kartta_dugmeler_gizli_ve_form_doldurulmaz()
    {
        var (_, vm) = Kur();
        await vm.YukleAsync();
        var bos = vm.Kartlar.Single(x => x.Id == 8);
        var dolu = vm.Kartlar.Single(x => x.Id == 7);

        Assert.False(bos.EkstreOdenebilir);
        Assert.False(bos.TamamiOdenebilir);
        Assert.True(dolu.EkstreOdenebilir);
        Assert.Equal("Ekstreyi öde · 4.250,00", dolu.EkstreOdeMetni);
        Assert.Equal("Tamamını öde · 6.200,00", dolu.TamaminiOdeMetni);

        vm.EkstreyiOdeCommand.Execute(bos);
        Assert.Equal(0m, bos.OdemeTutarGiris);
        Assert.Equal("Ödenecek borç yok.", vm.Hata);
    }
}

public class PaketDKartMutabakatTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);
    private static readonly DateOnly Kesim = new(2026, 9, 15);
    private static readonly DateOnly OncekiKesim = new(2026, 8, 15);

    private static KartMutabakatDetayDto Detay(DateOnly kesim, decimal? ekstre = null, KartMutabakatDurumu? durum = null,
        int? id = null, decimal? kayittaki = null, params int[] tikli)
        => new(7, "Bonus", kesim.AddMonths(-1).AddDays(1), kesim, kesim.AddDays(10),
            1_000m, 2_500m, 1_000m, 2_500m,
            new List<KartMutabakatIslemDto>
            {
                new(11, kesim.AddDays(-10), "Market", 1_500m, null, tikli.Contains(11)),
                new(12, kesim.AddDays(-5), "Yazılım", 1_000m, "abonelik", tikli.Contains(12)),
            },
            new List<KartMutabakatOdemeDto> { new(21, kesim.AddDays(-12), 1_000m, null) },
            id, ekstre, ekstre - 2_500m, null, null, durum, kayittaki);

    private static (SahteApi api, KartMutabakatViewModel vm) Kur(bool editor = true)
    {
        var api = new SahteApi
        {
            KartDonemleriListe = new List<KartDonemDto>
            {
                new(new DateOnly(2026, 8, 16), Kesim, new DateOnly(2026, 9, 25), 2_500m, null, null, null, null),
                new(new DateOnly(2026, 7, 16), OncekiKesim, new DateOnly(2026, 8, 25), 1_000m, 5, 1_120m, 120m, KartMutabakatDurumu.FarkKabul),
            },
            KartMutabakatUret = (_, k) => Detay(k),
        };
        return (api, new KartMutabakatViewModel(api, new SabitSaat(Bugun.AddHours(10))) { EditorMu = editor });
    }

    [Fact]
    public async Task Yukle_donemleri_ve_en_yeni_donemin_ayrintisini_getirir()
    {
        var (api, vm) = Kur();

        await vm.YukleAsync(7);

        Assert.Null(vm.Hata);
        Assert.Equal([7], api.KartDonemleriCagrilari);
        Assert.Equal((7, Kesim), api.KartMutabakatCagrilari.Single());
        Assert.Equal("Bonus", vm.KartAdi);
        Assert.Equal(2, vm.Donemler.Count);
        Assert.True(vm.Donemler[0].Secili);
        Assert.Equal("Mutabakat yok", vm.Donemler[0].DurumMetni);
        Assert.Equal("Fark kabul · +120,00", vm.Donemler[1].DurumMetni);
        Assert.Equal("16 Ağu – 15 Eyl 2026", vm.Donemler[0].DonemMetni);
        Assert.Equal("Devreden 1.000,00 + harcama 2.500,00 − ödeme 1.000,00 = 2.500,00 ₺", vm.HesapKirilimi);
        Assert.Equal(2, vm.Islemler.Count);
        Assert.Single(vm.Odemeler);
        Assert.False(vm.EkstreGirildi);
        Assert.Null(vm.Fark);                                   // ekstre yazılmadan fark yok
        Assert.Equal("0/2 işlem ekstrede işaretlendi · işaretlenmeyenler 2.500,00 ₺", vm.TikOzeti);
        Assert.Equal("Bu dönem için mutabakat kaydı yok.", vm.DurumMetni);
    }

    [Fact]
    public async Task Ekstre_yazilinca_fark_canli_hesaplanir_tikler_toplami_degistirir()
    {
        var (_, vm) = Kur();
        await vm.YukleAsync(7);

        vm.EkstreTutari = 2_620m;
        Assert.True(vm.EkstreGirildi);
        Assert.Equal(120m, vm.Fark);
        Assert.True(vm.FarkVar);
        Assert.Equal(-120m, vm.FarkRenkDegeri);
        Assert.Equal("Ekstre 120,00 ₺ fazla: girilmemiş harcama, faiz ya da ücret olabilir.", vm.FarkMetni);

        vm.Islemler[0].Tikli = true;
        Assert.Equal(1_000m, vm.TiksizToplam);
        Assert.Equal(1, vm.TikliAdet);

        vm.EkstreTutari = 2_500m;
        Assert.True(vm.Uyusuyor);
        Assert.Equal("Ekstre uygulamanın hesabıyla uyuşuyor.", vm.FarkMetni);

        vm.EkstreTutari = 2_000m;
        Assert.Equal("Ekstre 500,00 ₺ eksik: uygulamada fazladan ya da yanlış tarihli harcama olabilir.", vm.FarkMetni);

        vm.TumunuTikleCommand.Execute(null);
        Assert.Equal(2, vm.TikliAdet);
        vm.TumunuTikleCommand.Execute(null);
        Assert.Equal(0, vm.TikliAdet);
    }

    [Fact]
    public async Task Kaydet_ekstre_tikler_ve_notla_yazar_fark_kabul_ayri_dugme()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync(7);

        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(KartMutabakatViewModel.EkstreTutariMesaji, vm.Hata);
        Assert.Null(api.SonKartMutabakatKaydet);

        vm.EkstreTutari = 2_620m;
        vm.Islemler[1].Tikli = true;
        vm.Not = "  Kart ücreti  ";
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        var g = api.SonKartMutabakatKaydet!;
        Assert.Equal((7, Kesim, 2_620m, "Kart ücreti", false), (g.KrediKartiId, g.Kesim, g.EkstreTutari, g.Not, g.FarkKabul));
        Assert.Equal([12], g.TikliIslemIdleri);
        Assert.Equal(KartMutabakatDurumu.Acik, vm.Detay!.Durum);
        Assert.Equal("Mutabakat kaydedildi; fark açık duruyor.", vm.Bilgi);
        Assert.True(vm.Islemler[1].Tikli);                      // kayıttan dönen tikler
        Assert.True(vm.KayitVar);

        await vm.FarkiKabulEtCommand.ExecuteAsync(null);
        Assert.True(api.SonKartMutabakatKaydet!.FarkKabul);
        Assert.Equal("Fark kabul edildi", vm.DurumMetni);
    }

    [Fact]
    public async Task Kayitli_mutabakat_formu_doldurur_sonradan_degisim_uyarisi_verir_silinebilir()
    {
        var (api, vm) = Kur();
        api.KartMutabakatUret = null;
        api.KartMutabakatDetay = Detay(Kesim, 2_500m, KartMutabakatDurumu.Mutabik, 900, 2_300m, 11, 12);

        await vm.YukleAsync(7);

        Assert.True(vm.EkstreGirildi);
        Assert.Equal(2_500m, vm.EkstreTutari);
        Assert.All(vm.Islemler, i => Assert.True(i.Tikli));
        Assert.Equal("Mutabık", vm.DurumMetni);
        Assert.True(vm.DegisimVar);
        Assert.Equal("Mutabakat kaydedildiğinde hesaplanan borç 2.300,00 ₺ idi; dönem kayıtları sonradan değişti.", vm.DegisimUyarisi);

        await vm.SilCommand.ExecuteAsync(null);
        Assert.Equal(900, api.SonKartMutabakatSil);
        Assert.False(vm.KayitVar);
        Assert.False(vm.EkstreGirildi);
    }

    [Fact]
    public async Task Kayit_sil_dugmesi_C_ikinci_onayini_ister_geri_alinamaz_der()
    {
        // Paket C · 32 ile ortak: mutabakat silmesi geri alınamaz; tek basış silmez, ikinci basış siler.
        var (api, _) = Kur();
        var saat = new SabitSaat(Bugun.AddHours(10));
        var vm = new KartMutabakatViewModel(api, saat) { EditorMu = true };
        api.KartMutabakatUret = null;
        api.KartMutabakatDetay = Detay(Kesim, 2_500m, KartMutabakatDurumu.Mutabik, 900, 2_500m, 11, 12);
        await vm.YukleAsync(7);
        string Dugme() => SilmeOnayi.DugmeMetni(vm.Detay!.MutabakatId, vm.Silme.Bekleyen, vm.Silme.OnayDugmesi, "Kaydı sil");
        Assert.Equal("Kaydı sil", Dugme());

        await vm.OnayliSilCommand.ExecuteAsync(null);
        Assert.Null(api.SonKartMutabakatSil);
        Assert.True(vm.KayitVar);
        Assert.Equal(SilmeOnayi.OnayDugmesiGeriAlinmaz, Dugme());

        // Süre geçince onay düşer: sonraki basış yeniden sorar.
        saat.Ilerle(SilmeOnayi.OnaySuresi + TimeSpan.FromSeconds(1));
        await vm.OnayliSilCommand.ExecuteAsync(null);
        Assert.Null(api.SonKartMutabakatSil);

        await vm.OnayliSilCommand.ExecuteAsync(null);
        Assert.Equal(900, api.SonKartMutabakatSil);
        Assert.False(vm.KayitVar);
        Assert.False(vm.Silme.SeritGorunur);            // geri alınamaz: "Geri al" şeridi yok
        Assert.Empty(api.SonSilmeCagrilari);
        Assert.Equal("Kaydı sil", Dugme());
    }

    [Fact]
    public async Task Donem_secilince_o_donem_yuklenir_izleyici_kaydedemez()
    {
        var (api, vm) = Kur(editor: false);
        await vm.YukleAsync(7);

        await vm.SecDonemCommand.ExecuteAsync(vm.Donemler[1]);
        Assert.Equal(OncekiKesim, api.KartMutabakatCagrilari[^1].Kesim);
        Assert.True(vm.Donemler[1].Secili);
        Assert.False(vm.Donemler[0].Secili);

        vm.EkstreTutari = 1_000m;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(HataMesaji.Yetkisiz, vm.Hata);
        Assert.Null(api.SonKartMutabakatKaydet);
    }

    [Fact]
    public async Task Donemi_olmayan_kartta_ayrinti_bos()
    {
        var (api, vm) = Kur();
        api.KartDonemleriListe = new List<KartDonemDto>();

        await vm.YukleAsync(7);

        Assert.Null(vm.Hata);
        Assert.False(vm.DetayVar);
        Assert.Empty(api.KartMutabakatCagrilari);
    }
}
