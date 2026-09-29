using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class GeriYuklemeGuvenligiSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        CekirdekSurumleriSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.SistemDurumuEntity", e =>
        {
            e.ToTable("SistemDurumu");
            e.Property<int>("Id").ValueGeneratedNever().HasColumnType("INTEGER");
            e.HasKey("Id");
            e.Property<string>("OturumDonemi").IsRequired().HasColumnType("TEXT");
            e.Property<DateTimeOffset?>("YedekZamani").HasColumnType("TEXT");
            e.Property<DateTimeOffset?>("SonGeriYukleme").HasColumnType("TEXT");
            e.Property<string>("GeriYuklemeRaporu").HasColumnType("TEXT");
        });
    }
}
