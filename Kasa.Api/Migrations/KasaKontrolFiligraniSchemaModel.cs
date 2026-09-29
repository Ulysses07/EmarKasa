using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class KasaKontrolFiligraniSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        EkstreEslesmesiSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.KasaKontrolEntity", e =>
        {
            e.Property<int>("Surum").HasColumnType("INTEGER").IsConcurrencyToken();
            e.Property<DateOnly?>("HesapTarihi").HasColumnType("TEXT");
            e.Property<string>("KanalBakiyeleriJson").HasColumnType("TEXT");
            e.Property<int?>("SonIslemId").HasColumnType("INTEGER");
            e.Property<int?>("SonFinansIstekId").HasColumnType("INTEGER");
            e.Property<int?>("SonDenetimOlayId").HasColumnType("INTEGER");
            e.Property<string>("FarkAciklamasi").HasColumnType("TEXT");
            e.Property<long?>("FarkAciklamaZamani").HasColumnType("INTEGER");
        });
    }
}
