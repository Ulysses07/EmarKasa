using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class YapayZekaSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        ErpHareketleriSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.YapayZekaCagriEntity", e =>
        {
            e.ToTable("YapayZekaCagrilar");
            e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            e.HasKey("Id");
            e.Property<Guid>("IstekId").HasColumnType("TEXT");
            e.Property<int?>("BelgeId").HasColumnType("INTEGER");
            foreach (var p in new[] { "Tur", "Ay", "Model", "Durum" })
                e.Property<string>(p).IsRequired().HasColumnType("TEXT");
            e.Property<string>("SonucJson").HasColumnType("TEXT");
            e.Property<int>("SatirSayisi").HasColumnType("INTEGER");
            foreach (var p in new[] { "Zaman", "GirdiJetonu", "CiktiJetonu" })
                e.Property<long>(p).HasColumnType("INTEGER");
            e.Property<decimal>("MaliyetUsd").HasColumnType("TEXT");
            e.HasIndex("IstekId").IsUnique();
            e.HasIndex("Ay");
            e.HasIndex("BelgeId");
            e.HasOne("Kasa.Api.Data.EkstreBelgeEntity", null).WithMany().HasForeignKey("BelgeId").OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity("Kasa.Api.Data.YapayZekaOneriEntity", e =>
        {
            e.ToTable("YapayZekaOneriler");
            e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            e.HasKey("Id");
            foreach (var p in new[] { "BelgeId", "SatirNo", "CagriId" })
                e.Property<int>(p).HasColumnType("INTEGER");
            foreach (var p in new[] { "IslemTuru", "KanalIdsJson", "Kategori", "Guven" })
                e.Property<string>(p).IsRequired().HasColumnType("TEXT");
            foreach (var p in new[] { "DagilimTuru", "Aciklama" })
                e.Property<string>(p).HasColumnType("TEXT");
            e.HasIndex("BelgeId", "SatirNo").IsUnique();
            e.HasIndex("CagriId");
            e.HasOne("Kasa.Api.Data.EkstreBelgeEntity", null).WithMany().HasForeignKey("BelgeId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            e.HasOne("Kasa.Api.Data.YapayZekaCagriEntity", null).WithMany().HasForeignKey("CagriId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
    }
}
