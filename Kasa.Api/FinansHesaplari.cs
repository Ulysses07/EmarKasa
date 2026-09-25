using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public static class FinansHesaplari
{
    public static string Ozet(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    public static void IstekKaydet(KasaDbContext db, Guid id, string tur, string ozet, int sonucId, string? onceki = null) =>
        db.FinansIstekler.Add(new FinansIstekEntity { IstekId = id, Tur = tur, Ozet = ozet, SonucId = sonucId, OncekiJson = onceki });

    public static IResult? Tekrar(KasaDbContext db, Guid id, string tur, string ozet, Func<int, IResult> result)
    {
        if (id == Guid.Empty) return Results.ValidationProblem(new Dictionary<string, string[]> { ["istekId"] = ["Geçerli bir istek kimliği gerekir."] });
        var old = db.FinansIstekler.AsNoTracking().SingleOrDefault(x => x.IstekId == id);
        return old is null ? null : old.Tur == tur && old.Ozet == ozet ? result(old.SonucId) : AlisEndpoints.Conflict("İstek kimliği başka bir işlem veya farklı içerik için kullanılmış.");
    }

    public static IReadOnlyList<Gelen> EkGelirler(KasaDbContext db, IReadOnlyList<Donem> donemler) => db.HesapHareketler.AsNoTracking().Include(h => h.Kanal)
        .Where(h => h.IslemId == null && h.GelenId == null && h.KartOdemeId == null && h.KrediId == null).ToList()
        .Select(h => (Hareket: h, Donem: donemler.FirstOrDefault(d => d.Icerir(h.Tarih))))
        .Where(x => x.Donem is not null).Select(x => new Gelen(x.Donem!.Start, x.Hareket.Kanal!.Ad, x.Hareket.Tutar)).ToList();
}
