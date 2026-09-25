using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

internal static class OperationsSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        FinanceSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.EditorGuvenlikEntity", e =>
        {
            e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            e.Property<string>("SifreHash").HasColumnType("TEXT");
            e.Property<string>("KurtarmaHash").HasColumnType("TEXT");
            e.Property<int>("Surum").IsConcurrencyToken().HasColumnType("INTEGER");
            e.HasKey("Id"); e.ToTable("EditorGuvenlik");
        });
        b.Entity("Kasa.Api.Data.BelgeEntity", e =>
        {
            e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            e.Property<int>("AlisId").HasColumnType("INTEGER");
            e.Property<int?>("OdemeId").HasColumnType("INTEGER");
            e.Property<string>("DosyaAdi").IsRequired().HasColumnType("TEXT");
            e.Property<string>("IcerikTuru").IsRequired().HasColumnType("TEXT");
            e.Property<long>("Boyut").HasColumnType("INTEGER");
            e.Property<DateTimeOffset>("Yuklendi").HasColumnType("TEXT");
            e.Property<byte[]>("Icerik").IsRequired().HasColumnType("BLOB");
            e.HasKey("Id"); e.HasIndex("AlisId"); e.HasIndex("OdemeId"); e.ToTable("Belgeler");
            e.HasOne("Kasa.Api.Data.AlisEntity", null).WithMany().HasForeignKey("AlisId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            e.HasOne("Kasa.Api.Data.AlisOdemeEntity", null).WithMany().HasForeignKey("OdemeId").OnDelete(DeleteBehavior.SetNull);
        });
    }
}
