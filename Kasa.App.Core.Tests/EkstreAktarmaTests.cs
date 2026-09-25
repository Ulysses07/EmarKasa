using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class EkstreAktarmaTests
{
    private static readonly DateOnly Tarih = new(2026, 9, 27);
    private static EkstreOkunanSatir Satir(int no, string para = "TRY") => new(no, 1, $"27.09.2026 Hareket {no} 100,00", Tarih, $"Hareket {no}", 100m, "Cikis", "Gider", "Hareket", para, []);
    private static void Sec(EkstreSatirEditor s) { s.DagilimTuru = s.DagilimTurleri.Single(x => x.Kod == "Genel"); s.Secili = true; }
    private static EkstreBelgeDto Belge() => new(1, 2, "Banka", "Akbank", "Ana hesap", null, "hesap.pdf", DateTimeOffset.UtcNow, [], [Satir(1), Satir(2)], []);
    private static async Task<(EkstreAktarmaViewModel Vm, Fake Api, AuthViewModel Auth)> Hazir(Fake? api = null, Rol rol = Rol.Editor, FinansTakipTests.Fake? takip = null)
    {
        api ??= new(); var auth = new AuthViewModel(new SahteApi()) { AktifRol = rol };
        var vm = new EkstreAktarmaViewModel(api, new SahteApi { KanallarListe = [new(1, "MEZAT", true, 0, 0), new(2, "PERAKENDE", true, 1, 0)] }, takip ?? new FinansTakipTests.Fake(), auth);
        await vm.YukleAsync(); await vm.BelgeAcAsync(1); return (vm, api, auth);
    }
    [Fact] public async Task Butun_satirlar_gorunur_hicbiri_secili_degil_yukleme_para_kaydetmez()
    {
        var (vm, api, _) = await Hazir(); Assert.Equal(2, vm.Satirlar.Count); Assert.All(vm.Satirlar, s => Assert.False(s.Secili));
        await vm.OnizleCommand.ExecuteAsync(null); Assert.Equal(0, api.OnizlemeSayisi); Assert.Empty(api.KaydetIstekleri);
        vm.Kaynak = vm.Kaynaklar.Single(x => x.Kod == "Banka"); vm.Banka = vm.Bankalar[0]; vm.HesapAdi = "Son 1234";
        var secim = vm.YuklemeSecimi()!; await vm.PdfYukleAsync([1, 2], "dosya.pdf", secim);
        Assert.Equal(1, api.YuklemeSayisi); Assert.Empty(api.KaydetIstekleri); Assert.All(vm.Satirlar, s => Assert.False(s.Secili));
    }
    [Fact] public async Task Yalniz_secilen_duzeltilen_satirlar_onaydan_sonra_kaydedilir()
    {
        var (vm, api, _) = await Hazir(); var s = vm.Satirlar[1]; s.DagilimTuru = s.DagilimTurleri.Single(x => x.Kod == "Genel"); s.Secili = true; s.TutarMetni = "123,45"; s.Aciklama = "Düzeltilmiş hareket";
        await vm.OnizleCommand.ExecuteAsync(null); await vm.KaydetCommand.ExecuteAsync(null); Assert.Empty(api.KaydetIstekleri);
        vm.Onay = true; await vm.KaydetCommand.ExecuteAsync(null);
        var kayit = Assert.Single(api.KaydetIstekleri); var y = Assert.Single(kayit.Satirlar);
        Assert.Equal(2, y.SatirNo); Assert.Equal(123.45m, y.Tutar); Assert.Equal("Düzeltilmiş hareket", y.Aciklama); Assert.Equal("ozet", kayit.OnizlemeOzeti);
        Assert.All(vm.Satirlar, r => Assert.False(r.Secili)); Assert.False(vm.OnizlemeVar);
    }
    [Theory] [InlineData("tutar")] [InlineData("secim")] [InlineData("pay")]
    public async Task Form_ve_alt_kanal_degisiklikleri_eski_onayi_gecersiz_kilar(string alan)
    {
        var (vm, api, _) = await Hazir(); var s = vm.Satirlar[0]; s.DagilimTuru = s.DagilimTurleri.Single(x => x.Kod == "Genel"); s.Secili = true;
        s.DagilimTuru = s.DagilimTurleri.Single(x => x.Kod == "Esit"); s.PayEkle(); s.Paylar[0].Kanal = vm.Kanallar[0];
        await vm.OnizleCommand.ExecuteAsync(null); vm.Onay = true;
        if (alan == "tutar") s.TutarMetni = "50"; else if (alan == "secim") vm.Satirlar[1].Secili = true; else s.Paylar[0].Kanal = vm.Kanallar[1];
        await vm.KaydetCommand.ExecuteAsync(null); Assert.Empty(api.KaydetIstekleri); Assert.False(vm.OnizlemeVar); Assert.False(vm.Onay);
    }
    [Fact] public async Task Ag_hatasi_ayni_istek_anahtari_ve_onizlemeyle_tekrarlanir()
    {
        var (vm, api, _) = await Hazir(); Sec(vm.Satirlar[0]); api.KaydetHata = true;
        await vm.OnizleCommand.ExecuteAsync(null); vm.Onay = true; await vm.KaydetCommand.ExecuteAsync(null);
        api.KaydetHata = false; await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.KaydetIstekleri.Count); Assert.Equal(api.KaydetIstekleri[0], api.KaydetIstekleri[1]); Assert.NotEqual(Guid.Empty, api.KaydetIstekleri[0].IstekId);
    }
    [Fact] public async Task Benzer_kayit_uyarisi_ek_onay_gerektirir()
    {
        var (vm, api, _) = await Hazir(new() { TekrarGerekli = true }); Sec(vm.Satirlar[0]);
        await vm.OnizleCommand.ExecuteAsync(null); vm.Onay = true; await vm.KaydetCommand.ExecuteAsync(null); Assert.Empty(api.KaydetIstekleri);
        vm.TekrarOnay = true; await vm.KaydetCommand.ExecuteAsync(null); Assert.True(Assert.Single(api.KaydetIstekleri).TekrarOnay);
    }
    [Fact] public async Task Geciken_onizleme_form_degisince_gosterilmez()
    {
        var bekle = new TaskCompletionSource<EkstreOnizlemeDto>(); var (vm, api, _) = await Hazir(new() { OnizlemeYaniti = bekle.Task }); Sec(vm.Satirlar[0]);
        var islem = vm.OnizleCommand.ExecuteAsync(null); vm.Satirlar[0].TutarMetni = "20";
        bekle.SetResult(new("eski", -100, [], [], false)); await islem; Assert.False(vm.OnizlemeVar);
    }
    [Fact] public async Task Geciken_kayit_sonucu_yeni_oturuma_tasinmaz()
    {
        var bekle = new TaskCompletionSource<EkstreBelgeDto>(); var (vm, api, auth) = await Hazir(new() { KayitYaniti = bekle.Task }); Sec(vm.Satirlar[0]);
        await vm.OnizleCommand.ExecuteAsync(null); vm.Onay = true; var islem = vm.KaydetCommand.ExecuteAsync(null); auth.OturumSurumu++;
        bekle.SetResult(Belge()); await islem; Assert.Null(vm.Belge); Assert.Empty(vm.Satirlar); Assert.Empty(vm.Gecmis); Assert.Null(vm.Mesaj);
    }
    [Theory] [InlineData(Rol.Izleyici)] [InlineData(Rol.Alici)]
    public async Task Editor_disinda_liste_ve_mutasyon_yok(Rol rol)
    {
        var (vm, api, _) = await Hazir(rol: rol); await vm.OnizleCommand.ExecuteAsync(null); await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.ListeSayisi); Assert.Equal(0, api.BelgeSayisi); Assert.Empty(api.KaydetIstekleri); Assert.DoesNotContain(Bolum.EkstreAktar, SekmeModeli.Bolumler(rol));
    }
    [Theory] [InlineData("USD")] [InlineData("EUR")]
    public async Task Yabanci_para_secili_yapilsa_bile_yazilmaz(string para)
    {
        var (vm, api, _) = await Hazir(new() { Veri = Belge() with { Satirlar = [Satir(1, para)] } });
        Assert.False(vm.Satirlar[0].Secilebilir); Sec(vm.Satirlar[0]); await vm.OnizleCommand.ExecuteAsync(null); Assert.Equal(0, api.OnizlemeSayisi); Assert.Contains("TL", vm.Hata);
    }
    [Theory] [InlineData("", "10")] [InlineData("2026-02-30", "10")] [InlineData("2026-09-27", "0")] [InlineData("2026-09-27", "-10")] [InlineData("2026-09-27", "10,001")]
    public async Task Eksik_ve_gecersiz_tarih_tutar_kullanici_duzeltmeden_yazilmaz(string tarih, string tutar)
    {
        var (vm, api, _) = await Hazir(); var s = vm.Satirlar[0]; s.DagilimTuru = s.DagilimTurleri.Single(x => x.Kod == "Genel"); s.Secili = true; s.TarihMetni = tarih; s.TutarMetni = tutar;
        await vm.OnizleCommand.ExecuteAsync(null); Assert.Equal(0, api.OnizlemeSayisi); Assert.Contains("Satır 1", vm.Hata);
    }
    [Fact] public async Task Aktif_kayitli_satir_secilemez_iptal_edilmis_satir_secilir()
    {
        var k = new EkstreKayitDto(7, 1, Tarih, "Eski", 100, "Gider", "Genel", [], null, 5, null, null, false);
        var (vm, api, _) = await Hazir(new() { Veri = Belge() with { Kayitlar = [k] } });
        Assert.False(vm.Satirlar[0].Secilebilir); Sec(vm.Satirlar[0]); await vm.OnizleCommand.ExecuteAsync(null); Assert.Equal(0, api.OnizlemeSayisi);
        api.Veri = api.Veri with { Kayitlar = [k with { Iptal = true }] }; await vm.BelgeAcAsync(1); Assert.True(vm.Satirlar[0].Secilebilir); Assert.False(vm.Satirlar[0].Secili);
    }
    [Fact] public async Task Dosya_seciminde_degisen_kaynak_ve_oturum_yukleme_yapmaz()
    {
        var (vm, api, auth) = await Hazir(); vm.Kaynak = vm.Kaynaklar[1]; vm.Banka = vm.Bankalar[0]; vm.HesapAdi = "Ana"; var secim = vm.YuklemeSecimi()!;
        vm.Banka = vm.Bankalar[1]; await vm.PdfYukleAsync([1], "a.pdf", secim); Assert.Equal(0, api.YuklemeSayisi);
        vm.Banka = vm.Bankalar[0]; auth.OturumSurumu++; await vm.PdfYukleAsync([1], "a.pdf", secim); Assert.Equal(0, api.YuklemeSayisi);
    }
    [Fact] public async Task Iptal_onayinda_eski_oturum_ve_eski_belge_satiri_gonderilmez()
    {
        var k = new EkstreKayitDto(7, 1, Tarih, "Eski", 100, "Gider", "Genel", [], null, 5, null, null, false);
        var (vm, api, auth) = await Hazir(new() { Veri = Belge() with { Kayitlar = [k] } }); var s = vm.Kayitlar[0]; var nesil = vm.OturumNesli;
        await vm.BelgeAcAsync(1); await vm.KayitIptalAsync(s, "Yanlış", nesil, 1); // aynı değerli DTO olsa da eski ekran onayı geçersiz.
        Assert.Equal(0, api.IptalSayisi);
        auth.OturumSurumu++; await vm.KayitIptalAsync(s, "Yanlış", nesil, 1); Assert.Equal(0, api.IptalSayisi);
    }
    [Fact] public void Kart_harcamasi_kanal_ister_iade_kaynak_paylarini_otomatik_alir()
    {
        var kart = FinansTakipTests.Fake.OrnekKart() with { Harcamalar = [new(7, null, Tarih, "Kaynak", 100, 1, false, [new(2, "PERAKENDE", 100)])] };
        var b = Belge() with { Kaynak = "Kart", KartId = kart.Id, Satirlar = [Satir(1) with { OnerilenIslem = "KartHarcama" }] };
        var row = new EkstreSatirEditor(b.Satirlar[0], b, [new(1, "MEZAT", true, 0, 0)], [kart], () => { });
        Assert.Throws<KasaApiException>(() => row.Yaz()); row.PayEkle(); row.Paylar[0].Kanal = row.Kanallar[0]; Assert.Equal("Esit", row.Yaz().DagilimTuru);
        row.IslemTuru = row.IslemTurleri.Single(x => x.Kod == "KartIade"); row.KaynakHarcama = row.KaynakHarcamalar.Single();
        var g = row.Yaz(); Assert.Equal(7, g.KaynakHarcamaId); Assert.Equal("Otomatik", g.DagilimTuru); Assert.Empty(g.Dagilimlar); Assert.Equal(100, g.Tutar);
    }
    [Fact] public async Task Ithal_islem_duzenlenmez_silinmez_ve_genel_gelir_ay_sonucuna_eklenir()
    {
        var api = new SahteApi { AylikRapor = new(2026, 9, [], 10, 20, 100) };
        var vm = new IslemlerViewModel(api); var i = new IslemDto(2, Tarih, "PDF", 10, "", GiderTipi.Cari, null, EkstreKayitId: 9);
        vm.Duzenle(i); Assert.Equal(0, vm.DuzenId); await vm.SilCommand.ExecuteAsync(i); Assert.Null(api.SonIslemSil); Assert.Contains("Ekstre", vm.Hata);
        var rapor = new AylikViewModel(api); await rapor.YukleAsync(); Assert.Equal(70, rapor.GenelAySonucu); Assert.True(rapor.GenelGelirVar);
    }
    [Fact] public async Task Banka_satirinda_genel_kasa_varsayilmaz_acik_secim_istenir()
    {
        var (vm, api, _) = await Hazir(); var s = vm.Satirlar[0]; Assert.Null(s.DagilimTuru); s.Secili = true;
        await vm.OnizleCommand.ExecuteAsync(null); Assert.Equal(0, api.OnizlemeSayisi); Assert.Contains("dağılım", vm.Hata);
        s.DagilimTuru = s.DagilimTurleri.Single(x => x.Kod == "Genel"); await vm.OnizleCommand.ExecuteAsync(null); Assert.Contains("Yalnız genel kasa", vm.OnizlemeMetni);
    }
    [Fact] public async Task Pasif_yeni_karta_odeme_girilebilir_ve_belge_karti_formdan_bagimsiz_gosterilir()
    {
        var kart = FinansTakipTests.Fake.OrnekKart() with { Aktif = false, Ad = "Pasif kart" };
        var (vm, api, _) = await Hazir(new() { Veri = Belge() with { Kaynak = "Kart", KartId = 1, Satirlar = [Satir(1) with { OnerilenIslem = "KartOdemesi" }] } }, takip: new() { Kart = kart });
        Assert.Single(vm.Kartlar); Assert.Contains("Pasif kart (#1)", vm.BelgeOzeti); vm.Satirlar[0].Secili = true;
        await vm.OnizleCommand.ExecuteAsync(null); Assert.Equal(1, api.OnizlemeSayisi); Assert.True(vm.OnizlemeVar);
    }
    [Fact] public async Task Eski_belge_sayfalamasi_kaynak_lookup_ile_bozulmaz()
    {
        EkstreBelgeOzetDto Ozet(int id) => new(id, 0, "Banka", "QNB", "Ana", null, $"{id}.pdf", DateTimeOffset.UtcNow, 1, 0);
        var api = new Fake { Liste = Enumerable.Range(51, 50).Reverse().Select(Ozet).ToArray(), EskiListe = Enumerable.Range(1, 50).Reverse().Select(Ozet).ToArray() };
        var (vm, _, _) = await Hazir(api); Assert.True(vm.EskiBelgeVar);
        await vm.KaynakAcAsync(444); Assert.Equal(444, api.KaynakId);
        await vm.EskiBelgeleriYukleCommand.ExecuteAsync(null); Assert.Equal(51, api.SonBeforeId); Assert.Equal(100, vm.Gecmis.Select(g => g.Veri.Id).Distinct().Count());
        api.EskiListe = []; await vm.EskiBelgeleriYukleCommand.ExecuteAsync(null); Assert.Equal(1, api.SonBeforeId); Assert.False(vm.EskiBelgeVar);
    }
    [Fact] public async Task Geciken_kaynak_lookup_oturum_degistiginde_yansitilmaz()
    {
        var tcs = new TaskCompletionSource<EkstreBelgeDto>(); var (vm, api, auth) = await Hazir(new() { KaynakYaniti = tcs.Task });
        var islem = vm.KaynakAcAsync(44); auth.OturumSurumu++; tcs.SetResult(Belge()); await islem; Assert.Null(vm.Belge); Assert.Empty(vm.Kayitlar);
    }
    private sealed class Fake : IEkstreAktarmaApi
    {
        public EkstreBelgeDto Veri = Belge(); public int ListeSayisi, BelgeSayisi, YuklemeSayisi, OnizlemeSayisi, IptalSayisi;
        public bool KaydetHata, TekrarGerekli; public Task<EkstreOnizlemeDto>? OnizlemeYaniti; public Task<EkstreBelgeDto>? KayitYaniti, KaynakYaniti;
        public IReadOnlyList<EkstreBelgeOzetDto> Liste = [], EskiListe = []; public int? SonBeforeId, KaynakId;
        public List<EkstreKaydetYaz> KaydetIstekleri = [];
        public Task<IReadOnlyList<EkstreBelgeOzetDto>> EkstreBelgelerAsync(int? beforeId = null) { ListeSayisi++; SonBeforeId = beforeId; return Task.FromResult(beforeId is null ? Liste : EskiListe); }
        public Task<EkstreBelgeDto> EkstreBelgeAsync(int id) { BelgeSayisi++; return Task.FromResult(Veri); }
        public Task<EkstreBelgeDto> EkstreKaynakBelgeAsync(int kayitId) { KaynakId = kayitId; return KaynakYaniti ?? Task.FromResult(Veri); }
        public Task<EkstreBelgeDto> EkstreYukleAsync(byte[] b, string ad, string kaynak, string banka, string hesapAdi, int? kartId, CancellationToken ct = default) { YuklemeSayisi++; return Task.FromResult(Veri); }
        public Task<IndirilenDosya> EkstreDosyaAsync(int id) => Task.FromResult(new IndirilenDosya([1], "belge.pdf", "application/pdf"));
        public Task<EkstreOnizlemeDto> EkstreOnizlemeAsync(int id, EkstreKaydetYaz g) { OnizlemeSayisi++; return OnizlemeYaniti ?? Task.FromResult(new EkstreOnizlemeDto("ozet", -g.Satirlar.Sum(s => s.Tutar), g.Satirlar.Select(s => new EkstreSatirOnizleme(s.SatirNo, s.Tarih, s.Aciklama, s.Tutar, s.IslemTuru, -s.Tutar, [], [])).ToArray(), [], TekrarGerekli)); }
        public Task<EkstreBelgeDto> EkstreKaydetAsync(int id, EkstreKaydetYaz g) { KaydetIstekleri.Add(g); if (KaydetHata) throw new HttpRequestException(); return KayitYaniti ?? Task.FromResult(Veri with { Surum = Veri.Surum + 1 }); }
        public Task<EkstreBelgeDto> EkstreKayitIptalAsync(int id, int kayitId, EkstreIptalYaz g) { IptalSayisi++; return Task.FromResult(Veri); }
    }
}
