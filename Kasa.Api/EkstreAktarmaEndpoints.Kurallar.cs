using Kasa.Api.Data;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api;

public static partial class EkstreAktarmaEndpoints
{
    private static void MapKurallar(RouteGroupBuilder api)
    {
        api.MapGet("/kurallar", (KasaDbContext db) => Safe(() => Results.Ok(db.EkstreKurallar.AsNoTracking().OrderBy(k => k.Id).ToList().Select(EkstreKuralServisi.Dto).ToList())));
        api.MapPost("/kurallar", (EkstreKuralYaz dto, KasaDbContext db) => Safe(() => AlisEndpoints.Mutate(db, () => KuralYaz(db, null, dto))));
        api.MapPut("/kurallar/{id:int}", (int id, EkstreKuralYaz dto, KasaDbContext db) => Safe(() => AlisEndpoints.Mutate(db, () => KuralYaz(db, id, dto))));
        api.MapDelete("/kurallar/{id:int}", (int id, int surum, KasaDbContext db) => Safe(() => AlisEndpoints.Mutate(db, () =>
        {
            var k = db.EkstreKurallar.SingleOrDefault(k => k.Id == id);
            Require(k is not null, "Kural bulunamadı.", 404);
            Require(k.Surum == surum, "Kural değişmiş. Listeyi yenileyip yeniden deneyin.", 409);
            db.EkstreKurallar.Remove(k);
            db.SaveChanges();
            return Results.NoContent();
        })));
        api.MapGet("/{id:int}/oneriler", (int id, KasaDbContext db) => Safe(() => AlisEndpoints.Oku(db, () => Results.Ok(EkstreKuralServisi.Oneriler(db, GetDocument(db, id))))));
        api.MapGet("/hafiza", (KasaDbContext db) => Safe(() => AlisEndpoints.Oku(db, () => Results.Ok(EkstreHafizaServisi.Liste(db)))));
        api.MapPost("/hafiza/unut", (EkstreHafizaUnutYaz dto, KasaDbContext db) => Safe(() => AlisEndpoints.Mutate(db, () => HafizaUnut(db, dto))));
    }

    /// <summary>Hafızadaki anahtarı unutur (mali etkisi yoktur; kayıtlar ve raporlar değişmez).</summary>
    private static IResult HafizaUnut(KasaDbContext db, EkstreHafizaUnutYaz dto)
    {
        Require(dto is { Kaynak: EkstreKaynaklari.Banka or EkstreKaynaklari.Kart, Yon: "Giris" or "Cikis" or "Belirsiz", Anahtar.Length: > 0 and <= 2000 }, "Geçerli hafıza kaydını seçin.");
        var digest = FinansHesaplari.Ozet(new { dto.Kaynak, dto.Anahtar, dto.Yon });
        if (FinansHesaplari.Tekrar(db, dto.IstekId, "EkstreHafizaUnut", digest, _ => Results.NoContent()) is { } repeat)
            return repeat;
        Require(EkstreHafizaServisi.Kararlar(db).Any(k => k.Kaynak == dto.Kaynak && k.Anahtar == dto.Anahtar && k.Yon == dto.Yon), "Bu kayıt hafızada yok; listeyi yenileyin.", 404);
        EkstreHafizaServisi.Unut(db, dto);
        FinansHesaplari.IstekKaydet(db, dto.IstekId, "EkstreHafizaUnut", digest, 0);
        db.SaveChanges();
        return Results.NoContent();
    }

    private static IResult KuralYaz(KasaDbContext db, int? id, EkstreKuralYaz dto)
    {
        Require(dto is not null, "Kural bilgilerini girin.");
        var digest = FinansHesaplari.Ozet(new { id, dto.Surum, dto.Ad, dto.Kaynak, dto.Banka, dto.AciklamaIcerir, dto.Yon, dto.IslemTuru, dto.DagilimTuru, dto.KanalIds, dto.Aktif });
        if (FinansHesaplari.Tekrar(db, dto.IstekId, "EkstreKural", digest, key =>
        {
            var existing = db.EkstreKurallar.AsNoTracking().SingleOrDefault(k => k.Id == key);
            Require(existing is not null, "Bu isteğin kuralı silinmiş; listeyi yenileyin.", 409);
            return Results.Ok(EkstreKuralServisi.Dto(existing));
        }) is { } repeat)
            return repeat;
        var v = new GirdiDogrulama();
        v.Metin(dto.Ad, nameof(dto.Ad), 100);
        v.Metin(dto.AciklamaIcerir, nameof(dto.AciklamaIcerir), 200);
        if (v.Sonuc() is { } error)
            return error;
        Require(EkstreKuralServisi.Normalize(dto.AciklamaIcerir).Split(' ').Any(w => w.Length >= 3), "Açıklama koşulunda en az üç harf/rakamlı bir sözcük bulunmalı.");
        Require(dto.Kaynak is EkstreKaynaklari.Banka or EkstreKaynaklari.Kart, "Belge türünü seçin.");
        Require(dto.Banka == null || Bankalar.Any(b => b.Kod == dto.Banka), "Geçerli banka seçin.");
        Require(dto.Yon is null or "Giris" or "Cikis", "Geçerli giriş/çıkış yönü seçin.");
        Require(dto.IslemTuru == EkstreIslemTurleri.Atla || (dto.Kaynak == EkstreKaynaklari.Banka
            ? dto.IslemTuru is EkstreIslemTurleri.Gelir or EkstreIslemTurleri.Gider : dto.IslemTuru == EkstreIslemTurleri.KartHarcama), "Kural banka gelir/gideri, kart harcaması veya Atla önerebilir. Ödeme/iade ve mevcut kayıt eşleşmesini satırda seçin.");
        Require(dto.Yon == null || dto.IslemTuru == EkstreIslemTurleri.Atla || dto.Yon == (dto.IslemTuru == EkstreIslemTurleri.Gelir ? "Giris" : "Cikis"), "Kuralın işlem türü ve yönü çelişiyor.");
        Require(dto.KanalIds is not null && dto.KanalIds.Count <= 20 && dto.KanalIds.Distinct().Count() == dto.KanalIds.Count, "En fazla 20 farklı kanal seçin.");
        Require(dto.DagilimTuru == DagilimBicimleri.Genel && dto.KanalIds.Count == 0 && (dto.IslemTuru == EkstreIslemTurleri.Atla || dto.Kaynak == EkstreKaynaklari.Banka)
            || dto.IslemTuru != EkstreIslemTurleri.Atla && dto.DagilimTuru == DagilimBicimleri.Esit && dto.KanalIds.Count > 0, "Dağılım genel kasa veya seçilen kanallara eşit olmalı.");
        var k = id is { } value ? db.EkstreKurallar.SingleOrDefault(k => k.Id == value) : new EkstreKuralEntity();
        Require(k is not null, "Kural bulunamadı.", 404);
        // Kullanılamayan hedefi olan mevcut kural da kapatılabilmeli; yeni/değişmiş hedefler ve etkinleştirme aktif kanal ister.
        var hedefleriKoruyarakKapat = id is not null && !dto.Aktif && dto.KanalIds.Order().SequenceEqual(Read<int>(k.KanalIdsJson).Order());
        Require(hedefleriKoruyarakKapat || db.Kanallar.Count(c => c.Aktif && dto.KanalIds.Contains(c.Id)) == dto.KanalIds.Count, "Kural için aktif kanalları seçin.");
        if (id is not null)
        {
            Require(dto.Surum == k.Surum, "Kural değişmiş. Listeyi yenileyip yeniden deneyin.", 409);
            k.Surum++;
        }
        else
        {
            Require(dto.Surum == 0, "Yeni kuralın sürümü sıfır olmalı.");
            Require(db.EkstreKurallar.Count() < 200, "En fazla 200 kural saklanabilir; kullanılmayanları silin.");
            db.EkstreKurallar.Add(k);
        }
        k.Ad = dto.Ad.Trim();
        k.Kaynak = dto.Kaynak;
        k.Banka = dto.Banka;
        k.AciklamaIcerir = dto.AciklamaIcerir.Trim();
        k.Yon = dto.Yon;
        k.IslemTuru = dto.IslemTuru;
        k.DagilimTuru = dto.DagilimTuru;
        k.KanalIdsJson = Json(dto.KanalIds.Order().ToList());
        k.Aktif = dto.Aktif;
        db.SaveChanges();
        FinansHesaplari.IstekKaydet(db, dto.IstekId, "EkstreKural", digest, k.Id);
        db.SaveChanges();
        return Results.Ok(EkstreKuralServisi.Dto(k));
    }
}
