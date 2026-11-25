using System.Net;
using Kasa.ApiClient;
using Kasa.App.Views;

namespace Kasa.App.Core.Tests;

// Etki gösteriminin hataları, Bekleyen/Kapanan, yok say, toplu uygula ve giriş-çıkış gösterimi (docs/specs/2026-11-25-pdf-ekrani.md).
public partial class EkstreAktarmaTests
{
    private static EkstreBelgeDto DurumBelgesi(params EkstreOkunanSatir[] satirlar) => Belge() with { Satirlar = satirlar, YokSayilanlar = [], Atlananlar = [], YenidenOkunabilir = true };
    private static EkstreOkunanSatir YonSatiri(int no, string yon, decimal? tutar, string oneri, string? kaynak = null) => Satir(no) with { Yon = yon, Tutar = tutar, OnerilenIslem = oneri, YonKaynagi = kaynak };
    private static EkstreKayitDto Kayit(int id, int satirNo, bool iptal = false, string tur = "Gider") => new(id, satirNo, Tarih, "Kayıt", 100, tur, "Genel", [], null, 5, null, null, iptal, iptal ? "Yanlış" : null);

    [Fact]
    public async Task Eksiklerin_hepsi_satir_satir_listelenir_satira_gidilir_duzelen_satirin_hatasi_kalkar()
    {
        var (vm, api, _) = await Hazir();
        var gosterim = 0;
        EkstreSatirEditor? gidilen = null;
        vm.SonucGosterIstendi += (_, _) => gosterim++;
        vm.SatirGosterIstendi += (_, s) => gidilen = s;
        vm.Satirlar[0].Secili = true;
        vm.Satirlar[0].TarihMetni = "";
        vm.Satirlar[1].Secili = true;
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.Equal(0, api.OnizlemeSayisi);
        Assert.Equal([(1, "Tarihi yıl-ay-gün biçiminde girin (2026-09-27)."), (1, "Bu hareket için kanal dağılımını seçin."), (2, "Bu hareket için kanal dağılımını seçin.")],
            vm.OnizlemeHatalari.Select(h => (h.SatirNo!.Value, h.Ileti)));
        Assert.True(vm.OnizlemeHataVar);
        Assert.Equal(1, gosterim);
        Assert.Null(vm.Hata);

        vm.SeciliSekme = EkstreSekmesi.Kapanan;
        vm.SatiraGit(vm.OnizlemeHatalari[2]);
        Assert.Same(vm.Satirlar[1], vm.SeciliSatir);
        Assert.Same(vm.Satirlar[1], gidilen);
        Assert.Equal(EkstreSekmesi.Bekleyen, vm.SeciliSekme);

        // Bir satırı düzeltmek diğerinin hatasını silmez: liste yapılacaklar listesidir.
        Sec(vm.Satirlar[1]);
        Assert.All(vm.OnizlemeHatalari, h => Assert.Equal(1, h.SatirNo));
        Sec(vm.Satirlar[0]);
        vm.Satirlar[0].TarihMetni = "2026-09-27";
        Assert.False(vm.OnizlemeHataVar);
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.True(vm.OnizlemeVar);
        Assert.Equal(2, gosterim);
        Assert.Contains("· Gider", vm.OnizlemeMetni);
    }

    [Fact]
    public async Task Acik_ama_isaretsiz_satir_secilmis_sayilmaz_nedeni_soylenir()
    {
        var (vm, _, _) = await Hazir();
        vm.SeciliSatir = vm.Satirlar[1];
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.StartsWith("Satır 2 açık ama işaretli değil.", Assert.Single(vm.OnizlemeHatalari).Metin);
        // Satır işaretlenince ileti kalkar.
        vm.Satirlar[1].Secili = true;
        Assert.False(vm.OnizlemeHataVar);
    }

    [Fact]
    public async Task Sunucunun_satir_hatalari_satirlara_baglanir_baglanti_hatasi_genel_kalir()
    {
        var (vm, api, _) = await Hazir();
        Sec(vm.Satirlar[0]);
        Sec(vm.Satirlar[1]);
        api.OnizlemeYaniti = Task.FromException<EkstreOnizlemeDto>(new KasaApiException(HttpStatusCode.BadRequest, "Satır 1: Kart seçin.\nSatır 2: Bu tarih kilitli.\nSatır 99: Bilinmeyen satır.\nGenel neden."));
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.Equal([(1, "Kart seçin."), (2, "Bu tarih kilitli."), (null, "Satır 99: Bilinmeyen satır."), ((int?)null, "Genel neden.")], vm.OnizlemeHatalari.Select(h => (h.SatirNo, h.Ileti)));
        Assert.Null(vm.Hata);
        api.OnizlemeYaniti = Task.FromException<EkstreOnizlemeDto>(new HttpRequestException());
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.StartsWith("Sunucuya ulaşılamadı", Assert.Single(vm.OnizlemeHatalari).Metin);
        api.OnizlemeYaniti = null;
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.False(vm.OnizlemeHataVar);
        Assert.True(vm.OnizlemeVar);
    }

    [Fact]
    public async Task Kaydetme_hatasi_dugmenin_altindadir_cakismada_onizleme_kalkar_nedeni_kalir()
    {
        var (vm, api, _) = await Hazir();
        Sec(vm.Satirlar[0]);
        await vm.OnizleCommand.ExecuteAsync(null);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.StartsWith("Önizlemeyi kontrol edin", vm.KayitHatasi);
        vm.Onay = true;
        api.KayitYaniti = Task.FromException<EkstreBelgeDto>(new KasaApiException(HttpStatusCode.Conflict, "Bilgiler değişmiş. Yeniden önizleyip onaylayın."));
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.False(vm.OnizlemeVar);
        Assert.Equal("Bilgiler değişmiş. Yeniden önizleyip onaylayın.", Assert.Single(vm.OnizlemeHatalari).Metin);
        Assert.Null(vm.Hata);
        api.KayitYaniti = null;
        await vm.OnizleCommand.ExecuteAsync(null);
        vm.Onay = true;
        api.KaydetHata = true;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(Yurutucu.KayitBaglantiIletisi, vm.KayitHatasi);
        Assert.True(vm.OnizlemeVar);
    }

    [Fact]
    public async Task Satirlar_bekleyen_ve_kapanan_sekmelerine_ayrilir_iptal_edilen_yeniden_bekler()
    {
        var belge = DurumBelgesi(Satir(1), Satir(2), Satir(3), Satir(4)) with { Kayitlar = [Kayit(7, 1), Kayit(8, 2, iptal: true)], YokSayilanlar = [3] };
        var (vm, _, _) = await Hazir(new() { Veri = belge });
        Assert.Equal([2, 4], vm.Bekleyenler.Select(s => s.Kaynak.No));
        Assert.Equal([1, 3], vm.Kapananlar.Select(s => s.Kaynak.No));
        Assert.Equal(["Bekleyen (2)", "Kapanan (2)"], vm.Sekmeler.Select(s => s.Ad));
        Assert.True(vm.Sekmeler[0].Secili);
        Assert.Contains("Gider · Kaydedildi", vm.Kapananlar[0].SonucMetni);
        Assert.Same(vm.Kayitlar[0], vm.Kapananlar[0].Kayit);
        Assert.StartsWith("Yok sayıldı", vm.Kapananlar[1].SonucMetni);
        Assert.False(vm.Kapananlar[1].Secilebilir);
        Assert.Equal(8, Assert.Single(vm.IptalEdilenler).Veri.Id);
        Assert.True(vm.IptalEdilenVar);
        Assert.Contains("4 satır · 2 bekliyor · 2 kapandı (1 kayıt, 1 yok sayıldı)", vm.Gecmis[0].Ozet);
        vm.SekmeSecCommand.Execute(vm.Sekmeler[1]);
        Assert.Equal((EkstreSekmesi.Kapanan, false, true), (vm.SeciliSekme, vm.Sekmeler[0].Secili, vm.Sekmeler[1].Secili));
        // Başka belge Bekleyen sekmesinden açılır; aynı belgenin yenilenmesi sekmeyi bozmaz.
        await vm.YukleAsync();
        Assert.Equal(EkstreSekmesi.Kapanan, vm.SeciliSekme);
    }

    [Fact]
    public async Task Yok_sayma_satiri_kapatir_diger_satirlarin_taslagini_ve_isaretini_korur_geri_alinir()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3)) });
        Assert.True(vm.YokSayDestekleniyor);
        Sec(vm.Satirlar[1]);
        vm.Satirlar[1].Aciklama = "Taslak";
        var taslak = vm.Satirlar[1];
        vm.SeciliSatir = taslak;
        await vm.YokSayAsync([vm.Satirlar[0]], true);
        Assert.Equal((2, true), (Assert.Single(api.YokSayIstekleri).Surum, api.YokSayIstekleri[0].YokSay));
        Assert.Equal([1], api.YokSayIstekleri[0].SatirNolar);
        Assert.NotEqual(Guid.Empty, api.YokSayIstekleri[0].IstekId);
        Assert.Same(taslak, vm.Satirlar[1]);
        Assert.Same(taslak, vm.SeciliSatir);
        Assert.Equal(("Taslak", true), (vm.Satirlar[1].Aciklama, vm.Satirlar[1].Secili));
        Assert.Equal([1], vm.Kapananlar.Select(s => s.Kaynak.No));
        Assert.Contains("Satır 1 yok sayıldı", vm.SatirMesaji);
        Assert.Empty(api.KaydetIstekleri);
        // Korunan satırın değişikliği hâlâ önizlemeyi geçersiz kılar.
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.True(vm.OnizlemeVar);
        taslak.TutarMetni = "5";
        Assert.False(vm.OnizlemeVar);

        await vm.YokSayAsync([vm.Kapananlar[0]], false);
        Assert.Empty(vm.Kapananlar);
        Assert.Equal(3, vm.Bekleyenler.Count);
        Assert.False(api.YokSayIstekleri[1].YokSay);
        Assert.True(vm.Satirlar[1].Secili);
    }

    [Fact]
    public async Task Secilenler_toplu_yok_sayilir_secim_yoksa_istek_gitmez()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2, "USD"), Satir(3)) });
        await vm.SeciliyiYokSayCommand.ExecuteAsync(null);
        Assert.Empty(api.YokSayIstekleri);
        Assert.Contains("işaretleyin", vm.SatirHatasi);
        vm.Satirlar[0].Secili = true;
        vm.Satirlar[2].Secili = true;
        await vm.SeciliyiYokSayCommand.ExecuteAsync(null);
        Assert.Equal([1, 3], Assert.Single(api.YokSayIstekleri).SatirNolar);
        Assert.Contains("2 satır yok sayıldı", vm.SatirMesaji);
        Assert.Null(vm.SatirHatasi);
        // TL olmayan satır kaydedilemez ama kapatılabilir; yoksa Bekleyen hiç boşalmaz.
        Assert.False(vm.Bekleyenler[0].Secilebilir);
        Assert.True(vm.Bekleyenler[0].YokSayilabilir);
        await vm.YokSayAsync([vm.Bekleyenler[0]], true);
        Assert.Empty(vm.Bekleyenler);
        // Kayıtlı ya da başka belgenin satırı gönderilmez.
        var sayi = api.YokSayIstekleri.Count;
        await vm.YokSayAsync([vm.Kapananlar[0]], true);
        Assert.Equal(sayi, api.YokSayIstekleri.Count);
    }

    [Fact]
    public async Task Yok_sayma_hatasi_listenin_ustunde_gorunur_satirlar_degismez()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2)) });
        api.YokSayYaniti = _ => throw new KasaApiException(HttpStatusCode.Conflict, "Belge değişmiş. Yenileyip yeniden deneyin.");
        await vm.YokSayAsync([vm.Satirlar[0]], true);
        Assert.Equal("Belge değişmiş. Yenileyip yeniden deneyin.", vm.SatirHatasi);
        Assert.Null(vm.SatirMesaji);
        Assert.Equal(2, vm.Bekleyenler.Count);
    }

    [Fact]
    public async Task Eski_sunucuda_yok_say_ve_yeniden_oku_gorunmez_istek_gitmez()
    {
        var (vm, api, _) = await Hazir();
        Assert.False(vm.YokSayDestekleniyor);
        Assert.False(vm.YenidenOkunabilir);
        Assert.False(vm.Satirlar[0].YokSayilabilir);
        await vm.YokSayAsync([vm.Satirlar[0]], true);
        await vm.YenidenOkuAsync();
        Assert.Empty(api.YokSayIstekleri);
        Assert.Equal(0, api.YenidenOkumaSayisi);
        Assert.Equal("Bekleyen (2)", vm.Sekmeler[0].Ad);
    }

    [Fact]
    public async Task Kaydetmeden_sonra_diger_satirlarin_taslagi_korunur_kaydedilen_kapanir()
    {
        var api = new Sahte { Veri = DurumBelgesi(Satir(1), Satir(2)) };
        var (vm, _, _) = await Hazir(api);
        Sec(vm.Satirlar[0]);
        vm.Satirlar[1].Aciklama = "Taslak";
        vm.Satirlar[1].DagilimTuru = vm.Satirlar[1].DagilimTurleri.Single(x => x.Kod == "Genel");
        await vm.OnizleCommand.ExecuteAsync(null);
        vm.Onay = true;
        api.KayitYaniti = Task.FromResult(api.Veri with { Surum = 3, Kayitlar = [Kayit(7, 1)] });
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal([1], vm.Kapananlar.Select(s => s.Kaynak.No));
        var bekleyen = Assert.Single(vm.Bekleyenler);
        Assert.Equal(("Taslak", "Genel"), (bekleyen.Aciklama, bekleyen.DagilimTuru!.Kod));
        Assert.Contains("1 satır kaydedildi ve Kapanan", vm.SatirMesaji);
        Assert.False(vm.OnizlemeVar);
    }

    [Fact]
    public async Task Yenileme_taslaklari_isaretleri_ve_acik_satiri_korur_kanal_listesi_yenilenir()
    {
        var kanallar = new SahteApi { KanallarListe = [new(1, "MEZAT", true, 0, 0), new(2, "PERAKENDE", true, 1, 0)] };
        var api = new Sahte { Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3)) };
        var vm = new EkstreAktarmaViewModel(api, kanallar, new FinansTakipTests.Sahte(), new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        await vm.BelgeAcAsync(1);
        var s = vm.Satirlar[0];
        s.Aciklama = "Taslak";
        s.TarihMetni = "2026-09-20";
        s.TutarMetni = "77,50";
        s.DagilimTuru = s.DagilimTurleri.Single(x => x.Kod == "Esit");
        s.PayEkle();
        s.PayEkle();
        s.Paylar[0].Kanal = vm.Kanallar[0];
        s.Paylar[1].Kanal = vm.Kanallar[1];
        s.Secili = true;
        vm.SeciliSatir = s;
        vm.TopluDagilim = vm.TopluDagilimlar.Single(d => d.Kod == "Esit");
        foreach (var k in vm.TopluKanallar)
            k.Secili = true;
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.True(vm.OnizlemeVar);

        // PERAKENDE pasifleşti, yeni kanal geldi; sayfa yenilendi (elle ya da bağlantı dönünce).
        kanallar.KanallarListe = [new(1, "MEZAT", true, 0, 0), new(2, "PERAKENDE", false, 1, 0), new(3, "TOPTAN", true, 2, 0)];
        await vm.YukleAsync();
        var yeni = vm.Satirlar[0];
        Assert.Equal(("Taslak", "2026-09-20", "77,50", "Gider", "Esit", true), (yeni.Aciklama, yeni.TarihMetni, yeni.TutarMetni, yeni.IslemTuru!.Kod, yeni.DagilimTuru!.Kod, yeni.Secili));
        Assert.True(yeni.ElleDegisti);
        Assert.Same(yeni, vm.SeciliSatir);
        Assert.Same(yeni, vm.Bekleyenler[0]);
        Assert.False(vm.OnizlemeVar);
        // Artık olmayan kanalın payı sessizce kaymaz: kanalsız kalır ve seçilmesi istenir.
        Assert.Equal([1, (int?)null], yeni.Paylar.Select(p => p.Kanal?.Id));
        Assert.Contains("kanalları birer kez", Assert.Throws<DogrulamaHatasi>(() => yeni.Yaz()).Message);
        Assert.Equal([1, 3], yeni.Kanallar.Select(k => k.Id));
        Assert.Equal([(1, true), (3, false)], vm.TopluKanallar.Select(k => (k.Veri.Id, k.Secili)));
        // Değişiklik yeni modelden izlenir.
        yeni.Paylar[1].Kanal = yeni.Kanallar[1];
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.True(vm.OnizlemeVar);
        yeni.TutarMetni = "1";
        Assert.False(vm.OnizlemeVar);
    }

    [Fact]
    public async Task Baska_cihazda_yeniden_okunan_satirin_eski_okumasi_taslak_sayilmaz()
    {
        var api = new Sahte { Veri = DurumBelgesi(YonSatiri(1, "Giris", 100m, "Gelir"), YonSatiri(2, "Giris", 50m, "Gelir"), Satir(3)) };
        var (vm, _, _) = await Hazir(api);
        vm.Satirlar[0].Secili = true;
        vm.Satirlar[1].Aciklama = "Elle";
        vm.Satirlar[1].Secili = true;
        vm.Satirlar[2].Secili = true;
        api.Veri = api.Veri with { Satirlar = [YonSatiri(1, "Cikis", 100m, "Gider", "Bakiye"), YonSatiri(2, "Cikis", 50m, "Gider", "Bakiye"), Satir(3)] };
        await vm.YukleAsync();
        // Dokunulmayan satır yeni okumayla gelir; elle değiştirilenin taslağı kalır; ikisinin de işareti kalkar.
        Assert.Equal(("Gider", "Çıkış − · bakiyeden", false), (vm.Satirlar[0].IslemTuru!.Kod, vm.Satirlar[0].YonMetni, vm.Satirlar[0].Secili));
        Assert.Equal(("Elle", false), (vm.Satirlar[1].Aciklama, vm.Satirlar[1].Secili));
        Assert.Contains("belgede: Çıkış −", vm.Satirlar[1].YonMetni);
        Assert.True(vm.Satirlar[2].Secili);
    }

    [Fact]
    public void Toplu_uygula_eksik_kanalda_satira_dokunmaz()
    {
        var s = new EkstreSatirEditor(Satir(1), Belge(), [new(1, "MEZAT", true, 0, 0)], [], () => { });
        Assert.False(s.TopluUygula(null, "Esit", [1, 2]));
        Assert.Null(s.DagilimTuru);
        Assert.True(s.TopluUygula(null, "Esit", [1]));
    }

    [Fact]
    public async Task Yok_sayilan_satirin_taslagi_geri_alininca_gelir_hepsi_birden_geri_alinir()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3)) });
        foreach (var s in vm.Satirlar)
        {
            Sec(s);
            s.Aciklama = "Taslak " + s.Kaynak.No;
        }
        Assert.False(vm.TopluGeriAlGorunur);
        await vm.SeciliyiYokSayCommand.ExecuteAsync(null);
        Assert.Equal((0, 3, true), (vm.Bekleyenler.Count, vm.YokSayilanSayisi, vm.TopluGeriAlGorunur));
        Assert.Equal("Yok sayılanların hepsini geri al (3)", vm.TopluGeriAlDugmesi);
        await vm.YokSayilanlariGeriAlCommand.ExecuteAsync(null);
        Assert.Equal([1, 2, 3], api.YokSayIstekleri[1].SatirNolar);
        Assert.Equal(["Taslak 1", "Taslak 2", "Taslak 3"], vm.Bekleyenler.Select(s => s.Aciklama));
        Assert.All(vm.Bekleyenler, s => Assert.Equal(("Genel", false), (s.DagilimTuru!.Kod, s.Secili)));
        // Başka belgeye geçince saklanan taslak taşınmaz.
        await vm.YokSayAsync([vm.Bekleyenler[0]], true);
        api.Veri = api.Veri with { Id = 2, YokSayilanlar = [] };
        await vm.BelgeAcAsync(2);
        Assert.Equal("Hareket 1", vm.Bekleyenler[0].Aciklama);
    }

    [Fact]
    public async Task Yok_sayilan_dovizli_satir_tl_toplamina_girmez_tutarsiz_satir_soylenir()
    {
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(YonSatiri(1, "Giris", 100m, "Gelir"), YonSatiri(2, "Giris", 1000m, "Atla") with { ParaBirimi = "USD" }, YonSatiri(3, "Cikis", null, "Gider")) });
        const string beklenen = "Giriş +100,00 ₺ (1 satır) · Çıkış −0,00 ₺ (1 satır) · 1 satırın tutarı okunamadı, toplama girmedi";
        Assert.Equal(beklenen, vm.YonOzeti);
        await vm.YokSayAsync([vm.Satirlar[1]], true);
        Assert.Equal(beklenen, vm.YonOzeti);
    }

    [Fact]
    public async Task Iptalden_sonra_diger_taslaklar_korunur_kaydirma_satir_sonucunadir()
    {
        var api = new Sahte { Veri = DurumBelgesi(Satir(1), Satir(2)) with { Kayitlar = [Kayit(7, 1)] } };
        var (vm, _, _) = await Hazir(api);
        vm.Satirlar[1].Aciklama = "Taslak";
        var taslak = vm.Satirlar[1];
        api.Veri = api.Veri with { Surum = 3, Kayitlar = [Kayit(7, 1, iptal: true)] };
        await vm.KayitIptalAsync(vm.Kayitlar[0], "Yanlış", vm.OturumNesli, 1);
        Assert.Equal([1, 2], vm.Bekleyenler.Select(s => s.Kaynak.No));
        Assert.Same(taslak, vm.Bekleyenler[1]);
        Assert.Contains("Satır 1 kaydı iptal edildi", vm.SatirMesaji);
        // Ön denetim hatası da satır sonucunda görünür.
        EkstreSonucu? hedef = null;
        vm.SonucGosterIstendi += (_, s) => hedef = s;
        await vm.KayitIptalAsync(vm.Kayitlar[0], "Yanlış", vm.OturumNesli, 1);
        Assert.Contains("yeniden onaylayın", vm.SatirHatasi);
        Assert.Equal(EkstreSonucu.Satirlar, hedef);
    }

    [Fact]
    public async Task Hata_gizli_sekmede_kalmaz_satir_hatasi_acik_onizlemeye_kaydirmaz()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2)) });
        var hedefler = new List<EkstreSonucu>();
        vm.SonucGosterIstendi += (_, s) => hedefler.Add(s);
        Sec(vm.Satirlar[0]);
        await vm.OnizleCommand.ExecuteAsync(null);
        vm.Onay = true;
        api.YokSayYaniti = _ => throw new KasaApiException(HttpStatusCode.Conflict, "Belge değişmiş.");
        await vm.YokSayAsync([vm.Satirlar[1]], true);
        Assert.True(vm.OnizlemeVar);
        vm.SeciliSekme = EkstreSekmesi.Kapanan;
        api.KayitYaniti = Task.FromException<EkstreBelgeDto>(new KasaApiException(HttpStatusCode.Conflict, "Bilgiler değişmiş."));
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(EkstreSekmesi.Bekleyen, vm.SeciliSekme);
        Assert.True(vm.OnizlemeHataVar);
        Assert.Equal([EkstreSonucu.Onizleme, EkstreSonucu.Satirlar, EkstreSonucu.Onizleme], hedefler);
        // Yenileme başarılıysa eski hata iletileri kalkar.
        Assert.NotNull(vm.SatirHatasi);
        await vm.YukleAsync();
        Assert.Null(vm.SatirHatasi);
        Assert.False(vm.OnizlemeHataVar);
    }

    [Fact]
    public async Task Kaydedilince_toplu_uygulama_iletisi_kalkar()
    {
        var api = new Sahte { Veri = DurumBelgesi(Satir(1), Satir(2)) };
        var (vm, _, _) = await Hazir(api);
        vm.Satirlar[0].Secili = true;
        vm.TopluDagilim = vm.TopluDagilimlar.Single(d => d.Kod == "Genel");
        vm.TopluUygulaCommand.Execute(null);
        Assert.Contains("henüz kaydedilmedi", vm.TopluMesaji);
        await vm.OnizleCommand.ExecuteAsync(null);
        vm.Onay = true;
        api.KayitYaniti = Task.FromResult(api.Veri with { Surum = 3, Kayitlar = [Kayit(7, 1)] });
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.TopluMesaji);
        Assert.Contains("1 satır kaydedildi", vm.SatirMesaji);
    }

    [Fact]
    public async Task Kaynagi_acilan_kayit_kapanan_sekmesinde_gosterilir()
    {
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2)) with { Kayitlar = [Kayit(7, 2)] } });
        EkstreSatirEditor? gidilen = null;
        vm.SatirGosterIstendi += (_, s) => gidilen = s;
        await vm.KaynakAcAsync(7);
        Assert.Equal(EkstreSekmesi.Kapanan, vm.SeciliSekme);
        Assert.Same(vm.Kapananlar[0], gidilen);
    }

    [Fact]
    public async Task Tek_satir_kapaninca_liste_yeniden_kurulmaz()
    {
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3)) });
        var olaylar = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        vm.Bekleyenler.CollectionChanged += (_, e) => olaylar.Add(e.Action);
        await vm.YokSayAsync([vm.Satirlar[1]], true);
        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Remove], olaylar);
        Assert.Equal([1, 3], vm.Bekleyenler.Select(s => s.Kaynak.No));
        await vm.YokSayAsync([vm.Kapananlar[0]], false);
        Assert.Equal([1, 2, 3], vm.Bekleyenler.Select(s => s.Kaynak.No));
    }

    [Fact]
    public async Task Yeniden_okuma_satirlari_bastan_getirir()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2)) });
        Assert.True(vm.YenidenOkunabilir);
        vm.Satirlar[0].Aciklama = "Taslak";
        await vm.YenidenOkuAsync();
        Assert.Equal(1, api.YenidenOkumaSayisi);
        Assert.Equal("Hareket 1", vm.Satirlar[0].Aciklama);
        Assert.Contains("yeniden okundu: 2 satır", vm.SatirMesaji);
    }

    [Fact]
    public async Task Toplu_uygula_isaretli_satirlari_doldurur_kaydetmez_dagilimi_secilmeyenleri_atlar()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2), YonSatiri(3, "Belirsiz", 50m, "Atla"), Satir(4) with { OnerilenIslem = "KartOdemesi" }, Satir(5)) });
        Assert.Equal(["Satırdaki tür kalsın", "Gelir", "Gider"], vm.TopluTurler.Select(t => t.Ad));
        vm.TopluUygulaCommand.Execute(null);
        Assert.Contains("kutularını işaretleyin", vm.TopluHatasi);
        foreach (var s in vm.Satirlar.Take(4))
            s.Secili = true;
        vm.TopluUygulaCommand.Execute(null);
        Assert.Contains("kanal dağılımını seçin", vm.TopluHatasi);
        vm.TopluDagilim = vm.TopluDagilimlar.Single(d => d.Kod == "Esit");
        Assert.True(vm.TopluKanalGorunur);
        vm.TopluUygulaCommand.Execute(null);
        Assert.Contains("kanalları işaretleyin", vm.TopluHatasi);
        foreach (var k in vm.TopluKanallar)
            k.Secili = true;
        vm.TopluUygulaCommand.Execute(null);
        Assert.Null(vm.TopluHatasi);
        Assert.StartsWith("2 satıra uygulandı; henüz kaydedilmedi. 2 satır değişmedi", vm.TopluMesaji);
        Assert.All(vm.Satirlar.Take(2), s => Assert.Equal(("Gider", "Esit", 2), (s.IslemTuru!.Kod, s.DagilimTuru!.Kod, s.Paylar.Count)));
        Assert.Null(vm.Satirlar[2].IslemTuru);
        Assert.Equal("Otomatik", vm.Satirlar[3].DagilimTuru!.Kod);
        Assert.Null(vm.Satirlar[4].DagilimTuru);
        Assert.Equal([1, 2], vm.Satirlar.Take(2).Select(s => s.Yaz().SatirNo));

        // Tür seçilirse yönü belirsiz satır da dolar; önceki paylar kalmaz.
        vm.TopluTur = vm.TopluTurler.Single(t => t.Kod == "Gelir");
        vm.TopluDagilim = vm.TopluDagilimlar.Single(d => d.Kod == "Genel");
        vm.TopluUygulaCommand.Execute(null);
        Assert.StartsWith("3 satıra", vm.TopluMesaji);
        Assert.All(vm.Satirlar.Take(3), s => Assert.Equal(("Gelir", "Genel", 0), (s.IslemTuru!.Kod, s.DagilimTuru!.Kod, s.Paylar.Count)));
        Assert.Empty(api.KaydetIstekleri);
        Assert.Equal(0, api.OnizlemeSayisi);
    }

    [Fact]
    public async Task Toplu_uygula_kart_belgesinde_genel_kasa_sunmaz()
    {
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1) with { OnerilenIslem = "KartHarcama" }) with { Kaynak = "Kart", HesapAdi = "", KartId = 3 } });
        Assert.Equal(["Satırdaki tür kalsın", "Kart harcaması"], vm.TopluTurler.Select(t => t.Ad));
        Assert.Equal("Esit", Assert.Single(vm.TopluDagilimlar).Kod);
        Assert.Same(vm.TopluDagilimlar[0], vm.TopluDagilim);
    }

    [Fact]
    public async Task Toplu_uygula_onizlemeyi_gecersiz_kilar()
    {
        var (vm, _, _) = await Hazir();
        Sec(vm.Satirlar[0]);
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.True(vm.OnizlemeVar);
        vm.TopluDagilim = vm.TopluDagilimlar.Single(d => d.Kod == "Genel");
        vm.TopluUygulaCommand.Execute(null);
        Assert.False(vm.OnizlemeVar);
    }

    [Fact]
    public async Task Giris_cikis_metni_dayanagi_ve_toplamlari_gorunur_secilen_tur_yonu_belirler()
    {
        var (vm, _, _) = await Hazir(new()
        {
            Veri = DurumBelgesi(YonSatiri(1, "Giris", 250m, "Gelir", "Bakiye"), YonSatiri(2, "Cikis", 100m, "Gider", "Isaret"), YonSatiri(3, "Belirsiz", 40m, "Atla"),
                YonSatiri(4, "Cikis", 10m, "Gider", "Sozcuk"), YonSatiri(5, "Giris", 5m, "Gelir", "Sutun"), YonSatiri(6, "Cikis", 7m, "Gider"), Satir(7, "USD"))
        });
        Assert.Equal(["Giriş + · bakiyeden", "Çıkış − · tutarın işaretinden", "Belirsiz", "Çıkış − · açıklamadan", "Giriş + · belgenin kolonundan", "Çıkış −"], vm.Satirlar.Take(6).Select(s => s.YonMetni));
        Assert.Equal("Giriş +255,00 ₺ (2 satır) · Çıkış −117,00 ₺ (3 satır) · Belirsiz 1 satır", vm.YonOzeti);
        Assert.Equal("Hiç satır işaretli değil.", vm.SecimOzeti);

        var s = vm.Satirlar[2];
        var degisen = new List<string?>();
        s.PropertyChanged += (_, e) => degisen.Add(e.PropertyName);
        s.IslemTuru = s.IslemTurleri.Single(t => t.Kod == "Gelir");
        Assert.Contains(nameof(s.YonMetni), degisen);
        Assert.Equal(("Giris", "Giriş +"), (s.EtkinYon, s.YonMetni));
        s.TutarMetni = "60";
        s.Secili = true;
        Assert.Equal("1 satır işaretli · Giriş +60,00 ₺ (1 satır) · Çıkış −0,00 ₺ (0 satır)", vm.SecimOzeti);
        Assert.Equal("Giriş +315,00 ₺ (3 satır) · Çıkış −117,00 ₺ (3 satır)", vm.YonOzeti);
        // Seçilen tür PDF'in yönüyle çelişirse ikisi de yazar.
        vm.Satirlar[1].IslemTuru = vm.Satirlar[1].IslemTurleri.Single(t => t.Kod == "Gelir");
        Assert.Equal("Giriş + · belgede: Çıkış −", vm.Satirlar[1].YonMetni);
        // Geçersiz yazılan tutar toplamda PDF'in tutarıdır.
        s.TutarMetni = "abc";
        Assert.Equal(40m, s.EtkinTutar);
    }

    [Theory]
    [InlineData("2026-09-27", "2500,00", "TRY", "#1 · 27.09.2026 · Hareket 1 · 2.500,00 ₺")]
    [InlineData("", "", "TRY", "#1 · tarih okunamadı · Hareket 1 · tutar okunamadı")]
    [InlineData("27.9", "abc", "USD", "#1 · 27.9 · Hareket 1 · abc USD")]
    [InlineData("2026-09-27", "5", "Belirsiz", "#1 · 27.09.2026 · Hareket 1 · 5,00 (para birimi belirsiz)")]
    public void Satir_ozeti_turkce_tarih_ve_tutar_yazar(string tarih, string tutar, string para, string beklenen)
    {
        var s = new EkstreSatirEditor(Satir(1, para), Belge(), [], [], () => { }) { TarihMetni = tarih, TutarMetni = tutar };
        Assert.Equal(beklenen, s.Ozet);
    }

    [Fact]
    public async Task Kapanan_satir_kaydedilen_degerleri_gosterir()
    {
        var kayit = Kayit(7, 1) with { Aciklama = "Düzeltilmiş", Tutar = 75m };
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1)) with { Kayitlar = [kayit] } });
        Assert.Equal("Satır 1 · 27.09.2026 · Düzeltilmiş · 75,00 ₺", vm.Kapananlar[0].KapananBaslik);
        Assert.Equal(75m, vm.Kapananlar[0].EtkinTutar);
    }

    [Fact]
    public void Kart_odemesi_banka_belgesinde_cikis_kart_belgesinde_giristir()
    {
        EkstreSatirEditor Yeni(string kaynak) => new(Satir(1) with { OnerilenIslem = "KartOdemesi", Yon = "Belirsiz" }, Belge() with { Kaynak = kaynak }, [], [], () => { });
        Assert.Equal("Cikis", Yeni("Banka").EtkinYon);
        Assert.Equal("Giris", Yeni("Kart").EtkinYon);
    }

    [Fact]
    public async Task Oneri_gelmesi_onizlemeyi_ve_onayi_gecersiz_kilmaz()
    {
        var (vm, _, _) = await Hazir();
        Sec(vm.Satirlar[0]);
        await vm.OnizleCommand.ExecuteAsync(null);
        vm.Onay = true;
        vm.Satirlar[1].Oneri = new(2, "Yok", [], null, null, [], "Kural yok");
        Assert.True(vm.OnizlemeVar);
        Assert.True(vm.Onay);
    }

    [Fact]
    public async Task Atlanan_satirlar_acilip_gorulur()
    {
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1)) with { Atlananlar = [new(2, "Bloke 5,00", "Satırın başında tarih yok")] } });
        Assert.True(vm.AtlananVar);
        Assert.Equal("Atlanan satırlar (1) göster", vm.AtlananDugmesi);
        Assert.Equal("", vm.AtlananMetni);
        vm.AtlananAcKapatCommand.Execute(null);
        Assert.Equal("Atlanan satırlar (1) gizle", vm.AtlananDugmesi);
        Assert.Equal("Sayfa 2 · Satırın başında tarih yok: Bloke 5,00", vm.AtlananMetni);
    }

    [Fact]
    public async Task Oturum_degisince_sekmeler_hatalar_ve_iletiler_temizlenir()
    {
        var (vm, _, auth) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2)) with { YokSayilanlar = [2] } });
        vm.SeciliSekme = EkstreSekmesi.Kapanan;
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.True(vm.OnizlemeHataVar);
        auth.OturumSurumu++;
        Assert.False(vm.OnizlemeHataVar);
        Assert.Empty(vm.Bekleyenler);
        Assert.Empty(vm.Kapananlar);
        Assert.Empty(vm.TopluTurler);
        Assert.Equal(EkstreSekmesi.Bekleyen, vm.SeciliSekme);
        Assert.Equal(("", ""), (vm.YonOzeti, vm.AtlananMetni));
    }

    [Fact]
    public async Task Sayfada_sekme_hata_listesi_ve_kapanan_satir_dugmesi_calisir()
    {
        GorunumOrtami.Kur();
        var belge = DurumBelgesi(Satir(1), Satir(2), Satir(3)) with { Kayitlar = [Kayit(7, 1)], YokSayilanlar = [2] };
        var (vm, api, _) = await Hazir(new() { Veri = belge });
        var page = new EkstreAktarmaPage(vm);
        var kapanan = page.GetVisualTreeDescendants().OfType<CollectionView>().Single(l => l.AutomationId == "KapananSatirlar");
        Assert.Single(BekleyenListe(page).ItemsSource.Cast<object>());
        Assert.Equal(2, kapanan.ItemsSource.Cast<object>().Count());
        static bool Gorunur(Element e) => e is not VisualElement { IsVisible: false } && (e.Parent is null || Gorunur(e.Parent));
        Assert.True(Gorunur(BekleyenListe(page)));
        Assert.False(Gorunur(kapanan));
        vm.SekmeSecCommand.Execute(vm.Sekmeler[1]);
        Assert.True(Gorunur(kapanan));
        Assert.False(Gorunur(BekleyenListe(page)));

        Assert.Equal("Gerekçeyle iptal et", SatirGorunumu(kapanan, vm.Kapananlar[0]).GetVisualTreeDescendants().OfType<Button>().Single().Text);
        var geriAl = SatirGorunumu(kapanan, vm.Kapananlar[1]).GetVisualTreeDescendants().OfType<Button>().Single();
        Assert.Equal("Yok saymayı geri al", geriAl.Text);
        ((IButtonController)geriAl).SendClicked();
        Assert.False(Assert.Single(api.YokSayIstekleri).YokSay);

        vm.SekmeSecCommand.Execute(vm.Sekmeler[0]);
        vm.Satirlar[2].Secili = true;
        await vm.OnizleCommand.ExecuteAsync(null);
        var git = page.GetVisualTreeDescendants().OfType<Button>().Single(b => b.Text == "Satıra git");
        Assert.True(Gorunur(git));
        ((IButtonController)git).SendClicked();
        Assert.Same(vm.Satirlar[2], vm.SeciliSatir);
        var satir = SatirGorunumu(BekleyenListe(page), vm.Satirlar[2]);
        Assert.Contains(satir.GetVisualTreeDescendants().OfType<Label>(), l => l.Text == "Çıkış −");
        Assert.Contains(satir.GetVisualTreeDescendants().OfType<Button>(), b => b.Text == "Yok say");
    }
}
