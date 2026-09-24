using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket E: kayda soru sorma (hedef, yönlendirme, Sorular sayfası, Panel kartı).</summary>
public class PaketESoruTests
{
    private static readonly DateTime An = new(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc);

    private static SoruDto S(int id, SoruDurumu durum = SoruDurumu.Acik, string? cevap = null, SoruHedefTuru tur = SoruHedefTuru.Islem)
        => new(id, tur, tur == SoruHedefTuru.Islem ? 40 + id : null, null,
            tur == SoruHedefTuru.Islem ? "24.09.2026 · MEZAT · 1.250,50 ₺ · Nakit" : null,
            $"Soru {id}", 5, "AHMET", "viewer", An, cevap, cevap is null ? null : "EMAR", cevap is null ? null : An.AddHours(1),
            durum, durum == SoruDurumu.Kapali ? An.AddHours(1) : null);

    private static (SahteApi api, SorularViewModel vm) Kur(bool editor = false, SoruYonlendirme? y = null, Action<SahteApi>? ayar = null)
    {
        var api = new SahteApi();
        ayar?.Invoke(api);
        var vm = new SorularViewModel(api, y, new SabitSaat(new DateTime(2026, 9, 24, 12, 0, 0))) { EditorMu = editor };
        return (api, vm);
    }

    // ── Hedef ──

    [Fact]
    public void Islem_satirindan_hedef()
    {
        var h = SoruHedefi.Olustur(new IslemDto(12, new DateOnly(2026, 9, 24), "MEZAT alış", 1250.5m, "Nakit", GiderTipi.Cari, null))!;
        Assert.Equal(SoruHedefTuru.Islem, h.Tur);
        Assert.Equal(12, h.Id);
        Assert.Null(h.Hafta);
        Assert.Equal("24.09.2026 · MEZAT alış · 1.250,50 ₺ · Nakit", h.Ozet);
    }

    [Fact]
    public void Cek_gorunumunden_hedef()
    {
        var cek = new CekDto(3, CekYonu.Alinan, "0012", "Ziraat", "Veli", 5000m, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 15),
            "Nakit", CekDurumu.Portfoyde, null, null);
        var h = SoruHedefi.Olustur(new CekGorunum(cek, new DateOnly(2026, 9, 24)))!;
        Assert.Equal(SoruHedefTuru.Cek, h.Tur);
        Assert.Equal(3, h.Id);
        Assert.Equal("Alınan çek · Veli · 5.000,00 ₺ · vade 15.10.2026", h.Ozet);
    }

    [Fact]
    public void Hafta_satirindan_hedef_donem_baslangici()
    {
        var donem = new DonemDto(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), 2026, 9);
        var h = SoruHedefi.Olustur(new HaftalikOzetDto(donem, [], 0, 0, 0, 0))!;
        Assert.Equal(SoruHedefTuru.Hafta, h.Tur);
        Assert.Null(h.Id);
        Assert.Equal(new DateOnly(2026, 9, 21), h.Hafta);
        Assert.Equal("Hafta 21.09 – 27.09.2026", h.Ozet);
    }

    [Fact]
    public void Taninmayan_kayit_null()
        => Assert.Null(SoruHedefi.Olustur("x"));

    [Fact]
    public void Yonlendirme_hedefi_bekletir_ve_sorular_sayfasini_ister()
    {
        var y = new Yonlendirme();
        var s = new SoruYonlendirme(y);
        s.Sor(new IslemDto(12, new DateOnly(2026, 9, 24), "a", 1m, "Nakit", GiderTipi.Cari, null));

        Assert.Equal("//sorular", y.Al());
        var h = s.Al();
        Assert.Equal(12, h!.Id);
        Assert.Null(s.Al());                     // bir kez alınır
    }

    [Fact]
    public void Statik_komut_varsayilan_ornege_gider()
    {
        var y = new Yonlendirme();
        var onceki = SoruYonlendirme.Varsayilan;
        try
        {
            SoruYonlendirme.Varsayilan = new SoruYonlendirme(y);
            SoruYonlendirme.SorKomutu.Execute(new DonemDto(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), 2026, 9));
            Assert.Equal(SoruHedefTuru.Hafta, SoruYonlendirme.Varsayilan.Al()!.Tur);
            Assert.Equal("//sorular", y.Al());
        }
        finally { SoruYonlendirme.Varsayilan = onceki; }
    }

    // ── Sorular sayfası ──

    [Fact]
    public async Task Yukle_acik_sorulari_getirir_basligi_kurar()
    {
        var (api, vm) = Kur(ayar: a => a.SorularListe = [S(1), S(2, SoruDurumu.Kapali, "tamam"), S(3)]);
        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.Equal(SoruDurumu.Acik, api.SoruCagrilari.Single().Durum);
        Assert.Equal([3, 1], vm.Sorular.Select(s => s.Id));
        Assert.Equal("Açık sorular (2)", vm.Baslik);
        Assert.Equal("İşlem · 24.09.2026 · MEZAT · 1.250,50 ₺ · Nakit", vm.Sorular[0].HedefBaslik);
        Assert.Equal("AHMET · İzleyici · 24.09.2026 09:00", vm.Sorular[0].SoranMetni);
    }

    [Fact]
    public async Task Filtre_tumu_hepsini_getirir()
    {
        var (api, vm) = Kur(ayar: a => a.SorularListe = [S(1), S(2, SoruDurumu.Kapali, "tamam")]);
        await vm.YukleAsync();

        await vm.FiltreSecCommand.ExecuteAsync(vm.Filtreler.Single(f => f.Ad == SorularViewModel.FiltreTumu));

        Assert.Null(api.SoruCagrilari[^1].Durum);
        Assert.Equal(2, vm.Sorular.Count);
        var kapali = vm.Sorular.Single(s => s.Id == 2);
        Assert.Equal("Kapalı", kapali.DurumAdi);
        Assert.Equal("Cevap · EMAR · 24.09.2026 10:00", kapali.CevapMetni);
    }

    [Fact]
    public async Task Satirdan_gelince_form_hedefle_acilir_ve_soru_gonderilir()
    {
        var y = new SoruYonlendirme();
        y.Sor(new IslemDto(12, new DateOnly(2026, 9, 24), "MEZAT alış", 1250.5m, "Nakit", GiderTipi.Cari, null));
        var (api, vm) = Kur(y: y);

        await vm.YukleAsync();
        Assert.True(vm.FormAcik);
        Assert.Equal("İşlem · 24.09.2026 · MEZAT alış · 1.250,50 ₺ · Nakit", vm.HedefMetni);

        vm.YeniMetin = "  Bu ödeme hangi fatura?  ";
        await vm.SorCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal(new SoruYaz(SoruHedefTuru.Islem, 12, null, "Bu ödeme hangi fatura?"), api.SonSoru);
        Assert.False(vm.FormAcik);
        Assert.NotNull(vm.Bilgi);
        Assert.Single(vm.Sorular);
        Assert.Equal(SoruHedefi.Genel, vm.Hedef);
    }

    [Fact]
    public async Task Bos_soru_gonderilmez()
    {
        var (api, vm) = Kur();
        vm.GenelSoruCommand.Execute(null);
        vm.YeniMetin = "   ";
        await vm.SorCommand.ExecuteAsync(null);
        Assert.Equal("Sorunuzu yazın.", vm.Hata);
        Assert.Null(api.SonSoru);
    }

    [Fact]
    public async Task Cok_uzun_soru_gonderilmez()
    {
        var (api, vm) = Kur();
        vm.GenelSoruCommand.Execute(null);
        vm.YeniMetin = new string('a', SorularViewModel.MetinEnCok + 1);
        await vm.SorCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Hata);
        Assert.Null(api.SonSoru);
    }

    [Fact]
    public void Hedef_kaldirilinca_genel_soru()
    {
        var (_, vm) = Kur();
        vm.FormuAc(new SoruHedefi(SoruHedefTuru.Cek, 3, null, "x"));
        Assert.True(vm.HedefKaldirilabilir);
        vm.HedefiKaldirCommand.Execute(null);
        Assert.Equal("Genel soru", vm.HedefMetni);
        Assert.False(vm.HedefKaldirilabilir);
    }

    [Fact]
    public async Task Izleyicide_editor_dugmeleri_gizli_ve_cevap_reddedilir()
    {
        var (api, vm) = Kur(editor: false, ayar: a => a.SorularListe = [S(1)]);
        await vm.YukleAsync();
        var s = vm.Sorular[0];
        Assert.False(s.CevapFormuGorunur);
        Assert.False(s.KapatGorunur);

        s.CevapTaslak = "cevap";
        await vm.CevaplaCommand.ExecuteAsync(s);

        Assert.Equal(HataMesaji.Yetkisiz, vm.Hata);
        Assert.Null(api.SonCevap);
    }

    [Fact]
    public async Task Editor_cevaplar_ve_kapatir()
    {
        var (api, vm) = Kur(editor: true, ayar: a => a.SorularListe = [S(1)]);
        await vm.YukleAsync();
        var s = vm.Sorular[0];
        Assert.True(s.KapatGorunur);

        s.CevapTaslak = "  Fatura 123.  ";
        await vm.CevaplaCommand.ExecuteAsync(s);

        Assert.Null(vm.Hata);
        Assert.Equal((1, "Fatura 123.", true), api.SonCevap);
        Assert.Empty(vm.Sorular);                 // açık filtresinde kapananlar düşer
        Assert.Equal("Sorular", vm.Baslik);
    }

    [Fact]
    public async Task Editor_acik_kalsin_ile_cevaplar()
    {
        var (api, vm) = Kur(editor: true, ayar: a => a.SorularListe = [S(1)]);
        await vm.YukleAsync();
        var s = vm.Sorular[0];
        s.CevapTaslak = "Bakıyorum.";
        await vm.CevaplaAcikKalsinCommand.ExecuteAsync(s);

        Assert.Equal((1, "Bakıyorum.", false), api.SonCevap);
        Assert.Equal("Cevaplandı", vm.Sorular.Single().DurumAdi);
    }

    [Fact]
    public async Task Bos_cevap_gonderilmez()
    {
        var (api, vm) = Kur(editor: true, ayar: a => a.SorularListe = [S(1)]);
        await vm.YukleAsync();
        await vm.CevaplaCommand.ExecuteAsync(vm.Sorular[0]);
        Assert.Equal("Cevabı yazın.", vm.Hata);
        Assert.Null(api.SonCevap);
    }

    [Fact]
    public async Task Editor_kapatir_ve_yeniden_acar()
    {
        var (api, vm) = Kur(editor: true, ayar: a => a.SorularListe = [S(1)]);
        await vm.YukleAsync();
        await vm.KapatCommand.ExecuteAsync(vm.Sorular[0]);
        Assert.Equal(1, api.SonKapatilanSoru);

        await vm.FiltreSecCommand.ExecuteAsync(vm.Filtreler.Single(f => f.Ad == SorularViewModel.FiltreKapali));
        var kapali = vm.Sorular.Single();
        Assert.True(kapali.AcGorunur);
        await vm.AcCommand.ExecuteAsync(kapali);
        Assert.Equal(1, api.SonAcilanSoru);
        Assert.Empty(vm.Sorular);
    }

    [Fact]
    public async Task Silme_ikinci_basista_olur()
    {
        var (api, vm) = Kur(editor: true, ayar: a => a.SorularListe = [S(1), S(2)]);
        await vm.YukleAsync();
        var s = vm.Sorular.Single(x => x.Id == 1);

        await vm.SilCommand.ExecuteAsync(s);
        Assert.Null(api.SonSilinenSoru);
        Assert.Equal("Emin misiniz? Sil", s.SilMetni);

        await vm.SilCommand.ExecuteAsync(s);
        Assert.Equal(1, api.SonSilinenSoru);
        Assert.Equal([2], vm.Sorular.Select(x => x.Id));
    }

    [Fact]
    public async Task Sunucu_hatasi_gosterilir()
    {
        var (api, vm) = Kur(ayar: a => a.GuvenlikYazHatasi = new KasaApiException(HttpStatusCode.BadRequest, "Çok fazla açık sorunuz var."));
        vm.GenelSoruCommand.Execute(null);
        vm.YeniMetin = "soru";
        await vm.SorCommand.ExecuteAsync(null);
        Assert.Equal("Çok fazla açık sorunuz var.", vm.Hata);
        Assert.True(vm.FormAcik);
    }

    // ── Panel kartı ──

    [Fact]
    public async Task Panel_karti_acik_soru_sayisini_gosterir()
    {
        var api = new SahteApi { SorularListe = [S(1), S(2, cevap: "bakıyorum"), S(3, SoruDurumu.Kapali, "tamam")] };
        var y = new Yonlendirme();
        var vm = new AcikSorularViewModel(api, y, new SabitSaat(new DateTime(2026, 9, 24, 12, 0, 0))) { EditorMu = true };

        await vm.YukleAsync();

        Assert.True(vm.Gorunur);
        Assert.Equal("Açık sorular (2)", vm.Baslik);
        Assert.Equal("1 soru cevabınızı bekliyor.", vm.AltMetin);
        Assert.Equal([2, 1], vm.SonSorular.Select(s => s.Id));

        vm.SorularaGitCommand.Execute(null);
        Assert.Equal("//sorular", y.Al());
    }

    [Fact]
    public async Task Panel_karti_acik_soru_yoksa_gizli()
    {
        var vm = new AcikSorularViewModel(new SahteApi { SorularListe = [S(1, SoruDurumu.Kapali, "x")] });
        await vm.YukleAsync();
        Assert.False(vm.Gorunur);
    }

    [Fact]
    public async Task Panel_karti_okunamazsa_gizlenir_hata_yazmaz()
    {
        var vm = new AcikSorularViewModel(new SahteApi { YuklemeHatasi = new KasaApiException(HttpStatusCode.NotFound) });
        await vm.YukleAsync();
        Assert.False(vm.Gorunur);
        Assert.Null(vm.Hata);
    }
}
