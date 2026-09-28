using System.Reflection;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Kart ve kredi takibinin yazma komutları (tests-10, ViewModel tarafı): kart kaydetme ve düzenleme, kart ve kredi durum
/// değişimi, ekstre asgari/son ödeme düzeltmesi, ödeme ve harcama iptali. Kaydeden sahte API her çağrının gövdesini
/// tutar: yeni kartta kimliksiz (null) istek ve Surum 0, düzenlemede seçili kartın kimliği ve Surum'u, kırpılmış ad ve
/// gerekçe, gönderilen Aktif değerinin seçili kaydın tersi olduğu, rota kimlikleri, boş alanın ve izleyicinin reddi, ağ
/// hatasından sonra aynı IstekId ve aynı gövdeyle yeniden deneme ve başarıdan sonra yeni anahtar doğrulanır.
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

    private static string Json(object deger) => System.Text.Json.JsonSerializer.Serialize(deger);

    [Fact]
    public async Task Yeni_kart_kimliksiz_surum_sifirla_kirpilmis_ad_acilis_alanlari_ve_paylarla_kaydedilir()
    {
        var (vm, api) = await KartVm();
        vm.YeniCommand.Execute(null);
        vm.Ad = "  Yeni kart  "; vm.Limit = 5000; vm.KesimGunu = 5; vm.SonOdemeGunu = 15; vm.AcilisTarihi = new DateTime(2026, 9, 1); vm.AcilisBorc = 120;
        vm.PayEkle(vm.AcilisPaylari); vm.AcilisPaylari[0].Kanal = vm.Kanallar[0]; vm.AcilisPaylari[0].Tutar = 120;
        await vm.KaydetCommand.ExecuteAsync(null);
        var (a, g) = api.Tek<KartTakipYaz>(nameof(IFinansTakipApi.TakipKartKaydetAsync));
        Assert.Null(a[0]);
        Assert.Equal((0, "Yeni kart", 5000m, 5, 15, new DateOnly(2026, 9, 1), 120m), (g.Surum, g.Ad, g.Limit, g.KesimGunu, g.SonOdemeGunu, g.AcilisTarihi, g.AcilisBorc));
        Assert.Equal(new[] { new KanalPayYaz(1, 120) }, g.AcilisDagilimlari);
        Assert.NotEqual(Guid.Empty, g.IstekId);
        // Sunucunun döndürdüğü yeni kart listeye eklenir ve seçilir; önceki kart yerinde kalır.
        Assert.Equal((KaydedenTakipApi.YeniKartId, 1), (vm.Secili!.Id, vm.Secili.Surum));
        Assert.Equal(new[] { 1, KaydedenTakipApi.YeniKartId }, vm.Kartlar.Select(k => k.Veri.Id));
        Assert.Equal("Kart kaydedildi.", vm.Mesaj); Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Secili_kart_duzenlenirken_kimligi_surumu_ve_kirpilmis_adiyla_gider_ikinci_kart_olusmaz()
    {
        var (vm, api) = await KartVm();
        vm.Ad = "  Kart (düzenlendi)  "; vm.Limit = 2500; vm.KesimGunu = 12; vm.SonOdemeGunu = 22; vm.AcilisTarihi = new DateTime(2026, 9, 1);
        await vm.KaydetCommand.ExecuteAsync(null);
        var (a, ilk) = api.Tek<KartTakipYaz>(nameof(IFinansTakipApi.TakipKartKaydetAsync));
        Assert.Equal(1, (int)a[0]!); // null gitseydi sunucu ikinci bir kart açardı
        Assert.Equal((3, "Kart (düzenlendi)", 2500m, 12, 22, 0m), (ilk.Surum, ilk.Ad, ilk.Limit, ilk.KesimGunu, ilk.SonOdemeGunu, ilk.AcilisBorc));
        Assert.NotEqual(Guid.Empty, ilk.IstekId);
        Assert.Equal((1, 4, "Kart (düzenlendi)"), (vm.Secili!.Id, vm.Secili.Surum, Assert.Single(vm.Kartlar).Veri.Ad));

        // İkinci düzenleme sunucunun döndürdüğü kartın sürümüyle ve yeni istek anahtarıyla gider.
        api.Cagrilar.Clear(); vm.Limit = 3000;
        await vm.KaydetCommand.ExecuteAsync(null);
        var (a2, ikinci) = api.Tek<KartTakipYaz>(nameof(IFinansTakipApi.TakipKartKaydetAsync));
        Assert.Equal((1, 4, 3000m), ((int)a2[0]!, ikinci.Surum, ikinci.Limit));
        Assert.NotEqual(ilk.IstekId, ikinci.IstekId);
        Assert.Equal(5, vm.Secili!.Surum); Assert.Single(vm.Kartlar);
    }

    [Fact]
    public async Task Kart_kaydi_ag_hatasindan_sonra_ayni_kimlik_ayni_istek_anahtari_ve_ayni_govdeyle_yinelenir()
    {
        var (vm, api) = await KartVm(); vm.Ad = "Kart"; vm.Limit = 1500; vm.AcilisTarihi = new DateTime(2026, 9, 1);
        api.SonrakiHata = new HttpRequestException();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Contains("ulaşılamadı", vm.Hata); Assert.Equal(3, vm.Secili!.Surum);
        await vm.KaydetCommand.ExecuteAsync(null);
        var duzenleme = api.Hepsi<KartTakipYaz>(nameof(IFinansTakipApi.TakipKartKaydetAsync));
        Assert.Equal(2, duzenleme.Count); Assert.Equal(Json(duzenleme[0]), Json(duzenleme[1])); Assert.Equal(3, duzenleme[1].Surum);
        Assert.All(api.Cagrilar, c => Assert.Equal(1, (int)c.Argumanlar[0]!));
        Assert.Equal(4, vm.Secili!.Surum); Assert.Null(vm.Hata);

        // Yeni kart: ilk istek sunucuya ulaşıp yanıtı kaybolduysa yeni anahtarla yineleme ikinci bir kart açardı.
        // Yineleme yine kimliksiz, aynı anahtar ve aynı gövdeyle gider (sunucu tekrarı tanır).
        api.Cagrilar.Clear(); vm.YeniCommand.Execute(null);
        vm.Ad = "Yeni kart"; vm.Limit = 800; vm.AcilisTarihi = new DateTime(2026, 9, 1); vm.AcilisBorc = 50;
        vm.PayEkle(vm.AcilisPaylari); vm.AcilisPaylari[0].Kanal = vm.Kanallar[0]; vm.AcilisPaylari[0].Tutar = 50;
        api.SonrakiHata = new HttpRequestException();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Contains("ulaşılamadı", vm.Hata); Assert.Null(vm.Secili);
        await vm.KaydetCommand.ExecuteAsync(null);
        var yeni = api.Hepsi<KartTakipYaz>(nameof(IFinansTakipApi.TakipKartKaydetAsync));
        Assert.Equal(2, yeni.Count); Assert.Equal(Json(yeni[0]), Json(yeni[1])); Assert.Equal(0, yeni[1].Surum);
        Assert.All(api.Cagrilar, c => Assert.Null(c.Argumanlar[0]));
        Assert.NotEqual(duzenleme[0].IstekId, yeni[0].IstekId);
        Assert.Equal(KaydedenTakipApi.YeniKartId, vm.Secili!.Id); Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Kart_kaydi_bos_adla_gecersiz_gunle_ve_izleyiciyle_gonderilmez()
    {
        var (vm, api) = await KartVm(); vm.Ad = "   ";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.Cagrilar); Assert.Contains("Kart adını", vm.Hata);
        vm.Ad = "Kart"; vm.KesimGunu = 32;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.Cagrilar); Assert.Contains("1–31", vm.Hata);

        var (izleyici, izleyiciApi) = await KartVm(Rol.Izleyici); izleyici.Ad = "Kart 2";
        await izleyici.KaydetCommand.ExecuteAsync(null);
        Assert.Empty(izleyiciApi.Cagrilar); Assert.Equal("Kart", izleyici.Secili!.Ad);
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
        public const int YeniKartId = 50;
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
                nameof(IFinansTakipApi.TakipKartKaydetAsync) => Task.FromResult(KartKaydet((int?)a[0], (KartTakipYaz)a[1]!)),
                nameof(IFinansTakipApi.TakipKartDurumAsync) =>Task.FromResult(KartKaydi = KartKaydi with { Aktif = ((TakipDurumYaz)a[1]!).Aktif, Surum = KartKaydi.Surum + 1 }),
                nameof(IFinansTakipApi.TakipEkstreKaydetAsync) or nameof(IFinansTakipApi.TakipOdemeIptalAsync) or nameof(IFinansTakipApi.TakipHarcamaIptalAsync)
                    => Task.FromResult(KartKaydi = KartKaydi with { Surum = KartKaydi.Surum + 1 }),
                nameof(IFinansTakipApi.TakipKrediDurumAsync) => Task.FromResult(KrediKaydi = KrediKaydi with { Aktif = ((TakipDurumYaz)a[1]!).Aktif, Surum = KrediKaydi.Surum + 1 }),
                _ => throw new InvalidOperationException($"Beklenmeyen çağrı: {metot.Name}"),
            };
        }

        /// <summary>Kimliksiz kayıt sunucu gibi yeni kimlikli kart açar (Surum 1; seçili kart değişmez); kimlikli kayıt
        /// yalnız o kartın ad, limit ve günlerini günceller ve Surum'u artırır. Bilinmeyen kimlik testi düşürür.</summary>
        private KartTakipDto KartKaydet(int? id, KartTakipYaz g)
        {
            if (id is not null && id != KartKaydi.Id) throw new InvalidOperationException($"Bilinmeyen kart: {id}");
            var temel = id is null ? KartKaydi with { Id = YeniKartId, Surum = 0, Ekstreler = [], Harcamalar = [], Odemeler = [] } : KartKaydi;
            var sonuc = temel with { Surum = temel.Surum + 1, Ad = g.Ad, Limit = g.Limit, KesimGunu = g.KesimGunu, SonOdemeGunu = g.SonOdemeGunu };
            if (id is not null) KartKaydi = sonuc;
            return sonuc;
        }
    }
}
