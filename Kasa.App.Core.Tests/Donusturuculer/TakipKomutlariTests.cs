using System.Reflection;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Kart ve kredi takibinin gerekçeli komutları (tests-10, ViewModel tarafı): kart ve kredi durum değişimi, ekstre
/// asgari/son ödeme düzeltmesi, ödeme ve harcama iptali. Kaydeden sahte API her çağrının gövdesini tutar: gönderilen
/// Aktif değerinin seçili kaydın tersi olduğu, seçili kaydın Surum'u, kırpılmış gerekçe, rota kimlikleri, boş gerekçenin
/// ve izleyicinin reddi, ağ hatasından sonra aynı IstekId ile yeniden deneme ve başarıdan sonra yeni anahtar doğrulanır.
/// </summary>
public class TakipKomutlariTests
{
    private static readonly DateOnly Tarih = new(2026, 9, 23);
    private static readonly TakipKanalPayi[] Pay = [new(1, "MEZAT", 10)];

    private static KartTakipDto Kart() => new(1, 3, "Kart", true, true, Tarih, 1, 10, 1000, 100, 100,
        [new KartEkstreDto(7, Tarih, Tarih.AddDays(10), 100, 0, 100, null)],
        [new KartHarcamaDto(11, 91, Tarih, "Malzeme", 40, 1, false, Pay), new KartHarcamaDto(12, 92, Tarih, "Ekstreden gelen", 10, 1, false, Pay, EkstreKayitId: 5)],
        [new KartTakipOdemeDto(21, Tarih, 30, 30, null, false, Pay), new KartTakipOdemeDto(22, Tarih, 20, 20, "Ekstreden", false, Pay, EkstreKayitId: 6)]);

    private static KrediTakipDto Kredi() => new(2, 3, "Kredi", true, true, Tarih, 100, Tarih, 20, Pay,
        [new KrediPlanTaksitDto(3, 1, Tarih, 10, "KasayaIslendi", null, Pay)]);

    private static AuthViewModel Auth(Rol rol = Rol.Editor) => new(new SahteApi()) { AktifRol = rol };
    private static SahteApi Finans() => new() { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };

    private static async Task<(KartTakipViewModel Vm, KaydedenTakipApi Api)> KartVm(Rol rol = Rol.Editor)
    {
        var (vekil, api) = KaydedenTakipApi.Olustur();
        var vm = new KartTakipViewModel(vekil, Finans(), Auth(rol));
        await vm.YukleAsync(); vm.SecCommand.Execute(vm.Kartlar[0]);
        api.Cagrilar.Clear();
        return (vm, api);
    }

    private static async Task<(KrediTakipViewModel Vm, KaydedenTakipApi Api)> KrediVm(Rol rol = Rol.Editor)
    {
        var (vekil, api) = KaydedenTakipApi.Olustur();
        var vm = new KrediTakipViewModel(vekil, Finans(), Auth(rol));
        await vm.YukleAsync(); vm.SecCommand.Execute(vm.Krediler[0]);
        api.Cagrilar.Clear();
        return (vm, api);
    }

    [Fact]
    public async Task Kart_durum_degisimi_aktifi_tersine_cevirir_secili_surum_ve_kirpilmis_gerekceyle_gider()
    {
        var (vm, api) = await KartVm(); vm.Gerekce = "  Kart kapatıldı  ";
        await vm.DurumDegistirAsync();
        var (id, ilk) = api.Tek<TakipDurumYaz>(nameof(IFinansTakipApi.TakipKartDurumAsync));
        Assert.Equal((1, false, 3, "Kart kapatıldı"), ((int)id[0]!, ilk.Aktif, ilk.Surum, ilk.Aciklama));
        Assert.NotEqual(Guid.Empty, ilk.IstekId);
        Assert.Equal((false, 4), (vm.Secili!.Aktif, vm.Secili.Surum)); Assert.Null(vm.Hata);

        // Sunucunun döndürdüğü güncel kartla ikinci değişim: bu kez açılır, yeni Surum ve yeni anahtar.
        api.Cagrilar.Clear(); vm.Gerekce = "Yeniden açıldı";
        await vm.DurumDegistirAsync();
        var (_, ikinci) = api.Tek<TakipDurumYaz>(nameof(IFinansTakipApi.TakipKartDurumAsync));
        Assert.Equal((true, 4), (ikinci.Aktif, ikinci.Surum));
        Assert.NotEqual(ilk.IstekId, ikinci.IstekId);
    }

    [Fact]
    public async Task Kart_durum_degisimi_bos_gerekceyle_ve_izleyiciyle_gonderilmez()
    {
        var (vm, api) = await KartVm(); vm.Gerekce = "   ";
        await vm.DurumDegistirAsync();
        Assert.Empty(api.Cagrilar); Assert.Contains("gerekçe", vm.Hata);

        var (izleyici, izleyiciApi) = await KartVm(Rol.Izleyici); izleyici.Gerekce = "Deneme";
        await izleyici.DurumDegistirAsync();
        Assert.Empty(izleyiciApi.Cagrilar); Assert.True(izleyici.Secili!.Aktif);
    }

    [Fact]
    public async Task Kart_durum_degisimi_ag_hatasindan_sonra_ayni_istek_anahtariyla_yeniden_denenir()
    {
        var (vm, api) = await KartVm(); vm.Gerekce = "Kart kapatıldı";
        api.SonrakiHata = new HttpRequestException();
        await vm.DurumDegistirAsync();
        Assert.Contains("ulaşılamadı", vm.Hata); Assert.True(vm.Secili!.Aktif);
        await vm.DurumDegistirAsync();
        var istekler = api.Hepsi<TakipDurumYaz>(nameof(IFinansTakipApi.TakipKartDurumAsync));
        Assert.Equal(2, istekler.Count); Assert.Equal(istekler[0], istekler[1]); Assert.False(istekler[1].Aktif);
        Assert.False(vm.Secili!.Aktif); Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Ekstre_duzeltmesi_secili_ekstreye_surum_asgari_ve_gerekceyle_gider()
    {
        var (vm, api) = await KartVm();
        vm.EkstreSecCommand.Execute(vm.Ekstreler.Single(e => e.Veri.Id == 7));
        vm.EkstreSonOdeme = new DateTime(2026, 10, 5); vm.AsgariVar = true; vm.AsgariTutar = 25; vm.Gerekce = " Banka asgarisi ";
        await vm.EkstreKaydetCommand.ExecuteAsync(null);
        var (a, g) = api.Tek<KartEkstreYaz>(nameof(IFinansTakipApi.TakipEkstreKaydetAsync));
        Assert.Equal((1, 7), ((int)a[0]!, (int)a[1]!));
        Assert.Equal((3, new DateOnly(2026, 10, 5), (decimal?)25, "Banka asgarisi"), (g.Surum, g.SonOdemeTarihi, g.AsgariOdeme, g.Aciklama));
        Assert.NotEqual(Guid.Empty, g.IstekId); Assert.Null(vm.DuzenlenenEkstre);

        // Asgari yoksa null gider (0 değil): sunucu "asgari bilinmiyor" ile "asgari 0"ı ayırır.
        api.Cagrilar.Clear(); vm.EkstreSecCommand.Execute(vm.Ekstreler.Single()); vm.AsgariVar = false; vm.AsgariTutar = 25;
        await vm.EkstreKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.Tek<KartEkstreYaz>(nameof(IFinansTakipApi.TakipEkstreKaydetAsync)).Govde.AsgariOdeme);
    }

    [Fact]
    public async Task Ekstre_duzeltmesi_bos_gerekceyle_gonderilmez_ag_hatasinda_ayni_anahtarla_yinelenir()
    {
        var (vm, api) = await KartVm();
        vm.EkstreSecCommand.Execute(vm.Ekstreler.Single()); vm.AsgariVar = true; vm.AsgariTutar = 25; vm.Gerekce = "";
        await vm.EkstreKaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.Cagrilar); Assert.Contains("gerekçe", vm.Hata);

        vm.Gerekce = "Banka asgarisi"; api.SonrakiHata = new HttpRequestException();
        await vm.EkstreKaydetCommand.ExecuteAsync(null);
        Assert.NotNull(vm.DuzenlenenEkstre);
        await vm.EkstreKaydetCommand.ExecuteAsync(null);
        var istekler = api.Hepsi<KartEkstreYaz>(nameof(IFinansTakipApi.TakipEkstreKaydetAsync));
        Assert.Equal(2, istekler.Count); Assert.Equal(istekler[0].IstekId, istekler[1].IstekId); Assert.Equal(istekler[0], istekler[1]);
    }

    [Fact]
    public async Task Odeme_ve_harcama_iptali_kayit_kimligi_surum_ve_gerekceyle_ayri_anahtarlarla_gider()
    {
        var (vm, api) = await KartVm(); vm.Gerekce = " Yanlış kayıt ";
        await vm.OdemeIptalAsync(vm.Odemeler.Single(o => o.Veri.Id == 21));
        var (oa, odeme) = api.Tek<TakipIptalYaz>(nameof(IFinansTakipApi.TakipOdemeIptalAsync));
        Assert.Equal((1, 21, 3, "Yanlış kayıt"), ((int)oa[0]!, (int)oa[1]!, odeme.Surum, odeme.Aciklama));

        api.Cagrilar.Clear();
        await vm.HarcamaIptalAsync(vm.Harcamalar.Single(h => h.Veri.Id == 11));
        var (ha, harcama) = api.Tek<TakipIptalYaz>(nameof(IFinansTakipApi.TakipHarcamaIptalAsync));
        Assert.Equal((1, 11, 4), ((int)ha[0]!, (int)ha[1]!, harcama.Surum)); // ödeme iptalinden dönen kartın sürümü
        Assert.NotEqual(Guid.Empty, harcama.IstekId); Assert.NotEqual(odeme.IstekId, harcama.IstekId);
    }

    [Fact]
    public async Task Iptal_bos_gerekceyle_ve_ekstre_kaydinda_gonderilmez_ag_hatasinda_ayni_anahtarla_yinelenir()
    {
        var (vm, api) = await KartVm(); vm.Gerekce = " ";
        await vm.HarcamaIptalAsync(vm.Harcamalar.Single(h => h.Veri.Id == 11));
        Assert.Empty(api.Cagrilar); Assert.Contains("gerekçe", vm.Hata);

        vm.Gerekce = "Yanlış kayıt";
        await vm.OdemeIptalAsync(vm.Odemeler.Single(o => o.Veri.Id == 22));
        await vm.HarcamaIptalAsync(vm.Harcamalar.Single(h => h.Veri.Id == 12));
        Assert.Empty(api.Cagrilar); Assert.Contains("Ekstre İçe Aktar", vm.Hata);

        api.SonrakiHata = new HttpRequestException();
        await vm.OdemeIptalAsync(vm.Odemeler.Single(o => o.Veri.Id == 21));
        Assert.Contains("ulaşılamadı", vm.Hata);
        await vm.OdemeIptalAsync(vm.Odemeler.Single(o => o.Veri.Id == 21));
        var istekler = api.Hepsi<TakipIptalYaz>(nameof(IFinansTakipApi.TakipOdemeIptalAsync));
        Assert.Equal(2, istekler.Count); Assert.Equal(istekler[0], istekler[1]); Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Kredi_durum_degisimi_aktifi_tersine_cevirir_bos_gerekceyi_reddeder_ayni_anahtarla_yinelenir()
    {
        var (vm, api) = await KrediVm(); vm.Gerekce = "";
        await vm.DurumDegistirAsync();
        Assert.Empty(api.Cagrilar); Assert.Contains("gerekçe", vm.Hata);

        vm.Gerekce = " Kredi kapandı, arşive "; api.SonrakiHata = new HttpRequestException();
        await vm.DurumDegistirAsync();
        Assert.True(vm.Secili!.Aktif);
        await vm.DurumDegistirAsync();
        var istekler = api.Hepsi<TakipDurumYaz>(nameof(IFinansTakipApi.TakipKrediDurumAsync));
        Assert.Equal(2, istekler.Count); Assert.Equal(istekler[0], istekler[1]);
        Assert.Equal((false, 3, "Kredi kapandı, arşive"), (istekler[0].Aktif, istekler[0].Surum, istekler[0].Aciklama));
        Assert.Equal(2, (int)api.Cagrilar.Last().Argumanlar[0]!);
        Assert.Equal((false, 4), (vm.Secili!.Aktif, vm.Secili.Surum));

        api.Cagrilar.Clear(); vm.Gerekce = "Yeniden izlemeye";
        await vm.DurumDegistirAsync();
        var (_, geri) = api.Tek<TakipDurumYaz>(nameof(IFinansTakipApi.TakipKrediDurumAsync));
        Assert.Equal((true, 4), (geri.Aktif, geri.Surum)); Assert.NotEqual(istekler[0].IstekId, geri.IstekId);
    }

    [Fact]
    public async Task Izleyici_kredi_durumunu_degistiremez()
    {
        var (vm, api) = await KrediVm(Rol.Izleyici); vm.Gerekce = "Deneme";
        await vm.DurumDegistirAsync();
        Assert.Empty(api.Cagrilar);
    }

    /// <summary>
    /// Kaydeden sahte IFinansTakipApi: her çağrıyı (metot, argümanlar) sırasıyla tutar ve sunucu gibi yanıtlar. Durum
    /// komutu Aktif'i istekteki değere çeker; her yazma kaydın Surum'unu bir artırır. <see cref="SonrakiHata"/> doluysa
    /// sonraki yazma çağrısı kaydedilir ve o hatayla düşer (ağ hatası). Beklenmeyen metot çağrısı testi düşürür.
    /// </summary>
    public class KaydedenTakipApi : DispatchProxy
    {
        public List<(string Metot, object?[] Argumanlar)> Cagrilar { get; } = [];
        public KartTakipDto KartKaydi { get; set; } = Kart();
        public KrediTakipDto KrediKaydi { get; set; } = Kredi();
        public Exception? SonrakiHata { get; set; }

        public static (IFinansTakipApi Vekil, KaydedenTakipApi Kayit) Olustur()
        {
            var vekil = Create<IFinansTakipApi, KaydedenTakipApi>();
            return (vekil, (KaydedenTakipApi)(object)vekil);
        }

        public (object?[] Argumanlar, T Govde) Tek<T>(string metot)
        {
            var cagri = Assert.Single(Cagrilar, c => c.Metot == metot);
            Assert.Single(Cagrilar);
            return (cagri.Argumanlar, Assert.IsType<T>(cagri.Argumanlar[^1]));
        }

        public List<T> Hepsi<T>(string metot)
        {
            Assert.All(Cagrilar, c => Assert.Equal(metot, c.Metot));
            return Cagrilar.Select(c => Assert.IsType<T>(c.Argumanlar[^1])).ToList();
        }

        protected override object? Invoke(MethodInfo? metot, object?[]? argumanlar)
        {
            ArgumentNullException.ThrowIfNull(metot);
            var a = argumanlar ?? [];
            if (metot.Name == nameof(IFinansTakipApi.TakipKartlarAsync)) return Task.FromResult<IReadOnlyList<KartTakipDto>>([KartKaydi]);
            if (metot.Name == nameof(IFinansTakipApi.TakipKredilerAsync)) return Task.FromResult<IReadOnlyList<KrediTakipDto>>([KrediKaydi]);
            Cagrilar.Add((metot.Name, a));
            if (SonrakiHata is { } hata)
            {
                SonrakiHata = null;
                return metot.ReturnType == typeof(Task<KrediTakipDto>) ? Task.FromException<KrediTakipDto>(hata) : Task.FromException<KartTakipDto>(hata);
            }
            return metot.Name switch
            {
                nameof(IFinansTakipApi.TakipKartDurumAsync) => Task.FromResult(KartKaydi = KartKaydi with { Aktif = ((TakipDurumYaz)a[1]!).Aktif, Surum = KartKaydi.Surum + 1 }),
                nameof(IFinansTakipApi.TakipEkstreKaydetAsync) or nameof(IFinansTakipApi.TakipOdemeIptalAsync) or nameof(IFinansTakipApi.TakipHarcamaIptalAsync)
                    => Task.FromResult(KartKaydi = KartKaydi with { Surum = KartKaydi.Surum + 1 }),
                nameof(IFinansTakipApi.TakipKrediDurumAsync) => Task.FromResult(KrediKaydi = KrediKaydi with { Aktif = ((TakipDurumYaz)a[1]!).Aktif, Surum = KrediKaydi.Surum + 1 }),
                _ => throw new InvalidOperationException($"Beklenmeyen çağrı: {metot.Name}"),
            };
        }
    }
}
