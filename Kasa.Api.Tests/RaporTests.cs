using System.Data.Common;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class RaporTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public RaporTests(KasaWebFactory factory) => _factory = factory;

    private record KanalHaftalikYanit(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir);
    private record HaftalikOzetYanit(Donem Donem, List<KanalHaftalikYanit> Kanallar, decimal ToplamGelen, decimal ToplamGiden, decimal KasaSonucu, decimal KasaDevir);

    private void Tohumla()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();

        // İki test aynı DB'yi paylaştığından sıra-bağımsızlık için tümünü temizle.
        db.Islemler.RemoveRange(db.Islemler);
        db.Gelenler.RemoveRange(db.Gelenler);
        // Kanallar silinmez (tamamlanmış ayların kanal kümesinde yer alan kanal silinemez; AyKanalKumesi): adıyla yeniden tohumlanır.
        foreach (var (ad, sira, devir) in new[] { ("MEZAT", 0, 4_991_052m), ("PERAKENDE", 1, 2_013_516m), ("TOPTAN", 2, 619_647m) })
        {
            var kanal = db.Kanallar.SingleOrDefault(k => k.Ad == ad) ?? db.Kanallar.Add(new KanalEntity { Ad = ad }).Entity;
            kanal.Sira = sira;
            kanal.Aktif = true;
            kanal.AcilisDevri = devir;
        }

        var ayar = db.Ayarlar.First();
        ayar.TakipBaslangic = new DateOnly(2026, 6, 29);
        ayar.KasaAcilisDevri = 2_907_053.21m;

        db.Gelenler.AddRange(
            new GelenEntity { DonemStart = new DateOnly(2026, 6, 29), Kanal = "MEZAT", TutarTl = 289_425m },
            new GelenEntity { DonemStart = new DateOnly(2026, 6, 29), Kanal = "PERAKENDE", TutarTl = 271_006m },
            new GelenEntity { DonemStart = new DateOnly(2026, 6, 29), Kanal = "TOPTAN", TutarTl = 207_000m });

        db.Islemler.AddRange(
            new IslemEntity { Tarih = new DateOnly(2026, 6, 29), Cari = "MEZAT-cari", TutarTl = 1_308_800m, Kanal = "MEZAT", Tip = GiderTipi.Cari },
            new IslemEntity { Tarih = new DateOnly(2026, 6, 29), Cari = "PER-cari", TutarTl = 1_221_374m, Kanal = "PERAKENDE", Tip = GiderTipi.Cari },
            new IslemEntity { Tarih = new DateOnly(2026, 6, 29), Cari = "TOP-cari", TutarTl = 360_000m, Kanal = "TOPTAN", Tip = GiderTipi.Cari },
            new IslemEntity { Tarih = new DateOnly(2026, 6, 30), Cari = "SGK/Vergi", TutarTl = 455_321m, Kanal = Kanallar.Ortak, Tip = GiderTipi.SabitGider });

        db.SaveChanges();
    }

    [Fact]
    public async Task Haftalik_rapor_haziran_excel_rakamlarini_uretir()
    {
        Tohumla();
        var client = await _factory.EditorClientAsync();

        var ozetler = await client.GetFromJsonAsync<List<HaftalikOzetYanit>>("/api/rapor/haftalik");
        var d = ozetler!.Single(o => o.Donem.Start == new DateOnly(2026, 6, 29));

        var mezat = d.Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal(-1_019_375m, mezat.Sonuc);
        Assert.Equal(3_971_677m, mezat.Devir);

        Assert.Equal(767_431m, d.ToplamGelen);
        Assert.Equal(3_345_495m, d.ToplamGiden);
        Assert.Equal(328_989.21m, d.KasaDevir);
    }

    [Fact]
    public async Task Panel_guncel_kasayi_ve_kanal_bakiyelerini_doner()
    {
        Tohumla();
        var client = await _factory.EditorClientAsync();

        var panel = await client.GetFromJsonAsync<PanelDto>("/api/rapor/panel");
        Assert.NotNull(panel);
        // 29-30 Haziran sonrası boş dönemler kasayı değiştirmez.
        Assert.Equal(328_989.21m, panel!.GuncelKasa);
        Assert.Equal(3_971_677m, panel.Kanallar.Single(k => k.Kanal == "MEZAT").Bakiye);
    }
}

/// <summary>
/// İptal yayılımı (gap-okuma-yolu-maliyet-kilit-cekismesi-6): rapor hesabı istemcinin bıraktığı isteğin belirtecini
/// (RequestAborted) sorgular arasında ve döngülerde denetler; iptalden sonra yeni sorgu çalıştırmaz, okuma anlık
/// görüntüsünü bırakır ve bağlantıyı yazmaya açık bırakır.
/// </summary>
public class RaporIptalTests
{
    private sealed class IptalKesicisi : DbCommandInterceptor
    {
        public CancellationTokenSource? Iptal;
        public string? Tablo;
        public int IptalSonrasiKomut;
        private bool _iptalEdildi;
        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            if (_iptalEdildi)
                Interlocked.Increment(ref IptalSonrasiKomut);
            else if (Tablo is not null && command.CommandText.Contains($"FROM \"{Tablo}\"", StringComparison.Ordinal))
            { _iptalEdildi = true; Iptal!.Cancel(); }
            return result;
        }
    }

    private sealed class KesiciliFabrika(IptalKesicisi kesici) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.ConfigureDbContext<KasaDbContext>(o => o.AddInterceptors(kesici)));
        }
    }

    [Theory]
    [InlineData("Islemler")]
    [InlineData("TakipKartOdemeler")]
    public async Task Iptal_edilen_rapor_hesabi_sonraki_sorguyu_calistirmaz_ve_anlik_goruntuyu_birakir(string tablo)
    {
        var kesici = new IptalKesicisi();
        await using var f = new KesiciliFabrika(kesici) { Saat = new SabitSaat(KasaWebFactory.VarsayilanBugun) };
        using var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(2026, 1, 1), kasaAcilisDevri = 1_000m })).EnsureSuccessStatusCode();
        var kart = await AltinTohum.Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Kart", 10_000m, 5, 15, new(2026, 1, 1), 100m, [new(1, 100m)]));
        await AltinTohum.Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, new(2026, 2, 1), 40m));
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(new(2026, 3, 1), "Gider", 10m, "MEZAT", GiderTipi.Cari))).EnsureSuccessStatusCode();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var hesap = new HesapServisi(db);
        using var iptal = new CancellationTokenSource();
        kesici.Iptal = iptal;
        kesici.Tablo = tablo;
        Assert.Throws<OperationCanceledException>(() => hesap.Panel(iptal.Token));
        Assert.Equal(0, kesici.IptalSonrasiKomut);
        Assert.Null(db.Database.CurrentTransaction);
        // Anlık görüntü bırakıldı: aynı bağlantı yeniden yazmaya açık.
        db.Kanallar.Add(new() { Ad = "İptalden sonra" });
        db.SaveChanges();
        // Önceden bırakılmış istek hiç hesaplanmaz.
        Assert.Throws<OperationCanceledException>(() => hesap.Haftalik(new CancellationToken(true)));
        Assert.Throws<OperationCanceledException>(() => FinansTakipServisi.Kart(new TakipHesapBaglami(db, new CancellationToken(true)), kart.Id));
        // Aynı servis sonraki istekte eksiksiz hesaplar: 1.000 − 40 kart ödemesi − 10 gider.
        kesici.Tablo = null;
        Assert.Equal(950m, hesap.Panel().GuncelKasa);
    }
}
