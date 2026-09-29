using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class CekirdekSurumleriSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        KasaKontrolFiligraniSchemaModel.Build(b);
        foreach (var varlik in new[] { "IslemEntity", "GelenEntity", "KanalEntity", "AyarEntity" })
            b.Entity("Kasa.Api.Data." + varlik, e => e.Property<int>("Surum").HasColumnType("INTEGER").IsConcurrencyToken());
    }
}
