using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Frozen migration model: never refer to mutable runtime entity configurations here.
internal static class NotificationsSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        CardLoanTrackingSchemaModel.Build(b);
        Define(b, "BildirimAyarEntity", "BildirimAyarlari", ["Saat", "Dakika", "Surum"], ["Etkin"], [], []);
        b.Entity("Kasa.Api.Data.BildirimAyarEntity").Property<int>("Surum").IsConcurrencyToken();
        Define(b, "BildirimEntity", "Bildirimler", ["KaynakId"], ["Okundu", "Iptal"],
            ["OlayAnahtari", "Baslik", "Mesaj", "Hedef", "Tur"], []);
        b.Entity("Kasa.Api.Data.BildirimEntity", e => { e.Property<DateOnly>("Tarih").HasColumnType("TEXT"); e.HasIndex("OlayAnahtari").IsUnique(); });
        Define(b, "PushAbonelikEntity", "PushAbonelikler", [], ["Etkin"],
            ["Endpoint", "P256dh", "Auth", "CihazId", "CihazAdi", "OturumDamgasi"], ["Olusturuldu"]);
        b.Entity("Kasa.Api.Data.PushAbonelikEntity", e => { e.Property<long?>("SonBasarili").HasColumnType("INTEGER"); e.HasIndex("Endpoint").IsUnique(); });
        Define(b, "BildirimTeslimEntity", "BildirimTeslimler", ["BildirimId", "AbonelikId", "Deneme"], ["Iptal"], [], ["SonrakiDeneme", "KilitBitis"]);
        b.Entity("Kasa.Api.Data.BildirimTeslimEntity", e =>
        {
            e.Property<string>("Kilit").HasColumnType("TEXT");
            e.Property<long?>("Gonderildi").HasColumnType("INTEGER");
            e.HasIndex("BildirimId", "AbonelikId").IsUnique(); e.HasIndex("AbonelikId");
            e.HasOne("Kasa.Api.Data.BildirimEntity", null).WithMany().HasForeignKey("BildirimId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            e.HasOne("Kasa.Api.Data.PushAbonelikEntity", null).WithMany().HasForeignKey("AbonelikId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });
    }

    private static void Define(ModelBuilder b, string name, string table, string[] ints, string[] bools, string[] strings, string[] longs)
        => b.Entity("Kasa.Api.Data." + name, e =>
        {
            e.ToTable(table); e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER"); e.HasKey("Id");
            foreach (var x in ints) e.Property<int>(x).HasColumnType("INTEGER");
            foreach (var x in bools) e.Property<bool>(x).HasColumnType("INTEGER");
            foreach (var x in strings) e.Property<string>(x).IsRequired().HasColumnType("TEXT");
            foreach (var x in longs) e.Property<long>(x).HasColumnType("INTEGER");
        });
}
