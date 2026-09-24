using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket D inceleme bulguları: işaretli tutar girişi, sıfır/eksi ekstre, karta bağlı şablonda kalem ipucu.</summary>
public class ParaGirisIsaretliTests
{
    [Theory]
    [InlineData("-250", -250)]
    [InlineData("−1.500,50", -1500.50)]
    [InlineData(" - 12,5 ₺", -12.5)]
    [InlineData("-0", 0)]
    [InlineData("1.500", 1500)]
    [InlineData("", 0)]
    public void Gecerli(string metin, double beklenen)
    {
        var r = ParaGiris.AyristirIsaretli(metin);
        Assert.True(r.Gecerli, r.Hata);
        Assert.Equal((decimal)beklenen, r.Tutar);
    }

    [Theory]
    [InlineData("-", ParaGiris.HataGecersiz)]
    [InlineData("--5", ParaGiris.HataGecersiz)]
    [InlineData("-(5)", ParaGiris.HataGecersiz)]
    [InlineData("-12,345", ParaGiris.HataKurus)]
    [InlineData("-abc", ParaGiris.HataGecersiz)]
    public void Gecersiz(string metin, string hata)
    {
        var r = ParaGiris.AyristirIsaretli(metin);
        Assert.False(r.Gecerli);
        Assert.Equal(hata, r.Hata);
    }

    [Fact]
    public void Isaretsiz_ayristirma_eksiyi_yine_reddeder()
        => Assert.Equal(ParaGiris.HataNegatif, ParaGiris.Ayristir("-250").Hata);
}

public class PaketDMutabakatGirisTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);
    private static readonly DateOnly Kesim = new(2026, 9, 15);

    /// <summary>Hesaplanan borcu verilen, harcaması olmayan bir dönem (ekstre kayıtlıysa onunla).</summary>
    private static KartMutabakatDetayDto Detay(decimal hesaplanan, decimal? ekstre = null)
        => new(7, "Bonus", new DateOnly(2026, 8, 16), Kesim, new DateOnly(2026, 9, 25),
            hesaplanan, 0m, 0m, hesaplanan,
            new List<KartMutabakatIslemDto>(), new List<KartMutabakatOdemeDto>(),
            ekstre is null ? null : 900, ekstre, ekstre - hesaplanan, ekstre is null ? null : 0m, null,
            ekstre is null ? null : ekstre == hesaplanan ? KartMutabakatDurumu.Mutabik : KartMutabakatDurumu.Acik,
            ekstre is null ? null : hesaplanan);

    private static async Task<(SahteApi api, KartMutabakatViewModel vm)> Kur(decimal hesaplanan, decimal? ekstre = null)
    {
        var api = new SahteApi
        {
            KartDonemleriListe = new List<KartDonemDto>
            {
                new(new DateOnly(2026, 8, 16), Kesim, new DateOnly(2026, 9, 25), hesaplanan, null, ekstre, null, null),
            },
            KartMutabakatDetay = Detay(hesaplanan, ekstre),
        };
        var vm = new KartMutabakatViewModel(api, new SabitSaat(Bugun.AddHours(10))) { EditorMu = true };
        await vm.YukleAsync(7);
        return (api, vm);
    }

    [Fact]
    public async Task Kayitsiz_donemde_sifir_ekstre_yazilip_mutabik_kaydedilir()
    {
        var (api, vm) = await Kur(hesaplanan: 0m);
        Assert.Equal("", vm.EkstreGiris);
        Assert.False(vm.EkstreGirildi);

        vm.EkstreGiris = "0";

        Assert.True(vm.EkstreGirildi);
        Assert.Equal(0m, vm.Fark);
        Assert.True(vm.Uyusuyor);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal(0m, api.SonKartMutabakatKaydet!.EkstreTutari);
        Assert.Equal("Mutabakat kaydedildi: ekstre uyuşuyor.", vm.Bilgi);
        Assert.Equal("0", vm.EkstreGiris);                     // kayıtlı sıfır kutuda "0" görünür, boş değil
        Assert.True(vm.EkstreGirildi);
    }

    [Fact]
    public async Task Kutu_bosaltilinca_ekstre_yazilmamis_sayilir_sifir_kaydedilmez()
    {
        var (api, vm) = await Kur(hesaplanan: 500m);
        vm.EkstreGiris = "520";
        Assert.Equal(20m, vm.Fark);

        vm.EkstreGiris = "";

        Assert.False(vm.EkstreGirildi);
        Assert.Null(vm.Fark);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(KartMutabakatViewModel.EkstreTutariMesaji, vm.Hata);
        Assert.Null(api.SonKartMutabakatKaydet);
    }

    [Fact]
    public async Task Alacakli_kartin_eksi_ekstresi_yazilir_ve_mutabik_olur()
    {
        var (api, vm) = await Kur(hesaplanan: -250m);

        vm.EkstreGiris = "-250,00";

        Assert.Equal(-250m, vm.EkstreTutari);
        Assert.True(vm.Uyusuyor);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal(-250m, api.SonKartMutabakatKaydet!.EkstreTutari);
        Assert.Equal(KartMutabakatDurumu.Mutabik, vm.Detay!.Durum);
        Assert.Equal("-250", vm.EkstreGiris);
    }

    [Fact]
    public async Task Alacakli_kartta_sifir_ekstre_farki_eksi_isaretini_hatirlatir()
    {
        var (_, vm) = await Kur(hesaplanan: -250m);

        vm.EkstreGiris = "0";
        Assert.Equal(250m, vm.Fark);
        Assert.Equal("Ekstre 250,00 ₺ fazla. Uygulamaya göre kart 250,00 ₺ alacaklı; ekstrede alacak bakiye varsa eksiyle yazın (ör. -250,00). Yoksa girilmemiş harcama, faiz ya da ücret olabilir.",
            vm.FarkMetni);

        // Borçlu kartta metin eskisi gibi.
        var (_, borclu) = await Kur(hesaplanan: 100m);
        borclu.EkstreGiris = "120";
        Assert.Equal("Ekstre 20,00 ₺ fazla: girilmemiş harcama, faiz ya da ücret olabilir.", borclu.FarkMetni);
        // Ekstre de eksi (daha az alacak) ise alacak ipucu gerekmez.
        vm.EkstreGiris = "-200";
        Assert.Equal("Ekstre 50,00 ₺ fazla: girilmemiş harcama, faiz ya da ücret olabilir.", vm.FarkMetni);
    }

    [Fact]
    public async Task Gecersiz_metin_kaydi_durdurur_son_gecerli_tutar_kalir()
    {
        var (api, vm) = await Kur(hesaplanan: 500m);
        vm.EkstreGiris = "510";

        vm.EkstreGiris = "5x0";

        Assert.Equal(ParaGiris.HataGecersiz, vm.EkstreGirisHatasi);
        Assert.Equal(510m, vm.EkstreTutari);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(ParaGiris.HataGecersiz, vm.Hata);
        Assert.Null(api.SonKartMutabakatKaydet);

        vm.EkstreGiris = "1.500,5";
        Assert.Null(vm.EkstreGirisHatasi);
        Assert.Equal(1_500.5m, vm.EkstreTutari);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(-120.5, "-120,5")]
    [InlineData(2500, "2500")]
    public async Task Kayitli_ekstre_kutuya_yazilir(double ekstre, string metin)
    {
        var (_, vm) = await Kur(hesaplanan: 0m, ekstre: (decimal)ekstre);
        Assert.Equal(metin, vm.EkstreGiris);
        Assert.True(vm.EkstreGirildi);
        Assert.Null(vm.EkstreGirisHatasi);
    }

    [Fact]
    public async Task Tutar_koddan_yazilinca_kutu_da_guncellenir()
    {
        var (_, vm) = await Kur(hesaplanan: 0m);
        vm.EkstreTutari = 1_250.75m;
        Assert.Equal("1250,75", vm.EkstreGiris);
        Assert.True(vm.EkstreGirildi);
    }
}

public class PaketDTekrarKalemIpucuTests
{
    [Fact]
    public async Task Karta_bagli_sablonda_cari_yoksa_ipucu_cariler_sayfasini_gosterir()
    {
        var api = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false),
            KanallarListe = new List<KanalDto> { new(1, "MEZAT", true, 0, 0m) },
            KrediKartlariListe = new List<KrediKartiDto> { new(7, "Bonus", new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 25), 50_000m, 0m) },
            CarilerListe = new List<CariDto> { new(2, "Pasif", false) },
            TekrarlayanListe = new List<TekrarlayanGiderDto>(),
            HazirListe = new List<TekrarlayanHazirDto>(),
        };
        var vm = new AyarlarViewModel(api, new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0)));
        await vm.YukleAsync();
        Assert.Equal("Önce yukarıdaki Sabit Gider Kalemleri bölümünden bir kalem ekleyin.", vm.TekrarKalemYokMetni);

        var degisenler = new List<string?>();
        vm.PropertyChanged += (_, e) => degisenler.Add(e.PropertyName);
        vm.SecTekrarKartCommand.Execute(vm.TekrarKartCipleri.Single(c => c.Ad == "Bonus"));

        Assert.True(vm.TekrarKalemYok);                          // aktif cari yok
        Assert.Equal("Önce Cariler sayfasından bir cari ekleyin.", vm.TekrarKalemYokMetni);
        Assert.Contains(nameof(AyarlarViewModel.TekrarKalemYokMetni), degisenler);

        vm.SecTekrarKartCommand.Execute(vm.TekrarKartCipleri[0]);
        Assert.Equal("Önce yukarıdaki Sabit Gider Kalemleri bölümünden bir kalem ekleyin.", vm.TekrarKalemYokMetni);
    }
}
