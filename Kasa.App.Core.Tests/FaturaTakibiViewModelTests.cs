using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Fatura takibi sayfası (Paket F, madde 41).</summary>
public class FaturaTakibiViewModelTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    private static FaturaIslemDto Odeme(int id, DateOnly t, string cari, decimal tutar, BelgeTuru? tur = null, string? no = null, int ek = 0)
        => new(id, t, cari, tutar, "MEZAT", GiderTipi.Cari, null, tur, no, true, ek, null);

    private static FaturaTakibiDto Veri() => new(2026, 9,
        [
            new FaturaBekleyenCariDto("Yılmaz Gıda", 3000m, 2, new DateOnly(2026, 8, 3),
                [Odeme(1, new(2026, 8, 3), "Yılmaz Gıda", 1000m, BelgeTuru.EFatura, "F-1", 2), Odeme(2, new(2026, 9, 1), "Yılmaz Gıda", 2000m)]),
            new FaturaBekleyenCariDto("Kaya Ambalaj", 500m, 1, new DateOnly(2026, 9, 10), [Odeme(3, new(2026, 9, 10), "Kaya Ambalaj", 500m)]),
        ], 3500m, 3,
        [
            new BelgeTuruToplamDto(BelgeTuru.EFatura, "e-Fatura", 10_000m, 4),
            new BelgeTuruToplamDto(BelgeTuru.Belgesiz, "Belgesiz", 750m, 2),
            new BelgeTuruToplamDto(null, "Belirtilmemiş", 1_250m, 3),
        ], 12_000m, 9, 750m, 2);

    private static (SahteApi api, FaturaTakibiViewModel vm, SahteKaydedici kaydedici) Kur()
    {
        var api = new SahteApi { FaturaTakibi = Veri() };
        var kaydedici = new SahteKaydedici();
        var vm = new FaturaTakibiViewModel(api, new SabitSaat(Bugun.AddHours(10)), kaydedici) { EditorMu = true };
        return (api, vm, kaydedici);
    }

    [Fact]
    public async Task Yukle_bu_ayi_getirir_bekleyenleri_cari_cari_gosterir()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.Equal([(2026, 9)], api.FaturaTakibiCagrilari);
        Assert.Equal("Eylül 2026", vm.AyBasligi);
        Assert.Equal(["Yılmaz Gıda", "Kaya Ambalaj"], vm.Bekleyenler.Select(g => g.Cari));
        Assert.Equal("2 ödeme · en eski 3 Ağustos 2026", vm.Bekleyenler[0].Ozet);
        Assert.Equal("MEZAT · e-Fatura · F-1 · 2 ek", vm.Bekleyenler[0].Islemler[0].Aciklama);
        Assert.True(vm.Bekleyenler[0].Islemler[0].EkVar);
        Assert.Equal("MEZAT", vm.Bekleyenler[0].Islemler[1].Aciklama);
        Assert.False(vm.BekleyenYok);
        Assert.Equal("3 ödeme · 3.500,00 ₺ faturası bekleniyor", vm.BekleyenOzeti);
        Assert.Equal("750,00 ₺ (2 işlem)", vm.BelgesizOzeti);
        Assert.Equal(3, vm.AyOzeti.Count);
    }

    [Fact]
    public async Task Bekleyen_yoksa_bos_mesaj()
    {
        var (api, vm, _) = Kur();
        api.FaturaTakibi = null;
        await vm.YukleAsync();

        Assert.True(vm.BekleyenYok);
        Assert.Equal("Faturası beklenen ödeme yok.", vm.BekleyenOzeti);
    }

    [Fact]
    public async Task Ay_gezinmesi_yil_donumunde_dogru()
    {
        var (api, vm, _) = Kur();
        vm.Ay = 1;
        await vm.OncekiAyCommand.ExecuteAsync(null);
        Assert.Equal((2025, 12), (vm.Yil, vm.Ay));
        await vm.SonrakiAyCommand.ExecuteAsync(null);
        Assert.Equal((2026, 1), (vm.Yil, vm.Ay));
        Assert.Equal([(2025, 12), (2026, 1)], api.FaturaTakibiCagrilari);
    }

    [Fact]
    public async Task Fatura_geldi_formu_tur_ve_noyu_onerir_kaydedince_bekleniyor_kalkar()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();

        // Ödemede fatura türü varsa o önerilir (no da gelir); kaydetmeden API'ye gidilmez.
        vm.FaturaGeldiCommand.Execute(vm.Bekleyenler[0].Islemler[0]);
        Assert.True(vm.GelenFormuGorunur);
        Assert.Equal("Fatura geldi · Yılmaz Gıda · 1.000,00 ₺ · 03.08.2026", vm.GelenBasligi);
        Assert.Equal((BelgeTuru.EFatura, "F-1"), (vm.GelenTur, vm.GelenNo));
        Assert.True(vm.GelenTurCipleri.Single(c => c.Ad == "e-Fatura").Secili);
        Assert.Equal(["e-Fatura", "e-Arşiv", "Fiş", "Makbuz"], vm.GelenTurCipleri.Select(c => c.Ad));   // "Belgesiz" yok
        Assert.Empty(api.BelgeGuncellemeleri);

        vm.SecGelenTurCommand.Execute(vm.GelenTurCipleri.Single(c => c.Ad == "e-Arşiv"));
        vm.GelenNo = "  GIB2026000777 ";
        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal((1, new BelgeBilgisi(BelgeTuru.EArsiv, "GIB2026000777", false)), Assert.Single(api.BelgeGuncellemeleri));
        Assert.False(vm.GelenFormuGorunur);
        Assert.Equal(2, api.FaturaTakibiCagrilari.Count);   // liste yenilendi
    }

    /// <summary>
    /// Bulgu: "Fatura geldi" yalnız işareti kaldırıyordu; türsüz ödeme türsüz, Belgesiz işaretli ödeme Belgesiz
    /// kalıyordu (ayın belgesiz toplamında ve muhasebeci listesinde). Artık gelen faturanın türü ve no'su girilir.
    /// </summary>
    [Fact]
    public async Task Fatura_geldi_turu_bos_ya_da_belgesizse_e_fatura_onerir_no_ve_ek_ile_kaydeder()
    {
        var (api, vm, _) = Kur();
        var secici = new SahteDosyaSecici();
        vm.DosyaSecici = secici;
        api.FaturaTakibi = Veri() with
        {
            Bekleyenler = [new FaturaBekleyenCariDto("Kaya Ambalaj", 500m, 1, new DateOnly(2026, 9, 10),
                [Odeme(3, new(2026, 9, 10), "Kaya Ambalaj", 500m, BelgeTuru.Belgesiz)])],
        };
        await vm.YukleAsync();

        vm.FaturaGeldiCommand.Execute(vm.Bekleyenler[0].Islemler[0]);
        Assert.Equal((BelgeTuru.EFatura, (string?)null), (vm.GelenTur, vm.GelenNo));
        vm.GelenNo = "KA-2026-15";
        secici.Belgeler.Enqueue([new SecilenDosya("fatura.pdf", [1, 2, 3])]);
        await vm.GelenEkEkleCommand.ExecuteAsync(null);
        Assert.True(vm.GelenEkVar);
        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal((3, new BelgeBilgisi(BelgeTuru.EFatura, "KA-2026-15", false)), Assert.Single(api.BelgeGuncellemeleri));
        Assert.Equal((3, "fatura.pdf"), Assert.Single(api.EkYuklemeleri) is var y ? (y.IslemId, y.Ad) : default);
        Assert.False(vm.GelenFormuGorunur);
        Assert.Empty(vm.GelenEkler);
    }

    [Fact]
    public async Task Fatura_geldi_ek_yuklenemezse_fatura_kayitli_form_acik_kalir_tekrar_denenir()
    {
        var (api, vm, _) = Kur();
        var secici = new SahteDosyaSecici();
        vm.DosyaSecici = secici;
        api.YuklenemeyenEkler.Add("bozuk.jpg");
        await vm.YukleAsync();

        vm.FaturaGeldiCommand.Execute(vm.Bekleyenler[1].Islemler[0]);
        secici.Belgeler.Enqueue([new SecilenDosya("iyi.pdf", [1]), new SecilenDosya("bozuk.jpg", [2])]);
        await vm.GelenEkEkleCommand.ExecuteAsync(null);
        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.StartsWith("Fatura bilgisi kaydedildi ama 1 dosya yüklenemedi (bozuk.jpg:", vm.Hata);
        Assert.Single(api.BelgeGuncellemeleri);
        Assert.Equal(["iyi.pdf"], api.EkYuklemeleri.Select(e => e.Ad));
        Assert.True(vm.GelenFormuGorunur);
        Assert.Equal(["bozuk.jpg"], vm.GelenEkler.Select(e => e.Ad));

        api.YuklenemeyenEkler.Clear();
        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal(["iyi.pdf", "bozuk.jpg"], api.EkYuklemeleri.Select(e => e.Ad));
        Assert.False(vm.GelenFormuGorunur);
    }

    [Fact]
    public async Task Fatura_geldi_dogrulamalari_ve_vazgec()
    {
        var (api, vm, _) = Kur();
        var secici = new SahteDosyaSecici();
        await vm.YukleAsync();

        vm.FaturaGeldiCommand.Execute(vm.Bekleyenler[0].Islemler[1]);
        vm.GelenNo = new string('X', 51);
        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal(FaturaTakibiViewModel.BelgeNoUzunMesaji, vm.Hata);
        Assert.Empty(api.BelgeGuncellemeleri);

        // Seçici yoksa anlaşılır hata; uygun olmayan dosya eklenmez; işlem başına 10 ek sınırı (var olan ekler dahil).
        await vm.GelenEkEkleCommand.ExecuteAsync(null);
        Assert.Equal(IslemlerViewModel.SeciciYokMesaji, vm.Hata);
        vm.DosyaSecici = secici;
        secici.Belgeler.Enqueue([new SecilenDosya("not.txt", [1])]);
        await vm.GelenEkEkleCommand.ExecuteAsync(null);
        Assert.Equal(EkKurallari.TurMesaji, vm.Hata);
        Assert.False(vm.GelenEkVar);

        vm.FaturaGeldiCommand.Execute(vm.Bekleyenler[0].Islemler[0]);   // 2 eki var
        secici.Belgeler.Enqueue(Enumerable.Range(1, 9).Select(n => new SecilenDosya($"{n}.jpg", [1])).ToList());
        await vm.GelenEkEkleCommand.ExecuteAsync(null);
        Assert.Equal(EkKurallari.SayiMesaji, vm.Hata);
        Assert.Equal(8, vm.GelenEkler.Count);
        vm.GelenEkKaldirCommand.Execute(vm.GelenEkler[0]);
        Assert.Equal(7, vm.GelenEkler.Count);

        vm.GelenVazgecCommand.Execute(null);
        Assert.False(vm.GelenFormuGorunur);
        Assert.Empty(vm.GelenEkler);
        await vm.GelenKaydetCommand.ExecuteAsync(null);   // form kapalı: bir şey yapılmaz
        Assert.Empty(api.BelgeGuncellemeleri);
        Assert.Empty(api.EkYuklemeleri);
    }

    [Fact]
    public async Task Fatura_geldi_izleyicide_yapilamaz()
    {
        var (api, vm, _) = Kur();
        vm.EditorMu = false;
        await vm.YukleAsync();

        vm.FaturaGeldiCommand.Execute(vm.Bekleyenler[0].Islemler[0]);

        Assert.Equal(HataMesaji.Yetkisiz, vm.Hata);
        Assert.False(vm.GelenFormuGorunur);
        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.BelgeGuncellemeleri);
    }

    [Fact]
    public async Task Muhasebeci_listesi_secili_ayin_csvsini_kaydeder()
    {
        var (api, vm, kaydedici) = Kur();
        vm.Ay = 8;

        await vm.MuhasebeciListesiCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal((2026, 8), api.SonMuhasebeciCsv);
        Assert.Equal("kasa-muhasebeci-2026-08.csv", Assert.Single(kaydedici.Kaydedilenler).Ad);
        Assert.Equal(SahteKaydedici.Klasor + "kasa-muhasebeci-2026-08.csv", vm.AktarilanDosya);
    }

    [Fact]
    public async Task Ekler_paneli_acilir_ek_acilir_kapanir()
    {
        var (api, vm, _) = Kur();
        var acici = new SahteEkAcici();
        vm.EkAcici = acici;
        api.EklerSozluk[1] = [new EkDto(7, 1, "fatura.pdf", "application/pdf", 10, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc))];
        await vm.YukleAsync();

        await vm.EkleriGosterCommand.ExecuteAsync(vm.Bekleyenler[0].Islemler[0]);
        Assert.True(vm.EkPaneliGorunur);
        Assert.True(Assert.Single(vm.SeciliEkler).PdfMi);

        await vm.EkAcCommand.ExecuteAsync(vm.SeciliEkler[0]);
        Assert.Equal("fatura.pdf", Assert.Single(acici.Acilanlar).Ad);

        vm.EkleriKapatCommand.Execute(null);
        Assert.False(vm.EkPaneliGorunur);
        Assert.Empty(vm.SeciliEkler);
    }

    [Fact]
    public async Task Yukleme_hatasi_turkce_gosterilir()
    {
        var (api, vm, _) = Kur();
        api.YuklemeHatasi = new KasaApiException(HttpStatusCode.InternalServerError);
        await vm.YukleAsync();

        Assert.NotNull(vm.Hata);
        Assert.False(vm.Mesgul);
    }
}
