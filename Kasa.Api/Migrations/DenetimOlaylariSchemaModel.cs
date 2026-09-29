using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class DenetimOlaylariSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        AyRaporAnlikGoruntuleriSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.DenetimOlayEntity", e =>
        {
            e.ToTable("DenetimOlaylari");
            e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            e.HasKey("Id");
            e.Property<long>("ZamanUtc").HasColumnType("INTEGER");
            foreach (var p in new[] { "AktorRol", "Tur", "Varlik" })
                e.Property<string>(p).IsRequired().HasColumnType("TEXT");
            foreach (var p in new[] { "IstemciIp", "VarlikId", "OncekiJson", "YeniJson", "Gerekce", "TraceId" })
                e.Property<string>(p).HasColumnType("TEXT");
            e.Property<int?>("AktorId").HasColumnType("INTEGER");
            e.Property<int?>("KilitAcmaOlayiId").HasColumnType("INTEGER");
            e.Property<Guid?>("IstekId").HasColumnType("TEXT");
            e.HasIndex("Varlik", "VarlikId");
            e.HasIndex("ZamanUtc");
            e.HasIndex("KilitAcmaOlayiId");
        });
    }
}
