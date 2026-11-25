using System.Net;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core.Tests;

// İşlem hafızası (docs/specs/2026-11-25-islem-hafizasi.md): kendiliğinden doldurma, hazırları işaretleme, yok sayma önerisi, liste ve unutma.
public partial class EkstreAktarmaTests
{
    private static EkstreKuralOnerisi HafizaOnerisi(int no, string tur = "Gider", string? dagilim = "Genel", int[]? kanallar = null, string durum = EkstreHafizaDurumlari.Hazir, string? aciklama = null, int? kart = null) =>
        new(no, durum, [], tur, dagilim, kanallar ?? [], "Hafızadan: 2 kez aynı işlendi, son 27.08.2026.", new("HAREKET", 2, new(2026, 8, 27), kart, aciklama));

    [Fact]
    public async Task Hafiza_satiri_kendiliginden_doldurur_secmez_hazirlar_tek_dugmeyle_isaretlenip_onizlenir()
    {
        var (vm, api, _) = await Hazir(new()
        {
            Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3)),
            Oneriler = [HafizaOnerisi(1, dagilim: "Esit", kanallar: [2], aciklama: "Yurtiçi Kargo"), HafizaOnerisi(2, durum: EkstreHafizaDurumlari.Kontrol), new(3, "Yok", [], null, null, [], "Eşleşen yok.")],
        });
        var s = vm.Satirlar[0];
        Assert.Equal(("Gider", "Esit", "Yurtiçi Kargo", false, false), (s.IslemTuru!.Kod, s.DagilimTuru!.Kod, s.Aciklama, s.Secili, s.ElleDegisti));
        Assert.Equal("PERAKENDE", Assert.Single(s.Paylar).Kanal!.Ad);
        Assert.Equal("Gider · PERAKENDE · Hafızadan: 2 kez aynı işlendi, son 27.08.2026.", s.HafizaMetni);
        Assert.Equal("Hafızadan: 2 kez aynı işlendi, son 27.08.2026.", s.OneriMetni);
        Assert.Equal("Gider · Yalnız genel kasa · Hafızadan: 2 kez aynı işlendi, son 27.08.2026.", vm.Satirlar[1].HafizaMetni);
        // Kontrol gerektiren satır da doldurulur ama hazır sayılmaz; önerisi olmayan satır değişmez.
        Assert.Equal(("Genel", true, false), (vm.Satirlar[1].DagilimTuru!.Kod, vm.Satirlar[1].HafizaVar, vm.Satirlar[1].HazirOneri));
        Assert.Equal((false, "", (string?)null), (vm.Satirlar[2].HafizaVar, vm.Satirlar[2].HafizaMetni, vm.Satirlar[2].DagilimTuru?.Kod));
        Assert.Equal((true, "1 satır hazır · Giriş +0,00 ₺ (0 satır) · Çıkış −100,00 ₺ (1 satır)"), (vm.HazirVar, vm.HazirOzeti));

        await vm.HazirlariIsaretleCommand.ExecuteAsync(null);
        Assert.Equal([true, false, false], vm.Satirlar.Select(x => x.Secili));
        Assert.Equal(1, api.OnizlemeSayisi);
        Assert.True(vm.OnizlemeVar);
        Assert.Empty(api.KaydetIstekleri);
        Assert.Contains("Belge okundu. Hafızadan hazır satırlar var", await YuklemeMesaji(vm));
    }

    private static async Task<string?> YuklemeMesaji(EkstreAktarmaViewModel vm)
    {
        vm.Kaynak = vm.Kaynaklar.Single(k => k.Kod == "Banka");
        vm.Banka = vm.Bankalar[0];
        vm.HesapAdi = "Ana hesap";
        await vm.PdfYukleAsync([1], "a.pdf", vm.YuklemeSecimi()!);
        return vm.Mesaj;
    }

    [Fact]
    public async Task Elle_degistirilen_satir_hafizadan_ezilmez_hazir_sayilmaz_yenilemede_korunur()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2)), Oneriler = [HafizaOnerisi(1), HafizaOnerisi(2)] });
        vm.Satirlar[0].Aciklama = "Benim açıklamam";
        Assert.Equal([false, true], vm.Satirlar.Select(s => s.HazirOneri));
        await vm.KurallariYenileAsync();
        await vm.YukleAsync();
        Assert.Equal("Benim açıklamam", vm.Satirlar[0].Aciklama);
        Assert.Equal([false, true], vm.Satirlar.Select(s => s.HazirOneri));
        Assert.Equal("1 satır hazır · Giriş +0,00 ₺ (0 satır) · Çıkış −100,00 ₺ (1 satır)", vm.HazirOzeti);
        Assert.Empty(api.KaydetIstekleri);
    }

    [Fact]
    public async Task Oneri_kalkinca_hafizadan_dolan_satir_ilk_hâline_doner_ayni_oneri_onizlemeyi_dusurmez()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2)), Oneriler = [HafizaOnerisi(1, dagilim: "Esit", kanallar: [1, 2], aciklama: "Kısa ad")] });
        await vm.HazirlariIsaretleCommand.ExecuteAsync(null);
        Assert.True(vm.OnizlemeVar);
        await vm.KurallariYenileAsync();
        Assert.True(vm.OnizlemeVar);
        Assert.Equal(2, vm.Satirlar[0].Paylar.Count);

        api.Oneriler = [];
        await vm.KurallariYenileAsync();
        var s = vm.Satirlar[0];
        Assert.Equal(("Gider", (string?)null, "Hareket 1", 0, false), (s.IslemTuru!.Kod, s.DagilimTuru?.Kod, s.Aciklama, s.Paylar.Count, s.HazirOneri));
        Assert.False(vm.OnizlemeVar);
        Assert.False(vm.HazirVar);
    }

    /// <summary>Codex incelemesi (EmarKasa#62): yenilemede model baştan kurulur (kanal listesi değişti); hafıza bu arada unutulduysa satır
    /// PDF'ten okunan hâline döner, öneri sürüyorsa ilk tür hafızanınkiyle karışmaz.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Yenilenen_model_hafizadan_doldugunu_ve_ilk_turu_korur(bool oncelikleUnutulur)
    {
        var finans = new SahteApi { KanallarListe = [new(1, "MEZAT", true, 0, 0)] };
        var api = new Sahte { Veri = DurumBelgesi(Satir(1) with { OnerilenIslem = "Gelir" }), Oneriler = [HafizaOnerisi(1, dagilim: "Esit", kanallar: [1], aciklama: "Kısa ad")] };
        var vm = new EkstreAktarmaViewModel(api, finans, new FinansTakipTests.Sahte(), new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        await vm.BelgeAcAsync(1);
        Assert.Equal(("Gider", "Kısa ad"), (vm.Satirlar[0].IslemTuru!.Kod, vm.Satirlar[0].Aciklama));
        var ilk = vm.Satirlar[0];
        finans.KanallarListe = [new(1, "MEZAT", true, 0, 0), new(5, "YENİ", true, 1, 0)];
        if (oncelikleUnutulur)
            api.Oneriler = [];
        await vm.YukleAsync();
        Assert.NotSame(ilk, vm.Satirlar[0]);
        if (!oncelikleUnutulur)
        {
            Assert.Equal("Gider", vm.Satirlar[0].IslemTuru!.Kod);
            api.Oneriler = [];
            await vm.KurallariYenileAsync();
        }
        Assert.Equal(("Gelir", (string?)null, "Hareket 1", 0), (vm.Satirlar[0].IslemTuru!.Kod, vm.Satirlar[0].DagilimTuru?.Kod, vm.Satirlar[0].Aciklama, vm.Satirlar[0].Paylar.Count));
    }

    [Fact]
    public async Task Pasif_kanalli_ozel_ve_eksik_oneri_hazir_sayilmaz()
    {
        var (vm, _, _) = await Hazir(new()
        {
            Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3), Satir(4)),
            Oneriler = [HafizaOnerisi(1, dagilim: "Esit", kanallar: [9]), HafizaOnerisi(2, dagilim: "Ozel", kanallar: [1, 2]), HafizaOnerisi(3, dagilim: null), HafizaOnerisi(4, "KartOdemesi", "Otomatik", kart: 99)],
        });
        Assert.All(vm.Satirlar, s => Assert.False(s.HazirOneri));
        Assert.Equal(2, vm.Satirlar[1].Paylar.Count);
        Assert.Equal(("KartOdemesi", "Otomatik", (int?)null), (vm.Satirlar[3].IslemTuru!.Kod, vm.Satirlar[3].DagilimTuru!.Kod, vm.Satirlar[3].Kart?.Id));
        Assert.False(vm.HazirVar);
    }

    [Fact]
    public async Task Karta_odemede_kart_hafizadan_gelir()
    {
        // Takipteki örnek kartın kimliği 1'dir.
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1)), Oneriler = [HafizaOnerisi(1, "KartOdemesi", "Otomatik", kart: 1)] });
        Assert.Equal((1, true), (vm.Satirlar[0].Kart?.Id, vm.Satirlar[0].HazirOneri));
        Assert.StartsWith("Karta ödeme · Kart · Hafızadan", vm.Satirlar[0].HafizaMetni);
        Assert.Equal(1, vm.Satirlar[0].Yaz().KrediKartiId);
    }

    [Fact]
    public async Task Kisisel_kural_onerisi_de_hazirlara_girer_dugme_uygular()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2)), Oneriler = [new(1, "Oneri", ["Kargo"], "Gider", "Esit", [1], "Kural"), new(2, "Oneri", ["Atla"], "Atla", "Genel", [], "Atla")] });
        Assert.Null(vm.Satirlar[0].DagilimTuru);
        Assert.Equal([true, false], vm.Satirlar.Select(s => s.HazirOneri));
        await vm.HazirlariIsaretleCommand.ExecuteAsync(null);
        Assert.Equal(("Esit", true), (vm.Satirlar[0].DagilimTuru?.Kod, vm.Satirlar[0].Secili));
        Assert.False(vm.Satirlar[1].Secili);
        Assert.Equal(1, api.OnizlemeSayisi);
    }

    [Fact]
    public async Task Daha_once_yok_sayilanlar_ayri_dugmeyle_yok_sayilir()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3)), Oneriler = [HafizaOnerisi(1, "Atla"), HafizaOnerisi(2), HafizaOnerisi(3, "Atla")] });
        Assert.Equal("Gider", vm.Satirlar[0].IslemTuru!.Kod);   // yok sayma önerisi satırın alanlarını değiştirmez
        Assert.StartsWith("Yok sayma önerisi · Hafızadan", vm.Satirlar[0].HafizaMetni);
        Assert.Equal((true, "Daha önce yok sayılanları yok say (2)"), (vm.YokSayOneriVar, vm.YokSayOneriDugmesi));
        await vm.OnerilenleriYokSayAsync();
        Assert.Equal([1, 3], Assert.Single(api.YokSayIstekleri).SatirNolar);
        Assert.False(vm.YokSayOneriVar);
        Assert.True(vm.HazirVar);
        // Eski sunucu yok saymayı bilmiyorsa öneri görünmez.
        var (eski, _, _) = await Hazir(new() { Oneriler = [HafizaOnerisi(1, "Atla")] });
        Assert.False(eski.YokSayOneriVar);
    }

    [Fact]
    public async Task Hafiza_listesi_acilinca_yuklenir_unutunca_yenilenir_ve_satir_geri_doner()
    {
        var (vm, api, _) = await Hazir(new()
        {
            Veri = DurumBelgesi(Satir(1)),
            Oneriler = [HafizaOnerisi(1)],
            Hafiza = [new("Banka", "HAREKET", "Cikis", "Gider", "Esit", [1, 9], null, "Kısa ad", 3, new(2026, 8, 27), false), new("Kart", "MIGROS", "Cikis", "KartHarcama", "Genel", [], null, null, 1, new(2026, 7, 1), true),
                new("Banka", "VIRMAN", "Cikis", "Atla", "Genel", [], null, null, 2, new(2026, 6, 1), false)],
        });
        Assert.Empty(vm.Hafiza);
        await vm.HafizaAcKapatCommand.ExecuteAsync(null);
        Assert.Equal(("Hafıza listesini gizle", "3 hafıza kaydı, en yeni önce."), (vm.HafizaDugmesi, vm.HafizaDurumu));
        Assert.Equal("HAREKET · Çıkış −", vm.Hafiza[0].Baslik);
        Assert.Equal("Banka hareketi · Gider · Eşit: MEZAT, kanal #9 · 3 kez, son 27.08.2026 · Açıklama: Kısa ad", vm.Hafiza[0].Ozet);
        Assert.Equal("Kart ekstresi · son iki karar farklı, önerilmez; son: Kart harcaması · Yalnız genel kasa · 1 kez, son 01.07.2026", vm.Hafiza[1].Ozet);
        Assert.Equal("Banka hareketi · Yok sayıldı · 2 kez, son 01.06.2026", vm.Hafiza[2].Ozet);

        api.Hafiza = [];
        api.Oneriler = [];
        await vm.HafizaUnutAsync("Banka", "HAREKET", "Cikis");
        Assert.Equal(("Banka", "HAREKET", "Cikis"), (api.Unutulanlar.Single().Kaynak, api.Unutulanlar[0].Anahtar, api.Unutulanlar[0].Yon));
        Assert.NotEqual(Guid.Empty, api.Unutulanlar[0].IstekId);
        Assert.Empty(vm.Hafiza);
        Assert.Null(vm.Satirlar[0].DagilimTuru);
        Assert.Equal("\"HAREKET\" hafızadan çıktı; kayıtlar değişmedi.", vm.Mesaj);
        await vm.HafizaAcKapatCommand.ExecuteAsync(null);
        Assert.False(vm.HafizaAcik);
    }

    [Fact]
    public async Task Eski_sunucuda_hafiza_listesi_aciklamayla_bos_kalir_izleyici_yuklemez()
    {
        var (vm, _, auth) = await Hazir(new() { HafizaHatasi = new KasaApiException(HttpStatusCode.NotFound) });
        await vm.HafizaAcKapatCommand.ExecuteAsync(null);
        Assert.Equal("Sunucu işlem hafızasını desteklemiyor; sunucu güncellenmeli.", vm.HafizaDurumu);
        Assert.Null(vm.Hata);
        auth.OturumSurumu++;
        Assert.False(vm.HafizaAcik);
        Assert.Equal("", vm.HafizaDurumu);
    }
}
