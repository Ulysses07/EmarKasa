using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Kasa.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Denetim;

/// <summary>Yazılacak denetim olayı. Aktör verilmezse isteğin aktörü, istek kimliği verilmezse bağlamın
/// (<see cref="KasaDbContext.DenetimIstekId"/>) kullanılır.</summary>
public sealed record DenetimOlayi(string Tur, string Varlik, string? VarlikId, string? OncekiJson, string? YeniJson,
    string? Gerekce = null, int? KilitAcmaOlayiId = null, DenetimAktoru? Aktor = null, Guid? IstekId = null);

/// <summary>
/// Olayları doğrudan SQL ile ekler: değişiklik izleyiciye girmez (bir sonraki SaveChanges'e karışmaz, yeniden
/// denetlenmez). Bağlamda açık transaction varsa onun içinde yazılır (ana işlemle birlikte kalıcı olur ya da geri alınır);
/// yoksa kendi başına kalıcı olur (ör. başarısız giriş). Tablo henüz yoksa (migration öncesi bağlam) hiçbir şey yazılmaz.
/// </summary>
public static class DenetimYazici
{
    private const int ToplamaBoyu = 200;
    private const string Sutunlar = "\"ZamanUtc\", \"AktorRol\", \"AktorId\", \"IstemciIp\", \"Tur\", \"Varlik\", \"VarlikId\", \"OncekiJson\", \"YeniJson\", \"Gerekce\", \"IstekId\", \"TraceId\", \"KilitAcmaOlayiId\"";

    /// <summary>Olay JSON'u: enum adıyla, Türkçe karakterler kaçışsız, girintisiz.</summary>
    public static readonly JsonSerializerOptions JsonAyarlari = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter(), new SadeOndalik() },
    };

    /// <summary>Tutarlar ölçeksiz yazılır (veritabanından '12500.0' okunan ile istekten gelen 12500 aynı görünür).</summary>
    private sealed class SadeOndalik : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value / 1.0000000000000000000000000000m);
    }

    public static string? Json(object? deger) => deger is null ? null : JsonSerializer.Serialize(deger, JsonAyarlari);

    public static void Yaz(KasaDbContext db, DenetimOlayi olay) => Yaz(db, [olay]);

    public static void Yaz(KasaDbContext db, IReadOnlyList<DenetimOlayi> olaylar)
    {
        if (olaylar.Count == 0 || !TabloVar(db)) return;
        var istekAktoru = DenetimBaglami.Aktor(DenetimBaglami.Istek(db));
        var zaman = db.Saati().GetUtcNow().ToUnixTimeMilliseconds();
        for (var bas = 0; bas < olaylar.Count; bas += ToplamaBoyu)
        {
            var sql = new StringBuilder("INSERT INTO \"DenetimOlaylari\" (").Append(Sutunlar).Append(") VALUES ");
            var parametreler = new List<SqliteParameter>();
            foreach (var (olay, sira) in olaylar.Skip(bas).Take(ToplamaBoyu).Select((o, i) => (o, i)))
            {
                var aktor = olay.Aktor ?? istekAktoru;
                var istekId = olay.IstekId ?? db.DenetimIstekId;
                object?[] degerler =
                [
                    zaman, aktor.Rol, aktor.Id, aktor.Ip, olay.Tur, olay.Varlik, olay.VarlikId, olay.OncekiJson, olay.YeniJson,
                    Kisalt(olay.Gerekce), istekId is { } g && g != Guid.Empty ? g.ToString().ToUpperInvariant() : null, aktor.TraceId, olay.KilitAcmaOlayiId,
                ];
                if (sira > 0) sql.Append(", ");
                sql.Append('(');
                for (var i = 0; i < degerler.Length; i++)
                {
                    var ad = "$d" + sira.ToString(CultureInfo.InvariantCulture) + "_" + i.ToString(CultureInfo.InvariantCulture);
                    if (i > 0) sql.Append(", ");
                    sql.Append(ad);
                    parametreler.Add(new SqliteParameter(ad, degerler[i] ?? DBNull.Value));
                }
                sql.Append(')');
            }
            db.Database.ExecuteSqlRaw(sql.Append(';').ToString(), parametreler);
        }
    }

    /// <summary>Bağlamın bağlandığı veritabanında olay tablosu var mı? Bir kez görülünce bağlam boyunca yeniden sorulmaz.</summary>
    internal static bool TabloVar(KasaDbContext db)
    {
        if (db.DenetimTablosuGoruldu) return true;
        var var = db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='DenetimOlaylari'").Single() > 0;
        db.DenetimTablosuGoruldu = var;
        return var;
    }

    // Uçlar gerekçeyi 2000 karakterle sınırlar; iç yollardan gelen daha uzun metin kırpılır, kayıt reddedilmez.
    private static string? Kisalt(string? gerekce) => gerekce is { Length: > 2000 } ? gerekce[..2000] : gerekce;
}
