using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api;

// Satırı kaydetmeden kapatma (yok say) ve hiç işlenmemiş belgenin PDF'ini yeniden okuma (docs/specs/2026-11-25-pdf-ekrani.md).
public static partial class EkstreAktarmaEndpoints
{
    private const string YenidenOkunamaz = "Kaydı (iptal edilmişler dahil) ya da yok sayılan satırı olan belge yeniden okunamaz.";

    private static void MapSatirDurumu(RouteGroupBuilder api)
    {
        // Mali etkisi yoktur: kasa, kart borcu, raporlar değişmez ve ay kilidi aranmaz.
        api.MapPost("/{id:int}/yok-say", (int id, EkstreYokSayYaz dto, KasaDbContext db) => Safe(() => AlisEndpoints.Mutate(db, () => YokSay(db, id, dto))));
        api.MapPost("/{id:int}/yeniden-oku", YenidenOku).RequireRateLimiting("guvenlik");
    }

    private static IResult YokSay(KasaDbContext db, int id, EkstreYokSayYaz dto)
    {
        Require(dto?.SatirNolar is { Count: > 0 and <= 1500 } && dto.SatirNolar.Distinct().Count() == dto.SatirNolar.Count, "Satırları birer kez seçin.");
        var digest = FinansHesaplari.Ozet(new { id, dto.Surum, dto.SatirNolar, dto.YokSay });
        if (FinansHesaplari.Tekrar(db, dto.IstekId, "EkstreYokSay", digest, key => Results.Ok(Document(db, key))) is { } old)
            return old;
        var document = GetDocument(db, id);
        Require(document.Surum == dto.Surum, "Belge değişmiş. Yenileyip yeniden deneyin.", 409);
        var nolar = dto.SatirNolar.ToHashSet();
        Require(nolar.IsSubsetOf(Read<EkstreOkunanSatir>(document.SatirlarJson).Select(r => r.No)), "Kaynak satır bu belgede bulunamadı.");
        var kayitli = db.EkstreKayitlar.AsNoTracking().Where(k => k.BelgeId == id && !k.Iptal).Select(k => k.SatirNo).ToHashSet();
        Require(!nolar.Overlaps(kayitli), $"Satır {nolar.Intersect(kayitli).DefaultIfEmpty().Min()}: Bu satır zaten kaydedilmiş; yok sayılmaz. Önce kaydını iptal edin.", 409);
        var mevcut = db.EkstreYokSayilanlar.Where(y => y.BelgeId == id).ToDictionary(y => y.SatirNo);
        if (dto.YokSay)
            db.EkstreYokSayilanlar.AddRange(nolar.Where(no => !mevcut.ContainsKey(no)).Order().Select(no => new EkstreYokSayilanEntity { BelgeId = id, SatirNo = no }));
        else
            db.EkstreYokSayilanlar.RemoveRange(nolar.Where(mevcut.ContainsKey).Select(no => mevcut[no]));
        document.Surum++;
        FinansHesaplari.IstekKaydet(db, dto.IstekId, "EkstreYokSay", digest, id);
        db.SaveChanges();
        return Results.Ok(Document(db, id));
    }

    private static async Task<IResult> YenidenOku(int id, EkstreYenidenOkuYaz dto, KasaDbContext db, IPdfMetinOkuyucu pdf, BelgeDeposu depo, ILoggerFactory loglar, CancellationToken ct)
    {
        var d = db.EkstreBelgeler.AsNoTracking().SingleOrDefault(d => d.Id == id);
        if (d is null)
            return Error("Belge bulunamadı.", 404);
        if (d.Kaynak == EkstreKaynaklari.Erp)
            return Error("ERP12 belgesi yeniden okunmaz; silip yeniden yükleyebilirsiniz.", 409);
        if (Islenmis(db, id))
            return Error(YenidenOkunamaz, 409);
        byte[] bytes;
        try
        {
            await using var akis = depo.Ac(d.DosyaOzeti);
            using var buffer = new MemoryStream();
            await akis.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }
        catch (BelgeDosyasiYokException)
        {
            loglar.CreateLogger("Kasa.Api.EkstreImportEndpoints").LogError("Ekstre belgesi {Id} dosyası belge deposunda yok ({Ozet}, {Depo}).", id, d.DosyaOzeti, depo.Kok);
            return Results.NotFound(new { hata = BelgeEndpoints.DosyaYok });
        }
        var (read, hata) = await BelgeOku(pdf, bytes, d.Kaynak, d.Banka, ct);
        if (read is null)
            return hata!;
        return Safe(() => AlisEndpoints.Mutate(db, () =>
        {
            var digest = FinansHesaplari.Ozet(new { id, dto.Surum });
            if (FinansHesaplari.Tekrar(db, dto.IstekId, "EkstreYenidenOku", digest, key => Results.Ok(Document(db, key))) is { } old)
                return old;
            var document = GetDocument(db, id);
            Require(document.Surum == dto.Surum, "Belge değişmiş. Yenileyip yeniden deneyin.", 409);
            Require(!Islenmis(db, id), YenidenOkunamaz, 409);
            document.SatirlarJson = read.Satirlar;
            document.UyarilarJson = read.Uyarilar;
            document.AtlananJson = read.Atlananlar;
            document.Surum++;
            FinansHesaplari.IstekKaydet(db, dto.IstekId, "EkstreYenidenOku", digest, id);
            db.SaveChanges();
            return Results.Ok(Document(db, id));
        }));
    }

    /// <summary>Belgenin satır numaralarına bağlı karar var mı (kayıt, iptal edilmiş kayıt, yok sayılan satır).</summary>
    private static bool Islenmis(KasaDbContext db, int id) =>
        db.EkstreKayitlar.AsNoTracking().Any(k => k.BelgeId == id) || db.EkstreYokSayilanlar.AsNoTracking().Any(y => y.BelgeId == id);
}
