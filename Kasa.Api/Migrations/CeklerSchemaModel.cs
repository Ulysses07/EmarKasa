using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class CeklerSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        EditorSifirlamaIziSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.CekEntity", e =>
        {
            e.ToTable("Cekler");
            e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            e.HasKey("Id");
            foreach (var p in new[] { "Tur", "Yon", "No", "Kisi" })
                e.Property<string>(p).IsRequired().HasColumnType("TEXT");
            foreach (var p in new[] { "Banka", "Konum", "Not" })
                e.Property<string>(p).HasColumnType("TEXT");
            e.Property<decimal>("Tutar").HasColumnType("TEXT");
            e.Property<DateOnly>("VadeTarihi").HasColumnType("TEXT");
            e.Property<int?>("KanalId").HasColumnType("INTEGER");
            e.Property<bool>("Teminat").HasColumnType("INTEGER");
            e.Property<int>("Surum").IsConcurrencyToken().HasColumnType("INTEGER");
            e.HasIndex("KanalId");
            e.HasOne("Kasa.Api.Data.KanalEntity", null).WithMany().HasForeignKey("KanalId").OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity("Kasa.Api.Data.CekHareketEntity", e =>
        {
            e.ToTable("CekHareketler");
            e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            e.HasKey("Id");
            foreach (var p in new[] { "CekId", "Sira" })
                e.Property<int>(p).HasColumnType("INTEGER");
            e.Property<string>("Tur").IsRequired().HasColumnType("TEXT");
            e.Property<DateOnly>("Tarih").HasColumnType("TEXT");
            e.Property<decimal>("Tutar").HasColumnType("TEXT");
            e.Property<decimal?>("NetTutar").HasColumnType("TEXT");
            e.Property<int?>("KanalId").HasColumnType("INTEGER");
            e.Property<string>("Karsi").HasColumnType("TEXT");
            e.HasIndex("CekId", "Sira").IsUnique();
            e.HasIndex("KanalId");
            e.HasOne("Kasa.Api.Data.CekEntity", null).WithMany().HasForeignKey("CekId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            e.HasOne("Kasa.Api.Data.KanalEntity", null).WithMany().HasForeignKey("KanalId").OnDelete(DeleteBehavior.Restrict);
        });
    }
}
