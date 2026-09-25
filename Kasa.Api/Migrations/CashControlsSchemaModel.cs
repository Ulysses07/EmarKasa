using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class CashControlsSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        MonthlyExpensesAndLocksSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.KasaEsikEntity", e =>
        {
            e.ToTable("KasaEsikleri"); e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER"); e.HasKey("Id");
            e.Property<int>("KanalId").HasColumnType("INTEGER"); e.HasIndex("KanalId").IsUnique();
            e.Property<int>("Surum").HasColumnType("INTEGER").IsConcurrencyToken();
            e.Property<int>("OlaySayisi").HasColumnType("INTEGER"); e.Property<decimal>("Tutar").HasColumnType("TEXT");
            e.Property<bool>("Etkin").HasColumnType("INTEGER"); e.Property<bool>("AlarmAcik").HasColumnType("INTEGER");
            e.Property<DateOnly?>("UyariTarihi").HasColumnType("TEXT");
            e.HasOne("Kasa.Api.Data.KanalEntity", null).WithMany().HasForeignKey("KanalId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
        b.Entity("Kasa.Api.Data.KasaKontrolEntity", e =>
        {
            e.ToTable("KasaKontrolleri"); e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER"); e.HasKey("Id");
            e.Property<long>("Kaydedildi").HasColumnType("INTEGER"); e.Property<string>("Not").HasColumnType("TEXT");
            foreach (var name in new[] { "SistemBakiye", "GercekBakiye", "Fark" }) e.Property<decimal>(name).HasColumnType("TEXT");
        });
    }
}
