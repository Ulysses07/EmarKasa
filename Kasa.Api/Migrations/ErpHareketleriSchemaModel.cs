using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class ErpHareketleriSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        EkstreHafizasiSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.ErpHareketEntity", e =>
        {
            e.ToTable("ErpHareketler");
            e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            e.HasKey("Id");
            e.Property<int>("BelgeId").HasColumnType("INTEGER");
            e.Property<int?>("SatirNo").HasColumnType("INTEGER");
            e.Property<DateOnly>("Tarih").HasColumnType("TEXT");
            foreach (var p in new[] { "CariAdi", "CariAnahtari", "Ozet" })
                e.Property<string>(p).IsRequired().HasColumnType("TEXT");
            foreach (var p in new[] { "CariKodu", "IslemTuru", "BelgeNo", "Aciklama" })
                e.Property<string>(p).HasColumnType("TEXT");
            foreach (var p in new[] { "Borc", "Alacak" })
                e.Property<decimal>(p).HasColumnType("TEXT");
            e.HasIndex("Ozet").IsUnique();
            e.HasIndex("BelgeId", "SatirNo");
            e.HasIndex("CariAnahtari");
            e.HasOne("Kasa.Api.Data.EkstreBelgeEntity", null).WithMany().HasForeignKey("BelgeId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
    }
}
