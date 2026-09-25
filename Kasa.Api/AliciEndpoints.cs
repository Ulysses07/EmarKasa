using System.Text.RegularExpressions;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public static partial class AliciEndpoints
{
    public static void MapAliciEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/alicilar").RequireAuthorization("Editor");
        api.MapGet("", (KasaDbContext db) => db.Alicilar.AsNoTracking().OrderBy(a => a.Ad)
            .Select(a => new AliciDto(a.Id, a.Kullanici, a.Ad, a.Aktif)).ToList());
        api.MapPost("", (AliciYaz dto, KasaDbContext db, IConfiguration cfg) =>
        {
            if (Dogrula(dto, db, cfg, null) is { } hata) return hata;
            var e = new AliciEntity
            {
                Kullanici = dto.Kullanici.Trim().ToLowerInvariant(), Ad = dto.Ad.Trim(),
                SifreHash = SifreHasher.Hashle(dto.Sifre!), Aktif = dto.Aktif
            };
            db.Alicilar.Add(e);
            db.SaveChanges();
            return Results.Created($"/api/alicilar/{e.Id}", Oku(e));
        });
        api.MapPut("/{id:int}", (int id, AliciYaz dto, KasaDbContext db, IConfiguration cfg) =>
        {
            var e = db.Alicilar.Find(id);
            if (e is null) return Results.NotFound();
            if (Dogrula(dto, db, cfg, id) is { } hata) return hata;
            var oturumlariKapat = e.Aktif != dto.Aktif || e.Kullanici != dto.Kullanici.Trim().ToLowerInvariant();
            e.Kullanici = dto.Kullanici.Trim().ToLowerInvariant();
            e.Ad = dto.Ad.Trim();
            // Pasife alıp tekrar açmak eski oturumu diriltmesin.
            if (!string.IsNullOrEmpty(dto.Sifre)) e.SifreHash = SifreHasher.Hashle(dto.Sifre);
            if (oturumlariKapat) e.OturumSurumu++;
            e.Aktif = dto.Aktif;
            db.SaveChanges();
            return Results.Ok(Oku(e));
        });
    }

    private static AliciDto Oku(AliciEntity e) => new(e.Id, e.Kullanici, e.Ad, e.Aktif);

    private static IResult? Dogrula(AliciYaz dto, KasaDbContext db, IConfiguration cfg, int? id)
    {
        var v = new GirdiDogrulama();
        v.Metin(dto.Kullanici, "kullanici", 64);
        v.Metin(dto.Ad, "ad");
        var kullanici = dto.Kullanici?.Trim().ToLowerInvariant() ?? "";
        v.Kontrol(KullaniciDeseni().IsMatch(kullanici), "kullanici", "Kullanıcı adı 3–64 karakter olmalı; a–z, 0–9, nokta, tire ve alt çizgi kullanılabilir.");
        v.Kontrol(!string.Equals(kullanici, cfg["Kasa:EditorKullanici"], StringComparison.OrdinalIgnoreCase),
            "kullanici", "Editörün kullanıcı adı kullanılamaz.");
        if (id is null || !string.IsNullOrEmpty(dto.Sifre))
            v.Kontrol(!string.IsNullOrWhiteSpace(dto.Sifre) && dto.Sifre.Length is >= 8 and <= 1024,
                "sifre", "Şifre 8–1024 karakter olmalı.");
        if (v.Sonuc() is { } hata) return hata;
        return db.Alicilar.Any(a => a.Id != id && a.Kullanici == kullanici)
            ? Results.Conflict(new { hata = "Bu kullanıcı adı zaten kullanılıyor." }) : null;
    }

    [GeneratedRegex("^[a-z0-9._-]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex KullaniciDeseni();
}
