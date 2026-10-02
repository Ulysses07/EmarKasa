using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core.Kodlar;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>Çek vade hatırlatmaları (docs/specs/2026-10-01-cekler.md "Bildirimler"): dört kural, günleri, metinleri ve hedef; teminat,
/// kapanmış ve karşılıksız çek hariç. Bugün 25 Eylül 2026.</summary>
public class CekBildirimTests
{
    [Fact]
    public async Task Uc_kural_gunleri_metinleri_ve_hedefi()
    {
        await using var f = Fabrika();
        await Editor(f);
        var vade3 = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(vade: Today.AddDays(3)));
        var kismen = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "K-1", vade: Today),
            new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = Today.AddDays(-1), Tutar = 20_000.5m, KanalId = 1 });
        var ibraz = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "I-1", vade: Today.AddDays(-7)));
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "I-2", vade: Today.AddDays(-7)),
            new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = Today, Tutar = 1m, KanalId = 1 });
        var verilen = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(CekYonleri.Verilen, 30_000m, kisi: "Mehmet Ticaret", no: "V-1", vade: Today.AddDays(3)));
        var senet = CekVeriModeliTests.Cek(no: "S-1", vade: Today);
        senet.Tur = CekTurleri.Senet;
        senet = await CekRaporTests.CekEkle(f, senet);
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "T-1", teminat: true, vade: Today));
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "G-2", vade: Today.AddDays(2)));
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "KS-1", vade: Today),
            new CekHareketEntity { Tur = CekHareketTurleri.Karsiliksiz, Tarih = Today });
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "TE-1", vade: Today),
            new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = Today, Tutar = 50_000m, KanalId = 1 });

        using var scope = f.Services.CreateScope();
        var taslaklar = CekBildirimleri.Oku(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), Today).OrderBy(t => t.KaynakId).ToList();
        Assert.Equal(
        [
            ($"Cekler:{vade3.Id}:Vade:2026-09-28:3", "Çek vadesi", "Ahmet Yılmaz · 50.000,00 TL · 28 Eylül", $"/#cheques/{vade3.Id}", CekBildirimleri.VadeTuru),
            ($"Cekler:{kismen.Id}:Vade:2026-09-25:0", "Çek vadesi", "Ahmet Yılmaz · 29.999,50 TL · 25 Eylül", $"/#cheques/{kismen.Id}", CekBildirimleri.VadeTuru),
            ($"Cekler:{ibraz.Id}:Ibraz:2026-09-18:-7", "İbraz süresi doluyor", "Ahmet Yılmaz · 50.000,00 TL · vade 18 Eylül", $"/#cheques/{ibraz.Id}", CekBildirimleri.IbrazTuru),
            ($"Cekler:{verilen.Id}:Odenecek:2026-09-28:3", "Ödenecek çek", "Mehmet Ticaret · 30.000,00 TL · hesapta bulunmalı", $"/#cheques/{verilen.Id}", CekBildirimleri.OdemeTuru),
            ($"Cekler:{senet.Id}:Vade:2026-09-25:0", "Senet vadesi", "Ahmet Yılmaz · 50.000,00 TL · 25 Eylül", $"/#cheques/{senet.Id}", CekBildirimleri.VadeTuru),
        ], taslaklar.Select(t => (t.Anahtar, t.Baslik, t.Mesaj, t.Hedef, t.Tur)));
        Assert.All(taslaklar, t => Assert.Equal(Today, t.Tarih));
    }

    [Fact]
    public void Hesaplanamayan_cek_kaynaginin_uyarisi_cekler_sayfasini_hedefler()
        => Assert.Equal("/#cheques", BildirimTakvimi.Hata(new(CekBildirimleri.Kaynak, 0, "Çek hatırlatmaları", new InvalidOperationException()), Today).Hedef);

    /// <summary>Senedin ibraz süresi farklı hukuki rejime tabidir (kambiyo mevzuatı çeke özgü 7 günlük ibraz süresi tanır); bu
    /// yüzden "İbraz süresi doluyor" yalnız çekte çıkar, aynı durumdaki (portföyde, vade+7) senette çıkmaz.</summary>
    [Fact]
    public async Task Senette_ibraz_hatirlatmasi_yok_cekte_var()
    {
        await using var f = Fabrika();
        await Editor(f);
        var senet = CekVeriModeliTests.Cek(no: "SN-1", vade: Today.AddDays(-7));
        senet.Tur = CekTurleri.Senet;
        senet = await CekRaporTests.CekEkle(f, senet);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "CK-1", vade: Today.AddDays(-7)));

        using var scope = f.Services.CreateScope();
        var taslaklar = CekBildirimleri.Oku(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), Today);
        Assert.DoesNotContain(taslaklar, t => t.KaynakId == senet.Id);
        Assert.Equal(CekBildirimleri.IbrazTuru, Assert.Single(taslaklar, t => t.KaynakId == cek.Id).Tur);
    }

    /// <summary>"Ödenecek çek": vade günü (fark 0) ve kısmen ödenmiş (kalan tutarla) aynı kuralı tetikler.</summary>
    [Fact]
    public async Task Odenecek_cek_vade_gununde_ve_kismen_odenmiste_bildirim_cikar()
    {
        await using var f = Fabrika();
        await Editor(f);
        var vadeGunu = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(CekYonleri.Verilen, 10_000m, kisi: "Zeynep A.Ş.", no: "VG-1", vade: Today));
        var kismenOdenen = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(CekYonleri.Verilen, 10_000m, kisi: "Kerem Ltd.", no: "KO-1", vade: Today.AddDays(3)),
            new CekHareketEntity { Tur = CekHareketTurleri.Odeme, Tarih = Today.AddDays(-1), Tutar = 4_000m, KanalId = 1 });

        using var scope = f.Services.CreateScope();
        var taslaklar = CekBildirimleri.Oku(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), Today);
        Assert.Equal(("Ödenecek çek", "Zeynep A.Ş. · 10.000,00 TL · hesapta bulunmalı"),
            taslaklar.Where(t => t.KaynakId == vadeGunu.Id).Select(t => (t.Baslik, t.Mesaj)).Single());
        Assert.Equal(("Ödenecek çek", "Kerem Ltd. · 6.000,00 TL · hesapta bulunmalı"),
            taslaklar.Where(t => t.KaynakId == kismenOdenen.Id).Select(t => (t.Baslik, t.Mesaj)).Single());
    }

    /// <summary>"Ödenmemiş çek" (2026-10-02 ürün sahibi kararı): verilen, teminatsız, açık çekin vadesinin üzerinden tam bir gün
    /// geçtiyse (fark -1) tek bildirim; kısmen ödenmişte kalan tutar yazılır, senette başlık "Ödenmemiş senet".</summary>
    [Fact]
    public async Task Vadesi_dun_gecmis_odenmemis_verilen_cek_icin_tek_bildirim_cikar()
    {
        await using var f = Fabrika();
        await Editor(f);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(CekYonleri.Verilen, 30_000m, kisi: "Mehmet Ticaret", no: "VG-1", vade: Today.AddDays(-1)));
        var kismen = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(CekYonleri.Verilen, 10_000m, kisi: "Kerem Ltd.", no: "VG-2", vade: Today.AddDays(-1)),
            new CekHareketEntity { Tur = CekHareketTurleri.Odeme, Tarih = Today.AddDays(-2), Tutar = 4_000.5m, KanalId = 1 });
        var senet = CekVeriModeliTests.Cek(CekYonleri.Verilen, 7_000m, kisi: "Veli", no: "VS-1", vade: Today.AddDays(-1));
        senet.Tur = CekTurleri.Senet;
        senet = await CekRaporTests.CekEkle(f, senet);

        using var scope = f.Services.CreateScope();
        var taslaklar = CekBildirimleri.Oku(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), Today).OrderBy(t => t.KaynakId).ToList();
        Assert.Equal(
        [
            ($"Cekler:{cek.Id}:Gecmis:2026-09-24:-1", "Ödenmemiş çek", "Mehmet Ticaret · 30.000,00 TL · vade 24 Eylül geçti", $"/#cheques/{cek.Id}", CekBildirimleri.GecmisOdemeTuru),
            ($"Cekler:{kismen.Id}:Gecmis:2026-09-24:-1", "Ödenmemiş çek", "Kerem Ltd. · 5.999,50 TL · vade 24 Eylül geçti", $"/#cheques/{kismen.Id}", CekBildirimleri.GecmisOdemeTuru),
            ($"Cekler:{senet.Id}:Gecmis:2026-09-24:-1", "Ödenmemiş senet", "Veli · 7.000,00 TL · vade 24 Eylül geçti", $"/#cheques/{senet.Id}", CekBildirimleri.GecmisOdemeTuru),
        ], taslaklar.Select(t => (t.Anahtar, t.Baslik, t.Mesaj, t.Hedef, t.Tur)));
        Assert.All(taslaklar, t => Assert.Equal(Today, t.Tarih));
    }

    /// <summary>Ödenmemiş çek uyarısı yalnız fark -1'de, yalnız verilen ve teminatsız çekte çıkar: fark -2, alınan çek (vadesi dün)
    /// ve teminatlı verilen çek için bildirim yoktur.</summary>
    [Fact]
    public async Task Odenmemis_cek_uyarisi_fark_iki_alinan_ve_teminatli_cekte_cikmaz()
    {
        await using var f = Fabrika();
        await Editor(f);
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(CekYonleri.Verilen, no: "V-2", vade: Today.AddDays(-2)));
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "A-1", vade: Today.AddDays(-1)));
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(CekYonleri.Verilen, no: "T-1", teminat: true, vade: Today.AddDays(-1)));

        using var scope = f.Services.CreateScope();
        Assert.Empty(CekBildirimleri.Oku(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), Today));
    }

    /// <summary>Yıl sonu sınırı: vade farkı DayNumber'dan hesaplandığı ve aday seçimi SQLite'ta DateOnly eşitliğiyle süzüldüğü için
    /// yıl dönümünde de doğru çalışır (bugün 30 Aralık 2026, vade 2 Ocak 2027, fark 3).</summary>
    [Fact]
    public async Task Yil_sonu_sinirinda_vade_farki_dogru_hesaplanir()
    {
        await using var f = Fabrika();
        await Editor(f);
        var yilSonu = new DateOnly(2026, 12, 30);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "YS-1", vade: new DateOnly(2027, 1, 2)));

        using var scope = f.Services.CreateScope();
        var taslaklar = CekBildirimleri.Oku(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), yilSonu);
        var taslak = Assert.Single(taslaklar);
        Assert.Equal(("Çek vadesi", "Ahmet Yılmaz · 50.000,00 TL · 2 Ocak", $"/#cheques/{cek.Id}", CekBildirimleri.VadeTuru),
            (taslak.Baslik, taslak.Mesaj, taslak.Hedef, taslak.Tur));
    }
}
