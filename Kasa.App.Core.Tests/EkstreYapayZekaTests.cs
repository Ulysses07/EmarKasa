using System.Net;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core.Tests;

// Yapay zekâ önerileri (docs/specs/2026-11-25-yapay-zeka-onerileri.md): yalnız düğmeyle sorulur, satırı doldurur, hazır saymaz, kaydetmez.
public partial class EkstreAktarmaTests
{
    private static YapayZekaDurumDto YzDurum(bool acik = true, string? neden = null, int sonuclanan = 0, int ayni = 0, params YapayZekaOlcumDto[] olcumler) =>
        new(acik, neden, "claude-haiku-5-5", 0.0026m, 2m, 3, sonuclanan, ayni, olcumler);
    private static EkstreKuralOnerisi YzOneri(int no, string? tur = "Gider", string? dagilim = "Genel", int[]? kanallar = null, string? aciklama = null) =>
        new(no, YapayZekaDurumlari.Oneri, [], tur, dagilim, kanallar ?? [], "Yapay zekâ (yüksek güven): kargo. Kontrol edip kendiniz işaretleyin.", YapayZeka: new("Yuksek", "kargo", aciklama));
    private static EkstreKuralOnerisi Yok(int no) => new(no, "Yok", [], null, null, [], "Eşleşen yok.");

    [Fact]
    public async Task Yapay_zeka_onerisi_satiri_doldurur_hazir_saymaz_secmez_elle_degisen_ezilmez()
    {
        var (vm, api, _) = await Hazir(new()
        {
            Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3), Satir(4) with { Yon = "Belirsiz" }),
            Oneriler = [YzOneri(1, dagilim: "Esit", kanallar: [2], aciklama: "Sürat Kargo"), HafizaOnerisi(2), YzOneri(3, EkstreIslemTurleri.Atla, null), YzOneri(4, null, null)],
        });
        var s = vm.Satirlar[0];
        Assert.Equal(("Gider", "Esit", "Sürat Kargo", false, false, false), (s.IslemTuru!.Kod, s.DagilimTuru!.Kod, s.Aciklama, s.Secili, s.ElleDegisti, s.HazirOneri));
        Assert.Equal("Gider · PERAKENDE · Yapay zekâ (yüksek güven): kargo. Kontrol edip kendiniz işaretleyin.", s.HafizaMetni);
        Assert.Equal("PERAKENDE", Assert.Single(s.Paylar).Kanal!.Ad);
        // Kasa hareketi olmayabilir ve yönü okunamayan satırda tür doldurulmaz; öneri yazıdır.
        Assert.All([vm.Satirlar[2], vm.Satirlar[3]], x => Assert.Equal((true, "Yapay zekâ (yüksek güven): kargo. Kontrol edip kendiniz işaretleyin.", "Hareket " + x.Kaynak.No), (x.YapayZekaVar, x.HafizaMetni, x.Aciklama)));
        Assert.False(vm.Satirlar[2].YokSayOnerisi);
        await vm.HazirlariIsaretleCommand.ExecuteAsync(null);
        Assert.Equal([false, true, false, false], vm.Satirlar.Select(x => x.Secili));
        // Kullanıcı kendisi işaretlerse öneriyle önizlenir.
        s.Secili = true;
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.True(vm.OnizlemeVar);

        s.Aciklama = "Benim açıklamam";
        await vm.KurallariYenileAsync();
        Assert.Equal("Benim açıklamam", s.Aciklama);
        // Öneri kalkınca (belge yeniden okundu) elle değişmemiş satır ilk hâline döner.
        api.Oneriler = [];
        vm.Satirlar[1].Secili = false;
        await vm.KurallariYenileAsync();
        Assert.Equal((null, "Hareket 2"), (vm.Satirlar[1].DagilimTuru?.Kod, vm.Satirlar[1].Aciklama));
    }

    [Fact]
    public async Task Sor_dugmesi_yalniz_acikken_ve_taninmayan_satir_varken_gorunur()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3, "USD")), Oneriler = [Yok(1), HafizaOnerisi(2), Yok(3)] });
        // Sunucu desteklemiyor: hiçbir şey görünmez.
        Assert.Equal((false, false, false, ""), (vm.YapayZekaGorunur, vm.YapayZekaAcik, vm.YapayZekaSorulabilir, vm.YapayZekaMetni));
        api.YzDurumu = YzDurum(false, "Yapay zekâ sunucuda kapalı.");
        await vm.YukleAsync();
        Assert.Equal((true, false, false, "Yapay zekâ sunucuda kapalı."), (vm.YapayZekaGorunur, vm.YapayZekaAcik, vm.YapayZekaSorulabilir, vm.YapayZekaMetni));
        await vm.YapayZekaSorAsync();
        await vm.YapayZekaOlcAsync("Banka");
        Assert.Null(await vm.YapayZekaOnizleAsync());
        Assert.Equal((0, 0, 0), (api.YzSorulari.Count, api.YzOlcumleri.Count, api.YzOnizlemeSayisi));

        api.YzDurumu = YzDurum(sonuclanan: 12, ayni: 11, olcumler: new YapayZekaOlcumDto("Banka", 40, 37, 30, 24, 25, 25, 2, 0.0041m, new(2026, 11, 25, 12, 0, 0, TimeSpan.FromHours(3))));
        await vm.YukleAsync();
        Assert.Equal((true, "Tanınmayan 1 satırı yapay zekâya sor"), (vm.YapayZekaSorulabilir, vm.YapayZekaSorDugmesi));
        Assert.Equal("Bu ay 0,0026 $ / 2,00 $ · 3 çağrı · claude-haiku-5-5\nÖnerisi sonuçlanan 12 satırın 11 tanesi önerilen türde kapandı (%92).\n"
            + "Banka ölçümü (25.11.2026): 40 karar · tür %93 · dağılım %80 · yüksek güvenlide tür %100 · 2 karara öneri yok · 0,0041 $", vm.YapayZekaMetni);
        // Durum alınamazsa bölüm gizlenir, ekran çalışır.
        api.YzDurumu = null;
        await vm.YukleAsync();
        Assert.Equal((false, true, null), (vm.YapayZekaGorunur, vm.VeriHazir, vm.Hata));
    }

    [Fact]
    public async Task Sorulan_satirlar_dolar_mesaj_ve_harcama_guncellenir()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2)), Oneriler = [Yok(1), Yok(2)], YzDurumu = YzDurum() });
        await vm.GonderilecekAcKapatCommand.ExecuteAsync(null);
        Assert.Equal((true, "Gönderilecek metni gizle"), (vm.GonderilecekAcik, vm.GonderilecekDugmesi));
        Assert.Equal(["1 | CIKIS | 100-1000 | [1] KARGO"], vm.GonderilecekSatirlar);
        Assert.Empty(api.YzSorulari);

        api.YzSorYaniti = g => { api.Oneriler = [YzOneri(1), Yok(2)]; api.YzDurumu = YzDurum() with { AyHarcamasiUsd = 0.5m }; return new(1, 1, 1, 0.0025m, null); };
        await vm.YapayZekaSorAsync();
        Assert.Equal("1 satır soruldu, 1 satıra öneri geldi (0,0025 $). Öneriler satırları doldurdu; kontrol edip kendiniz işaretleyin. 1 satır kaldı; yeniden sorabilirsiniz.", vm.YapayZekaMesaji);
        Assert.Equal(("Gider", false, false, true, "Tanınmayan 1 satırı yapay zekâya sor"), (vm.Satirlar[0].IslemTuru!.Kod, vm.Satirlar[0].Secili, vm.GonderilecekAcik, vm.YapayZekaSorulabilir, vm.YapayZekaSorDugmesi));
        Assert.Contains("0,50 $ / 2,00 $", vm.YapayZekaMetni);
        Assert.Empty(api.KaydetIstekleri);
        // Kullanılamayan yanıtta yeniden sorulabilir; yanıt gelip satır yine tanınmadıysa düğme kalkar.
        api.YzSorYaniti = _ => new(1, 0, 0, 0.0025m, "Yanıt yarım kaldı.");
        await vm.YapayZekaSorAsync();
        Assert.Equal(("Yanıt yarım kaldı. (0,0025 $)", true), (vm.YapayZekaMesaji, vm.YapayZekaSorulabilir));
        api.YzSorYaniti = _ => new(1, 0, 0, 0.0025m, null);
        await vm.YapayZekaSorAsync();
        Assert.False(vm.YapayZekaSorulabilir);
        api.Oneriler = [Yok(1), Yok(2)];
        await vm.KurallariYenileAsync();
        Assert.True(vm.YapayZekaSorulabilir);
    }

    [Fact]
    public async Task Onay_metni_gonderilmeyeni_soyler_yalniz_ad_ve_sayi_varsa_sorulmaz()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2)), Oneriler = [Yok(1), Yok(2)], YzDurumu = YzDurum() });
        var onizleme = await vm.YapayZekaOnizleAsync();
        Assert.Equal("1 satırın maskeli açıklaması ve 2 geçmiş kararın maskeli örneği Anthropic'e gönderilecek. En çok 0,0091 $ tutar. Yalnız ad ve sayıdan oluşan 1 satır gönderilmez.", vm.YapayZekaOnayi(onizleme!));
        Assert.Equal("70 satırın maskeli açıklaması Anthropic'e gönderilecek. En çok 0,0200 $ tutar. 4 satır sonraki soruya kalır.", vm.YapayZekaOnayi(new(70, 4, 0, [], 0.02m)));
        api.YzOnizleme = new(0, 0, 0, [], 0);
        Assert.Null(await vm.YapayZekaOnizleAsync());
        Assert.Equal((false, "Tanınmayan satırlar yalnız ad ve sayıdan oluşuyor; gönderilecek bilgi yok."), (vm.YapayZekaSorulabilir, vm.YapayZekaMesaji));
        Assert.Empty(api.YzSorulari);
    }

    /// <summary>Bağlantı kesilirse aynı istek kimliğiyle yinelenir (ikinci kez ücret doğmaz); sunucu reddinde yeni kimlik alınır.</summary>
    [Fact]
    public async Task Yanit_kaybolursa_ayni_kimlikle_yinelenir_sunucu_reddinde_yenilenir()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1)), Oneriler = [Yok(1)], YzDurumu = YzDurum() });
        api.YzSorYaniti = _ => throw new HttpRequestException();
        await vm.YapayZekaSorAsync();
        Assert.NotNull(vm.YapayZekaHatasi);
        api.YzSorYaniti = _ => throw new KasaApiException(HttpStatusCode.Conflict, "Önceki istek tamamlanmadı; yeniden sorun.");
        await vm.YapayZekaSorAsync();
        Assert.Contains("yeniden sorun", vm.YapayZekaHatasi);
        api.YzSorYaniti = _ => new(1, 1, 0, 0.0025m, null);
        await vm.YapayZekaSorAsync();
        Assert.Null(vm.YapayZekaHatasi);
        Assert.Equal((api.YzSorulari[0].IstekId, true), (api.YzSorulari[1].IstekId, api.YzSorulari[1].IstekId != api.YzSorulari[2].IstekId));
    }

    [Fact]
    public async Task Olcum_sorulur_hatasi_kendi_kartinda_gorunur()
    {
        var (vm, api, _) = await Hazir(new() { YzDurumu = YzDurum() });
        api.YzOlcumYaniti = _ => throw new KasaApiException(HttpStatusCode.Conflict, "Ölçüm için en az 10 farklı banka kararı gerekir.");
        await vm.YapayZekaOlcAsync("Banka");
        Assert.Contains("en az 10", vm.OlcumHatasi);
        var olcum = new YapayZekaOlcumDto("Kart", 10, 10, 0, 0, 0, 0, 0, 0.001m, DateTimeOffset.UtcNow);
        api.YzOlcumYaniti = _ => { api.YzDurumu = YzDurum(olcumler: olcum); return olcum; };
        await vm.YapayZekaOlcAsync("Kart");
        Assert.Null(vm.OlcumHatasi);
        Assert.Equal(["Banka", "Kart"], api.YzOlcumleri.Select(o => o.Kaynak));
        Assert.Contains("Kart ölçümü", vm.YapayZekaMetni);
        Assert.Contains("dağılım — · yüksek güvenlide tür —", vm.YapayZekaMetni);
    }
}
