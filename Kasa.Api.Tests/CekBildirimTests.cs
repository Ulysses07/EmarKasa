using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core.Kodlar;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>Çek vade hatırlatmaları (docs/specs/2026-10-01-cekler.md "Bildirimler"): üç kural, günleri, metinleri ve hedef; teminat,
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
}
