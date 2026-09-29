using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class KartTakipDuzeltmeleriSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        AyKanalKumeleriSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.TakipIadeHesabiEntity", e =>
        {
            e.ToTable("TakipIadeHesaplari"); e.Property<int>("HarcamaId").ValueGeneratedNever().HasColumnType("INTEGER"); e.HasKey("HarcamaId");
            foreach (var p in new[] { "IadeAnindaOdenen", "KasadaSayilanDuzeltme" }) e.Property<decimal>(p).HasColumnType("TEXT");
            e.HasOne("Kasa.Api.Data.TakipHarcamaEntity", null).WithMany().HasForeignKey("HarcamaId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
        b.Entity("Kasa.Api.Data.TakipAvansTahsisEntity", e =>
        {
            e.ToTable("TakipAvansTahsisleri"); e.Property<int>("OdemeId").ValueGeneratedNever().HasColumnType("INTEGER"); e.HasKey("OdemeId");
            e.Property<int>("KaynakOdemeId").HasColumnType("INTEGER");
            e.HasIndex("KaynakOdemeId");
            e.HasOne("Kasa.Api.Data.TakipKartOdemeEntity", null).WithMany().HasForeignKey("OdemeId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            e.HasOne("Kasa.Api.Data.TakipKartOdemeEntity", null).WithMany().HasForeignKey("KaynakOdemeId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
    }
}
