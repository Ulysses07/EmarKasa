using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration modelleri donduruludur; önceki Cekler modeli değişmez.
internal static class IslemTarihIndeksiSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        CeklerSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.IslemEntity", e => e.HasIndex("Tarih", "Id"));
    }
}
