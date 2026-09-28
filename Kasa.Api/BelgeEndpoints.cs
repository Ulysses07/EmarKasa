using System.Globalization;
using System.Security.Claims;
using System.Text;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kasa.Api;

public record BelgeDto(int Id, int AlisId, int? OdemeId, string DosyaAdi, string IcerikTuru, long Boyut, DateTimeOffset Yuklendi);

public static class BelgeEndpoints
{
    public const int AzamiBoyut = 10 * 1024 * 1024;

    /// <summary>Belge olarak kabul edilen türler ve indirmede verilen tek uzantıları.</summary>
    private static readonly Dictionary<string, string> Uzantilar = new(StringComparer.Ordinal)
    {
        ["application/pdf"] = ".pdf", ["image/png"] = ".png", ["image/jpeg"] = ".jpg",
    };
    /// <summary>Yüklemede reddedilen ad uzantıları (addaki son noktadan sonrası, büyük/küçük harf duyarsız): Windows'ta
    /// çalıştırılabilir, betik, kısayol, yükleyici ya da disk kalıbı; tarayıcıda çalışan web sayfası ve görsel biçimleri.</summary>
    private static readonly HashSet<string> TehlikeliUzantilar = new(StringComparer.OrdinalIgnoreCase)
    {
        "hta", "htm", "html", "xhtml", "shtml", "mht", "mhtml", "svg", "svgz", "xml", "xsl", "xslt",
        "js", "jse", "mjs", "vbs", "vbe", "wsf", "wsh", "wsc", "sct", "ps1", "psm1", "psd1", "ps1xml", "psc1", "sh", "bash", "py", "pyw", "pl", "rb", "php",
        "cmd", "bat", "com", "exe", "scr", "pif", "cpl", "dll", "ocx", "sys", "drv", "msi", "msp", "mst", "msc", "msix", "msixbundle", "appx", "appxbundle",
        "lnk", "url", "website", "scf", "reg", "inf", "ins", "isp", "chm", "hlp", "jar", "jnlp", "application", "appref-ms", "gadget", "xbap", "xll",
        "settingcontent-ms", "library-ms", "search-ms", "diagcab", "iso", "img", "vhd", "vhdx",
    };
    /// <summary>Yüklemede reddedilen bildirilen içerik türleri; ayrıca 'html' ya da 'script' içeren ve '+xml' ile biten her tür.</summary>
    private static readonly HashSet<string> TehlikeliTurler = new(StringComparer.Ordinal)
    {
        "application/hta", "application/xml", "text/xml", "application/x-msdownload", "application/x-msdos-program", "application/x-dosexec",
        "application/vnd.microsoft.portable-executable", "application/x-ms-installer", "application/x-msi", "application/x-bat", "application/bat",
        "application/x-sh", "application/x-ms-shortcut", "application/x-ms-application", "application/java-archive", "application/x-java-jnlp-file",
        "application/internet-shortcut", "application/x-url", "message/rfc822", "multipart/related",
    };
    private static readonly HashSet<string> AyrilmisAdlar = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³",
    };

    public static WebApplication MapBelgeEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization("Alis");
        api.MapGet("/alis/{id:int}/belgeler", (int id, ClaimsPrincipal user, KasaDbContext db) =>
        {
            if (!Sahibi(db, id, user)) return Results.NotFound();
            // Eski kayıtların adı da okunurken aynı kuralla adlandırılır (veri dönüşümü gerekmez).
            return Results.Ok(db.Belgeler.AsNoTracking().Where(b => b.AlisId == id).OrderBy(b => b.Id)
                .Select(b => new { b.Id, b.AlisId, b.OdemeId, b.DosyaAdi, b.IcerikTuru, b.Boyut, b.Yuklendi }).AsEnumerable()
                .Select(b => new BelgeDto(b.Id, b.AlisId, b.OdemeId, GuvenliBelgeAdi(b.DosyaAdi, b.IcerikTuru), b.IcerikTuru, b.Boyut, b.Yuklendi)).ToList());
        });
        api.MapPost("/alis/{id:int}/belgeler", async (int id, HttpRequest request, ClaimsPrincipal user, KasaDbContext db, TimeProvider saat, IOptionsMonitor<AliciKotaAyarlari> kota) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { hata = "Dosyayı form olarak gönderin." });
            if (request.ContentLength is > AzamiBoyut + 64 * 1024) return Results.StatusCode(413);
            if (!Sahibi(db, id, user)) return Results.NotFound();
            var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
            var file = form.Files.GetFile("dosya");
            if (file is null || form.Files.Count != 1 || file.Length <= 0 || file.Length > AzamiBoyut)
                return Results.BadRequest(new { hata = "En fazla 10 MB büyüklüğünde tek PNG, JPEG veya PDF seçin." });
            int? odemeId = null;
            if (form.ContainsKey("odemeId") && !string.IsNullOrWhiteSpace(form["odemeId"]))
            {
                if (!int.TryParse(form["odemeId"], out var value) || value <= 0) return Results.BadRequest(new { hata = "Geçerli bir ödeme seçin." });
                odemeId = value;
            }
            using var content = new MemoryStream();
            await file.CopyToAsync(content, request.HttpContext.RequestAborted);
            var bytes = content.ToArray();
            var type = Tur(bytes);
            if (type is null) return Results.BadRequest(new { hata = "Yalnız PNG, JPEG veya PDF belgeleri kabul edilir." });
            if (Uyusmazlik(file.FileName, file.ContentType, type) is { } uyusmazlik) return Results.BadRequest(new { hata = uyusmazlik });
            var name = GuvenliBelgeAdi(file.FileName, type);
            using var tx = db.Database.BeginTransaction();
            // Yetki ve durum dosya okunurken değişmiş olabilir; yazma kilidi altında tekrar kontrol et.
            if (!Sahibi(db, id, user)) return Results.NotFound();
            if (!user.IsInRole("editor") && (odemeId is not null || !db.Alislar.Any(a => a.Id == id && a.Durum == AlisDurumlari.Taslak)))
                return Results.Conflict(new { hata = "Alıcı yalnız kendi taslağına alış belgesi ekleyebilir." });
            if (odemeId is not null && !db.AlisOdemeler.Any(o => o.Id == odemeId && o.AlisId == id))
                return Results.BadRequest(new { hata = "Ödeme bu alışa ait değil." });
            if (db.Belgeler.Count(b => b.AlisId == id) >= 30) return Results.Conflict(new { hata = "Bir alışa en fazla 30 belge eklenebilir." });
            var simdi = saat.GetUtcNow();
            if (AliciKotalari.Belge(db, user, id, bytes.Length, kota.CurrentValue, simdi) is { } kotaHatasi) return kotaHatasi;
            var belge = new BelgeEntity { AlisId = id, OdemeId = odemeId, DosyaAdi = name, IcerikTuru = type, Boyut = bytes.Length, Yuklendi = simdi, Icerik = bytes };
            db.Belgeler.Add(belge); db.SaveChanges(); tx.Commit();
            return Results.Created($"/api/belgeler/{belge.Id}", new BelgeDto(belge.Id, id, odemeId, name, type, bytes.Length, belge.Yuklendi));
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(AzamiBoyut + 64 * 1024)).RequireRateLimiting(HizSinirlari.AlisYukleme);

        api.MapGet("/belgeler/{id:int}", (int id, ClaimsPrincipal user, KasaDbContext db, HttpResponse response) =>
        {
            var parent = db.Belgeler.Where(b => b.Id == id).Select(b => (int?)b.AlisId).FirstOrDefault();
            if (parent is null || !Sahibi(db, parent.Value, user)) return Results.NotFound();
            var belge = db.Belgeler.AsNoTracking().Single(b => b.Id == id);
            // Her zaman indirme (Content-Disposition: attachment; RFC 6266 filename*): kullanıcı belgesi aynı origin'de
            // çalıştırılamaz. Ad ve uzantı saklanan türden türetilir; izinli türler dışındaki içerik tarayıcıda yorumlanmaz.
            response.Headers.XContentTypeOptions = "nosniff";
            var tur = Uzantilar.ContainsKey(belge.IcerikTuru) ? belge.IcerikTuru : "application/octet-stream";
            return Results.File(belge.Icerik, tur, GuvenliBelgeAdi(belge.DosyaAdi, belge.IcerikTuru));
        });
        api.MapDelete("/belgeler/{id:int}", (int id, ClaimsPrincipal user, KasaDbContext db) =>
        {
            using var tx = db.Database.BeginTransaction();
            var b = db.Belgeler.Find(id);
            if (b is null || !Sahibi(db, b.AlisId, user)) return Results.NotFound();
            if (!user.IsInRole("editor") && (b.OdemeId is not null || !db.Alislar.Any(a => a.Id == b.AlisId && a.Durum == AlisDurumlari.Taslak)))
                return Results.Conflict(new { hata = "Alıcı yalnız kendi taslağındaki alış belgesini kaldırabilir." });
            db.Belgeler.Remove(b); db.SaveChanges(); tx.Commit();
            return Results.NoContent();
        });
        return app;
    }

    /// <summary>
    /// Güvenli belge adı (purchase-1, apiclient-3): yol parçaları; kontrol, Unicode biçim (U+202E gibi yön işaretleri, sıfır
    /// genişlikli karakterler), satır/paragraf ayırıcı ve eşi olmayan vekil karakterleri; Windows'ta geçersiz karakterler
    /// atılır. Son uzantı (harf içeren, en çok 8 karakterlik) ve onun önünde kalan türün kendi uzantısı çıkarılır; gövde en
    /// çok 120 karakterdir. Uzantı YALNIZ içerik türünden gelir (.pdf/.png/.jpg; izinli olmayan türde .bin): istemcinin
    /// verdiği .hta/.cmd/.html gibi uzantı hiçbir zaman indirme adına geçmez. Windows ayrılmış adları (CON, NUL, COM1...)
    /// 'belge-' önekiyle, boş ad 'belge' olarak döner.
    /// </summary>
    public static string GuvenliBelgeAdi(string? ad, string icerikTuru)
    {
        var uzanti = Uzantilar.GetValueOrDefault(icerikTuru, ".bin");
        var govde = Temizle(ad);
        if (UzantiBenzeri(govde) is { } son) govde = govde[..^son.Length].TrimEnd(' ', '.');
        if (govde.EndsWith(uzanti, StringComparison.OrdinalIgnoreCase) || uzanti == ".jpg" && govde.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            govde = govde[..govde.LastIndexOf('.')].TrimEnd(' ', '.');
        if (govde.Length > 120) govde = govde[..(char.IsHighSurrogate(govde[119]) ? 119 : 120)].TrimEnd(' ', '.');
        if (govde.Length == 0) return "belge" + uzanti;
        return (AyrilmisAdlar.Contains(govde.Split('.')[0].TrimEnd(' ')) ? "belge-" + govde : govde) + uzanti;
    }

    /// <summary>
    /// Yüklemede reddedilen uyuşmazlık: tür yalnız sihirli baytlardan belirlenir ve indirme adı o türden kurulur, bu yüzden
    /// adın uzantısı ve tarayıcının bildirdiği tür güvenlik için gerekmez. Yalnız çalıştırılabilir dosya, betik, kısayol ya da web
    /// sayfası olarak işaretlenmiş çok biçimli dosya (ör. '%PDF-' ile başlayan .hta, text/html) hiç saklanmaz. Uzantısız ya da
    /// noktalı ad ('Fatura No.A12'), beyaz liste dışı tür (image/x-png, application/vnd.pdf) ve başka belge uzantısı meşrudur.
    /// </summary>
    private static string? Uyusmazlik(string? ad, string? bildirilen, string tur)
    {
        var temiz = Temizle(ad);
        var nokta = temiz.LastIndexOf('.');
        var uzanti = nokta < 0 ? "" : temiz[(nokta + 1)..];
        var bildirilenTur = (bildirilen ?? "").Split(';')[0].Trim().ToLowerInvariant();
        var tehlikeliTur = TehlikeliTurler.Contains(bildirilenTur) || bildirilenTur.Contains("html", StringComparison.Ordinal)
            || bildirilenTur.Contains("script", StringComparison.Ordinal) || bildirilenTur.EndsWith("+xml", StringComparison.Ordinal);
        if (!TehlikeliUzantilar.Contains(uzanti) && !tehlikeliTur) return null;
        var adi = tur switch { "application/pdf" => "PDF", "image/png" => "PNG", _ => "JPEG" };
        return $"Dosyanın uzantısı veya türü içeriğiyle ({adi}) uyuşmuyor: dosya çalıştırılabilir, betik ya da web sayfası olarak işaretlenmiş. Yalnız gerçek PDF, PNG ya da JPEG belgesi yükleyin; dosyayı .pdf, .png veya .jpg uzantısıyla kaydedip yeniden seçin.";
    }

    /// <summary>Yol ve yasak karakterlerden arınmış; baştaki boşlukları, sondaki boşluk ve noktaları kırpılmış ad.</summary>
    private static string Temizle(string? ad)
    {
        var s = (ad ?? "").Replace('\\', '/');
        s = s[(s.LastIndexOf('/') + 1)..];
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[++i]); continue; }
            if (char.IsSurrogate(c) || char.IsControl(c) || "<>:\"|?*".Contains(c)) continue;
            if (CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) continue;
            sb.Append(c);
        }
        return sb.ToString().TrimStart(' ').TrimEnd(' ', '.');
    }

    /// <summary>Addaki son uzantı: harf içeren, en çok 8 karakterlik '.xxx'; tarih gibi yalnız rakam içeren son ek uzantı sayılmaz.</summary>
    private static string? UzantiBenzeri(string ad)
    {
        var nokta = ad.LastIndexOf('.');
        if (nokta < 0 || ad.Length - nokta - 1 is < 1 or > 8) return null;
        var son = ad[nokta..];
        return son.Skip(1).All(char.IsLetterOrDigit) && son.Skip(1).Any(char.IsLetter) ? son : null;
    }

    private static bool Sahibi(KasaDbContext db, int id, ClaimsPrincipal user)
    {
        if (user.IsInRole("editor")) return db.Alislar.Any(a => a.Id == id);
        return int.TryParse(user.FindFirstValue("alici_id"), out var aliciId)
            && db.Alislar.Any(a => a.Id == id && a.AliciId == aliciId);
    }
    private static string? Tur(byte[] b)
    {
        if (b.Length >= 8 && b.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (b.Length >= 3 && b[0] == 255 && b[1] == 216 && b[2] == 255) return "image/jpeg";
        if (b.Length >= 5 && b.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) return "application/pdf";
        return null;
    }
}
