using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class YeniAkisTests
{
    private static AuthViewModel Auth(bool editor = true) => new(new SahteApi()) { AktifRol = editor ? Rol.Editor : Rol.Izleyici };
    private static readonly DateOnly Bugun = new(2026, 9, 23);
    private static AlisDto Alis(int id = 7) => new(id, 3, null, "Editör", Bugun, "Firma", null, "Taslak", null, 100, 10, 90,
        new[] { new AlisKalemDto(1, "Mal", 100, new[] { new AlisDagilimDto(1, "MEZAT", 100) }) },
        new[] { new AlisOdemeDto(4, 20, Bugun, 10, null, true, Array.Empty<AlisDagilimDto>()) }, 2, Bugun.AddDays(10));
    private static async Task<AlislarViewModel> AlisVm(Fake api, SahteApi? finans = null, IBenzerKayitApi? benzerlik = null, AuthViewModel? auth = null)
    {
        var vm = new AlislarViewModel(api, finans ?? new SahteApi(), auth ?? TestOturumu.Ac(), api, api, benzerlik);
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar.Single(a => a.Veri.Id == 7));
        return vm;
    }
    [Fact]
    public async Task Odeme_tasima_hedef_surumu_kullanir_ve_yanit_kaynak_alisi_korur()
    {
        var api = new Fake();
        var vm = await AlisVm(api);
        vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]);
        vm.HedefAlis = vm.DuzeltmeHedefleri[0];
        vm.DuzeltmeAciklamasi = "Yanlış alışa bağlandı";
        await vm.OdemeDuzeltKaydetCommand.ExecuteAsync(null);
        Assert.Equal(8, api.SonDuzelt!.HedefAlisId);
        Assert.Equal(3, api.SonDuzelt.HedefSurum);
        Assert.Equal(7, vm.Secili!.Id);
        Assert.Contains("taşındı", vm.Mesaj);
    }
    [Fact]
    public async Task Odeme_duzeltme_tutari_gecersizken_gonderilmez()
    {
        var api = new Fake();
        var vm = await AlisVm(api);
        vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]);
        vm.DuzeltmeAciklamasi = "Tutar düzeltildi";
        vm.DuzeltmeTutari = ParaAyristirici.Gecersiz;
        await vm.OdemeDuzeltKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonDuzelt);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);
    }
    [Fact]
    public async Task Odeme_duzeltme_ag_hatasinda_ayni_anahtarla_tekrarlanir()
    {
        var api = new Fake { DuzeltHata = true };
        var vm = await AlisVm(api);
        vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]);
        vm.DuzeltmeAciklamasi = "Tarih düzeltildi";
        await vm.OdemeDuzeltKaydetCommand.ExecuteAsync(null);
        var ilk = api.SonDuzelt;
        api.DuzeltHata = false;
        await vm.OdemeDuzeltKaydetCommand.ExecuteAsync(null);
        Assert.Equal(ilk!.IstekId, api.SonDuzelt!.IstekId);
    }
    [Fact]
    public async Task Iptal_gerekce_olmadan_gonderilmez()
    {
        var api = new Fake();
        var vm = await AlisVm(api);
        vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]);
        await vm.OdemeIptalAsync();
        Assert.Null(api.SonIptal);
        Assert.Contains("nedenini", vm.Hata);
        vm.DuzeltmeAciklamasi = "Hatalı kayıt";
        await vm.OdemeIptalAsync();
        Assert.Equal("Hatalı kayıt", api.SonIptal!.Aciklama);
    }
    [Fact]
    public async Task Karti_silinmis_eski_harcama_nakit_olarak_gosterilmez_ve_hesaba_baglanmaz()
    {
        // Bağlı gider bağlanabilir listede yoktur: eski kart harcaması bilgisi ödemenin kendisinden (sunucu) gelir.
        var ilk = Alis();
        var api = new Fake { IlkAlis = ilk with { Odemeler = new[] { ilk.Odemeler[0] with { EskiKartHarcamasi = true } } } };
        var vm = await AlisVm(api);
        vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]);
        Assert.True(vm.EskiKartHarcamasi);
        Assert.Contains("Eski kart", vm.DuzeltmeKarti!.Ad);
        vm.DuzeltmeAciklamasi = "Tarih düzeltildi";
        await vm.OdemeDuzeltKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonDuzelt!.HesapId);
    }
    [Fact]
    public async Task Oturum_degistiginde_eski_belge_listesi_yansitilmaz()
    {
        var bekleyen = new TaskCompletionSource<IReadOnlyList<BelgeDto>>();
        var api = new Fake { BelgeYaniti = bekleyen.Task };
        var auth = TestOturumu.Ac();
        var vm = await AlisVm(api, auth: auth);
        var islem = vm.BelgeleriYukleAsync();
        TestOturumu.YeniOturum(auth, Rol.Alici);
        bekleyen.SetResult(new[] { new BelgeDto(1, 7, null, "eski.pdf", "application/pdf", 10, DateTimeOffset.UtcNow) });
        await islem;
        Assert.Empty(vm.Belgeler);
        Assert.Null(vm.Secili);
    }
    [Fact]
    public async Task Kisa_editor_sifresi_ve_ters_rapor_tarihi_apiye_gitmez()
    {
        var api = new Fake();
        var vm = new GuvenlikViewModel(api, Auth()) { MevcutSifre = "eski", YeniSifre = "12345678" };
        await vm.SifreDegistirCommand.ExecuteAsync(null);
        Assert.False(api.SifreDegisti);
        Assert.Contains("12", vm.Hata);
        var rapor = new DisariAktarViewModel(api, new SahteApi(), Auth()) { Baslangic = new(2026, 10, 1), Bitis = new(2026, 9, 1) };
        Assert.Null(await rapor.IndirAsync("xlsx", new MemoryStream()));
        Assert.False(api.RaporIndirildi);
    }
    [Fact]
    public async Task Yedek_bellege_alinmadan_hedef_akisa_yazilir_ve_boyutuyla_bildirilir()
    {
        var vm = new GuvenlikViewModel(new Fake(), Auth());
        var hedef = new MemoryStream();
        var bilgi = await vm.YedekIndirAsync(hedef);
        Assert.Equal(new byte[] { 1, 2, 3 }, hedef.ToArray());
        Assert.Equal("yedek.zip", bilgi!.DosyaAdi);
        Assert.Contains("Yedek indirildi", vm.Mesaj);
        Assert.Null(vm.Hata);
        Assert.False(vm.YedekIndiriliyor);
        Assert.False(vm.Mesgul);
    }
    [Fact]
    public async Task Yedek_indirmesi_kullanici_iptaliyle_durur_hata_sayilmaz()
    {
        var basladi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken gorulen = default;
        var api = new Fake { YedekYaniti = async (_, ct) => { gorulen = ct; basladi.SetResult(); await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); } };
        var vm = new GuvenlikViewModel(api, Auth());
        var islem = vm.YedekIndirAsync(new MemoryStream());
        await basladi.Task;
        Assert.True(vm.YedekIndiriliyor);
        Assert.True(vm.YedekIptalCommand.CanExecute(null));
        vm.YedekIptalCommand.Execute(null);
        Assert.Null(await islem);
        Assert.True(gorulen.IsCancellationRequested);
        Assert.Null(vm.Hata);
        Assert.Equal("Yedek indirme iptal edildi.", vm.Mesaj);
        Assert.False(vm.YedekIndiriliyor);
        Assert.False(vm.Mesgul);
    }
    [Fact]
    public async Task Ekrandan_ayrilinca_suren_yedek_indirmesi_iptal_edilir()
    {
        var basladi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken gorulen = default;
        var api = new Fake { YedekYaniti = async (_, ct) => { gorulen = ct; basladi.SetResult(); await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); } };
        var vm = new GuvenlikViewModel(api, Auth());
        var islem = vm.YedekIndirAsync(new MemoryStream());
        await basladi.Task;
        vm.EkrandanAyril();
        Assert.Null(await islem);
        Assert.True(gorulen.IsCancellationRequested);
        Assert.Null(vm.Hata);
        Assert.Null(vm.Mesaj);
    }
    // CI'da kararsızdı: iptal devamı Cancel() içinde eşzamanlı (ya da başka iş parçacığında hemen) çalışırsa ekran henüz
    // geçersiz kılınmadığı için ayrılınmış ekrana 'iptal edildi' iletisi yazılıyordu. Burada devam Cancel() içinde çalışır.
    [Fact]
    public async Task Ekrandan_ayrilma_iptali_devam_hemen_calissa_da_ileti_birakmaz()
    {
        var basladi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new Fake
        {
            YedekYaniti = async (_, ct) =>
        {
            var bekle = new TaskCompletionSource<IndirmeBilgisi>(); // devamlar SetCanceled'i çağıranda (Cancel içinde) çalışır
            using var kayit = ct.Register(() => bekle.TrySetCanceled(ct));
            basladi.SetResult();
            return await bekle.Task;
        }
        };
        var vm = new GuvenlikViewModel(api, Auth());
        var islem = vm.YedekIndirAsync(new MemoryStream());
        await basladi.Task;
        vm.EkrandanAyril();
        Assert.Null(await islem);
        Assert.Null(vm.Hata);
        Assert.Null(vm.Mesaj);
    }
    [Fact]
    public async Task Yedek_zaman_asimi_baglanti_hatasindan_ayri_anlatilir()
    {
        var api = new Fake { YedekYaniti = (_, _) => Task.FromException<IndirmeBilgisi>(new TimeoutException(KasaZamanAsimlari.Ileti)) };
        var vm = new GuvenlikViewModel(api, Auth());
        Assert.Null(await vm.YedekIndirAsync(new MemoryStream()));
        Assert.Contains("zamanında yanıt vermedi", vm.Hata);
        Assert.DoesNotContain("ulaşılamadı", vm.Hata);
        Assert.False(vm.YedekIndiriliyor);
    }
    [Fact]
    public async Task Yedek_durumu_sunucunun_rotasyon_uyarisini_gosterir()
    {
        const string uyari = "Otomatik rotasyonu tamamlanamadı: saklama süresi dolan 1 yedek silinemedi (kasa-otomatik-20260801-000000.zip).";
        var api = new Fake { Durum = new(true, new(2026, 9, 23, 3, 0, 0, TimeSpan.Zero), new(2026, 9, 23, 3, 0, 0, TimeSpan.Zero), null, uyari) };
        var vm = new GuvenlikViewModel(api, Auth());
        await vm.YukleAsync();
        Assert.Equal(uyari, vm.YedekUyarisi);
        Assert.DoesNotContain(uyari, vm.YedekBilgisi);
        api.Durum = new(true, null, null, "Son yedekleme tamamlanamadı.");
        await vm.YukleAsync();
        Assert.Equal("Son yedekleme tamamlanamadı.", vm.YedekUyarisi);
        api.Durum = new(true, null, null, null);
        await vm.YukleAsync();
        Assert.Null(vm.YedekUyarisi);
    }
    // gap-denetim-izi-gozlemlenebilirlik-9: belge kaldırma yumuşak silmedir; editör gerekçe vermeden kaldıramaz (istek gitmez), alıcı
    // için gerekçe isteğe bağlıdır. Editör kaldırılanları da gösterebilir: liste silinenlerle istenir, kaldırma listeyi yeniler.
    [Fact]
    public async Task Editor_belgeyi_gerekcesiz_kaldiramaz_gerekce_gider_silinenler_istenirse_liste_yenilenir()
    {
        var belge = new BelgeDto(9, 7, null, "fis.pdf", "application/pdf", 4, DateTimeOffset.UtcNow, "alici", "Ayşe");
        var api = new Fake { BelgeYaniti = Task.FromResult<IReadOnlyList<BelgeDto>>(new[] { belge }) };
        var vm = await AlisVm(api);
        await vm.BelgeleriYukleCommand.ExecuteAsync(null);
        Assert.False(api.SonSilinenlerIstegi);
        await vm.BelgeSilAsync(belge, "   ");
        Assert.Null(api.SonSilme);
        Assert.Equal(AlislarViewModel.BelgeSilmeGerekcesiGerekli, vm.Hata);
        Assert.Single(vm.Belgeler);
        await vm.BelgeSilAsync(belge, "  Yanlış fiş  ");
        Assert.Equal((9, "Yanlış fiş"), api.SonSilme);
        Assert.Empty(vm.Belgeler);
        Assert.Contains("kaldırıldı", vm.Mesaj);

        var silinmis = belge with { Silindi = true, Silen = "Editör", SilmeGerekcesi = "Yanlış fiş", SilinmeZamani = DateTimeOffset.UtcNow };
        api.BelgeYaniti = Task.FromResult<IReadOnlyList<BelgeDto>>(new[] { silinmis });
        vm.SilinenBelgeleriGoster = true;
        await vm.BelgeleriYukleCommand.ExecuteAsync(null);
        Assert.True(api.SonSilinenlerIstegi);
        Assert.True(Assert.Single(vm.Belgeler).Silindi);
        // Kaldırılmış belge yeniden kaldırılmaz.
        api.SonSilme = null;
        await vm.BelgeSilAsync(silinmis, "tekrar");
        Assert.Null(api.SonSilme);
    }
    [Fact]
    public async Task Alici_belgeyi_gerekcesiz_kaldirabilir_silinenleri_isteyemez()
    {
        var belge = new BelgeDto(9, 7, null, "fis.pdf", "application/pdf", 4, DateTimeOffset.UtcNow, "alici", "Ayşe");
        var api = new Fake { BelgeYaniti = Task.FromResult<IReadOnlyList<BelgeDto>>(new[] { belge }) };
        var auth = TestOturumu.Ac();
        var vm = await AlisVm(api, auth: auth);
        auth.AktifRol = Rol.Alici;
        vm.SilinenBelgeleriGoster = true;
        await vm.BelgeleriYukleCommand.ExecuteAsync(null);
        Assert.False(api.SonSilinenlerIstegi);
        await vm.BelgeSilAsync(belge);
        Assert.Equal((9, (string?)null), api.SonSilme);
        Assert.Empty(vm.Belgeler);
        Assert.Null(vm.Hata);
    }
    [Fact]
    public void Belge_aciklamasi_yukleyeni_ve_kaldirma_izini_gosterir()
    {
        var belge = new BelgeDto(9, 7, null, "fis.pdf", "application/pdf", 4, DateTimeOffset.UtcNow, "alici", "Ayşe Alıcı");
        Assert.Equal("Yükleyen: Ayşe Alıcı", AlislarViewModel.BelgeAciklamasi(belge));
        Assert.Equal("Yükleyen: bilinmiyor (eski kayıt)", AlislarViewModel.BelgeAciklamasi(belge with { YukleyenRol = null, Yukleyen = null }));
        var silinen = AlislarViewModel.BelgeAciklamasi(belge with { Silindi = true, Silen = "Editör", SilmeGerekcesi = "İade sonrası yanlış fiş", SilinmeZamani = DateTimeOffset.UtcNow });
        Assert.StartsWith("Yükleyen: Ayşe Alıcı · Kaldırıldı: Editör · ", silinen);
        Assert.EndsWith(" · Gerekçe: İade sonrası yanlış fiş", silinen);
    }
    [Fact]
    public async Task Yedek_durumu_disk_alanlarini_gb_olarak_ve_disk_uyarisini_gosterir()
    {
        const long Gb = 1024L * 1024 * 1024;
        var api = new Fake
        {
            Durum = new(true, null, null, null, null, 100L * 1024 * 1024, 5 * Gb, 3 * Gb + Gb / 2, 2 * Gb,
            "Yedek diskinde 0,1 GB boş alan kaldı (asgari 2,0 GB).", "Son yedekte 1 belge dosyası bulunamadı.")
        };
        var vm = new GuvenlikViewModel(api, Auth());
        await vm.YukleAsync();
        Assert.Contains("Yedek diski boş alan: 0,1 GB", vm.YedekBilgisi);
        Assert.Contains("Veri diski boş alan: 5,0 GB", vm.YedekBilgisi);
        Assert.Contains("Yedeklerin toplam boyutu: 3,5 GB", vm.YedekBilgisi);
        Assert.Equal("Yedek diskinde 0,1 GB boş alan kaldı (asgari 2,0 GB).\nSon yedekte 1 belge dosyası bulunamadı.", vm.YedekUyarisi);
        api.Durum = new(true, null, null, null);
        await vm.YukleAsync();
        Assert.DoesNotContain("GB", vm.YedekBilgisi);
        Assert.Null(vm.YedekUyarisi);
    }
    [Fact]
    public async Task Yedek_durumu_son_geri_yuklemeyi_ve_rapor_maddelerini_gosterir_eski_sunucuda_gostermez()
    {
        var son = new DateTimeOffset(2026, 9, 28, 10, 15, 0, TimeSpan.Zero);
        var api = new Fake
        {
            Durum = new YedekDurumuDto(true, null, null, null) with
            {
                SonGeriYukleme = son,
                GeriYuklemeRaporu = ["Bütün oturumlar kapatıldı; herkes yeniden giriş yapmalı.", "Kurtarma kodu iptal edildi."]
            }
        };
        var vm = new GuvenlikViewModel(api, Auth());
        await vm.YukleAsync();
        Assert.EndsWith($"\nSon geri yükleme: {son.ToLocalTime():dd.MM.yyyy HH:mm}\n• Bütün oturumlar kapatıldı; herkes yeniden giriş yapmalı.\n• Kurtarma kodu iptal edildi.", vm.YedekBilgisi);
        api.Durum = new(true, null, null, null);
        await vm.YukleAsync();
        Assert.DoesNotContain("geri yükleme", vm.YedekBilgisi);
    }
    [Fact]
    public async Task Belge_yukleme_zaman_asiminda_liste_yenilenir_ki_tekrar_yuklemeden_once_gorulsun()
    {
        var sunucudaki = new BelgeDto(9, 7, null, "dekont.pdf", "application/pdf", 4, DateTimeOffset.UtcNow);
        var api = new Fake { BelgeYuklemeHatasi = new TimeoutException(KasaZamanAsimlari.Ileti) };
        var vm = await AlisVm(api);
        api.BelgeYaniti = Task.FromResult<IReadOnlyList<BelgeDto>>(new[] { sunucudaki });
        await vm.BelgeYukleAsync("dekont.pdf", "application/pdf", new byte[] { 1, 2, 3, 4 }, null);
        Assert.Contains("zamanında yanıt vermedi", vm.Hata);
        Assert.Equal(sunucudaki, Assert.Single(vm.Belgeler));
        Assert.Equal(1, api.BelgeYuklemeSayisi);
    }
    // maui-8: AlislarPage.BelgeEkleTiklandi mantığı (uzantıdan içerik türü, 10 MB sınırı, oturum/seçim koruması, okuma hatası)
    // AlislarViewModel.BelgeEkleAsync'te; sayfa yalnız dosya seçiciyi açar ve dönen uyarıyı gösterir.
    private static Func<Task<SecilenDosya?>> Secici(string ad, byte[] icerik, Action? secerken = null)
        => () => { secerken?.Invoke(); return Task.FromResult<SecilenDosya?>(new(ad, () => Task.FromResult<Stream>(new MemoryStream(icerik)))); };
    [Fact]
    public async Task Belge_ekleme_uzantidan_icerik_turuyle_yukler_desteklenmeyen_ve_buyuk_dosyayi_uyariyla_reddeder()
    {
        var api = new Fake();
        var vm = await AlisVm(api);
        Assert.Null(await vm.BelgeEkleAsync(Secici("Dekont.JPG", [1, 2, 3]), 4));
        Assert.Equal(("Dekont.JPG", "image/jpeg", 3, (int?)4), (api.SonBelge!.Value.Ad, api.SonBelge.Value.Tur, api.SonBelge.Value.Icerik.Length, api.SonBelge.Value.OdemeId));
        Assert.Equal("Belge eklendi.", vm.Mesaj);

        Assert.Equal(DosyaSecimKurallari.DesteklenmeyenBelge, await vm.BelgeEkleAsync(Secici("resim.gif", [1]), null));
        Assert.Equal(DosyaSecimKurallari.BuyukBelge, await vm.BelgeEkleAsync(Secici("buyuk.pdf", new byte[DosyaSecimKurallari.EnFazlaBayt + 1]), null));
        Assert.Equal(1, api.BelgeYuklemeSayisi);
        Assert.Null(await vm.BelgeEkleAsync(() => Task.FromResult<SecilenDosya?>(null), null));   // seçiciden vazgeçildi
        Assert.Equal(DosyaSecimKurallari.OkunamayanBelge, await vm.BelgeEkleAsync(() => Task.FromException<SecilenDosya?>(new IOException()), null));
        Assert.Equal(DosyaSecimKurallari.OkunamayanBelge, await vm.BelgeEkleAsync(() => Task.FromResult<SecilenDosya?>(new("a.pdf", () => Task.FromException<Stream>(new UnauthorizedAccessException()))), null));
        Assert.Equal(1, api.BelgeYuklemeSayisi);
    }
    [Fact]
    public async Task Belge_secilirken_alis_ya_da_oturum_degisirse_yuklenmez()
    {
        var api = new Fake();
        var auth = TestOturumu.Ac();
        var vm = await AlisVm(api, auth: auth);
        Assert.Null(await vm.BelgeEkleAsync(Secici("a.pdf", [1], () => vm.SecCommand.Execute(vm.Alislar.Single(a => a.Veri.Id == 8))), null));
        Assert.Equal(0, api.BelgeYuklemeSayisi);

        vm.SecCommand.Execute(vm.Alislar.Single(a => a.Veri.Id == 7));
        Assert.Null(await vm.BelgeEkleAsync(Secici("a.pdf", [1], () => auth.OturumSurumu++), null));
        Assert.Equal(0, api.BelgeYuklemeSayisi);

        var yeni = new AlislarViewModel(api, new SahteApi(), TestOturumu.Ac(), api, api);
        await yeni.YukleAsync();
        yeni.YeniCommand.Execute(null);                                                 // seçili alış yok: seçici açılmaz
        var acildi = false;
        Assert.Null(await yeni.BelgeEkleAsync(() => { acildi = true; return Task.FromResult<SecilenDosya?>(null); }, null));
        Assert.False(acildi);
    }
    [Fact]
    public async Task Gecikmis_kurtarma_kodu_ekrandan_ayrildiktan_sonra_gosterilmez()
    {
        var bekleyen = new TaskCompletionSource<KurtarmaKoduDto>();
        var api = new Fake { KurtarmaYaniti = bekleyen.Task };
        var vm = new GuvenlikViewModel(api, Auth()) { MevcutSifre = "eski-sifre" };
        var islem = vm.KurtarmaKoduOlusturCommand.ExecuteAsync(null);
        vm.EkrandanAyril();
        bekleyen.SetResult(new("gizli-kod"));
        await islem;
        Assert.Null(vm.KurtarmaKodu);
        Assert.Empty(vm.MevcutSifre);
    }

    [Fact]
    public async Task Alis_serbest_aciklama_tutar_ve_dagilimla_kaydedilir_erp_alanlari_gonderilmez()
    {
        var api = new Fake();
        var vm = await AlisVm(api);
        vm.Tedarikci = "  Açık artırmadan alındı  ";
        vm.Kalemler[0].Tutar = 80;
        vm.Kalemler[0].Dagilimlar[0].Tutar = 80;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal("Açık artırmadan alındı", api.SonAlis!.Tedarikci);
        Assert.Equal(80, api.SonAlis.Kalemler[0].Tutar);
        Assert.Null(api.SonAlis.TedarikciId);
        Assert.Null(api.SonAlis.Vade);
        Assert.Null(api.SonAlis.Kalemler[0].Miktar);
        Assert.Null(api.SonAlis.Kalemler[0].BirimFiyat);
    }
    [Fact]
    public async Task Odeme_duzeltmede_eski_hesap_alani_tekrar_yazilmaz()
    {
        var ilk = Alis();
        var api = new Fake { IlkAlis = ilk with { Odemeler = new[] { ilk.Odemeler[0] with { HesapId = 1 } } } };
        var vm = await AlisVm(api);
        vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]);
        vm.DuzeltmeAciklamasi = "Tarih düzeltildi";
        await vm.OdemeDuzeltKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonDuzelt!.HesapId);
    }
    [Fact]
    public async Task Yeni_alis_odemesi_benzer_kaydi_onayla_kaydeder_mevcut_gider_baglama_sormaz()
    {
        var giderler = new[] { new IslemDto(30, Bugun, "Yeni gider", 10, "MEZAT", GiderTipi.Cari, null) };
        var api = new Fake { Giderler = giderler };
        var lookup = new BenzerKayitTests.Fake();
        var vm = await AlisVm(api, benzerlik: lookup);
        vm.OdemeTutari = 10;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonOdeme);
        Assert.Equal("AlisOdeme", lookup.SonArama!.Tur);
        Assert.Equal(7, lookup.SonArama.AlisId);
        await vm.OdemeyiAyriKaydetCommand.ExecuteAsync(null);
        Assert.Equal(10, api.SonOdeme!.Tutar);
        var ikinciApi = new Fake { Giderler = giderler };
        var ikinciLookup = new BenzerKayitTests.Fake { Hata = true };
        var ikinci = await AlisVm(ikinciApi, benzerlik: ikinciLookup);
        ikinci.MevcutGiderKullan = true;
        ikinci.SeciliGider = ikinci.BaglanabilirGiderler.Single();
        await ikinci.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(30, ikinciApi.SonOdeme!.MevcutIslemId);
        Assert.Equal(0, ikinciLookup.Cagri);
    }
    [Fact]
    public async Task Alis_benzerlik_hatasi_odeme_eklemez()
    {
        var api = new Fake();
        var vm = await AlisVm(api, benzerlik: new BenzerKayitTests.Fake { Hata = true });
        vm.OdemeTutari = 10;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonOdeme);
        Assert.Contains("ulaşılamadı", vm.Hata);
    }

    private sealed class Fake : IAlisOdemeApi, IYonetimApi, IAlisApi
    {
        public AlisYaz? SonAlis;
        public AlisDto? IlkAlis;
        public AlisOdemeDuzeltYaz? SonDuzelt;
        public AlisOdemeIptalYaz? SonIptal;
        public AlisOdemeYaz? SonOdeme;
        public bool DuzeltHata, SifreDegisti, RaporIndirildi;
        public Task<IReadOnlyList<BelgeDto>>? BelgeYaniti;
        public YedekDurumuDto Durum = new(true, null, null, null);
        public Func<Stream, CancellationToken, Task<IndirmeBilgisi>>? YedekYaniti;
        public Exception? BelgeYuklemeHatasi;
        public int BelgeYuklemeSayisi;
        public Task<KurtarmaKoduDto>? KurtarmaYaniti;
        public Task<AlisDto> AlisOdemeDuzeltAsync(int a, int o, AlisOdemeDuzeltYaz g) { SonDuzelt = g; return DuzeltHata ? Task.FromException<AlisDto>(new HttpRequestException()) : Task.FromResult(Alis(a)); }
        public Task<AlisDto> AlisOdemeIptalAsync(int a, int o, AlisOdemeIptalYaz g) { SonIptal = g; return Task.FromResult(Alis(a)); }
        public Task SifreDegistirAsync(SifreDegistirYaz g) { SifreDegisti = true; return Task.CompletedTask; }
        public Task<KurtarmaKoduDto> KurtarmaKoduOlusturAsync(string s) => KurtarmaYaniti ?? Task.FromResult(new KurtarmaKoduDto("kod"));
        public Task SifreKurtarAsync(SifreKurtarYaz g) => Task.CompletedTask;
        public Task<SurumDto> SurumAsync() => Task.FromResult(new SurumDto("2.0.0", "2.0.0", null, null));
        public Task<YedekDurumuDto> YedekDurumuAsync() => Task.FromResult(Durum);
        public Task<IndirmeBilgisi> YedekIndirAsync(Stream hedef, CancellationToken ct = default) => YedekYaniti?.Invoke(hedef, ct) ?? Yaz(hedef, "yedek.zip");
        private static async Task<IndirmeBilgisi> Yaz(Stream hedef, string ad) { await hedef.WriteAsync(new byte[] { 1, 2, 3 }); return new(ad, "application/octet-stream", 3); }
        public bool? SonSilinenlerIstegi;
        public (int Id, string? Gerekce)? SonSilme;
        public Task<IReadOnlyList<BelgeDto>> BelgelerAsync(int id, bool silinenler = false) { SonSilinenlerIstegi = silinenler; return BelgeYaniti ?? Task.FromResult<IReadOnlyList<BelgeDto>>(Array.Empty<BelgeDto>()); }
        public (string Ad, string Tur, byte[] Icerik, int? OdemeId)? SonBelge;
        public Task<BelgeDto> BelgeYukleAsync(int id, string ad, string tur, byte[] b, int? odemeId = null, CancellationToken ct = default)
        { BelgeYuklemeSayisi++; SonBelge = (ad, tur, b, odemeId); return BelgeYuklemeHatasi is { } hata ? Task.FromException<BelgeDto>(hata) : Task.FromResult(new BelgeDto(1, id, odemeId, ad, tur, b.Length, DateTimeOffset.UtcNow)); }
        public Task<IndirmeBilgisi> BelgeIndirAsync(int id, Stream hedef, CancellationToken ct = default) => Yaz(hedef, "belge.pdf");
        public Task BelgeSilAsync(int id, string? gerekce = null) { SonSilme = (id, gerekce); return Task.CompletedTask; }
        public Task<IndirmeBilgisi> DisariAktarAsync(DateOnly b, DateOnly s, string? k, string bicim, Stream hedef, CancellationToken ct = default) { RaporIndirildi = true; return Yaz(hedef, "rapor." + bicim); }
        public Task<IReadOnlyList<AlisKanalDto>> AlisKanallariAsync() => Task.FromResult<IReadOnlyList<AlisKanalDto>>(new[] { new AlisKanalDto(1, "MEZAT", true) });
        public Task<IReadOnlyList<AlisDto>> AlislarAsync() => Task.FromResult<IReadOnlyList<AlisDto>>(new[] { IlkAlis ?? Alis(), Alis(8) });
        public Task<AlisDto> AlisOlusturAsync(AlisYaz g) { SonAlis = g; return Task.FromResult(Alis()); }
        public Task<AlisDto> AlisGuncelleAsync(int id, AlisYaz g) => AlisOlusturAsync(g);
        public Task<AlisDto> AlisGonderAsync(int id, AlisDurumYaz g) => Task.FromResult(Alis());
        public Task<AlisDto> AlisOnaylaAsync(int id, AlisDurumYaz g) => Task.FromResult(Alis());
        public Task<AlisDto> AlisIadeAsync(int id, AlisDurumYaz g) => Task.FromResult(Alis());
        public Task<AlisDto> AlisOdemeKaydetAsync(int id, AlisOdemeYaz g) { SonOdeme = g; return Task.FromResult(Alis()); }
        public Task<IReadOnlyList<AliciDto>> AlicilarAsync() => Task.FromResult<IReadOnlyList<AliciDto>>(Array.Empty<AliciDto>());
        public IReadOnlyList<IslemDto> Giderler = Array.Empty<IslemDto>();
        public Task<BaglanabilirGiderSayfasi> BaglanabilirGiderlerAsync(string? arama = null, decimal? tutar = null, DateOnly? baslangic = null, DateOnly? bitis = null, string? imlec = null, int? limit = null, decimal? aramaTutari = null)
            => Task.FromResult(new BaglanabilirGiderSayfasi(Giderler.Select(AlislarViewModelTests.SahteAlisApi.Baglanabilir).ToList(), null, false));
        public Task<IReadOnlyList<BaglanabilirKartHarcamasiDto>> BaglanabilirKartHarcamalariAsync(int krediKartiId, decimal? tutar = null)
            => Task.FromResult<IReadOnlyList<BaglanabilirKartHarcamasiDto>>(Array.Empty<BaglanabilirKartHarcamasiDto>());
        public Task<AliciDto> AliciOlusturAsync(AliciYaz g) => Task.FromResult(new AliciDto(1, g.Kullanici, g.Ad, true));
        public Task<AliciDto> AliciGuncelleAsync(int id, AliciYaz g) => AliciOlusturAsync(g);
    }
}
