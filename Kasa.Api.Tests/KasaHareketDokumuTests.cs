using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Servisler;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Kasa hareket dökümü (gap-denetim-izi-gozlemlenebilirlik-3): GET /api/kasa-hareketleri genel kasayı ve kanal kasalarını oluşturan
/// bütün hareketleri kaynağıyla verir. Hesap yolu değişmez; döküm aynı kuralları ayrı bir okuma yolunda uygular. Kural tekrarından
/// doğacak sapma altın rapor tohumunda (bütün kayıt türleri: eski/takipli/geçişli kart ve kredi, kart ödemesi ve iadesi, ekstre
/// geliri ve gideri, ek gelir, aylık gider, Ortak, onaysız alış) değişmezlerle sabitlenir: açılış + Σ = panelin genel kasası, her
/// kanal için açılış + Σ kanal etkisi = panelin kanal kasası; bir gün itibarıyla döküm bakiyesi o gün hesaplanan panelle aynıdır.
/// Karantinalı kayıtla değişmez RaporDayaniklilikTests'te sınanır.
/// </summary>
public class KasaHareketDokumuTests
{
    private static async Task<KasaHareketleriDto> Dokum(HttpClient c, string sorgu)
    {
        var r = await c.GetAsync("/api/kasa-hareketleri" + (sorgu.Length > 0 ? "?" + sorgu : ""));
        Assert.True(r.IsSuccessStatusCode, $"{sorgu}: {r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<KasaHareketleriDto>())!;
    }

    [Fact]
    public async Task Altin_tohumda_dokum_panelin_genel_kasasini_ve_kanal_kasalarini_birebir_verir()
    {
        await using var f = KasaWebFactory.Sabit(AltinTohum.Bugun);
        using var c = await f.EditorClientAsync();
        await AltinTohum.Kur(f, c);
        var panel = (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;

        var tum = await Dokum(c, "baslangic=2026-01-01&bitis=2026-09-25");
        Assert.Equal((50_000m, panel.GuncelKasa), (tum.AcilisBakiyesi, tum.KapanisBakiyesi));
        Assert.Equal(tum.KapanisBakiyesi - tum.AcilisBakiyesi, tum.Hareketler.Sum(h => h.GenelKasaEtkisi));
        Assert.Equal(tum.Hareketler.OrderBy(h => h.EtkiTarihi).Select(h => h.EtkiTarihi), tum.Hareketler.Select(h => h.EtkiTarihi));
        foreach (var kanal in panel.Kanallar)
        {
            var d = await Dokum(c, $"baslangic=2026-01-01&bitis=2026-09-25&kanalId={kanal.KanalId}");
            Assert.Equal((kanal.KanalId, kanal.Kanal, kanal.Bakiye), (d.KanalId, d.Kanal, d.KapanisBakiyesi));
            Assert.Equal(d.KapanisBakiyesi - d.AcilisBakiyesi, d.Hareketler.Sum(h => h.KanalEtkisi));
            Assert.All(d.Hareketler, h => Assert.Equal((kanal.Kanal, kanal.KanalId), (h.Kanal, h.KanalId)));
        }

        // Türetilmiş kalemler dahil bütün kaynak türleri listede; kendiliğinden işleyenler işaretli.
        var turler = tum.Hareketler.Select(h => h.Tur).ToHashSet();
        foreach (var tur in new[] { "Gelir", "EkstreGeliri", "EkGelir", "KrediCekimi", "Gider", "SabitGider", "AylikGider", "KartOdemesi", "KrediTaksidi", "KartAySonu" })
            Assert.Contains(tur, turler);
        Assert.All(tum.Hareketler, h => Assert.Equal(h.Tur is "KrediTaksidi" or "KartAySonu", h.Otomatik));
        Assert.Contains(tum.Hareketler, h => h.Kanal == "Dağılım bekliyor" && h.KanalId is null && h.KanalEtkisi == 0m && h.GenelKasaEtkisi == -700m);
        Assert.Contains(tum.Hareketler, h => h.Kanal == "Ortak" && h.KanalEtkisi == 0m);
        // Eski kartın Nisan harcaması Mayıs'ın son günü kasadan düşer.
        var kart = Assert.Single(tum.Hareketler, h => h.Tur == "KartAySonu" && h.KayitTarihi == new DateOnly(2026, 4, 20));
        Assert.Equal((new DateOnly(2026, 5, 31), -400m, 0m), (kart.EtkiTarihi, kart.GenelKasaEtkisi, kart.KanalEtkisi));
        Assert.All(tum.Hareketler.Where(h => h.KaynakAnahtari is not null), h => Assert.Matches("^[A-Za-z]+:[0-9]+$", h.KaynakAnahtari!));

        // Aralıklar birbirine bağlanır; bitiş bugünle sınırlı; varsayılan aralık bitişin ayı.
        var ilk = await Dokum(c, "baslangic=2026-01-01&bitis=2026-05-31");
        var son = await Dokum(c, "baslangic=2026-06-01&bitis=2027-01-01");
        Assert.Equal((ilk.KapanisBakiyesi, tum.KapanisBakiyesi, AltinTohum.Bugun), (son.AcilisBakiyesi, son.KapanisBakiyesi, son.Bitis));
        Assert.Equal(tum.Hareketler.Count, ilk.Hareketler.Count + son.Hareketler.Count);
        var varsayilan = await Dokum(c, "");
        Assert.Equal((new DateOnly(2026, 9, 1), AltinTohum.Bugun), (varsayilan.Baslangic, varsayilan.Bitis));
        foreach (var hatali in new[] { "baslangic=2026-09-26", "baslangic=2025-09-24&bitis=2026-09-25", "kanalId=999" })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/kasa-hareketleri?" + hatali)).StatusCode);

        // Bir gün itibarıyla döküm bakiyesi, o gün hesaplanan panelin bugünkü veriyle aynısıdır (kasa kontrolünün "sonradan
        // değişti" ölçüsü). Panel her gün için sunucu saati o güne alınarak hesaplanır.
        KasaDokumu bugunku;
        using (var scope = f.Services.CreateScope())
            bugunku = scope.ServiceProvider.GetRequiredService<HesapServisi>().Dokum();
        Assert.Equal(tum.Hareketler.Count, bugunku.Hareketler.Count(h => h.EtkiTarihi >= new DateOnly(2026, 1, 1)));
        var saat = (SabitSaat)f.Saat!;
        foreach (var gun in new DateOnly[] { new(2026, 1, 31), new(2026, 3, 15), new(2026, 5, 31), new(2026, 6, 30), new(2026, 8, 18), new(2026, 9, 24) })
        {
            saat.Ayarla(gun);
            using var scope = f.Services.CreateScope();
            var o = scope.ServiceProvider.GetRequiredService<HesapServisi>().Panel();
            Assert.True(o.GuncelKasa == bugunku.GenelKasa(gun), $"{gun}: panel {o.GuncelKasa}, döküm {bugunku.GenelKasa(gun)}");
            foreach (var k in o.Kanallar)
                Assert.True(k.Bakiye == bugunku.KanalBakiyesi(k.KanalId!.Value, gun), $"{gun} {k.Kanal}: panel {k.Bakiye}, döküm {bugunku.KanalBakiyesi(k.KanalId!.Value, gun)}");
        }
        saat.Ayarla(AltinTohum.Bugun);
    }
}
