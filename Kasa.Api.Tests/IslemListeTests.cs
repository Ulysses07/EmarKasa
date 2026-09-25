using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Tests;

public class IslemListeTests
{
    private static readonly DateOnly Tarih = new(2026, 9, 1);

    [Theory]
    [InlineData("Ortak")]
    [InlineData("Dağılım bekliyor")]
    public void Bagli_gider_onay_ve_iadede_guncel_kanallarla_gorunur_ve_filtrelenir(string kayitliKanal)
    {
        using var ortam = new Ortam();
        var gider = new IslemEntity
        {
            Tarih = Tarih, Cari = "Tedarikçi", TutarTl = 100m, Kanal = kayitliKanal,
            Tip = GiderTipi.Cari, Not = "Fiş 42",
        };
        var alis = Alis(100m, 60m, 40m, AlisDurumlari.Taslak);
        alis.Odemeler.Add(Odeme(gider));
        ortam.Db.Alislar.Add(alis);
        ortam.Db.SaveChanges();

        var bekleyen = Assert.Single(ortam.Servis.Liste(null, null, Kanallar.DagilimBekliyor, null));
        Assert.Equal(alis.Id, bekleyen.AlisId);
        Assert.True(bekleyen.DagilimBekliyor);
        Assert.Null(bekleyen.KanalId);
        Assert.Equal(gider.Id, bekleyen.Id);
        Assert.Equal(100m, bekleyen.TutarTl);
        Assert.Empty(ortam.Servis.Liste(null, null, "MEZAT", null));
        Assert.Empty(ortam.Servis.Liste(null, null, "Ortak", null));

        alis.Durum = AlisDurumlari.Onaylandi;
        ortam.Db.SaveChanges();

        var onayli = Assert.Single(ortam.Servis.Liste(null, null, null, null));
        Assert.Equal("MEZAT / TOPTAN", onayli.Kanal);
        Assert.Null(onayli.KanalId);
        Assert.False(onayli.DagilimBekliyor);
        Assert.Equal(alis.Id, onayli.AlisId);
        Assert.Equal(gider.Id, onayli.Id);
        Assert.Equal(100m, onayli.TutarTl);
        Assert.Equal(Tarih, onayli.Tarih);
        Assert.Equal("Tedarikçi", onayli.Cari);
        Assert.Equal("Fiş 42", onayli.Not);
        Assert.Equal(GiderTipi.Cari, onayli.Tip);
        Assert.Equal(onayli, Assert.Single(ortam.Servis.Liste(null, null, "MEZAT", null)));
        Assert.Equal(onayli, Assert.Single(ortam.Servis.Liste(null, null, "TOPTAN", null)));
        Assert.Empty(ortam.Servis.Liste(null, null, Kanallar.DagilimBekliyor, null));
        Assert.Empty(ortam.Servis.Liste(null, null, "Ortak", null));

        alis.Durum = AlisDurumlari.Taslak;
        ortam.Db.SaveChanges();
        Assert.True(Assert.Single(ortam.Servis.Liste(null, null, Kanallar.DagilimBekliyor, null)).DagilimBekliyor);
        Assert.Empty(ortam.Servis.Liste(null, null, "MEZAT", null));
        Assert.Equal(kayitliKanal, ortam.Db.Islemler.AsNoTracking().Single().Kanal);
        Assert.Single(ortam.Db.Islemler);
    }

    [Fact]
    public void Tarih_ve_cari_filtresi_eski_odemeyi_kumulatif_pay_hesabindan_cikarmaz()
    {
        using var ortam = new Ortam();
        var alis = Alis(0.02m, 0.01m, 0.01m);
        var ilk = new IslemEntity { Tarih = Tarih, Cari = "Tedarikçi A", TutarTl = 0.01m, Kanal = Kanallar.DagilimBekliyor };
        var ikinci = new IslemEntity { Tarih = Tarih.AddDays(1), Cari = "Tedarikçi B", TutarTl = 0.01m, Kanal = Kanallar.DagilimBekliyor };
        alis.Odemeler.Add(Odeme(ilk));
        ortam.Db.Alislar.Add(alis);
        ortam.Db.SaveChanges();
        alis.Odemeler.Add(Odeme(ikinci));
        ortam.Db.SaveChanges();

        var tumu = ortam.Servis.Liste(null, null, null, null);
        Assert.Equal(new[] { ilk.Id, ikinci.Id }, tumu.Select(i => i.Id));
        Assert.Equal("MEZAT", tumu[0].Kanal);
        Assert.Equal(1, tumu[0].KanalId);
        Assert.Equal("TOPTAN", tumu[1].Kanal);
        Assert.Equal(2, tumu[1].KanalId);
        Assert.Equal(tumu[1], Assert.Single(ortam.Servis.Liste(Tarih.AddDays(1), Tarih.AddDays(1), "TOPTAN", "B")));
        Assert.Empty(ortam.Servis.Liste(Tarih.AddDays(1), null, "MEZAT", null));
        Assert.Equal(tumu[1], Assert.Single(ortam.Servis.Liste(null, null, "TOPTAN", "B")));
    }

    [Fact]
    public void Kanal_yeniden_adlandirildiginda_projeksiyon_ve_filtre_kimlikten_cozulur()
    {
        using var ortam = new Ortam();
        var alis = Alis(100m, 100m, 0m);
        alis.Odemeler.Add(Odeme(new IslemEntity
            { Tarih = Tarih, Cari = "Tedarikçi", TutarTl = 100m, Kanal = "Eski ad", KanalId = 2 }));
        ortam.Db.Alislar.Add(alis);
        ortam.Db.SaveChanges();
        ortam.Db.Kanallar.Single(k => k.Id == 1).Ad = "YENİ MEZAT";
        ortam.Db.SaveChanges();

        var satir = Assert.Single(ortam.Servis.Liste(null, null, "YENİ MEZAT", null));
        Assert.Equal("YENİ MEZAT", satir.Kanal);
        Assert.Equal(1, satir.KanalId);
        Assert.Empty(ortam.Servis.Liste(null, null, "MEZAT", null));
        Assert.Empty(ortam.Servis.Liste(null, null, "TOPTAN", null));
        Assert.Empty(ortam.Servis.Liste(null, null, "Eski ad", null));
    }

    [Fact]
    public void Bagimsiz_islem_alanlari_korunur_ve_alis_dagilimiyla_karismaz()
    {
        using var ortam = new Ortam();
        var kart = new KrediKartiEntity { Ad = "Kart", KesimTarihi = Tarih, SonOdemeTarihi = Tarih };
        ortam.Db.KrediKartlari.Add(kart);
        ortam.Db.SaveChanges();
        var gider = new IslemEntity
        {
            Tarih = Tarih, Cari = "Firma", TutarTl = 7.89m, Kanal = "TOPTAN", KanalId = 2,
            Tip = GiderTipi.KrediKarti, Not = "Bağımsız kart gideri", KrediKartiId = kart.Id,
        };
        ortam.Db.Islemler.Add(gider);
        ortam.Db.SaveChanges();

        var satir = Assert.Single(ortam.Servis.Liste(Tarih, Tarih, "TOPTAN", "irm"));
        Assert.Equal(new IslemOkuDto(gider.Id, Tarih, "Firma", 7.89m, "TOPTAN", 2,
            GiderTipi.KrediKarti, "Bağımsız kart gideri", kart.Id), satir);
        Assert.Null(satir.AlisId);
        Assert.False(satir.DagilimBekliyor);
        Assert.Empty(ortam.Servis.Liste(Tarih.AddDays(1), null, null, null));
        Assert.Empty(ortam.Servis.Liste(null, Tarih.AddDays(-1), null, null));
        Assert.Empty(ortam.Servis.Liste(null, null, "MEZAT", null));
    }

    private static AlisEntity Alis(decimal toplam, decimal mezat, decimal toptan, string durum = AlisDurumlari.Onaylandi) => new()
    {
        Tarih = Tarih, Tedarikci = "Tedarikçi", Durum = durum,
        Kalemler = [new AlisKalemEntity
        {
            Aciklama = "Mal", Tutar = toplam,
            Dagilimlar = [new() { KanalId = 1, Tutar = mezat }, new() { KanalId = 2, Tutar = toptan }],
        }],
    };

    private static AlisOdemeEntity Odeme(IslemEntity islem) => new()
        { Islem = islem, IstekId = Guid.NewGuid(), IstekOzeti = "test" };

    private sealed class Ortam : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
        public KasaDbContext Db { get; }
        public IslemListeServisi Servis { get; }

        public Ortam()
        {
            _connection.Open();
            Db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(_connection).Options);
            Db.Database.Migrate();
            Db.Kanallar.AddRange(new KanalEntity { Id = 1, Ad = "MEZAT" }, new KanalEntity { Id = 2, Ad = "TOPTAN" });
            Db.SaveChanges();
            Servis = new IslemListeServisi(Db);
        }

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }
}
