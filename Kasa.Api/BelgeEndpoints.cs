using System.Globalization;
using System.Security.Claims;
using System.Text;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kasa.Api;

/// <summary>Alış belgesi. İlk yedi alan eski istemcilerin okuduğu biçimdir. Yükleyen (gap-denetim-izi-gozlemlenebilirlik-9):
/// rol ('editor'/'alici') ve görünen ad (alıcının adı ya da 'Editör'); bu sürümden önce yüklenenlerde bilinmez (null). Silinen
/// belgeler yalnız editörün <c>?silinenler=true</c> listesinde, silen ve gerekçesiyle görünür.</summary>
public record BelgeDto(int Id, int AlisId, int? OdemeId, string DosyaAdi, string IcerikTuru, long Boyut, DateTimeOffset Yuklendi,
    string? YukleyenRol = null, string? Yukleyen = null, bool Silindi = false, DateTimeOffset? SilinmeZamani = null,
    string? SilenRol = null, string? Silen = null, string? SilmeGerekcesi = null);

/// <summary>Belge silme isteğinin isteğe bağlı gövdesi: gerekçe (editör için zorunlu, en çok 2000 karakter). Gövdesiz istemci
/// gerekçeyi <c>X-Kasa-Gerekce</c> başlığıyla da verebilir.</summary>
public record BelgeSilYaz(string? Gerekce);

public static class BelgeEndpoints
{
    public const int AzamiBoyut = 10 * 1024 * 1024;
    /// <summary>Satırı olan belgenin dosyası belge deposunda yok (alış belgesi ve ekstre PDF'i indirmesi).</summary>
    public const string DosyaYok = "Belge dosyası bulunamadı.";

    /// <summary>Belge olarak kabul edilen türler ve indirmede verilen tek uzantıları.</summary>
    private static readonly Dictionary<string, string> Uzantilar = new(StringComparer.Ordinal)
    {
        ["application/pdf"] = ".pdf",
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
    };
    /// <summary>Yüklemede reddedilen ad uzantıları (addaki son noktadan sonrası, büyük/küçük harf duyarsız): Outlook'un doğrudan
    /// engellediği (Level1) ekler; ayrıca tarayıcıda çalışan web sayfası ve görsel biçimleri, betikler, sürücü ve uygulama
    /// paketleri, kitaplık/arama kısayolları ve disk kalıpları. Liste yalnız derinlemesine savunmadır: asıl koruma indirme adının
    /// uzantısının addan değil, sihirli baytlardan tespit edilen türden kurulmasıdır (<see cref="GuvenliBelgeAdi"/>); listede
    /// olmayan bir uzantı da indirmede .pdf/.png/.jpg olur.</summary>
    private static readonly HashSet<string> TehlikeliUzantilar = new(StringComparer.OrdinalIgnoreCase)
    {
        // Outlook Level1.
        "ade", "adp", "app", "application", "appref-ms", "asp", "aspx", "asx", "bas", "bat", "bgi", "cab", "cer", "chm", "cmd", "cnt",
        "com", "cpl", "crt", "csh", "der", "diagcab", "exe", "fxp", "gadget", "grp", "hlp", "hpj", "hta", "htc", "inf", "ins", "isp",
        "its", "jar", "jnlp", "js", "jse", "ksh", "lnk", "mad", "maf", "mag", "mam", "maq", "mar", "mas", "mat", "mau", "mav", "maw",
        "mcf", "mda", "mdb", "mde", "mdt", "mdw", "mdz", "msc", "msh", "msh1", "msh2", "mshxml", "msh1xml", "msh2xml", "msi", "msp",
        "mst", "msu", "ops", "osd", "pcd", "pif", "pl", "plg", "prf", "prg", "printerexport", "ps1", "ps1xml", "ps2", "ps2xml", "psc1",
        "psc2", "psd1", "psdm1", "pst", "py", "pyc", "pyo", "pyw", "pyz", "pyzw", "reg", "scf", "scr", "sct", "shb", "shs", "theme",
        "tmp", "url", "vb", "vbe", "vbp", "vbs", "vhd", "vhdx", "vsmacros", "vsw", "webpnp", "website", "ws", "wsc", "wsf", "wsh",
        "xbap", "xll", "xnk", "appcontent-ms", "settingcontent-ms",
        // Ek olarak.
        "htm", "html", "xhtml", "shtml", "mht", "mhtml", "svg", "svgz", "xml", "xsl", "xslt", "mjs", "psm1", "sh", "bash", "rb", "php",
        "dll", "ocx", "sys", "drv", "msix", "msixbundle", "appx", "appxbundle", "library-ms", "search-ms", "iso", "img",
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
        api.MapGet("/alis/{id:int}/belgeler", (int id, bool? silinenler, ClaimsPrincipal user, KasaDbContext db) =>
        {
            if (!Sahibi(db, id, user))
                return Results.NotFound();
            // Silinen belgeler yalnız editöre ve istenirse listelenir; alıcı silinmiş belgeyi hiç görmez.
            var hepsi = silinenler == true && user.IsInRole("editor");
            var satirlar = db.Belgeler.AsNoTracking().Where(b => b.AlisId == id && (hepsi || !b.Silindi)).OrderBy(b => b.Id).ToList();
            return Results.Ok(Dtolar(db, satirlar));
        });
        api.MapPost("/alis/{id:int}/belgeler", async (int id, HttpRequest request, ClaimsPrincipal user, KasaDbContext db, TimeProvider saat, IOptionsMonitor<AliciKotaAyarlari> kota, BelgeDeposu depo) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { hata = "Dosyayı form olarak gönderin." });
            if (request.ContentLength is > AzamiBoyut + 64 * 1024)
                return Results.StatusCode(413);
            if (!Sahibi(db, id, user))
                return Results.NotFound();
            var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
            var file = form.Files.GetFile("dosya");
            if (file is null || form.Files.Count != 1 || file.Length <= 0 || file.Length > AzamiBoyut)
                return Results.BadRequest(new { hata = "En fazla 10 MB büyüklüğünde tek PNG, JPEG veya PDF seçin." });
            int? odemeId = null;
            if (form.ContainsKey("odemeId") && !string.IsNullOrWhiteSpace(form["odemeId"]))
            {
                if (!int.TryParse(form["odemeId"], out var value) || value <= 0)
                    return Results.BadRequest(new { hata = "Geçerli bir ödeme seçin." });
                odemeId = value;
            }
            using var content = new MemoryStream();
            await file.CopyToAsync(content, request.HttpContext.RequestAborted);
            var bytes = content.ToArray();
            var type = Tur(bytes);
            if (type is null)
                return Results.BadRequest(new { hata = "Yalnız PNG, JPEG veya PDF belgeleri kabul edilir." });
            if (Uyusmazlik(file.FileName, file.ContentType, type) is { } uyusmazlik)
                return Results.BadRequest(new { hata = uyusmazlik });
            var name = GuvenliBelgeAdi(file.FileName, type);
            var editor = user.IsInRole("editor");
            IResult? Kurallar(DateTimeOffset an)
            {
                if (!Sahibi(db, id, user))
                    return Results.NotFound();
                if (!user.IsInRole("editor") && (odemeId is not null || !db.Alislar.Any(a => a.Id == id && a.Durum == AlisDurumlari.Taslak)))
                    return Results.Conflict(new { hata = "Alıcı yalnız kendi taslağına alış belgesi ekleyebilir." });
                if (odemeId is not null && !db.AlisOdemeler.Any(o => o.Id == odemeId && o.AlisId == id))
                    return Results.BadRequest(new { hata = "Ödeme bu alışa ait değil." });
                if (db.Belgeler.Count(b => b.AlisId == id && !b.Silindi) >= 30)
                    return Results.Conflict(new { hata = "Bir alışa en fazla 30 belge eklenebilir." });
                return AliciKotalari.Belge(db, user, id, bytes.Length, kota.CurrentValue, an);
            }
            // Kurallar önce kilitsiz denetlenir: reddedilecek yükleme belge deposuna dosya bırakmaz. İçerik, yazma kilidi alınmadan
            // depoya yazılır (diske işlenmiş, özeti hesaplanmış); satır ancak ondan sonra eklenir. Satır kaydedilemezse dosya hiçbir
            // kaydın göstermediği dosya olarak kalır ve bakımda silinir.
            if (Kurallar(saat.GetUtcNow()) is { } onHata)
                return onHata;
            var yazim = depo.Yaz(bytes, request.HttpContext.RequestAborted);
            using var tx = db.Database.BeginTransaction();
            // Yetki ve durum dosya okunurken değişmiş olabilir; yazma kilidi altında tekrar kontrol et.
            var simdi = saat.GetUtcNow();
            if (Kurallar(simdi) is { } hata)
                return hata;
            // Yükleyen iz olarak saklanır: rol ve alıcının kimliği (editör paylaşılan tek hesaptır).
            var belge = new BelgeEntity
            {
                AlisId = id,
                OdemeId = odemeId,
                DosyaAdi = name,
                IcerikTuru = type,
                Boyut = bytes.Length,
                Yuklendi = simdi,
                IcerikOzeti = yazim.Ozet,
                YukleyenRol = editor ? "editor" : "alici",
                YukleyenId = editor ? null : AliciKotalari.AliciId(user)
            };
            db.Belgeler.Add(belge);
            db.SaveChanges();
            tx.Commit();
            return Results.Created($"/api/belgeler/{belge.Id}", Dtolar(db, [belge]).Single());
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(AzamiBoyut + 64 * 1024)).RequireRateLimiting(HizSinirlari.AlisYukleme).AddEndpointFilter(AliciAlisYuklemeSiniri.Filtre);

        api.MapGet("/belgeler/{id:int}", (int id, ClaimsPrincipal user, KasaDbContext db, BelgeDeposu depo, HttpResponse response, ILoggerFactory loglar) =>
        {
            var belge = db.Belgeler.AsNoTracking().SingleOrDefault(b => b.Id == id);
            // Silinmiş belgenin içeriği korunur; yalnız editör indirebilir.
            if (belge is null || !Sahibi(db, belge.AlisId, user) || belge.Silindi && !user.IsInRole("editor"))
                return Results.NotFound();
            // İçerik belge deposundan akışla gelir (belleğe alınmaz). Dosya yoksa (ör. geri yüklemede unutulmuş belgeler/ klasörü)
            // sunucu kaydı yazılır, istemci açık bir 404 alır.
            Stream akis;
            try
            { akis = depo.Ac(belge.IcerikOzeti); }
            catch (BelgeDosyasiYokException)
            {
                loglar.CreateLogger("Kasa.Api.BelgeEndpoints").LogError("Belge {Id} dosyası belge deposunda yok ({Ozet}, {Depo}).", id, belge.IcerikOzeti, depo.Kok);
                return Results.NotFound(new { hata = DosyaYok });
            }
            // Her zaman indirme (Content-Disposition: attachment; RFC 6266 filename*): kullanıcı belgesi aynı origin'de
            // çalıştırılamaz. Ad ve uzantı saklanan türden türetilir; izinli türler dışındaki içerik tarayıcıda yorumlanmaz.
            response.Headers.XContentTypeOptions = "nosniff";
            var tur = Uzantilar.ContainsKey(belge.IcerikTuru) ? belge.IcerikTuru : "application/octet-stream";
            return Results.File(akis, tur, GuvenliBelgeAdi(belge.DosyaAdi, belge.IcerikTuru));
        });
        // Silme yumuşaktır (gap-denetim-izi-gozlemlenebilirlik-9): satır ve içerik korunur; silen rol/kimlik, zaman ve gerekçe yazılır,
        // değişiklik denetim izine gerekçesiyle düşer. Editör için gerekçe zorunludur. Alıcı yalnız kendi yüklediği, ödemeye bağlı
        // olmayan ve taslaktaki alışın belgesini kaldırabilir (yükleyeni bilinmeyen eski belgeyi kaldıramaz). Kilitli dönem alışının
        // belgesi kaldırılamaz (AyKilidiKurallari). Silinmiş belge yeniden silinemez (404).
        api.MapDelete("/belgeler/{id:int}", ([FromBody] BelgeSilYaz? girdi, int id, ClaimsPrincipal user, KasaDbContext db, TimeProvider saat, HttpContext http) =>
        {
            var editor = user.IsInRole("editor");
            var gerekce = (girdi?.Gerekce ?? Denetim.DenetimBaglami.IstekGerekcesi(http))?.Trim();
            if (string.IsNullOrEmpty(gerekce))
                gerekce = null;
            if (editor && gerekce is null)
                return Results.BadRequest(new { hata = GerekceGerekli });
            if (gerekce is { Length: > 2000 })
                return Results.BadRequest(new { hata = "Silme gerekçesi en fazla 2000 karakter olabilir." });
            if (GirdiDogrulama.GecersizKarakterIletisi(gerekce) is { } gecersiz)
                return Results.BadRequest(new { hata = $"Silme gerekçesi: {gecersiz}" });
            return AlisEndpoints.Mutate(db, () =>
            {
                var b = db.Belgeler.Find(id);
                if (b is null || b.Silindi || !Sahibi(db, b.AlisId, user))
                    return Results.NotFound();
                var aliciId = editor ? null : AliciKotalari.AliciId(user);
                if (!editor && (b.YukleyenRol != "alici" || b.YukleyenId != aliciId))
                    return Results.Conflict(new { hata = "Alıcı yalnız kendi yüklediği belgeyi kaldırabilir." });
                if (!editor && (b.OdemeId is not null || !db.Alislar.Any(a => a.Id == b.AlisId && a.Durum == AlisDurumlari.Taslak)))
                    return Results.Conflict(new { hata = "Alıcı yalnız kendi taslağındaki, ödemeye bağlı olmayan alış belgesini kaldırabilir." });
                b.Silindi = true;
                b.SilinmeZamani = saat.GetUtcNow();
                b.SilenRol = editor ? "editor" : "alici";
                b.SilenId = aliciId;
                b.SilmeGerekcesi = gerekce;
                using (db.Denetle(gerekce))
                    db.SaveChanges();
                return Results.NoContent();
            });
        });
        return app;
    }

    public const string GerekceGerekli = "Belge silme gerekçesi girin.";

    /// <summary>Satırların yanıtı: ad güvenli biçimde, yükleyen ve silen görünen adlarıyla (alıcı adı ya da 'Editör').</summary>
    private static List<BelgeDto> Dtolar(KasaDbContext db, IReadOnlyList<BelgeEntity> satirlar)
    {
        var aliciIdleri = satirlar.SelectMany(b => new[] { b.YukleyenRol == "alici" ? b.YukleyenId : null, b.SilenRol == "alici" ? b.SilenId : null })
            .OfType<int>().Distinct().ToList();
        var adlar = aliciIdleri.Count == 0 ? [] : db.Alicilar.AsNoTracking().Where(a => aliciIdleri.Contains(a.Id)).ToDictionary(a => a.Id, a => a.Ad);
        string? Ad(string? rol, int? kimlik) => rol switch
        {
            "editor" => "Editör",
            "alici" => kimlik is { } k && adlar.TryGetValue(k, out var ad) ? ad : "Alıcı",
            _ => null,
        };
        // Eski kayıtların adı da okunurken aynı kuralla adlandırılır (veri dönüşümü gerekmez).
        return satirlar.Select(b => new BelgeDto(b.Id, b.AlisId, b.OdemeId, GuvenliBelgeAdi(b.DosyaAdi, b.IcerikTuru), b.IcerikTuru, b.Boyut, b.Yuklendi,
            b.YukleyenRol, Ad(b.YukleyenRol, b.YukleyenId), b.Silindi, b.SilinmeZamani, b.SilenRol, Ad(b.SilenRol, b.SilenId), b.SilmeGerekcesi)).ToList();
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
        if (UzantiBenzeri(govde) is { } son)
            govde = govde[..^son.Length].TrimEnd(' ', '.');
        if (govde.EndsWith(uzanti, StringComparison.OrdinalIgnoreCase) || uzanti == ".jpg" && govde.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            govde = govde[..govde.LastIndexOf('.')].TrimEnd(' ', '.');
        if (govde.Length > 120)
            govde = govde[..(char.IsHighSurrogate(govde[119]) ? 119 : 120)].TrimEnd(' ', '.');
        if (govde.Length == 0)
            return "belge" + uzanti;
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
        if (!TehlikeliUzantilar.Contains(uzanti) && !tehlikeliTur)
            return null;
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
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            { sb.Append(c).Append(s[++i]); continue; }
            if (char.IsSurrogate(c) || char.IsControl(c) || "<>:\"|?*".Contains(c))
                continue;
            if (CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                continue;
            sb.Append(c);
        }
        return sb.ToString().TrimStart(' ').TrimEnd(' ', '.');
    }

    /// <summary>Addaki son uzantı: harf içeren, en çok 8 karakterlik '.xxx'; tarih gibi yalnız rakam içeren son ek uzantı sayılmaz.</summary>
    private static string? UzantiBenzeri(string ad)
    {
        var nokta = ad.LastIndexOf('.');
        if (nokta < 0 || ad.Length - nokta - 1 is < 1 or > 8)
            return null;
        var son = ad[nokta..];
        return son.Skip(1).All(char.IsLetterOrDigit) && son.Skip(1).Any(char.IsLetter) ? son : null;
    }

    private static bool Sahibi(KasaDbContext db, int id, ClaimsPrincipal user)
    {
        if (user.IsInRole("editor"))
            return db.Alislar.Any(a => a.Id == id);
        return int.TryParse(user.FindFirstValue("alici_id"), out var aliciId)
            && db.Alislar.Any(a => a.Id == id && a.AliciId == aliciId);
    }
    private static string? Tur(byte[] b)
    {
        if (b.Length >= 8 && b.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return "image/png";
        if (b.Length >= 3 && b[0] == 255 && b[1] == 216 && b[2] == 255)
            return "image/jpeg";
        if (b.Length >= 5 && b.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            return "application/pdf";
        return null;
    }
}
