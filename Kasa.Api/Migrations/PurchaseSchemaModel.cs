using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kasa.Api.Migrations;

internal static class PurchaseSchemaModel
{
    internal static void Build(ModelBuilder modelBuilder)
    {
        InitialStableSchemaModel.Build(modelBuilder);
        Entity(modelBuilder, "AliciEntity", "Alicilar", b =>
        {
            Text(b, "Kullanici").UseCollation("NOCASE"); Text(b, "Ad"); Text(b, "SifreHash");
            b.Property<bool>("Aktif").HasColumnType("INTEGER");
            b.Property<int>("OturumSurumu").HasColumnType("INTEGER");
            b.HasIndex("Kullanici").IsUnique();
        });
        Entity(modelBuilder, "AlisEntity", "Alislar", b =>
        {
            b.Property<int>("Surum").IsConcurrencyToken().HasColumnType("INTEGER");
            b.Property<int?>("AliciId").HasColumnType("INTEGER");
            b.Property<DateOnly>("Tarih").HasColumnType("TEXT");
            Text(b, "Tedarikci"); Text(b, "Durum");
            b.Property<string>("Not").HasColumnType("TEXT");
            b.Property<string>("EditorNotu").HasColumnType("TEXT");
            b.HasIndex("AliciId");
        });
        Entity(modelBuilder, "AlisKalemEntity", "AlisKalemler", b =>
        {
            b.Property<int>("AlisId").HasColumnType("INTEGER"); Text(b, "Aciklama");
            b.Property<decimal>("Tutar").HasColumnType("TEXT"); b.HasIndex("AlisId");
        });
        Entity(modelBuilder, "AlisDagilimEntity", "AlisDagilimlar", b =>
        {
            b.Property<int>("AlisKalemId").HasColumnType("INTEGER");
            b.Property<int>("KanalId").HasColumnType("INTEGER");
            b.Property<decimal>("Tutar").HasColumnType("TEXT");
            b.HasIndex("KanalId"); b.HasIndex("AlisKalemId", "KanalId").IsUnique();
        });
        Entity(modelBuilder, "AlisOdemeEntity", "AlisOdemeler", b =>
        {
            b.Property<int>("AlisId").HasColumnType("INTEGER");
            b.Property<int>("IslemId").HasColumnType("INTEGER");
            b.Property<Guid>("IstekId").HasColumnType("TEXT"); Text(b, "IstekOzeti");
            b.HasIndex("AlisId"); b.HasIndex("IslemId").IsUnique(); b.HasIndex("IstekId").IsUnique();
        });
        modelBuilder.Entity("Kasa.Api.Data.AlisEntity", b =>
        {
            b.HasOne("Kasa.Api.Data.AliciEntity", "AliciKaydi").WithMany().HasForeignKey("AliciId").OnDelete(DeleteBehavior.Restrict);
            b.Navigation("AliciKaydi");
        });
        modelBuilder.Entity("Kasa.Api.Data.AlisKalemEntity", b =>
            b.HasOne("Kasa.Api.Data.AlisEntity", null).WithMany("Kalemler").HasForeignKey("AlisId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Kasa.Api.Data.AlisDagilimEntity", b =>
        {
            b.HasOne("Kasa.Api.Data.AlisKalemEntity", null).WithMany("Dagilimlar").HasForeignKey("AlisKalemId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasOne("Kasa.Api.Data.KanalEntity", "KanalKaydi").WithMany().HasForeignKey("KanalId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.Navigation("KanalKaydi");
        });
        modelBuilder.Entity("Kasa.Api.Data.AlisOdemeEntity", b =>
        {
            b.HasOne("Kasa.Api.Data.AlisEntity", null).WithMany("Odemeler").HasForeignKey("AlisId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("Kasa.Api.Data.IslemEntity", "Islem").WithMany().HasForeignKey("IslemId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.Navigation("Islem");
        });
        modelBuilder.Entity("Kasa.Api.Data.AlisEntity", b => { b.Navigation("Kalemler"); b.Navigation("Odemeler"); });
        modelBuilder.Entity("Kasa.Api.Data.AlisKalemEntity", b => b.Navigation("Dagilimlar"));
    }

    private static void Entity(ModelBuilder modelBuilder, string entity, string table, Action<EntityTypeBuilder> configure) =>
        modelBuilder.Entity("Kasa.Api.Data." + entity, b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.HasKey("Id"); b.ToTable(table); configure(b);
        });
    private static PropertyBuilder<string> Text(EntityTypeBuilder b, string name) => b.Property<string>(name).IsRequired().HasColumnType("TEXT");
}
