using System.Net;
using System.Text;
using Kasa.ApiClient;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Paket B ve F'nin öteki paketlerle birleşiminde bulunan uygulama tarafı hatalar (entegrasyon incelemesi):
/// kilitli ayda gelen yazımı (B × C), Geçmiş sayfasının DI kurucusu (A × B), silinen işlemin ek paneli ve
/// Esc (C × F), POS satışında iki basışla silme (C × F), çek CSV'sinin tür/konum süzgeci (B × D).
/// </summary>
public class EntegrasyonBfVmTests
{
    private const string KilitMesaji =
        "Ağustos 2026 kilitli: bu aya ait kayıt eklenemez, değiştirilemez ya da silinemez. Editör Aylık rapordan ayın kilidini açabilir.";

    /// <summary>Kilitli ayı taklit eden sunucu: gelen tablosu okunur, PUT /api/gelenler kilit 409'u döner.</summary>
    private sealed class KilitliSunucu : HttpMessageHandler
    {
        public int PutSayisi;
        public int GelenOkumaSayisi;

        private static HttpResponseMessage Json(HttpStatusCode kod, string json)
            => new(kod) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var yol = r.RequestUri!.AbsolutePath;
            if (r.Method == HttpMethod.Put && yol == "/api/gelenler")
            {
                PutSayisi++;
                return Task.FromResult(Json(HttpStatusCode.Conflict, $$"""{"hata":"{{KilitMesaji}}","kilitli":true}"""));
            }
            if (yol == "/api/gelenler/tablo")
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {"donemStart":"2026-08-24","donemEnd":"2026-08-30","onceki":null,"sonraki":null,
                     "satirlar":[{"kanal":"MEZAT","aktif":true,"tutarTl":null,"gelenId":null}]}
                    """));
            if (yol == "/api/gelenler/eksik-liste") return Task.FromResult(Json(HttpStatusCode.OK, "[]"));
            if (yol == "/api/gelenler") { GelenOkumaSayisi++; return Task.FromResult(Json(HttpStatusCode.OK, "[]")); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>
    /// Bulgu (yüksek): kilit 409'unda mevcutTutar yok; istemci bunu eşzamanlı ekleme sayıp "Siz açtıktan sonra
    /// değişmiş: kayıtlı 0,00 ₺" diyordu, "Üzerine yaz" aynı 409'u alıyordu ve "kilitli" hiç görünmüyordu.
    /// Gerçek istemciyle: kilit mesajı hata olarak görünür, satır çakışma sayılmaz, tekrar okuma yapılmaz.
    /// </summary>
    [Fact]
    public async Task Gelen_tablosu_kilitli_ayda_cakisma_degil_kilit_mesaji_gosterir()
    {
        var sunucu = new KilitliSunucu();
        var api = new KasaApiClient(new HttpClient(sunucu) { BaseAddress = new Uri("https://ornek.test/") }, new BellekTokenStore());
        var vm = new GelenlerViewModel(api, new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0))) { EditorMu = true };
        await vm.YukleAsync();
        Assert.Null(vm.Hata);

        var satir = vm.Satirlar.Single();
        satir.GirisMetni = "150";
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(KilitMesaji, vm.Hata);
        Assert.False(satir.Cakisma);
        Assert.DoesNotContain("değişmiş", satir.Durum ?? "");
        Assert.True(string.IsNullOrEmpty(vm.Sonuc));
        Assert.Equal(1, sunucu.PutSayisi);
        Assert.Equal(0, sunucu.GelenOkumaSayisi);        // "güncel değeri yeniden oku" yoluna girilmedi
        Assert.Equal("150", satir.GirisMetni);             // yazılan kaybolmaz (kilit açılınca yeniden kaydedilir)
    }

    [Fact]
    public async Task Islemler_gelen_formu_kilitli_ayda_uzerine_yaz_sorusu_acmaz()
    {
        var api = new SahteApi
        {
            KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0m)],
            DonemlerListe = [new DonemDto(new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 30), 2026, 8)],
            GelenKorumaliHatasi = new KasaApiException(HttpStatusCode.Conflict, KilitMesaji),
        };
        var vm = new IslemlerViewModel(api, new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0))) { EditorMu = true };
        await vm.YukleAsync();
        vm.GelenTarih = new DateTime(2026, 8, 25); vm.GelenKanal = "MEZAT"; vm.GelenTutar = 500m;

        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.Equal(KilitMesaji, vm.Hata);
        Assert.False(vm.GelenCakismaVar);
        Assert.Single(api.KorumaliGelenCagrilari);
        Assert.Equal(("MEZAT", 500m), (vm.GelenKanal, vm.GelenTutar));   // form temizlenmedi
    }

    /// <summary>
    /// Bulgu (yüksek): paket A'nın (IKasaApi, TimeProvider?, IYerelDepo?) ve paket B'nin (IKasaApi, TimeProvider?,
    /// IDosyaKaydedici?) kurucuları Windows'taki DI kayıtlarıyla belirsizdi: Geçmiş sayfası hiç açılmıyordu.
    /// </summary>
    [Fact]
    public async Task Gecmis_vm_depo_ve_kaydediciyle_diden_kurulur_ikisini_de_kullanir()
    {
        var depo = new BellekYerelDepo();
        var kaydedici = new SahteKaydedici();
        // MauiProgram'daki gibi: saat, cihaz deposu (paket A) ve dosya kaydedici (paket B, Windows) kayıtlı.
        var s = new ServiceCollection();
        s.AddSingleton<TimeProvider>(new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0)));
        s.AddSingleton<IKasaApi>(new SahteApi());
        s.AddSingleton<IYerelDepo>(depo);
        s.AddSingleton<IDosyaKaydedici>(kaydedici);
        s.AddTransient<GecmisViewModel>();
        using var sp = s.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        var vm = sp.GetRequiredService<GecmisViewModel>();
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Single(kaydedici.Kaydedilenler);            // paket B'nin kaydedicisi geldi
        await vm.YukleAsync();
        Assert.NotNull(depo.Oku(YerelAnahtarlar.GecmisSonGorulenId));   // paket A'nın cihaz deposu da
    }

    /// <summary>
    /// Aynı sayıda parametreli iki kurucu MAUI DI'da "ambiguous constructors" hatasıdır (ikisi de karşılanabiliyorsa).
    /// Paketler görünüm modellerine ayrı kısmi dosyalardan kurucu eklediği için hepsi denetlenir.
    /// </summary>
    [Fact]
    public void Hicbir_gorunum_modelinde_esit_uzunlukta_iki_kurucu_yok()
    {
        var sorunlu = typeof(TemelViewModel).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && t.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            .SelectMany(t => t.GetConstructors()
                .GroupBy(c => c.GetParameters().Length)
                .Where(g => g.Count() > 1)
                .Select(g => t.Name + ": " + string.Join(" | ", g.Select(c => string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name))))))
            .ToList();
        Assert.Empty(sorunlu);
    }

    // ---------------------------------------------------------------- C × F

    private static EkDto Ek(int id, int islemId, string ad) => new(id, islemId, ad, "image/jpeg", 1234,
        new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc));

    private static IslemDto Islem(int id) => new(id, new DateOnly(2026, 9, 20), "X", 100m, "MEZAT", GiderTipi.Cari, null) { EkSayisi = 1 };

    /// <summary>Bulgu: arayüzün kullandığı iki basışlı silme, silinen işlemin açık ek panelini kapatmıyordu.</summary>
    [Fact]
    public async Task Onayli_silme_silinen_islemin_ek_panelini_kapatir()
    {
        var api = new SahteApi { IslemlerListe = [Islem(42), Islem(43)] };
        api.EklerSozluk[42] = [Ek(7, 42, "fis.jpg")];
        var vm = new IslemlerViewModel(api, new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0))) { EditorMu = true };
        await vm.YukleAsync();
        await vm.ListeEkleriGosterCommand.ExecuteAsync(vm.Islemler.Single(i => i.Id == 42));
        Assert.True(vm.ListeEkPaneliGorunur);

        // Başka işlemin silinmesi paneli kapatmaz.
        var baska = vm.Islemler.Single(i => i.Id == 43);
        await vm.OnayliSilCommand.ExecuteAsync(baska);
        await vm.OnayliSilCommand.ExecuteAsync(baska);
        Assert.Equal(43, api.SonIslemSil);
        Assert.True(vm.ListeEkPaneliGorunur);

        var silinen = vm.Islemler.Single(i => i.Id == 42);
        await vm.OnayliSilCommand.ExecuteAsync(silinen);
        await vm.OnayliSilCommand.ExecuteAsync(silinen);
        Assert.Equal(42, api.SonIslemSil);
        Assert.False(vm.ListeEkPaneliGorunur);
        Assert.Empty(vm.ListeEkleri);
    }

    /// <summary>Bulgu: "Eki sil?" açıkken Esc düzenlemeyi (yazılan belge no, bekleyen dosya) sessizce atıyordu.</summary>
    [Fact]
    public async Task Esc_once_ek_silme_onayini_kapatir_duzenleme_kalir()
    {
        var api = new SahteApi();
        api.EklerSozluk[42] = [Ek(7, 42, "fis.jpg")];
        var vm = new IslemlerViewModel(api, new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0))) { EditorMu = true };
        vm.DuzenleCommand.Execute(Islem(42));
        await Task.Yield();
        vm.DuzenBelgeNo = "F-12";
        vm.BekleyenEkler.Add(new BekleyenEk("yeni.jpg", [1, 2, 3]));
        vm.EkSilIsteCommand.Execute(vm.MevcutEkler.Single());
        Assert.True(vm.EkSilmeOnayiBekliyor);

        var esc = KlavyeKisayollari.Coz("Escape", ctrl: false, alt: false);
        Assert.True(vm.KisayolCalistir(esc));
        Assert.False(vm.EkSilmeOnayiBekliyor);
        Assert.Equal(42, vm.DuzenId);
        Assert.Equal("F-12", vm.DuzenBelgeNo);
        Assert.Single(vm.BekleyenEkler);
        Assert.Empty(api.SilinenEkler);

        Assert.True(vm.KisayolCalistir(esc));             // ikinci Esc düzenlemeden çıkar (eskisi gibi)
        Assert.Equal(0, vm.DuzenId);
    }

    /// <summary>Bulgu: POS satırındaki "Sil" tek basışta, onaysız siliyordu (C'nin iki basış kuralı dışında).</summary>
    [Fact]
    public async Task Pos_satisi_iki_basisla_silinir_geri_alinamaz_der()
    {
        var api = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 1, 0m)] };
        api.PosTanimlariListe.Add(new PosTanimDto(1, "Garanti", PosSaglayici.BankaPosu, 1, "MEZAT", 1.79m, 1, true));
        api.PosSatislariListe.Add(new PosSatisDto(5, new DateOnly(2026, 9, 20), 1, "Garanti", "MEZAT", 1000m, 1.79m,
            PosOnizleme.Komisyon(1000m, 1.79m), PosOnizleme.Net(1000m, 1.79m), 1, new DateOnly(2026, 9, 21), false, null));
        var saat = new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0));
        var vm = new PosViewModel(api, saat) { EditorMu = true };
        await vm.YukleAsync();
        var satis = vm.Satislar.Single();

        await vm.OnayliSatisSilCommand.ExecuteAsync(satis);
        Assert.Null(api.SonPosSatisSil);
        Assert.Equal(satis, vm.Silme.Bekleyen);
        Assert.Equal(SilmeOnayi.OnayDugmesiGeriAlinmaz, SilmeOnayi.DugmeMetni(satis, vm.Silme.Bekleyen, vm.Silme.OnayDugmesi));

        saat.Ilerle(TimeSpan.FromSeconds(6));               // süre geçti: yeniden onay ister
        await vm.OnayliSatisSilCommand.ExecuteAsync(satis);
        Assert.Null(api.SonPosSatisSil);

        await vm.OnayliSatisSilCommand.ExecuteAsync(satis);
        Assert.Equal(5, api.SonPosSatisSil);
        Assert.False(vm.Silme.OnayBekliyor);
        Assert.False(vm.Silme.SeritGorunur);               // POS satışı geçmişten geri getirilemez: şerit yok
    }

    // ---------------------------------------------------------------- B × D

    /// <summary>Bulgu: Çekler sayfasının "Excel'e aktar"ı ekrandaki tür/konum süzgecini göndermiyordu.</summary>
    [Fact]
    public async Task Cek_excel_aktarimi_tur_ve_konum_suzgecini_gonderir()
    {
        var api = new SahteApi();
        var vm = new CeklerViewModel(api, new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0)), new SahteKaydedici())
        {
            FiltreTur = CekTuru.Senet,
            FiltreKonum = CekKonumu.BankadaTahsilde,
        };
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal((CekTuru.Senet, CekKonumu.BankadaTahsilde), api.SonCekCsvEvrak!.Value);
    }
}
