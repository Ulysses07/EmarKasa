using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>
/// Kasa hareket dökümü (gap-denetim-izi-gozlemlenebilirlik-3): <see cref="HesapMotoru.KasaHareketleri"/> hesap yolunu değiştirmez,
/// HaftalikHesapla'nın kurallarını satır satır uygular. Kural tekrarından doğabilecek sapma değişmezlerle sabitlenir: açılış +
/// Σ genel kasa etkisi = son KasaDevir, her dönem sonunda birikimli toplam = o dönemin KasaDevir'i ve kanal açılışı + Σ kanal
/// etkisi = kanal devri; hem sabit tohumlu rastgele senaryolarda hem bütün kayıt türlerini içeren adlandırılmış senaryoda.
/// </summary>
public class KasaHareketleriTests
{
    [Fact]
    public void Rastgele_senaryolarda_dokum_haftalik_hesabin_kasa_ve_kanal_devrini_verir()
    {
        var rnd = new Random(20261004);
        for (var senaryo = 0; senaryo < 500; senaryo++)
        {
            var (acilis, kanallar, islemler, gelenler, donemler) = HaftalikHesapEsdegerlikTests.Senaryo(rnd, senaryo);
            Degismezler(acilis, kanallar, islemler, gelenler, donemler, $"Senaryo {senaryo}");
        }
    }

    [Fact]
    public void Butun_kayit_turlerinde_dokum_kasa_ve_kanal_devriyle_birebir_ve_etki_tarihleri_dogru()
    {
        var kanallar = new[] { new Kanal("MEZAT", 1_000m), new Kanal("PERAKENDE", 500m), new Kanal("ESKI", 0m, Aktif: false, Sira: 2) };
        var donemler = DonemUretici.Uret(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 18));
        Donem Donemi(DateOnly t) => donemler.Single(d => d.Icerir(t));
        var kredi = new Kredi("Eski kredi", 6_000m, new DateOnly(2026, 1, 10), 6, 1_050m, 5, "MEZAT");
        var islemler = new List<Islem>
        {
            new(new(2026, 1, 5), "Cari", 300m, "MEZAT", GiderTipi.Cari) { KaynakAnahtari = "Islem:1" },
            new(new(2026, 1, 12), "SGK", 900m, Kanallar.Ortak, GiderTipi.SabitGider) { KaynakAnahtari = "Islem:2" },
            // Eski kart: Ocak harcaması Şubat sonunda, Şubat harcaması Mart sonunda (ufkun dışında) düşer.
            new(new(2026, 1, 20), "Eski kart", 400m, "PERAKENDE", GiderTipi.KrediKarti) { KaynakAnahtari = "Islem:3" },
            new(new(2026, 2, 14), "Eski kart ortak", 333.33m, Kanallar.Ortak, GiderTipi.KrediKarti) { KaynakAnahtari = "Islem:4" },
            // Takipli kart ödemesi: nakit, kanal payıyla ve dağılım bekleyen payla.
            new(new(2026, 2, 3), "Kart ödemesi", 250m, "MEZAT", GiderTipi.KrediKarti, NakitKartOdemesi: true) { KaynakAnahtari = "TakipKartOdeme:44" },
            new(new(2026, 2, 3), "Kart ödemesi", 50m, Kanallar.DagilimBekliyor, GiderTipi.KrediKarti, DagilimBekliyor: true, NakitKartOdemesi: true) { KaynakAnahtari = "TakipKartOdeme:44" },
            new(new(2026, 2, 16), "Onaysız alış", 700m, Kanallar.DagilimBekliyor, GiderTipi.Cari, DagilimBekliyor: true) { KaynakAnahtari = "Islem:5" },
            new(new(2026, 2, 17), "Banka masrafı", 80m, "Genel kasa", GiderTipi.Cari, YalnizGenelKasa: true) { KaynakAnahtari = "Islem:6" },
            new(new(2026, 3, 2), "Kira", 4_000m, "PERAKENDE", GiderTipi.SabitGider, AylikGider: true) { KaynakAnahtari = "Islem:7" },
            // Aylık gider kartla: kanal devrine kendi gününde, genel kasaya ay sonunda (ufkun dışında) yazılır.
            new(new(2026, 3, 3), "Kartla kira", 120m, "MEZAT", GiderTipi.KrediKarti, AylikGider: true) { KaynakAnahtari = "Islem:8" },
            // Takipli kredi taksidi (kanal payları).
            new(new(2026, 3, 10), "Takip kredisi / 1. taksit", 600m, "MEZAT", GiderTipi.Cari) { KaynakAnahtari = "TakipKrediTaksit:9" },
            new(new(2026, 3, 10), "Takip kredisi / 1. taksit", 400m, "PERAKENDE", GiderTipi.Cari) { KaynakAnahtari = "TakipKrediTaksit:9" },
        };
        islemler.AddRange(KrediTuretici.TaksitGiderleri(kredi).Select(t => t with { KaynakAnahtari = "Kredi:5" }));
        var gelenler = new List<Gelen>
        {
            new(new(2026, 1, 1), "MEZAT", 5_000m) { KaynakAnahtari = "Gelen:1" },
            new(new(2026, 2, 2), "ESKI", 150m) { KaynakAnahtari = "Gelen:2" },
            KrediTuretici.CekimGeleni(kredi, donemler)! with { Tarih = kredi.CekimTarihi, KaynakAnahtari = "Kredi:5", Aciklama = kredi.Ad },
            new(Donemi(new(2026, 2, 11)).Start, "Genel kasa", 2_500m, GenelGelir: true) { Tarih = new(2026, 2, 11), KaynakAnahtari = "EkstreKayit:9", Aciklama = "Havale" },
            new(Donemi(new(2026, 2, 26)).Start, "PERAKENDE", 450m) { Tarih = new(2026, 2, 26), KaynakAnahtari = "HesapHareket:3", Aciklama = "Ek gelir" },
            new(Donemi(new(2026, 3, 4)).Start, "MEZAT", 10_000m, KrediGirisi: true) { Tarih = new(2026, 3, 4), KaynakAnahtari = "Kredi:6", Aciklama = "Takip kredisi" },
        };

        var dokum = Degismezler(20_000m, kanallar, islemler, gelenler, donemler, "Adlandırılmış senaryo");

        // Ertelemeli K.K: Ocak harcaması kendi gününde değil, Şubat'ın son günü genel kasadan düşer; kanal devrine hiç yazılmaz.
        var kk = Assert.Single(dokum, h => h.KaynakAnahtari == "Islem:3");
        Assert.Equal((new DateOnly(2026, 2, 28), -400m, 0m, true, new DateOnly(2026, 1, 20)), (kk.EtkiTarihi, kk.GenelKasaEtkisi, kk.KanalEtkisi, kk.KartAySonu, kk.KayitTarihi));
        Assert.DoesNotContain(dokum, h => h.KaynakAnahtari == "Islem:4");   // etki ayı (Mart) sonu ufkun dışında
        // Kartla aylık gider: kanal devrine kendi gününde, genel kasaya bu ufukta hiç.
        Assert.Equal((new DateOnly(2026, 3, 3), 0m, -120m), Tuple(Assert.Single(dokum, h => h.KaynakAnahtari == "Islem:8")));
        // Günü bilinen gelir kendi gününde, dönem geliri dönem başında; yalnız genel kasa geliri kanala yazılmaz; pasif kanalın geliri kanala yazılır.
        Assert.Equal((new DateOnly(2026, 2, 11), 2_500m, 0m), Tuple(dokum.Single(h => h.KaynakAnahtari == "EkstreKayit:9")));
        Assert.Equal((new DateOnly(2026, 2, 2), 150m, 150m), Tuple(dokum.Single(h => h.KaynakAnahtari == "Gelen:2")));
        Assert.Equal((new DateOnly(2026, 1, 10), 6_000m, 0m), Tuple(dokum.Single(h => h.Gelen?.KaynakAnahtari == "Kredi:5")));
        Assert.Equal((new DateOnly(2026, 3, 4), 10_000m, 10_000m), Tuple(dokum.Single(h => h.KaynakAnahtari == "Kredi:6")));
        // Sabit/Ortak/dağılım bekleyen/yalnız genel kasa giderleri yalnız genel kasayı; aylık gider ve nakit kart ödemesi payı kanalı da değiştirir.
        foreach (var anahtar in new[] { "Islem:2", "Islem:5", "Islem:6" })
            Assert.Equal(0m, dokum.Single(h => h.KaynakAnahtari == anahtar).KanalEtkisi);
        Assert.Equal((new DateOnly(2026, 3, 2), -4_000m, -4_000m), Tuple(dokum.Single(h => h.KaynakAnahtari == "Islem:7")));
        Assert.Equal(new[] { (-250m, -250m), (-50m, 0m) }, dokum.Where(h => h.KaynakAnahtari == "TakipKartOdeme:44").Select(h => (h.GenelKasaEtkisi, h.KanalEtkisi)));
        // Eski kredinin türetilmiş taksitleri (Şubat ve Mart 5'i) tarihinde, kanalıyla.
        Assert.Equal(new[] { new DateOnly(2026, 2, 5), new DateOnly(2026, 3, 5) }, dokum.Where(h => h.Islem?.KaynakAnahtari == "Kredi:5").Select(h => h.EtkiTarihi));
        Assert.Equal(dokum.OrderBy(h => h.EtkiTarihi).Select(h => h.EtkiTarihi), dokum.Select(h => h.EtkiTarihi));

        static (DateOnly, decimal, decimal) Tuple(KasaHareketi h) => (h.EtkiTarihi, h.GenelKasaEtkisi, h.KanalEtkisi);
    }

    [Fact]
    public void Hesap_yolu_girdileri_degismez_ve_bos_donem_listesi_bos_dokum_verir()
    {
        Assert.Empty(HesapMotoru.KasaHareketleri([new("A")], [new(new(2026, 1, 1), "C", 5m, "A", GiderTipi.Cari)], [new(new(2026, 1, 1), "A", 5m)], []));
        // Döküm alanları hesaba girmez: aynı satırların anahtarsız hali aynı haftalık sonucu verir.
        var donemler = DonemUretici.Uret(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        Islem[] islemler = [new(new(2026, 1, 5), "C", 10m, "A", GiderTipi.Cari)];
        Gelen[] gelenler = [new(new(2026, 1, 5), "A", 3m)];
        var once = HesapMotoru.HaftalikHesapla(0m, [new("A")], islemler, gelenler, donemler);
        var sonra = HesapMotoru.HaftalikHesapla(0m, [new("A")], islemler.Select(i => i with { KaynakAnahtari = "Islem:1" }).ToList(),
            gelenler.Select(g => g with { Tarih = new(2026, 1, 7), KaynakAnahtari = "Gelen:1", Aciklama = "x" }).ToList(), donemler);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(once), System.Text.Json.JsonSerializer.Serialize(sonra));
    }

    /// <summary>Dökümün değişmezleri: toplam ve kanal devri her senaryoda; dönem sonu birikimli toplamı çakışmayan dönemlerde.</summary>
    private static IReadOnlyList<KasaHareketi> Degismezler(decimal acilis, IReadOnlyList<Kanal> kanallar, IReadOnlyList<Islem> islemler,
        IReadOnlyList<Gelen> gelenler, IReadOnlyList<Donem> donemler, string ad)
    {
        var haftalik = HesapMotoru.HaftalikHesapla(acilis, kanallar, islemler, gelenler, donemler);
        var dokum = HesapMotoru.KasaHareketleri(kanallar, islemler, gelenler, donemler);
        var sonKasa = haftalik.Count > 0 ? haftalik[^1].KasaDevir : acilis;
        Assert.True(acilis + dokum.Sum(h => h.GenelKasaEtkisi) == sonKasa, $"{ad}: genel kasa {acilis + dokum.Sum(h => h.GenelKasaEtkisi)} ≠ {sonKasa}");
        foreach (var kanal in kanallar)
        {
            var devir = haftalik.Count > 0 ? haftalik[^1].Kanallar.Single(k => k.Kanal == kanal.Ad).Devir : kanal.AcilisDevri;
            var toplam = kanal.AcilisDevri + dokum.Where(h => h.Kanal == kanal.Ad).Sum(h => h.KanalEtkisi);
            Assert.True(toplam == devir, $"{ad}: {kanal.Ad} devri {toplam} ≠ {devir}");
        }
        Assert.All(dokum.Where(h => !kanallar.Any(k => k.Ad == h.Kanal)), h => Assert.Equal(0m, h.KanalEtkisi));
        var sirali = donemler.OrderBy(d => d.Start).ToList();
        if (sirali.Zip(sirali.Skip(1)).All(p => p.First.End < p.Second.Start))
            foreach (var ozet in haftalik)
            {
                var birikimli = acilis + dokum.Where(h => h.EtkiTarihi <= ozet.Donem.End).Sum(h => h.GenelKasaEtkisi);
                Assert.True(birikimli == ozet.KasaDevir, $"{ad}: {ozet.Donem.End:yyyy-MM-dd} sonunda {birikimli} ≠ {ozet.KasaDevir}");
                foreach (var kanal in ozet.Kanallar)
                {
                    var kanalBirikimli = kanallar.Single(k => k.Ad == kanal.Kanal).AcilisDevri
                        + dokum.Where(h => h.Kanal == kanal.Kanal && h.EtkiTarihi <= ozet.Donem.End).Sum(h => h.KanalEtkisi);
                    Assert.True(kanalBirikimli == kanal.Devir, $"{ad}: {kanal.Kanal} {ozet.Donem.End:yyyy-MM-dd} sonunda {kanalBirikimli} ≠ {kanal.Devir}");
                }
            }
        return dokum;
    }
}
