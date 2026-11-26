using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core.Tests;

// ERP12 cari hareketleri (docs/specs/2026-11-25-erp12-aktarimi.md): belge türü, kendiliğinden eşleşme, aday seçimi, nakit ve belgeyi silme.
public partial class EkstreAktarmaTests
{
    private static EkstreOkunanSatir ErpSatiri(int no, string oneri = "Atla", string yon = "Cikis") => Satir(no) with { Yon = yon, OnerilenIslem = oneri, EkBilgi = $"Havale · Belge {no}" };
    private static EkstreBelgeDto ErpBelge(params EkstreOkunanSatir[] satirlar) => DurumBelgesi(satirlar) with { Kaynak = EkstreKaynaklari.Erp, Banka = "ERP12", HesapAdi = "", YenidenOkunabilir = false };
    private static EkstreEslesmeAdayiDto ErpAday(int id, string tur = EslesmeTurleri.Gider) => new(tur, id, Tarih, 100m, $"FAST KAYIT {id}", null, null, null);
    private static EkstreKuralOnerisi ErpOneri(int no, string durum, string? tur, params EkstreEslesmeAdayiDto[] adaylar) =>
        new(no, durum, [], tur, tur == EkstreIslemTurleri.Eslestir ? DagilimBicimleri.Eslesme : null, [], $"{durum} iletisi", null, adaylar.Length == 0 ? null : adaylar);

    [Fact]
    public async Task Erp_belge_turunde_banka_ve_hesap_secilmez()
    {
        var (vm, api, _) = await Hazir();
        vm.Kaynak = vm.Kaynaklar.Single(k => k.Kod == "Banka");
        Assert.Equal((true, false, true), (vm.BankaSecilir, vm.ErpMi, vm.BankaMi));
        vm.Kaynak = vm.Kaynaklar.Single(k => k.Kod == EkstreKaynaklari.Erp);
        Assert.Equal((false, true, false, false), (vm.BankaSecilir, vm.ErpMi, vm.BankaMi, vm.KartMi));
        var secim = vm.YuklemeSecimi()!;
        Assert.Equal((EkstreKaynaklari.Erp, "", "", (int?)null), (secim.Kaynak, secim.Banka, secim.HesapAdi, secim.KartId));
        await vm.PdfYukleAsync([1], "erp.xlsx", secim);
        Assert.Equal(1, api.YuklemeSayisi);
        // Dosya seçilirken belge türü değiştirilirse yüklenmez.
        vm.Kaynak = vm.Kaynaklar.Single(k => k.Kod == "Banka");
        await vm.PdfYukleAsync([1], "erp.xlsx", secim);
        Assert.Equal(1, api.YuklemeSayisi);
    }

    [Fact]
    public async Task Erp_kesin_karsilik_kendiliginden_secilir_digerleri_kullaniciya_kalir()
    {
        var (vm, api, _) = await Hazir(new()
        {
            Veri = ErpBelge(ErpSatiri(1), ErpSatiri(2), ErpSatiri(3, "Gider"), ErpSatiri(4), ErpSatiri(5)),
            Oneriler =
            [
                ErpOneri(1, ErpOneriDurumlari.Eslesme, EkstreIslemTurleri.Eslestir, ErpAday(41), ErpAday(42)),
                ErpOneri(2, ErpOneriDurumlari.Secim, EkstreIslemTurleri.Eslestir, ErpAday(43), ErpAday(44)),
                ErpOneri(3, ErpOneriDurumlari.Nakit, EkstreIslemTurleri.Gider),
                ErpOneri(4, ErpOneriDurumlari.Bekle, null),
                ErpOneri(5, ErpOneriDurumlari.Secim, EkstreIslemTurleri.Eslestir, ErpAday(45, EslesmeTurleri.CekKaydi)),
            ],
        });
        Assert.Equal(["Gelir", "Gider", "Eslestir"], vm.Satirlar[0].IslemTurleri.Select(t => t.Kod));
        Assert.Equal(["Eslestir", "Eslestir", "Gider", null, "Eslestir"], vm.Satirlar.Select(s => s.IslemTuru?.Kod));
        Assert.Equal([41, null, null, null, 45], vm.Satirlar.Select(s => s.SeciliAday?.Veri.Id));
        Assert.Equal([2, 2, 0, 0, 1], vm.Satirlar.Select(s => s.EslesmeAdaylari.Count));
        Assert.Equal([true, false, false, false, false], vm.Satirlar.Select(s => s.HazirOneri));
        Assert.All(vm.Satirlar, s => Assert.Equal((false, false), (s.ElleDegisti, s.KartSecimiGorunur)));
        Assert.StartsWith("hesap.pdf · ERP12 cari hareketleri\n5 satır", vm.BelgeOzeti);
        Assert.StartsWith("Kasadaki karşılığı kesin olan", vm.HazirAciklamasi);
        Assert.Equal(("Havale · Belge 1", "ErpEslesme iletisi", "ErpBekle iletisi"), (vm.Satirlar[0].EkBilgi, vm.Satirlar[0].HafizaMetni, vm.Satirlar[3].HafizaMetni));

        // Kendiliğinden işaretleme yalnız kesin karşılığı işaretler; kayıt hedefi taşır.
        Assert.Equal([true, false, false, false, false], vm.Satirlar.Select(s => s.Secili));
        await vm.OnizleCommand.ExecuteAsync(null);
        vm.Onay = true;
        await vm.KaydetCommand.ExecuteAsync(null);
        var y = Assert.Single(Assert.Single(api.KaydetIstekleri).Satirlar);
        Assert.Equal((1, "Eslestir", "Eslesme", EslesmeTurleri.Gider, (int?)41), (y.SatirNo, y.IslemTuru, y.DagilimTuru, y.EslesenKayitTuru, y.EslesenKayitId));
    }

    [Fact]
    public async Task Erp_aday_secimi_elle_sayilir_yenilemede_korunur_aday_sorgusu_satir_numarasini_tasir()
    {
        var (vm, api, _) = await Hazir(new() { Veri = ErpBelge(ErpSatiri(1), ErpSatiri(2)), Oneriler = [ErpOneri(1, ErpOneriDurumlari.Secim, EkstreIslemTurleri.Eslestir, ErpAday(43), ErpAday(44))], Adaylar = [ErpAday(44), ErpAday(50)] });
        var s = vm.Satirlar[0];
        s.SeciliAday = s.EslesmeAdaylari[1];
        Assert.True(s.ElleDegisti);
        await vm.KurallariYenileAsync();
        Assert.Equal(44, s.SeciliAday!.Veri.Id);
        vm.SeciliSatir = s;
        await vm.EslesmeAdaylariniGetirCommand.ExecuteAsync(null);
        Assert.Equal((1, new EkstreEslesmeAdayiSorgu(Tarih, 100m, 1)), Assert.Single(api.AdaySorgulari));
        Assert.Equal((2, 44), (s.EslesmeAdaylari.Count, s.SeciliAday!.Veri.Id));
    }

    /// <summary>Karşılığı başka satıra bağlanan ya da kaydı silinen satırın önerisi kalkar; hafıza bilirse onunki yazılır.</summary>
    [Fact]
    public async Task Erp_onerisi_degisince_satir_yeni_oneriye_kalkinca_ilk_haline_doner_ayni_oneri_onizlemeyi_dusurmez()
    {
        var (vm, api, _) = await Hazir(new() { Veri = ErpBelge(ErpSatiri(1, "Gider"), ErpSatiri(2)), Oneriler = [ErpOneri(1, ErpOneriDurumlari.Eslesme, EkstreIslemTurleri.Eslestir, ErpAday(41))] });
        var s = vm.Satirlar[0];
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.True(vm.OnizlemeVar);
        await vm.KurallariYenileAsync();
        Assert.Equal((true, 41, true), (vm.OnizlemeVar, s.SeciliAday!.Veri.Id, s.HazirOneri));

        api.Oneriler = [HafizaOnerisi(1, dagilim: "Esit", kanallar: [2])];
        await vm.KurallariYenileAsync();
        Assert.Equal(("Gider", "Esit", 0, true, false), (s.IslemTuru!.Kod, s.DagilimTuru!.Kod, s.EslesmeAdaylari.Count, s.HazirOneri, vm.OnizlemeVar));
        api.Oneriler = [ErpOneri(1, ErpOneriDurumlari.Nakit, EkstreIslemTurleri.Gider)];
        await vm.KurallariYenileAsync();
        Assert.Equal(("Gider", 0, false, false), (s.IslemTuru!.Kod, s.Paylar.Count, s.HazirOneri, s.ElleDegisti));
        api.Oneriler = [ErpOneri(1, ErpOneriDurumlari.Bekle, null)];
        await vm.KurallariYenileAsync();
        Assert.Null(s.IslemTuru);
        api.Oneriler = [];
        await vm.KurallariYenileAsync();
        Assert.Equal(("Gider", ""), (s.IslemTuru!.Kod, s.HafizaMetni));
    }

    [Fact]
    public async Task Islenmemis_erp_belgesi_silinir_kaydi_olan_silinmez()
    {
        var (vm, api, _) = await Hazir(new() { Veri = ErpBelge(ErpSatiri(1)) with { Kayitlar = [Kayit(7, 1, iptal: true)] } });
        Assert.False(vm.ErpSilinebilir);
        await vm.ErpBelgeSilAsync();
        Assert.Empty(api.ErpSilinenler);

        api.Veri = ErpBelge(ErpSatiri(1));
        await vm.BelgeAcAsync(1);
        Assert.True(vm.ErpSilinebilir);
        await vm.ErpBelgeSilAsync();
        Assert.Equal([1], api.ErpSilinenler);
        Assert.Equal((null, 0, false), (vm.Belge, vm.Satirlar.Count, vm.ErpSilinebilir));
        Assert.Contains("kasa değişmedi", vm.Mesaj);

        // Banka belgesi silinmez.
        api.Veri = DurumBelgesi(Satir(1));
        await vm.BelgeAcAsync(1);
        Assert.False(vm.ErpSilinebilir);
    }

    [Fact]
    public void Erp_bilgisi_kapanan_satirda_ve_aday_basliginda_gorunur()
    {
        Assert.Contains(" · ERP12: Ahmet Yılmaz", new EkstreKayitSatiri(Kayit(1, 1) with { ErpCari = "Ahmet Yılmaz" }).Ozet);
        Assert.DoesNotContain("ERP12", new EkstreKayitSatiri(Kayit(1, 1)).Ozet);
        Assert.StartsWith("ERP12 cari hareketleri · 5 satır", new EkstreGecmisSatiri(new(1, 1, EkstreKaynaklari.Erp, "ERP12", "", null, "erp.xlsx", DateTimeOffset.UtcNow, 5, 0)).Ozet);
        Assert.StartsWith("Banka girişi #5", new EslesmeAdayiSatiri(ErpAday(5, EslesmeTurleri.EkstreGelir)).Baslik);
        Assert.StartsWith("Çek #5", new EslesmeAdayiSatiri(ErpAday(5, EslesmeTurleri.CekKaydi)).Baslik);
        Assert.StartsWith("Çek cirosu #5", new EslesmeAdayiSatiri(ErpAday(5, EslesmeTurleri.CekCirosu)).Baslik);
    }
}
