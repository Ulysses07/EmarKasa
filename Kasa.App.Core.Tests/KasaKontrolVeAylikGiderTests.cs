using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class KasaKontrolVeAylikGiderTests
{
    private static AuthViewModel Auth(Rol rol = Rol.Editor) => new(new SahteApi()) { AktifRol = rol };
    private static SahteApi Finans() => new() { KanallarListe = new[] { new KanalDto(1, "MEZAT", true, 0, 0), new KanalDto(2, "PERAKENDE", true, 1, 0) } };
    private static async Task<AylikGiderViewModel> Aylik(Fake f, AuthViewModel? auth = null) { var v = new AylikGiderViewModel(f, Finans(), auth ?? Auth()); await v.YukleAsync(); return v; }
    private static void SablonFormu(AylikGiderViewModel v) { v.Ad = "Kira"; v.Tur = v.Turler[0]; v.Tutar = 100; }
    [Fact] public async Task Sablon_okumasi_ve_kaydi_odeme_yaratmaz_dagilim_acikca_secilir()
    {
        var f = new Fake(); var v = await Aylik(f); Assert.Empty(f.Odemeler); SablonFormu(v);
        await v.SablonKaydetCommand.ExecuteAsync(null); Assert.Null(f.Sablon); Assert.Contains("dağılım", v.Hata);
        v.DagilimTuru = v.DagilimTurleri[0]; await v.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Equal("Genel", f.Sablon!.DagilimTuru); Assert.Empty(f.Sablon.Dagilimlar); Assert.Empty(f.Odemeler); Assert.Contains("oluşturulmadı", v.Mesaj);
    }
    [Fact] public async Task Esit_dagilim_secilen_kanallari_sifir_tutarla_sunucuya_gonderir()
    {
        var f = new Fake(); var v = await Aylik(f); SablonFormu(v); v.DagilimTuru = v.DagilimTurleri[1]; v.KanalSecimleri[1].Secili = true;
        await v.SablonKaydetCommand.ExecuteAsync(null); Assert.Equal(new KanalPayYaz(2, 0), Assert.Single(f.Sablon!.Dagilimlar));
    }
    [Fact] public async Task Ozel_dagilim_toplami_uyusmazsa_kaydetmez()
    {
        var f = new Fake(); var v = await Aylik(f); SablonFormu(v); v.DagilimTuru = v.DagilimTurleri[2]; v.PayEkle(); v.Paylar[0].Kanal = v.Kanallar[0]; v.Paylar[0].Tutar = 99;
        await v.SablonKaydetCommand.ExecuteAsync(null); Assert.Null(f.Sablon); Assert.Contains("toplamı", v.Hata);
    }
    [Fact] public async Task Aylik_odeme_onay_ister_ve_ag_hatasinda_ayni_istegi_tekrarlar()
    {
        var f = new Fake { OdemeHata = true }; var v = await Aylik(f); v.OdemeSec(v.Kayitlar[0]);
        await v.OdeCommand.ExecuteAsync(null); Assert.Empty(f.Odemeler);
        v.OdemeOnay = true; await v.OdeCommand.ExecuteAsync(null); f.OdemeHata = false; await v.OdeCommand.ExecuteAsync(null);
        Assert.Equal(2, f.Odemeler.Count); Assert.Equal(f.Odemeler[0], f.Odemeler[1]); Assert.NotEqual(Guid.Empty, f.Odemeler[0].IstekId); Assert.Null(v.SeciliOdeme);
    }
    [Fact] public async Task Ay_secimi_degistiginde_eski_plan_odenemez()
    {
        var f = new Fake(); var v = await Aylik(f); var satir = v.Kayitlar[0]; v.AyTarihi = v.AyTarihi.AddMonths(1); v.OdemeSec(satir); v.OdemeOnay = true;
        await v.OdeCommand.ExecuteAsync(null); Assert.Empty(f.Odemeler); Assert.Null(v.SeciliOdeme); Assert.True(v.AySecimiDegisti);
    }
    [Fact] public async Task Gec_donen_baska_ayin_yaniti_yeni_baslik_altinda_gosterilmez()
    {
        var t = new TaskCompletionSource<AylikGiderAyDto>(); var f = new Fake { BekleyenAy = t.Task }; var v = new AylikGiderViewModel(f, Finans(), Auth()); var eski = v.AyTarihi;
        var yukle = v.YukleAsync(); v.AyTarihi = eski.AddMonths(1); t.SetResult(f.Ay(eski.Year, eski.Month)); await yukle;
        Assert.False(v.VeriHazir); Assert.Empty(v.Kayitlar);
    }
    [Fact] public async Task Oturum_degisiminde_bekleyen_ay_ve_bakiye_yaniti_yok_sayilir()
    {
        var ay = new TaskCompletionSource<AylikGiderAyDto>(); var f = new Fake { BekleyenAy = ay.Task }; var auth = Auth(); var v = new AylikGiderViewModel(f, Finans(), auth); var islem = v.YukleAsync();
        auth.OturumSurumu++; ay.SetResult(f.Ay(2026, 9)); await islem; Assert.Empty(v.Kayitlar); Assert.False(v.VeriHazir);
        var preview = new TaskCompletionSource<KasaKontrolOnizlemeDto>(); f.BekleyenKontrol = preview.Task; var k = new KasaKontrolViewModel(f, auth) { GercekBakiye = 90 };
        var p = k.OnizleCommand.ExecuteAsync(null); auth.OturumSurumu++; preview.SetResult(new(100, 90, -10, "eski")); await p; Assert.Null(k.Karsilastirma);
    }
    [Fact] public async Task Gercek_bakiye_degistiginde_onizleme_tekrar_alinmalidir()
    {
        var f = new Fake(); var v = new KasaKontrolViewModel(f, Auth()) { GercekBakiye = -20 }; await v.OnizleCommand.ExecuteAsync(null); v.GercekBakiye = -21;
        await v.KaydetCommand.ExecuteAsync(null); Assert.Empty(f.Kontroller); Assert.Contains("önce", v.Hata!.ToLowerInvariant());
        await v.OnizleCommand.ExecuteAsync(null); await v.KaydetCommand.ExecuteAsync(null); Assert.Equal(-21, Assert.Single(f.Kontroller).GercekBakiye); Assert.Equal("hash", f.Kontroller[0].KontrolOzeti); Assert.Contains("değiştirilmedi", v.Mesaj);
    }
    [Fact] public async Task Bakiye_409_onizlemeyi_gecersizlestirir_ag_hatasi_anahtari_korur()
    {
        var f = new Fake { KontrolHata = new HttpRequestException() }; var v = new KasaKontrolViewModel(f, Auth()); await v.OnizleCommand.ExecuteAsync(null); await v.KaydetCommand.ExecuteAsync(null);
        f.KontrolHata = new KasaApiException(HttpStatusCode.Conflict, "Bakiye değişti"); await v.KaydetCommand.ExecuteAsync(null); Assert.Equal(f.Kontroller[0], f.Kontroller[1]);
        f.KontrolHata = null; await v.KaydetCommand.ExecuteAsync(null); Assert.Equal(2, f.Kontroller.Count); Assert.Null(v.Karsilastirma);
    }
    [Fact] public async Task Esik_sifir_degeri_ve_kapali_durum_acikca_kaydedilir()
    {
        var f = new Fake(); var v = new KasaEsikViewModel(f, Auth()); await v.YukleAsync(); v.Secili = v.Kanallar[0]; v.Tutar = 0; v.Etkin = false; await v.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(new KasaEsikYaz(2, 0, false), f.Esik); var panel = new KasaKontrolViewModel(f, Auth()); await panel.YukleAsync(); Assert.Contains("MEZAT", panel.EsikUyarilari); Assert.DoesNotContain("PERAKENDE", panel.EsikUyarilari);
    }
    [Fact] public async Task Negatif_esik_kaydedilmez()
    {
        var f = new Fake(); var v = new KasaEsikViewModel(f, Auth()); await v.YukleAsync(); v.Secili = v.Kanallar[0]; v.Tutar = -1; await v.KaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Esik); Assert.Contains("sıfır veya pozitif", v.Hata);
    }
    [Fact] public async Task Basarili_odeme_sonrasi_yenileme_hatasi_eski_plani_guncel_gostermez()
    {
        var f = new Fake(); var v = await Aylik(f); v.OdemeSec(v.Kayitlar[0]); v.OdemeOnay = true;
        f.BekleyenAy = Task.FromException<AylikGiderAyDto>(new HttpRequestException()); await v.OdeCommand.ExecuteAsync(null);
        Assert.Single(f.Odemeler); Assert.False(v.VeriHazir); Assert.Null(v.SeciliOdeme); Assert.Contains("ulaşılamadı", v.Hata);
    }
    [Fact] public async Task Izleyici_okuyabilir_ama_yeni_mali_akislara_yazamaz()
    {
        var f = new Fake(); var auth = Auth(Rol.Izleyici); var aylik = await Aylik(f, auth); SablonFormu(aylik); aylik.DagilimTuru = aylik.DagilimTurleri[0]; await aylik.SablonKaydetCommand.ExecuteAsync(null);
        var kontrol = new KasaKontrolViewModel(f, auth); await kontrol.OnizleCommand.ExecuteAsync(null); var esik = new KasaEsikViewModel(f, auth); await esik.YukleAsync(); esik.Secili = esik.Kanallar[0]; await esik.KaydetCommand.ExecuteAsync(null);
        var kilit = new AyKilidiViewModel(f, auth); await kilit.YukleAsync(); await kilit.DegistirAsync(true, 2025, 1, "Kontrol", kilit.OturumNesli, 3);
        Assert.Null(f.Sablon); Assert.Empty(f.Odemeler); Assert.Empty(f.Kontroller); Assert.Null(f.Esik); Assert.Null(f.KilitGirdi);
        Assert.Contains(Bolum.AylikGiderler, SekmeModeli.Bolumler(Rol.Izleyici)); Assert.DoesNotContain(Bolum.AylikGiderler, SekmeModeli.Bolumler(Rol.Alici));
    }
    [Fact] public async Task Ay_kilidi_tamamlanmamis_ayi_kapatmaz_ve_acma_yonunu_korur()
    {
        var f = new Fake(); var v = new AyKilidiViewModel(f, Auth()); await v.YukleAsync(); var bugun = DateTime.Today;
        await v.DegistirAsync(true, bugun.Year, bugun.Month, "Kontrol", v.OturumNesli, 3); Assert.Null(f.KilitGirdi);
        await v.DegistirAsync(false, 2025, 12, "Düzeltme", v.OturumNesli, 3); Assert.False(f.Kapat); Assert.Equal(3, f.KilitGirdi!.Surum); Assert.Equal(12, f.KilitGirdi.Ay); Assert.Contains("sonraki", v.Mesaj);
    }
    [Fact] public async Task Eski_oturumun_acik_iptal_penceresi_yeni_editor_adina_islem_yapmaz()
    {
        var f = new Fake { Odendi = true }; var auth = Auth(); var v = await Aylik(f, auth); var eskiSatir = v.Kayitlar[0]; var eskiOturum = v.OturumNesli;
        auth.OturumSurumu++; await v.YukleAsync(); await v.IptalAsync(eskiSatir, "Eski pencere", eskiOturum); Assert.Equal(0, f.IptalSayisi);
        var yeniSatir = v.Kayitlar[0]; v.AyTarihi = v.AyTarihi.AddMonths(1); await v.IptalAsync(yeniSatir, "Ay değişti", v.OturumNesli); Assert.Equal(0, f.IptalSayisi);
    }
    [Fact] public async Task Yenilenen_listede_artik_olmayan_odeme_iptal_edilemez()
    {
        var f = new Fake { Odendi = true }; var v = await Aylik(f); var eski = v.Kayitlar[0]; f.Odendi = false; await v.YukleAsync();
        await v.IptalAsync(eski, "Eski pencere", v.OturumNesli); Assert.Equal(0, f.IptalSayisi); Assert.Contains("yenileyip", v.Hata);
    }
    [Fact] public async Task Ay_kilidi_onayi_oturuma_ve_gosterilen_surume_baglidir()
    {
        var f = new Fake(); var auth = Auth(); var v = new AyKilidiViewModel(f, auth); await v.YukleAsync(); var oturum = v.OturumNesli;
        auth.OturumSurumu++; await v.YukleAsync(); await v.DegistirAsync(false, 2025, 12, "Eski pencere", oturum, 3); Assert.Null(f.KilitGirdi);
        f.KilitSurumu = 4; await v.YukleAsync(); await v.DegistirAsync(false, 2025, 12, "Eski sürüm", v.OturumNesli, 3); Assert.Null(f.KilitGirdi); Assert.Contains("yeniden onaylayın", v.Hata);
    }
    [Fact] public async Task Aylik_gider_gider_editorunden_degistirilemez_ve_genel_gider_raporda_bir_kez_duser()
    {
        var finans = Finans(); var v = new IslemlerViewModel(finans); var i = new IslemDto(2, new(2026, 9, 1), "Kira", 100, "", GiderTipi.Cari, null, AylikGiderOdemeId: 8); v.Duzenle(i); await v.SilCommand.ExecuteAsync(i);
        Assert.Equal(0, v.DuzenId); Assert.Null(finans.SonIslemSil); Assert.Contains("Aylık Giderler", v.Hata);
        var rapor = new AylikViewModel(finans) { Rapor = new(2026, 9, new[] { new KanalAylikDto("A", 0, 0, 0, 0, 0, 500) }, 20, 100) };
        Assert.Equal(380, rapor.GenelAySonucu); Assert.True(rapor.GenelGiderVar);
    }
    [Fact] public async Task Kart_masrafi_hash_ile_onizlenir_degisen_form_kaydedilmez_ve_retry_id_sabittir()
    {
        var f = new Fake(); var kart = new FinansTakipTests.Fake(); var v = new KartTakipViewModel(kart, Finans(), Auth(), kontrolApi: f); await v.YukleAsync(); v.SecCommand.Execute(v.Kartlar[0]); v.MasrafEkstresi = v.MasrafEkstreleri[0]; v.MasrafTutari = 10; v.MasrafAciklama = "Banka faizi";
        await v.MasrafOnizleCommand.ExecuteAsync(null); v.MasrafTutari = 11; await v.MasrafKaydetCommand.ExecuteAsync(null); Assert.Empty(f.Masraflar);
        await v.MasrafOnizleCommand.ExecuteAsync(null); f.MasrafHata = true; await v.MasrafKaydetCommand.ExecuteAsync(null); f.MasrafHata = false; await v.MasrafKaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, f.Masraflar.Count); Assert.Equal(f.Masraflar[0], f.Masraflar[1]); Assert.Equal("pay-hash", f.Masraflar[0].DagilimOzeti); Assert.NotEmpty(v.MasrafEkstreleri); Assert.Equal(0, v.MasrafTutari);
    }
    internal sealed class Fake : IKasaKontrolApi, IAylikGiderApi
    {
        public AylikGiderSablonYaz? Sablon; public List<AylikGiderOdemeYaz> Odemeler = new(); public bool OdemeHata; public Task<AylikGiderAyDto>? BekleyenAy; public bool Odendi; public int IptalSayisi; public int KilitSurumu = 3;
        public Task<KasaKontrolOnizlemeDto>? BekleyenKontrol; public List<KasaKontrolYaz> Kontroller = new(); public Exception? KontrolHata; public KasaEsikYaz? Esik; public AyKilidiYaz? KilitGirdi; public bool Kapat;
        public List<KartMasrafYaz> Masraflar = new(); public bool MasrafHata;
        public AylikGiderAyDto Ay(int y, int a) => new(y, a, 100, Odendi ? 100 : 0, new[] { new AylikGiderSatirDto(1, 4, "Kira", "Kira", 100, new(y, a, 1), "Genel", Array.Empty<TakipKanalPayi>(), Odendi ? "Odendi" : "Planlandi", Odendi ? 2 : null) });
        public Task<IReadOnlyList<AylikGiderSablonDto>> AylikGiderSablonlariAsync() => Task.FromResult<IReadOnlyList<AylikGiderSablonDto>>(Array.Empty<AylikGiderSablonDto>());
        public Task<AylikGiderSablonDto> AylikGiderSablonKaydetAsync(int? id, AylikGiderSablonYaz g) { Sablon = g; return Task.FromResult(new AylikGiderSablonDto(1, 1, g.Ad, g.Tur, g.Tutar, g.OdemeGunu, g.DagilimTuru, Array.Empty<TakipKanalPayi>(), g.GecerliAy, g.Aktif)); }
        public Task<AylikGiderAyDto> AylikGiderlerAsync(int y, int a) => BekleyenAy ?? Task.FromResult(Ay(y, a));
        public Task<AylikGiderSatirDto> AylikGiderOdeAsync(int id, AylikGiderOdemeYaz g) { Odemeler.Add(g); return OdemeHata ? Task.FromException<AylikGiderSatirDto>(new HttpRequestException()) : Task.FromResult(Ay(g.Yil, g.Ay).Kayitlar[0] with { Durum = "Odendi", OdemeId = 2 }); }
        public Task<AylikGiderSatirDto> AylikGiderIptalAsync(int id, AylikGiderIptalYaz g) { IptalSayisi++; return Task.FromResult(Ay(2026, 9).Kayitlar[0] with { Durum = "Iptal" }); }
        public Task<AyKilidiDto> AyKilidiAsync() => Task.FromResult(new AyKilidiDto(KilitSurumu, null, Array.Empty<AyKilidiOlayDto>()));
        public Task<AyKilidiDto> AyKilidiDegistirAsync(bool kapat, AyKilidiYaz g) { Kapat = kapat; KilitGirdi = g; return AyKilidiAsync(); }
        public Task<IReadOnlyList<KasaEsikDto>> KasaEsikleriAsync() => Task.FromResult<IReadOnlyList<KasaEsikDto>>(new[] { new KasaEsikDto(1, "MEZAT", 2, 10, true, -20, true), new KasaEsikDto(2, "PERAKENDE", 0, 100, false, 0, true) });
        public Task<KasaEsikDto> KasaEsigiKaydetAsync(int id, KasaEsikYaz g) { Esik = g; return Task.FromResult(new KasaEsikDto(id, "MEZAT", 3, g.Tutar, g.Etkin, -20, true)); }
        public Task<IReadOnlyList<KasaKontrolDto>> KasaKontrolleriAsync() => Task.FromResult<IReadOnlyList<KasaKontrolDto>>(Array.Empty<KasaKontrolDto>());
        public Task<KasaKontrolOnizlemeDto> KasaKontrolOnizleAsync(KasaKontrolOnizle g) => BekleyenKontrol ?? Task.FromResult(new KasaKontrolOnizlemeDto(100, g.GercekBakiye, g.GercekBakiye - 100, "hash"));
        public Task<KasaKontrolDto> KasaKontrolKaydetAsync(KasaKontrolYaz g) { Kontroller.Add(g); return KontrolHata is { } e ? Task.FromException<KasaKontrolDto>(e) : Task.FromResult(new KasaKontrolDto(1, DateTimeOffset.Now, 100, g.GercekBakiye, g.GercekBakiye - 100, g.Not)); }
        public Task<KartMasrafOnizlemeDto> KartMasrafOnizleAsync(int id, KartMasrafYaz g) => Task.FromResult(new KartMasrafOnizlemeDto(id, g.EkstreId, g.Tarih, g.Tutar, 100, new[] { new TakipKanalPayi(1, "MEZAT", g.Tutar) }, "pay-hash"));
        public Task<KartTakipDto> KartMasrafKaydetAsync(int id, KartMasrafYaz g) { Masraflar.Add(g); return MasrafHata ? Task.FromException<KartTakipDto>(new HttpRequestException()) : Task.FromResult(FinansTakipTests.Fake.OrnekKart() with { Surum = 4 }); }
    }
}
