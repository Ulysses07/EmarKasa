using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

internal static class MonthlyExpensesAndLocksSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        NotificationsSchemaModel.Build(b);
        Define(b, "AylikGiderSablonEntity", "AylikGiderSablonlar", ["Surum"], [], [], []);
        b.Entity("Kasa.Api.Data.AylikGiderSablonEntity").Property<int>("Surum").IsConcurrencyToken();
        Define(b, "AylikGiderRevizyonEntity", "AylikGiderRevizyonlar", ["SablonId", "Surum", "OdemeGunu"], ["Aktif"], ["Ad", "Tur", "DagilimTuru", "DagilimJson"], ["GecerliAy"]);
        b.Entity("Kasa.Api.Data.AylikGiderRevizyonEntity", e =>
        {
            e.Property<decimal>("Tutar").HasColumnType("TEXT"); e.HasIndex("SablonId", "Surum").IsUnique();
            e.HasOne("Kasa.Api.Data.AylikGiderSablonEntity", null).WithMany().HasForeignKey("SablonId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
        Define(b, "AylikGiderOdemeEntity", "AylikGiderOdemeler", ["SablonId", "RevizyonId"], ["Iptal"], [], ["Ay", "Tarih"]);
        b.Entity("Kasa.Api.Data.AylikGiderOdemeEntity", e =>
        {
            e.Property<decimal>("Tutar").HasColumnType("TEXT"); e.Property<int?>("IslemId").HasColumnType("INTEGER"); e.Property<string>("IptalAciklamasi").HasColumnType("TEXT");
            e.HasIndex("SablonId", "Ay").IsUnique().HasFilter("\"Iptal\" = 0"); e.HasIndex("RevizyonId"); e.HasIndex("IslemId").IsUnique();
            e.HasOne("Kasa.Api.Data.AylikGiderSablonEntity", null).WithMany().HasForeignKey("SablonId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            e.HasOne("Kasa.Api.Data.AylikGiderRevizyonEntity", null).WithMany().HasForeignKey("RevizyonId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            e.HasOne("Kasa.Api.Data.IslemEntity", null).WithMany().HasForeignKey("IslemId").OnDelete(DeleteBehavior.SetNull);
        });
        Define(b, "AyKilidiEntity", "AyKilidi", ["Surum"], [], [], []);
        b.Entity("Kasa.Api.Data.AyKilidiEntity", e => { e.Property<int>("Surum").IsConcurrencyToken(); e.Property<DateOnly?>("KilitliSonTarih").HasColumnType("TEXT"); });
        Define(b, "AyKilidiOlayEntity", "AyKilidiOlaylar", [], [], ["Aciklama"], []);
        b.Entity("Kasa.Api.Data.AyKilidiOlayEntity", e =>
        {
            e.Property<DateOnly?>("OncekiSonTarih").HasColumnType("TEXT"); e.Property<DateOnly?>("YeniSonTarih").HasColumnType("TEXT");
            e.Property<DateTimeOffset>("Zaman").HasColumnType("TEXT");
        });
    }
    private static void Define(ModelBuilder b, string name, string table, string[] ints, string[] bools, string[] strings, string[] dates) => b.Entity("Kasa.Api.Data." + name, e =>
    {
        e.ToTable(table); e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER"); e.HasKey("Id");
        foreach (var x in ints) e.Property<int>(x).HasColumnType("INTEGER");
        foreach (var x in bools) e.Property<bool>(x).HasColumnType("INTEGER");
        foreach (var x in strings) e.Property<string>(x).IsRequired().HasColumnType("TEXT");
        foreach (var x in dates) e.Property<DateOnly>(x).HasColumnType("TEXT");
    });
}
