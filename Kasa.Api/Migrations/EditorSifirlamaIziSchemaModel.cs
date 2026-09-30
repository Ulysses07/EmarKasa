using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class EditorSifirlamaIziSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        GeriYuklemeGuvenligiSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.SistemDurumuEntity", e => e.Property<string>("EditorSifirlamaIzi").HasColumnType("TEXT"));
    }
}
