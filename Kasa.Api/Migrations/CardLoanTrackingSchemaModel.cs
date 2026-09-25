using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

internal static class CardLoanTrackingSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        LegacyIncomeGroupsSchemaModel.Build(b);
        Define(b, "TakipKartEntity", "TakipKartlar", "KrediKartiId", ["Surum"], ["Aktif", "EskiKayit"], ["Baslangic"], [], []);
        Define(b, "TakipEkstreEntity", "TakipEkstreler", "Id", ["KrediKartiId"], [], ["KesimTarihi", "SonOdemeTarihi"], [], []);
        Define(b, "TakipHarcamaEntity", "TakipHarcamalar", "Id", ["KrediKartiId", "TaksitSayisi"], ["Iptal"], ["Tarih"], ["Tutar", "KasadaOncedenSayilanTutar"], ["Aciklama", "DagilimJson"]);
        Define(b, "TakipKartTaksitEntity", "TakipKartTaksitler", "Id", ["HarcamaId", "EkstreId"], [], [], ["Tutar"], []);
        Define(b, "TakipKartOdemeEntity", "TakipKartOdemeler", "Id", ["KrediKartiId"], ["Iptal"], ["Tarih"], ["Tutar"], ["PaylarJson"]);
        Define(b, "TakipKrediEntity", "TakipKrediler", "KrediId", ["Surum"], ["Aktif", "EskiKayit", "MevcutKredi"], ["Baslangic"], [], ["KanalIdleriJson", "CekimPaylariJson"]);
        Define(b, "TakipKrediTaksitEntity", "TakipKrediTaksitler", "Id", ["KrediId", "No"], ["Iptal"], ["Tarih"], ["Tutar"], ["DagilimJson"]);
        b.Entity("Kasa.Api.Data.TakipKartEntity", e => { e.Property<int>("Surum").IsConcurrencyToken(); e.HasOne("Kasa.Api.Data.KrediKartiEntity", null).WithMany().HasForeignKey("KrediKartiId").OnDelete(DeleteBehavior.Restrict).IsRequired(); });
        b.Entity("Kasa.Api.Data.TakipEkstreEntity", e => { e.Property<decimal?>("AsgariOdeme").HasColumnType("TEXT"); e.HasIndex("KrediKartiId", "KesimTarihi").IsUnique(); });
        b.Entity("Kasa.Api.Data.TakipHarcamaEntity", e => { e.Property<int?>("IslemId").HasColumnType("INTEGER"); e.HasIndex("IslemId").IsUnique(); e.HasOne("Kasa.Api.Data.IslemEntity", null).WithMany().HasForeignKey("IslemId").OnDelete(DeleteBehavior.Restrict); });
        b.Entity("Kasa.Api.Data.TakipHarcamaEntity", e => { e.Property<int?>("KaynakHarcamaId").HasColumnType("INTEGER"); e.HasIndex("KaynakHarcamaId"); e.HasOne("Kasa.Api.Data.TakipHarcamaEntity", null).WithMany().HasForeignKey("KaynakHarcamaId").OnDelete(DeleteBehavior.Restrict); });
        foreach (var name in new[] { "TakipEkstreEntity", "TakipHarcamaEntity", "TakipKartOdemeEntity" })
            b.Entity("Kasa.Api.Data." + name, e => { if (name != "TakipEkstreEntity") e.HasIndex("KrediKartiId"); e.HasOne("Kasa.Api.Data.TakipKartEntity", null).WithMany().HasForeignKey("KrediKartiId").OnDelete(DeleteBehavior.Restrict).IsRequired(); });
        b.Entity("Kasa.Api.Data.TakipKartTaksitEntity", e =>
        {
            e.HasIndex("HarcamaId"); e.HasIndex("EkstreId");
            e.HasOne("Kasa.Api.Data.TakipHarcamaEntity", null).WithMany().HasForeignKey("HarcamaId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            e.HasOne("Kasa.Api.Data.TakipEkstreEntity", null).WithMany().HasForeignKey("EkstreId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
        b.Entity("Kasa.Api.Data.TakipKrediEntity", e => { e.Property<int>("Surum").IsConcurrencyToken(); e.HasOne("Kasa.Api.Data.KrediEntity", null).WithMany().HasForeignKey("KrediId").OnDelete(DeleteBehavior.Restrict).IsRequired(); });
        b.Entity("Kasa.Api.Data.TakipKrediTaksitEntity", e => { e.HasIndex("KrediId", "No").IsUnique(); e.HasOne("Kasa.Api.Data.TakipKrediEntity", null).WithMany().HasForeignKey("KrediId").OnDelete(DeleteBehavior.Restrict).IsRequired(); });
        foreach (var name in new[] { "TakipKartOdemeEntity", "TakipKrediTaksitEntity" }) b.Entity("Kasa.Api.Data." + name, e => e.Property<string>("Not").HasColumnType("TEXT"));
    }

    private static void Define(ModelBuilder b, string name, string table, string key, string[] ints, string[] bools, string[] dates, string[] decimals, string[] texts) => b.Entity("Kasa.Api.Data." + name, e =>
    {
        var pk = e.Property<int>(key).HasColumnType("INTEGER");
        if (key == "Id") pk.ValueGeneratedOnAdd(); else pk.ValueGeneratedNever();
        foreach (var p in ints) e.Property<int>(p).HasColumnType("INTEGER");
        foreach (var p in bools) e.Property<bool>(p).HasColumnType("INTEGER");
        foreach (var p in dates) e.Property<DateOnly>(p).HasColumnType("TEXT");
        foreach (var p in decimals) e.Property<decimal>(p).HasColumnType("TEXT");
        foreach (var p in texts) e.Property<string>(p).IsRequired().HasColumnType("TEXT");
        e.HasKey(key); e.ToTable(table);
    });
}
