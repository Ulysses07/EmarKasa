using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Endpoints;

/// <summary>
/// Paket D — kasa sayımının devamı (özellik 35): fark durumu/açıklaması, "Neden değişti?" ve son
/// sayım tarihi (7 gün hatırlatması için). Sayım satırları POST /api/kasasayimlari'dadır. Hiçbiri
/// kasa hesabını değiştirmez.
/// </summary>
public static class KasaSayimEndpoints
{
    /// <summary>Kasa sayımını etkileyebilecek kayıt türleri.</summary>
    private static readonly string[] EtkileyenTurler =
    [
        GecmisTurleri.Islem, GecmisTurleri.Gelen, GecmisTurleri.Cek, GecmisTurleri.KartOdemesi,
        GecmisTurleri.Kanal, GecmisTurleri.Ayar,
    ];

    public static RouteGroupBuilder MapKasaSayimEkleri(this RouteGroupBuilder api, YazIslemi yaz)
    {
        // Son sayım (tarihe göre). Hiç sayım yoksa alanlar null.
        api.MapGet("/kasasayimlari/son", (KasaDbContext db, TimeProvider saat) =>
        {
            var s = db.KasaSayimlari.AsNoTracking().OrderByDescending(x => x.Tarih).ThenByDescending(x => x.Id).FirstOrDefault();
            return new SonSayimDto(s?.Tarih, s is null ? null : Saat.Bugun(saat).DayNumber - s.Tarih.DayNumber, s?.Id);
        });

        api.MapPut("/kasasayimlari/{id:int}/fark", (int id, SayimFarkYazDto dto, KasaDbContext db, HesapServisi svc) =>
            yaz(db, "Sayım kaydedilemedi; tekrar deneyin.", () =>
        {
            var s = db.KasaSayimlari.Find(id);
            if (s is null) return Results.NotFound();
            if (!Enum.IsDefined(dto.Durum)) return Yanit.Hata("Geçersiz fark durumu.");
            if (s.SayilanTutar == s.HesaplananTutar) return Yanit.Hata("Bu sayımda fark yok.");
            var aciklama = string.IsNullOrWhiteSpace(dto.Aciklama) ? null : dto.Aciklama.Trim();
            if (aciklama is { Length: > 1000 }) return Yanit.Hata("Açıklama en fazla 1000 karakter olabilir.");
            if (dto.Durum == SayimFarkDurumu.Aciklandi && aciklama is null) return Yanit.Hata("Farkı açıklamak için bir açıklama yazın.");
            s.FarkDurumu = dto.Durum;
            s.FarkAciklamasi = aciklama;
            db.SaveChanges();
            var guncel = svc.KasaTarihlerde([s.Tarih]);
            return Results.Ok(KasaSayimDto.Olustur(s, guncel.TryGetValue(s.Tarih, out var g) ? g : null));
        })).RequireAuthorization("Editor");

        // "Neden değişti?": sayım kaydedildikten sonra yazılmış, tarihi sayım gününe eşit ya da
        // önce olan kayıtların geçmiş satırları (eski sırayla).
        api.MapGet("/kasasayimlari/{id:int}/nedendegisti", (int id, KasaDbContext db, HesapServisi svc) =>
        {
            var s = db.KasaSayimlari.AsNoTracking().FirstOrDefault(x => x.Id == id);
            if (s is null) return Results.NotFound();
            // Sayımın kendi ekleme satırı: aynı saniyedeki sonraki satırlar da "sonra" sayılsın.
            var sinirId = db.Degisiklikler.AsNoTracking()
                .Where(d => d.Tur == GecmisTurleri.KasaSayimi && d.KayitId == s.Id && d.Eylem != Eylemler.Silindi && d.Eylem != Eylemler.Guncellendi)
                .OrderBy(d => d.Id).Select(d => (int?)d.Id).FirstOrDefault();
            var kayitZamani = s.KayitZamaniUtc;
            var adaylar = db.Degisiklikler.AsNoTracking()
                .Where(d => EtkileyenTurler.Contains(d.Tur) && (d.ZamanUtc > kayitZamani || (sinirId != null && d.Id > sinirId)))
                .OrderBy(d => d.Id)
                .ToList();
            var liste = adaylar.Where(d => SayimiEtkiler(d, s.Tarih))
                .Select(d => new SayimDegisikligiDto(d.Id, DateTime.SpecifyKind(d.ZamanUtc, DateTimeKind.Utc), d.Rol, d.Tur, d.Eylem, d.Ozet))
                .ToList();
            var guncel = svc.KasaTarihlerde([s.Tarih]);
            decimal? g = guncel.TryGetValue(s.Tarih, out var v) ? v : null;
            return Results.Ok(new NedenDegistiDto(s.Id, s.Tarih, s.HesaplananTutar, g, g - s.HesaplananTutar, liste));
        });
        return api;
    }

    /// <summary>
    /// Geçmiş satırı sayım gününün defter kasasını etkileyebilir mi: işlem/kart ödemesi tarihi, gelen
    /// dönem başı, çek işlem (tahsil/ödeme) tarihi eski ya da yeni halinde sayım gününe eşit/önce;
    /// kanal ve ayar satırlarında açılış devri (ayarda takip başlangıcı da) değiştiyse her gün etkilenir.
    /// </summary>
    internal static bool SayimiEtkiler(DegisiklikEntity d, DateOnly sayimTarihi)
    {
        try
        {
            var eski = Coz(d.EskiJson);
            var yeni = Coz(d.YeniJson);
            switch (d.Tur)
            {
                case GecmisTurleri.Kanal:
                    return Degisti(eski, yeni, "acilisDevri");
                case GecmisTurleri.Ayar:
                    return Degisti(eski, yeni, "kasaAcilisDevri") || Degisti(eski, yeni, "takipBaslangic");
            }
            var alan = d.Tur switch
            {
                GecmisTurleri.Gelen => "donemStart",
                GecmisTurleri.Cek => "islemTarihi",
                _ => "tarih",
            };
            return Tarihler(eski, alan).Concat(Tarihler(yeni, alan)).Any(t => t <= sayimTarihi);
        }
        catch (JsonException) { return false; }
    }

    private static JsonElement? Coz(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static IEnumerable<DateOnly> Tarihler(JsonElement? kok, string alan)
    {
        if (kok is not { } k) yield break;
        var ogeler = k.ValueKind == JsonValueKind.Array ? k.EnumerateArray().ToList() : [k];
        foreach (var o in ogeler)
        {
            if (o.ValueKind != JsonValueKind.Object) continue;
            // Toplu satırlar PascalCase de yazabilir (anonim nesne): büyük/küçük harf duyarsız ara.
            foreach (var p in o.EnumerateObject())
                if (string.Equals(p.Name, alan, StringComparison.OrdinalIgnoreCase)
                    && p.Value.ValueKind == JsonValueKind.String
                    && DateOnly.TryParseExact(p.Value.GetString(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var t))
                    yield return t;
        }
    }

    private static bool Degisti(JsonElement? eski, JsonElement? yeni, string alan)
    {
        string? Deger(JsonElement? e) => e is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(alan, out var v) ? v.GetRawText() : null;
        var a = Deger(eski); var b = Deger(yeni);
        if (a is null && b is null) return false;
        if (a is not null && b is not null && decimal.TryParse(a, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var x)
            && decimal.TryParse(b, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var y))
            return x != y;
        return a != b;
    }
}
