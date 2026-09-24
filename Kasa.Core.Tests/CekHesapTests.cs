using System.Text.Json;
using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>
/// Çek kuralı ("vadede kasaya"): çek kasayı yalnız tahsil edildiği / ödendiği gün etkiler.
/// Tahsilat o kanalın ek geleni, ödeme o kanalın (Ortak ise Ortak) Cari gideri gibi sayılır.
/// </summary>
public class CekHesapTests
{
    private static readonly Kanal[] UcKanal = { new("MEZAT"), new("PERAKENDE"), new("TOPTAN") };

    // Haziran 2026: 1 Haziran Pazartesi → 1–7, 8–14, 15–21, 22–28, 29–30.
    private static readonly IReadOnlyList<Donem> Haziran = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

    private static DateOnly G(int gun) => new(2026, 6, gun);

    private static Cek Tahsil(int gun, string kanal, decimal tutar) => new(CekYonu.Alinan, tutar, kanal, CekDurumu.TahsilEdildi, G(gun));
    private static Cek Odeme(int gun, string kanal, decimal tutar) => new(CekYonu.Verilen, tutar, kanal, CekDurumu.Odendi, G(gun));

    private static string Json(object o) => JsonSerializer.Serialize(o);

    // ---------------------------------------------------------------- kural

    [Theory]
    [InlineData(CekYonu.Alinan, CekDurumu.Portfoyde, true)]
    [InlineData(CekYonu.Alinan, CekDurumu.TahsilEdildi, true)]
    [InlineData(CekYonu.Alinan, CekDurumu.CiroEdildi, true)]
    [InlineData(CekYonu.Alinan, CekDurumu.Karsiliksiz, true)]
    [InlineData(CekYonu.Alinan, CekDurumu.IadeEdildi, true)]
    [InlineData(CekYonu.Alinan, CekDurumu.Odendi, false)]
    [InlineData(CekYonu.Verilen, CekDurumu.Portfoyde, true)]
    [InlineData(CekYonu.Verilen, CekDurumu.Odendi, true)]
    [InlineData(CekYonu.Verilen, CekDurumu.IadeEdildi, true)]
    [InlineData(CekYonu.Verilen, CekDurumu.TahsilEdildi, false)]
    [InlineData(CekYonu.Verilen, CekDurumu.CiroEdildi, false)]
    [InlineData(CekYonu.Verilen, CekDurumu.Karsiliksiz, false)]
    public void Durum_yone_gore_gecerli(CekYonu yon, CekDurumu durum, bool gecerli)
        => Assert.Equal(gecerli, CekKurali.DurumGecerliMi(yon, durum));

    [Theory]
    [InlineData(CekDurumu.TahsilEdildi, true)]
    [InlineData(CekDurumu.Odendi, true)]
    [InlineData(CekDurumu.CiroEdildi, true)]
    [InlineData(CekDurumu.Portfoyde, false)]
    [InlineData(CekDurumu.Karsiliksiz, false)]
    [InlineData(CekDurumu.IadeEdildi, false)]
    public void Islem_tarihi_tahsil_odeme_ve_ciroda_zorunlu(CekDurumu durum, bool zorunlu)
        => Assert.Equal(zorunlu, CekKurali.IslemTarihiGerekli(durum));

    [Fact]
    public void Kasa_hareketi_yalniz_tahsil_ve_odemede_dogar_tutar_kurusa_yuvarlanir()
    {
        var t = CekKurali.KasaHareketi(new Cek(CekYonu.Alinan, 100.005m, "MEZAT", CekDurumu.TahsilEdildi, G(3)));
        Assert.Equal(new CekHareketi(G(3), "MEZAT", 100.01m, CekYonu.Alinan), t);
        var o = CekKurali.KasaHareketi(new Cek(CekYonu.Verilen, 50m, Kanallar.Ortak, CekDurumu.Odendi, G(4)));
        Assert.Equal(new CekHareketi(G(4), Kanallar.Ortak, 50m, CekYonu.Verilen), o);

        // Tarihsiz tahsil/ödeme ve diğer tüm durumlar kasaya dokunmaz.
        Assert.Null(CekKurali.KasaHareketi(new Cek(CekYonu.Alinan, 1m, "MEZAT", CekDurumu.TahsilEdildi, null)));
        Assert.Null(CekKurali.KasaHareketi(new Cek(CekYonu.Alinan, 1m, "MEZAT", CekDurumu.CiroEdildi, G(3))));
        Assert.Null(CekKurali.KasaHareketi(new Cek(CekYonu.Verilen, 1m, "MEZAT", CekDurumu.Portfoyde, G(3))));
        Assert.Empty(CekKurali.Hareketler(null));
    }

    // ---------------------------------------------------------------- haftalık

    [Fact]
    public void Haftalik_tahsil_edilen_alinan_cek_kanala_ve_kasaya_tahsil_gununun_doneminde_girer()
    {
        var cekler = new[] { Tahsil(10, "MEZAT", 500m) };   // 10 Haziran → 8–14 dönemi

        var h = HesapMotoru.HaftalikHesapla(1000m, UcKanal, [], [], Haziran, cekler: cekler);

        // İlk dönemde etkisi yok.
        Assert.Equal(0m, h[0].KasaSonucu);
        Assert.Equal(0m, h[0].ToplamCekGelen);
        Assert.Equal(1000m, h[0].KasaDevir);

        var d = h[1];
        Assert.Equal(G(8), d.Donem.Start);
        Assert.Equal(0m, d.ToplamGelen);                  // gelen alanı çeksiz anlamını korur
        Assert.Equal(500m, d.ToplamCekGelen);
        Assert.Equal(0m, d.ToplamCekGiden);
        Assert.Equal(500m, d.KasaSonucu);
        Assert.Equal(1500m, d.KasaDevir);
        var mezat = d.Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal(0m, mezat.Gelen);
        Assert.Equal(500m, mezat.CekGelen);
        Assert.Equal(500m, mezat.Sonuc);
        Assert.Equal(500m, mezat.Devir);
        Assert.All(d.Kanallar.Where(k => k.Kanal != "MEZAT"), k => Assert.Equal(0m, k.Devir));

        // Sonraki dönemlere yalnız devir olarak taşınır.
        Assert.All(h.Skip(2), x => Assert.Equal(0m, x.KasaSonucu));
        Assert.Equal(1500m, h[^1].KasaDevir);
        Assert.Equal(500m, h[^1].Kanallar.Single(k => k.Kanal == "MEZAT").Devir);
    }

    [Fact]
    public void Haftalik_odenen_ortak_verilen_cek_ortak_cari_islem_gibi_yalniz_kasadan_duser()
    {
        var gelenler = new[] { new Gelen(G(1), "MEZAT", 2000m) };
        var islemli = HesapMotoru.HaftalikHesapla(0m, UcKanal,
            [new Islem(G(10), "Tedarikçi", 300m, Kanallar.Ortak, GiderTipi.Cari)], gelenler, Haziran);
        var cekli = HesapMotoru.HaftalikHesapla(0m, UcKanal, [], gelenler, Haziran,
            cekler: [Odeme(10, Kanallar.Ortak, 300m)]);

        Assert.Equal(islemli.Count, cekli.Count);
        for (int i = 0; i < islemli.Count; i++)
        {
            Assert.Equal(islemli[i].KasaSonucu, cekli[i].KasaSonucu);
            Assert.Equal(islemli[i].KasaDevir, cekli[i].KasaDevir);
            Assert.Equal(islemli[i].Kanallar.Select(k => (k.Sonuc, k.Devir)), cekli[i].Kanallar.Select(k => (k.Sonuc, k.Devir)));
            Assert.Equal(islemli[i].ToplamGiden, cekli[i].ToplamGiden + cekli[i].ToplamCekGiden);
        }
        Assert.Equal(300m, cekli[1].ToplamCekGiden);
        Assert.Equal(-300m, cekli[1].KasaSonucu);
        Assert.All(cekli.SelectMany(d => d.Kanallar), k => Assert.Equal(0m, k.CekGiden));   // Ortak: kanal satırı yok
    }

    [Fact]
    public void Haftalik_odenen_kanal_verilen_cek_o_kanalin_cari_gideri_gibi_ayri_alanda_gorunur()
    {
        var cekli = HesapMotoru.HaftalikHesapla(0m, UcKanal, [], [], Haziran, cekler: [Odeme(16, "TOPTAN", 250m)]);
        var islemli = HesapMotoru.HaftalikHesapla(0m, UcKanal,
            [new Islem(G(16), "Tedarikçi", 250m, "TOPTAN", GiderTipi.Cari)], [], Haziran);

        var d = cekli[2];   // 15–21
        var toptan = d.Kanallar.Single(k => k.Kanal == "TOPTAN");
        Assert.Equal(0m, toptan.Giden);
        Assert.Equal(250m, toptan.CekGiden);
        Assert.Equal(-250m, toptan.Sonuc);
        Assert.Equal(0m, d.ToplamGiden);
        Assert.Equal(250m, d.ToplamCekGiden);
        Assert.Equal(-250m, d.KasaSonucu);
        Assert.Equal(islemli.Select(x => x.KasaDevir), cekli.Select(x => x.KasaDevir));
        Assert.Equal(islemli.SelectMany(x => x.Kanallar).Select(k => k.Devir), cekli.SelectMany(x => x.Kanallar).Select(k => k.Devir));
    }

    public static TheoryData<Cek> EtkisizCekler => new()
    {
        new Cek(CekYonu.Alinan, 700m, "MEZAT", CekDurumu.Portfoyde, null),
        new Cek(CekYonu.Alinan, 700m, "MEZAT", CekDurumu.CiroEdildi, G(10)),
        new Cek(CekYonu.Alinan, 700m, "MEZAT", CekDurumu.Karsiliksiz, G(10)),
        new Cek(CekYonu.Alinan, 700m, "MEZAT", CekDurumu.IadeEdildi, G(10)),
        new Cek(CekYonu.Verilen, 700m, "MEZAT", CekDurumu.Portfoyde, G(10)),
        new Cek(CekYonu.Verilen, 700m, Kanallar.Ortak, CekDurumu.IadeEdildi, G(10)),
        // Yönle uyumsuz durumlar da (API reddeder) kasaya dokunmaz.
        new Cek(CekYonu.Alinan, 700m, "MEZAT", CekDurumu.Odendi, G(10)),
        new Cek(CekYonu.Verilen, 700m, "MEZAT", CekDurumu.TahsilEdildi, G(10)),
    };

    [Theory]
    [MemberData(nameof(EtkisizCekler))]
    public void Diger_durumlar_haftalik_ve_aylik_hicbir_rakami_degistirmez(Cek cek)
    {
        var gelenler = new[] { new Gelen(G(1), "MEZAT", 1000m), new Gelen(G(8), "TOPTAN", 50m) };
        var islemler = new[]
        {
            new Islem(G(3), "A", 100m, "MEZAT", GiderTipi.Cari),
            new Islem(G(9), "Kira", 90m, Kanallar.Ortak, GiderTipi.SabitGider),
        };

        Assert.Equal(
            Json(HesapMotoru.HaftalikHesapla(10m, UcKanal, islemler, gelenler, Haziran)),
            Json(HesapMotoru.HaftalikHesapla(10m, UcKanal, islemler, gelenler, Haziran, cekler: [cek])));
        Assert.Equal(
            Json(HesapMotoru.AylikHesapla(2026, 6, UcKanal, islemler, gelenler, Haziran)),
            Json(HesapMotoru.AylikHesapla(2026, 6, UcKanal, islemler, gelenler, Haziran, cekler: [cek])));
    }

    [Fact]
    public void Haftalik_takvim_disindaki_islem_tarihi_sayilmaz()
    {
        // Takvim bugünde biter: tarihi henüz gelmemiş (ileri tarihli) tahsil hiçbir döneme girmez.
        var donemler = DonemUretici.Uret(G(1), G(12));
        var h = HesapMotoru.HaftalikHesapla(0m, UcKanal, [], [], donemler,
            cekler: [Tahsil(20, "MEZAT", 500m), new Cek(CekYonu.Verilen, 5m, "MEZAT", CekDurumu.Odendi, new DateOnly(2026, 5, 31))]);
        Assert.All(h, d => Assert.Equal(0m, d.KasaSonucu));
    }

    // ---------------------------------------------------------------- aylık

    [Fact]
    public void Aylik_tahsilat_kanalin_ayri_cek_geleninde_gorunur_ve_ay_sonucuna_girer()
    {
        var gelenler = new[] { new Gelen(G(1), "MEZAT", 1000m) };
        var rapor = HesapMotoru.AylikHesapla(2026, 6, UcKanal, [], gelenler, Haziran,
            cekler: [Tahsil(10, "MEZAT", 400m), Tahsil(30, "MEZAT", 100m), new Cek(CekYonu.Alinan, 9m, "MEZAT", CekDurumu.TahsilEdildi, new DateOnly(2026, 7, 1))]);

        var mezat = rapor.Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal(1000m, mezat.Gelen);
        Assert.Equal(500m, mezat.CekGelen);            // Temmuz tahsilatı bu aya girmez
        Assert.Equal(0m, mezat.CekGiden);
        Assert.Equal(1500m, mezat.AySonucu);

        var temmuz = HesapMotoru.AylikHesapla(2026, 7, UcKanal, [], [], DonemUretici.Uret(G(1), new DateOnly(2026, 7, 31)),
            cekler: [Tahsil(10, "MEZAT", 400m), new Cek(CekYonu.Alinan, 9m, "MEZAT", CekDurumu.TahsilEdildi, new DateOnly(2026, 7, 1))]);
        Assert.Equal(9m, temmuz.Kanallar.Single(k => k.Kanal == "MEZAT").CekGelen);
    }

    [Fact]
    public void Aylik_ortak_cek_odemesi_ortak_cari_islem_gibi_bolunur_pay_cek_gidende_gorunur()
    {
        // Ortak işlem 100 + ortak çek 0,02 → üç kanala 33,34 / 33,34 / 33,34 (toplam 100,02).
        // Yalnız işlem 100 → 33,34 / 33,33 / 33,33; çek payı farktır: 0 / 0,01 / 0,01.
        var gelenler = UcKanal.Select(k => new Gelen(G(1), k.Ad, 1000m)).ToArray();
        var ortakIslem = new Islem(G(5), "Kira", 100m, Kanallar.Ortak, GiderTipi.SabitGider);

        var islemli = HesapMotoru.AylikHesapla(2026, 6, UcKanal,
            [ortakIslem, new Islem(G(12), "Tedarikçi", 0.02m, Kanallar.Ortak, GiderTipi.Cari)], gelenler, Haziran);
        var cekli = HesapMotoru.AylikHesapla(2026, 6, UcKanal, [ortakIslem], gelenler, Haziran,
            cekler: [Odeme(12, Kanallar.Ortak, 0.02m)]);

        for (int i = 0; i < 3; i++)
        {
            var a = islemli.Kanallar[i];
            var b = cekli.Kanallar[i];
            Assert.Equal(a.AySonucu, b.AySonucu);
            Assert.Equal(a.OrtakPay, b.OrtakPay + b.CekGiden);
            Assert.Equal(0m, b.CariGiden);
        }
        Assert.Equal([33.34m, 33.33m, 33.33m], cekli.Kanallar.Select(k => k.OrtakPay));   // çeksiz anlamı korunur
        Assert.Equal([0m, 0.01m, 0.01m], cekli.Kanallar.Select(k => k.CekGiden));
        Assert.Equal(0.02m, cekli.Kanallar.Sum(k => k.CekGiden));
    }

    [Fact]
    public void Aylik_kanal_cek_odemesi_cari_gibi_kanali_hareketli_yapar()
    {
        // TOPTAN'ın başka hareketi yok: çek ödemesi, Cari işlem gibi, onu Ortak paya ortak eder.
        var gelenler = new[] { new Gelen(G(1), "MEZAT", 1000m) };
        var kira = new Islem(G(5), "Kira", 90m, Kanallar.Ortak, GiderTipi.SabitGider);

        var islemli = HesapMotoru.AylikHesapla(2026, 6, UcKanal,
            [kira, new Islem(G(20), "Tedarikçi", 40m, "TOPTAN", GiderTipi.Cari)], gelenler, Haziran);
        var cekli = HesapMotoru.AylikHesapla(2026, 6, UcKanal, [kira], gelenler, Haziran,
            cekler: [Odeme(20, "TOPTAN", 40m)]);

        Assert.Equal(islemli.Kanallar.Select(k => (k.OrtakPay, k.AySonucu)), cekli.Kanallar.Select(k => (k.OrtakPay, k.AySonucu)));
        var toptan = cekli.Kanallar.Single(k => k.Kanal == "TOPTAN");
        Assert.Equal(45m, toptan.OrtakPay);    // 90 / 2 hareketli kanal (MEZAT, TOPTAN)
        Assert.Equal(40m, toptan.CekGiden);
        Assert.Equal(0m, toptan.CariGiden);
        Assert.Equal(-85m, toptan.AySonucu);
        Assert.Equal(0m, cekli.Kanallar.Single(k => k.Kanal == "PERAKENDE").OrtakPay);
    }

    [Fact]
    public void Aylik_takip_baslangicindan_onceki_tahsil_ve_odeme_sayilmaz()
    {
        // Takip 10 Haziran'da başlar: 5 Haziran'daki tahsil (gelen gibi) ve ödeme (işlem gibi) sayılmaz.
        var donemler = DonemUretici.Uret(G(10), G(30));
        var rapor = HesapMotoru.AylikHesapla(2026, 6, UcKanal, [], [], donemler, G(10),
            cekler: [Tahsil(5, "MEZAT", 100m), Odeme(5, "MEZAT", 30m), Tahsil(11, "MEZAT", 7m), Odeme(11, "MEZAT", 2m)]);
        var mezat = rapor.Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal(7m, mezat.CekGelen);
        Assert.Equal(2m, mezat.CekGiden);
        Assert.Equal(5m, mezat.AySonucu);
    }

    // ---------------------------------------------------------------- eşdeğerlik

    /// <summary>
    /// Rastgele verilerde: her tahsilatı aynı güne/kanala bir gelen, her ödemeyi aynı güne/kanala bir
    /// Cari işlem olarak girmekle çek girmek aynı kasa/kanal devrini ve ay sonucunu verir; yalnız
    /// tutarların hangi alanda göründüğü değişir.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Cek_gelen_ve_cari_islem_esdegeriyle_ayni_sonucu_verir(int tohum)
    {
        var r = new Random(tohum);
        var bas = new DateOnly(2026, 1, 1).AddDays(r.Next(0, 40));
        var bitis = new DateOnly(2026, 9, 24);
        var donemler = DonemUretici.Uret(bas, bitis);
        var kanallar = new[] { new Kanal("MEZAT", 10m), new Kanal("PERAKENDE"), new Kanal("TOPTAN", 0m, Aktif: false) };
        var adlar = new[] { "MEZAT", "PERAKENDE", "TOPTAN" };
        int gun = bitis.DayNumber - bas.DayNumber;
        DateOnly T() => bas.AddDays(r.Next(-30, gun + 30));
        decimal Tl() => r.Next(1, 1_000_000) / 100m;

        var islemler = Enumerable.Range(0, 300).Select(_ =>
            new Islem(T(), "C", Tl(), r.Next(4) == 0 ? Kanallar.Ortak : adlar[r.Next(3)], (GiderTipi)r.Next(3))).ToList();
        var gelenler = donemler.Where(_ => r.Next(2) == 0).Select(d => new Gelen(d.Start, adlar[r.Next(3)], Tl())).ToList();
        var cekler = Enumerable.Range(0, 120).Select(_ =>
        {
            var yon = (CekYonu)r.Next(2);
            var durum = (CekDurumu)r.Next(6);
            var kanal = yon == CekYonu.Verilen && r.Next(3) == 0 ? Kanallar.Ortak : adlar[r.Next(3)];
            return new Cek(yon, Tl(), kanal, durum, r.Next(10) == 0 ? null : T());
        }).ToList();

        // Eşdeğer: tahsilat → o güne gelen, ödeme → o güne Cari işlem.
        var hareketler = CekKurali.Hareketler(cekler);
        var esGelen = gelenler.Concat(hareketler.Where(h => h.Yon == CekYonu.Alinan).Select(h => new Gelen(h.Tarih, h.Kanal, h.Tutar))).ToList();
        var esIslem = islemler.Concat(hareketler.Where(h => h.Yon == CekYonu.Verilen).Select(h => new Islem(h.Tarih, "Çek", h.Tutar, h.Kanal, GiderTipi.Cari))).ToList();

        var cekli = HesapMotoru.HaftalikHesapla(55m, kanallar, islemler, gelenler, donemler, cekler: cekler);
        var esdeger = HesapMotoru.HaftalikHesapla(55m, kanallar, esIslem, esGelen, donemler);
        Assert.Equal(esdeger.Count, cekli.Count);
        for (int i = 0; i < cekli.Count; i++)
        {
            Assert.Equal(esdeger[i].KasaSonucu, cekli[i].KasaSonucu);
            Assert.Equal(esdeger[i].KasaDevir, cekli[i].KasaDevir);
            Assert.Equal(esdeger[i].ToplamGelen, cekli[i].ToplamGelen + cekli[i].ToplamCekGelen);
            Assert.Equal(esdeger[i].ToplamGiden, cekli[i].ToplamGiden + cekli[i].ToplamCekGiden);
            for (int k = 0; k < kanallar.Length; k++)
            {
                var e = esdeger[i].Kanallar[k];
                var c = cekli[i].Kanallar[k];
                Assert.Equal((e.Sonuc, e.Devir), (c.Sonuc, c.Devir));
                Assert.Equal(e.Gelen, c.Gelen + c.CekGelen);
                Assert.Equal(e.Giden, c.Giden + c.CekGiden);
            }
        }

        foreach (var ay in donemler.Select(d => (d.Yil, d.Ay)).Distinct())
        {
            var c = HesapMotoru.AylikHesapla(ay.Yil, ay.Ay, kanallar, islemler, gelenler, donemler, bas, cekler);
            var e = HesapMotoru.AylikHesapla(ay.Yil, ay.Ay, kanallar, esIslem, esGelen, donemler, bas);
            for (int k = 0; k < kanallar.Length; k++)
            {
                var ck = c.Kanallar[k];
                var ek = e.Kanallar[k];
                Assert.Equal(ek.AySonucu, ck.AySonucu);
                Assert.Equal(ek.Gelen, ck.Gelen + ck.CekGelen);
                Assert.Equal(ek.CariGiden + ek.OrtakPay, ck.CariGiden + ck.OrtakPay + ck.CekGiden);
                Assert.Equal((ek.SabitGider, ek.KrediKarti), (ck.SabitGider, ck.KrediKarti));
            }
        }
    }
}
