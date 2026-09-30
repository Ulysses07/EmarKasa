using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Kasa.Api;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Api.Denetim;
using Kasa.Core;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<KasaDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Kasa") ?? "Data Source=kasa.db"));
// Kasa saati: DI TimeProvider (üretimde sistem saati); istek boyunca parametresiz "bugün" de bu saati okur.
builder.Services.AddKasaSaati();

builder.Services.AddScoped<HesapServisi>();
builder.Services.AddScoped<IslemListeServisi>();
builder.Services.AddSingleton<IPdfMetinOkuyucu, PdfMetinOkuyucu>();
builder.Services.AddKasaBildirimleri();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<VeritabaniHataIsleyici>();
// Belge içerikleri veritabanında değil içerik adresli belge deposunda (Belge:Dizin; varsayılan veritabanı klasörü/belgeler).
builder.Services.AddSingleton<BelgeDeposu>();
builder.Services.AddSingleton<IDiskAlani, DiskAlani>();
builder.Services.AddSingleton<YedekServisi>();
// Veritabanı dışındaki güvenlik günlüğü (yedek dizininde): geri yüklemede yedekten sonraki kararlar buradan yeniden uygulanır.
builder.Services.AddSingleton<GuvenlikGunlugu>();
builder.Services.AddHostedService<OtomatikYedek>();
// Okumalar Sync yapmaz: tarihe bağlı takip türetmesi (kesim ekstreleri) gün dönümünde bakım adımıyla yazılır.
builder.Services.AddHostedService<FinansBakimi>();
// Ters vekil (nginx → docker köprüsü) arkasında gerçek istemci IP'si ve 'guvenlik'/'giris' hız sınırları.
builder.Services.AddKasaVekilVeHizSinirlari();
// Denetim olaylarının aktörü (rol, alıcı kimliği, gerçek istemci IP'si) istekten okunur.
builder.Services.AddKasaDenetim();
builder.Services.AddSingleton<IzleyiciSifreDurumu>();

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var jwtKey = KasaKimlikAyarlari.AnahtariDogrula(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        };
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (!ctx.Request.Headers.ContainsKey("Authorization")
                    && ctx.Request.Cookies.TryGetValue("kasa_auth", out var t))
                    ctx.Token = t;
                return Task.CompletedTask;
            },
            OnTokenValidated = ctx =>
            {
                var db = ctx.HttpContext.RequestServices.GetRequiredService<KasaDbContext>();
                var cfg = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var rol = ctx.Principal?.FindFirstValue(ClaimTypes.Role);
                var damga = ctx.Principal?.FindFirstValue(OturumDamgasi.ClaimAdi);
                int? aliciId = int.TryParse(ctx.Principal?.FindFirstValue("alici_id"), out var id) ? id : null;
                if (!OturumDamgasi.Esit(damga, OturumDamgasi.Uret(rol, cfg, db, aliciId)))
                    ctx.Fail("Oturum geçersiz. Yeniden giriş yapın.");
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Editor", p => p.RequireRole("editor"));
    o.AddPolicy("Finans", p => p.RequireRole("editor", "viewer"));
    o.AddPolicy("Alis", p => p.RequireRole("editor", "alici"));
});

var app = builder.Build();

// --- DB başlat + seed ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
    // Bekleyen migration/veri adımı varsa önce göç öncesi yedek alınır; alınamazsa açılış durur.
    var yedekServisi = scope.ServiceProvider.GetRequiredService<YedekServisi>();
    var belgeDeposu = scope.ServiceProvider.GetRequiredService<BelgeDeposu>();
    // Yedek aynasının temizliği (hiçbir yedeğin göstermediği dosyalar) belge deposunun kendisinde çalışırsa canlı belgeleri silerdi.
    if (string.Equals(Path.TrimEndingDirectorySeparator(yedekServisi.AynaDizini), Path.TrimEndingDirectorySeparator(belgeDeposu.Kok), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Yedek aynası ({yedekServisi.AynaDizini}) belge deposuyla (Belge:Dizin) aynı dizin olamaz. Yedek:Dizin'i veri dizininden ayırın.");
    KasaDatabaseInitializer.Initialize(db, yedekServisi, belgeDeposu, scope.ServiceProvider.GetRequiredService<IDiskAlani>());
    // Yedekten geri yüklenmiş dosya: oturumlar, kurtarma kodu, izleyici girişi ve bildirim kayıtları kapanır, yedekten sonraki
    // güvenlik kararları güvenlik günlüğünden yeniden uygulanır, kimlikler ileri alınır (HTTP açılmadan).
    var guvenlikGunlugu = scope.ServiceProvider.GetRequiredService<GuvenlikGunlugu>();
    GeriYuklemeIsleyici.Isle(db, guvenlikGunlugu);
    guvenlikGunlugu.Hazirla();
    // Kayıtların gösterdiği belge içeriği depoda yoksa (ör. geri yüklemede belgeler/ klasörü unutuldu) her açılışta görünür kılınır;
    // bu belgelerin indirmesi 404 'Belge dosyası bulunamadı.' döner.
    var eksikBelgeler = db.Belgeler.Select(b => b.IcerikOzeti).AsEnumerable().Concat(db.EkstreBelgeler.Select(d => d.DosyaOzeti).AsEnumerable())
        .Distinct(StringComparer.OrdinalIgnoreCase).Count(ozet => !belgeDeposu.Var(ozet));
    if (eksikBelgeler > 0)
        app.Logger.LogError("{Sayi} belge içeriği belge deposunda ({Depo}) bulunamadı; bu belgeler indirilemez. Geri yüklemede belgeler/ klasörü unutulduysa restore_backup.py --belge-aynasi ile açın.", eksikBelgeler, belgeDeposu.Kok);
    if (!db.Kanallar.Any())
    {
        db.Kanallar.AddRange(
            new KanalEntity { Ad = "MEZAT", Sira = 0 },
            new KanalEntity { Ad = "PERAKENDE", Sira = 1 },
            new KanalEntity { Ad = "TOPTAN", Sira = 2 });
    }
    if (!db.Ayarlar.Any())
    {
        db.Ayarlar.Add(new AyarEntity
        {
            TakipBaslangic = db.Bugunu(),
            KasaAcilisDevri = 0m,
            IzleyiciSifreHash = null,
        });
    }
    db.SaveChanges();
    // İlk sürüm kuralıyla (2.1–2.3) yapılmış kart geçişlerinde raporlara girmeyen eski ay sonu
    // düşümleri ve girilen tutarlara göre tahmini kasa farkı otomatik dönüştürülmez (doğru tutar
    // ancak banka/kasa kayıtlarıyla doğrulanabilir); her açılışta görünür kılınır. Aynı uyarı
    // kart ekranında da gösterilir.
    foreach (var kalinti in KartGecisHesabi.IlkSurumKalintilari(db))
        app.Logger.LogWarning("Kart {KartId} ({Kart}) yeni takibe ilk sürüm kuralıyla geçirildi. {Uyari}", kalinti.KartId, kalinti.KartAdi, KartGecisHesabi.Uyari(kalinti));
    // Aynı yaklaşım: ilk kesim gününe sabitlenmiş paralel ekstreler (finance-3) ve gider ekranından takipli karta girilmiş
    // eksi giderler (finance-9) eski kuralla yazılmıştır; otomatik dönüştürülmez, her açılışta görünür kılınır.
    foreach (var uyari in FinansTakipServisi.EskiKuralKalintilari(db))
        app.Logger.LogWarning("{Uyari}", uyari);
}

// İlk sırada: hız sınırı, kimlik doğrulama ve loglar güvenilen vekilin bildirdiği istemci IP'sini görür.
app.UseKasaVekilBasliklari();
app.UseExceptionHandler();
app.Use(async (http, next) =>
{
    http.Response.Headers.XContentTypeOptions = "nosniff";
    http.Response.Headers["Referrer-Policy"] = "same-origin";
    http.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
    if (http.Request.Path.StartsWithSegments("/api"))
    {
        http.Response.Headers.CacheControl = "no-store";
        var unsafeMethod = !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && !HttpMethods.IsOptions(http.Request.Method);
        // Masaüstü Bearer istekleri Origin taşımaz. Tarayıcı mutasyonları aynı
        // kaynaktan ve özel başlıkla gelmelidir; çerez tek başına yeterli değildir.
        if (unsafeMethod && !http.Request.Headers.ContainsKey("Authorization") &&
            (http.Request.Headers.ContainsKey("Origin") || http.Request.Headers.ContainsKey("Sec-Fetch-Site")))
        {
            var origin = http.Request.Headers.Origin.ToString();
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Authority, http.Request.Host.Value, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme is not ("http" or "https")
                || http.Request.Headers["X-Kasa-Request"] != "1"
                || http.Request.Headers["Sec-Fetch-Site"] == "cross-site")
            { http.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
        }
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
});
app.UseAuthentication();
app.UseAuthorization();
// Yetkilendirmeden sonra: kimliksiz istekler editöre özel uçlarda 401 alır, 'guvenlik' kovasını tüketmez.
app.UseRateLimiter();

app.MapSaglikEndpoints();
app.MapOturumEndpoints();

app.MapAlisEndpoints();
app.MapFinansTakipEndpoints();
app.MapBenzerKayitEndpoints();
app.MapAylikGiderEndpoints();
app.MapAyKilidiEndpoints();
app.MapDenetimEndpoints();
app.MapKasaKontrolEndpoints();
app.MapEkstreImportEndpoints();
app.MapBildirimEndpoints();
app.MapAliciEndpoints();
app.MapGuvenlikEndpoints();
app.MapBelgeEndpoints();
app.MapBaglanabilirGiderler();
app.MapAlisIncelemeOzeti();
app.MapYonetimEndpoints();

app.MapKanalEndpoints();
app.MapKrediKartiEndpoints();
app.MapKrediEndpoints();

// Finansal bilgiler yalnız editör ve izleyiciye açıktır.
var api = app.MapGroup("/api").RequireAuthorization("Finans");

// Islemler
api.MapGet("/islemler", (DateOnly? baslangic, DateOnly? bitis, string? kanal, string? cari, IslemListeServisi svc) =>
    svc.Liste(baslangic, bitis, kanal, cari));
api.MapPost("/islemler", (IslemYazDto dto, KasaDbContext db) =>
{
    using var transaction = db.Database.BeginTransaction();
    if (KayitGirdileri.IslemTekrari(dto, db) is { } tekrar)
        return tekrar;
    var (e, hata) = KayitGirdileri.Islem(dto, db);
    if (hata is not null)
        return hata;
    // finance-9: takipli karta eksi/sıfır gider kaynaksız alacak olurdu; iade Kredi Kartları ekranındaki akıştan girilir.
    if (FinansHesaplari.TakipliKartIadeHatasi(dto, db) is { } iade)
        return iade;
    db.Islemler.Add(e);
    db.SaveChanges();
    KayitGirdileri.IslemIstegiKaydet(dto, db, e.Id);
    // Takipli kart giderinin harcaması taksit planıyla hemen yazılır (gap-coklu-giris-cift-sayim-mutabakat-6); taksitsizde Sync'in
    // tek taksitli kaydıyla aynıdır.
    // (Takipli kartta takip başlangıcından önceki gider yukarıda reddedilir.)
    if (e.KrediKartiId is { } kart && db.TakipKartlar.Any(t => t.KrediKartiId == kart))
        FinansTakipServisi.KaynakHarcamaEkle(db, e, dto.TaksitSayisi ?? 1, dto.IlkKesimTarihi);
    FinansTakipServisi.Sync(db);
    transaction.Commit();
    return Results.Created($"/api/islemler/{e.Id}", e);
}).RequireAuthorization("Editor");
api.MapPut("/islemler/{id:int}", (int id, IslemYazDto dto, KasaDbContext db) =>
{
    using var transaction = db.Database.BeginTransaction();
    if (db.HesapHareketler.Any(h => h.IslemId == id) || db.KrediTaksitOdemeler.Any(o => o.IslemId == id))
        return Results.Conflict(new { hata = "Hesap veya krediye bağlı hareket genel gider ekranından değiştirilemez." });
    if (db.AlisOdemeler.Any(o => o.IslemId == id))
        return Results.Conflict(new { hata = "Bu gider bir alışa bağlı. Kanal dağılımını Alışlar ekranından düzenleyin; ödeme tutarı ve tarihi burada değiştirilemez." });
    var e = db.Islemler.Find(id);
    if (e is null)
        return Results.NotFound();
    // contract-6: istemcinin okuduğu gider arada (başka oturum ya da dolaylı yazım) değiştiyse eski değerler geri yazılmaz.
    if (CekirdekSurum.Denetle(dto.Surum, e.Surum, CekirdekSurum.GiderIletisi) is { } eskiSurum)
        return eskiSurum;
    // gap-coklu-giris-cift-sayim-mutabakat-5: takipli kart giderinin kart harcaması ödenmemiş, iadesiz ve ekstreye bağsızsa açıklama,
    // not ve kanal düzeltilir; tarih, tutar ve kart harcamanın taksit planıdır, değişmez.
    var harcama = FinansTakipServisi.KaynakHarcama(db, id);
    if (harcama is not null)
    {
        if (dto.Tarih != e.Tarih || dto.TutarTl != e.TutarTl || dto.KrediKartiId != e.KrediKartiId)
            return Results.Conflict(new { hata = "Kart takibindeki giderin tarihi, tutarı ve kartı değiştirilemez. Kart harcaması ödenmediyse gideri silip doğru bilgilerle yeniden girin; ödendiyse Kredi Kartları ekranında açıklamalı iade girin." });
        if (FinansTakipServisi.KaynakHarcamaEngeli(db, harcama) is { } engel)
            return Results.Conflict(new { hata = KartGideriEngeli(engel, "değiştirilemez") });
    }
    else if (FinansTakipServisi.IslemYonetiliyor(db, e) || (dto.KrediKartiId is { } newCard && db.TakipKartlar.Any(t => t.KrediKartiId == newCard)))
        return Results.Conflict(new { hata = "Kart takibine bağlı hareket için Kredi Kartları ekranından açıklamalı iade/düzeltme girin." });
    var (gelen, hata) = KayitGirdileri.Islem(dto, db, e);
    if (hata is not null)
        return hata;
    gelen.Id = id;
    var kanalDegisti = e.Kanal != gelen.Kanal || e.KanalId != gelen.KanalId;
    db.Entry(e).CurrentValues.SetValues(gelen);
    if (harcama is not null)
    {
        // Kanal değişince harcamanın dondurulmuş payı giderle aynı kuralla yeniden yazılır (ödeme payı yoktur).
        harcama.Aciklama = e.Cari;
        if (kanalDegisti)
            harcama.DagilimJson = FinansTakipServisi.Json(FinansTakipServisi.DonmusPaylar(db, e));
        db.TakipKartlar.Single(t => t.KrediKartiId == harcama.KrediKartiId).Surum++;
    }
    db.SaveChanges();
    transaction.Commit();
    return Results.Ok(e);
}).RequireAuthorization("Editor");
api.MapDelete("/islemler/{id:int}", (int id, KasaDbContext db) =>
{
    using var transaction = db.Database.BeginTransaction();
    if (db.HesapHareketler.Any(h => h.IslemId == id) || db.KrediTaksitOdemeler.Any(o => o.IslemId == id))
        return Results.Conflict(new { hata = "Hesap veya krediye bağlı hareket genel gider ekranından silinemez." });
    if (db.AlisOdemeler.Any(o => o.IslemId == id))
        return Results.Conflict(new { hata = "Alışa bağlı ödeme silinemez; alış ve kasa bağlantısı korunmalıdır." });
    var e = db.Islemler.Find(id);
    if (e is null)
        return Results.NotFound();
    // gap-coklu-giris-cift-sayim-mutabakat-5: ödenmemiş, iadesiz ve ekstreye bağsız kart harcamasının gideri harcamayla birlikte kalkar
    // (harcama iptal edilir, taksitleri borçtan çıkar); diğer takipli kart giderleri için açıklamalı iade yolu kalır.
    var harcama = FinansTakipServisi.KaynakHarcama(db, id);
    if (harcama is not null)
    {
        if (FinansTakipServisi.KaynakHarcamaEngeli(db, harcama) is { } engel)
            return Results.Conflict(new { hata = KartGideriEngeli(engel, "silinemez") });
        harcama.Iptal = true;
        harcama.IslemId = null;
        db.TakipKartlar.Single(t => t.KrediKartiId == harcama.KrediKartiId).Surum++;
        db.SaveChanges();
    }
    else if (FinansTakipServisi.IslemYonetiliyor(db, e))
        return Results.Conflict(new { hata = "Kart takibine bağlı hareket silinemez; açıklamalı iade girin." });
    db.Islemler.Remove(e);
    db.SaveChanges();
    transaction.Commit();
    return Results.NoContent();
}).RequireAuthorization("Editor");
static string KartGideriEngeli(string engel, string islem) => engel switch
{
    "ödendi" => $"Bu kart harcaması ödendi; gideri {islem}: önceki kart ödemesinin kanal payı değişirdi. Harcama gerçekleşmediyse Kredi Kartları ekranında açıklamalı iade girin.",
    "iadesi var" => $"Bu kart harcamasının iadesi var; gideri {islem}. Önce iadeyi Kredi Kartları ekranında gerekçeyle iptal edin.",
    _ => $"Bu kart harcaması bir ekstre satırıyla eşleştirildi; gideri {islem}. Önce PDF İçe Aktarma bölümünden eşleştirmeyi gerekçeyle iptal edin.",
};

// Kart ödemeleri (borç-only; kasa motoruna girmez)
api.MapGet("/kartodemeler", (int? krediKartiId, KasaDbContext db) =>
{
    var q = db.KartOdemeler.AsQueryable();
    if (krediKartiId is { } id)
        q = q.Where(o => o.KrediKartiId == id);
    return q.OrderByDescending(o => o.Tarih).ThenByDescending(o => o.Id).ToList();
});
api.MapPost("/kartodemeler", (KartOdemeYazDto dto, KasaDbContext db) =>
{
    if (db.TakipKartlar.Any(k => k.KrediKartiId == dto.KrediKartiId))
        return Results.Conflict(new { hata = "Yeni takipteki kartın ödemesini Kredi Kartları ekranından kaydedin." });
    var (e, hata) = KayitGirdileri.KartOdeme(dto, db);
    if (hata is not null)
        return hata;
    db.KartOdemeler.Add(e);
    db.SaveChanges();
    return Results.Created($"/api/kartodemeler/{e.Id}", e);
}).RequireAuthorization("Editor");
api.MapDelete("/kartodemeler/{id:int}", (int id, KasaDbContext db) =>
{
    using var transaction = db.Database.BeginTransaction();
    var e = db.KartOdemeler.Find(id);
    if (e is null)
        return Results.NotFound();
    if (db.TakipKartlar.Any(k => k.KrediKartiId == e.KrediKartiId))
        return Results.Conflict(new { hata = "Geçişi yapılmış kartın eski ödemeleri korunur." });
    if (db.HesapHareketler.Any(h => h.KartOdemeId == id))
        return Results.Conflict(new { hata = "Hesaba bağlı kart ödemesi silinemez." });
    db.KartOdemeler.Remove(e);
    db.SaveChanges();
    transaction.Commit();
    return Results.NoContent();
}).RequireAuthorization("Editor");

// Yeni gelirlerde dönem+kanal başına tek satır; eski yinelenen gruplar salt okunur.
api.MapGet("/gelenler", (DateOnly? donemStart, KasaDbContext db) =>
{
    var q = db.Gelenler.AsQueryable();
    if (donemStart is { } d)
        q = q.Where(g => g.DonemStart == d);
    return q.ToList();
});
api.MapPut("/gelenler", (GelenUpsertDto dto, KasaDbContext db) =>
{
    var v = new GirdiDogrulama();
    v.Tarih(dto.DonemStart, "donemStart");
    v.Para(dto.TutarTl, "tutarTl", negatifOlabilir: true);
    var kanal = v.Kanal(db, dto.Kanal, ortakOlabilir: false);
    var takipBaslangic = db.Ayarlar.Select(a => a.TakipBaslangic).First();
    v.Kontrol(dto.DonemStart >= takipBaslangic
              && (dto.DonemStart == takipBaslangic || dto.DonemStart.Day == 1 || dto.DonemStart.DayOfWeek == DayOfWeek.Monday),
        "donemStart", "Gelir için takip başlangıcından itibaren geçerli bir dönem başlangıcı seçin.");
    if (v.Sonuc() is { } hata)
        return hata;
    if (db.Gelenler.Any(g => g.EskiYinelenenGrup && g.DonemStart == dto.DonemStart
        && (g.KanalId == kanal!.Id || g.Kanal == kanal.Ad)))
        return Results.Conflict(new { hata = "Bu dönem ve kanalda birden fazla eski gelir kaydı var. Bütün kayıtlar tutarlarıyla korunur; bu eski grup salt okunurdur. Yeni dönemlere gelir girebilirsiniz." });
    // Tek SQL ifadesi: eşzamanlı ilk girişler çift gelir kaydı üretemez. Ham SQL SaveChanges kancasından geçmez: denetim
    // olayı açıkça ve upsert'le aynı (ertelenmiş) transaction'da yazılır; yazma kilidini autocommit'teki gibi upsert alır.
    // contract-6: sürüm de burada artar. Yeni satır 1 ile eklenir: satırı görmeden (0) kaydeden istemci, arada eklenmiş satırın
    // üzerine yazamaz. Sürüm gönderilmediyse (eski istemci) koşul yoktur; son yazan kazanır.
    var onceki = db.Gelenler.AsNoTracking().SingleOrDefault(g => g.DonemStart == dto.DonemStart && g.KanalId == kanal!.Id && !g.EskiYinelenenGrup);
    using var transaction = KancaDisiOlaylar.ErteliTransaction(db);
    var affected = db.Database.ExecuteSqlInterpolated($"""
        INSERT INTO "Gelenler" ("DonemStart", "Kanal", "KanalId", "TutarTl", "Surum")
        VALUES ({dto.DonemStart}, {kanal!.Ad}, {kanal.Id}, {dto.TutarTl}, 1)
        ON CONFLICT ("DonemStart", "KanalId") WHERE "EskiYinelenenGrup" = 0
        DO UPDATE SET "TutarTl" = excluded."TutarTl", "Kanal" = excluded."Kanal", "Surum" = "Gelenler"."Surum" + 1
        WHERE NOT EXISTS (SELECT 1 FROM "HesapHareketler" h WHERE h."GelenId" = "Gelenler"."Id")
          AND ({dto.Surum} IS NULL OR "Gelenler"."Surum" = {dto.Surum})
        """);
    var e = db.Gelenler.AsNoTracking().Single(g => g.DonemStart == dto.DonemStart && g.KanalId == kanal.Id);
    if (affected == 0)
    {
        if (!db.HesapHareketler.Any(h => h.GelenId == e.Id))
            return Results.Conflict(new { hata = CekirdekSurum.GelenIletisi });
        if (e.TutarTl != dto.TutarTl)
            return Results.Conflict(new { hata = "Hesaba bağlı gelir tutarı buradan değiştirilemez." });
    }
    if (affected > 0)
        KancaDisiOlaylar.GelenUpsert(db, onceki, e);
    transaction.Commit();
    return Results.Ok(e);
}).RequireAuthorization("Editor");

// Ayarlar
api.MapGet("/ayarlar", (KasaDbContext db, ClaimsPrincipal u, IzleyiciSifreDurumu izleyiciSifresi, VekilDurumu vekil) =>
{
    var a = db.Ayarlar.First();
    var editor = u.IsInRole("editor");
    return Results.Ok(new
    {
        a.TakipBaslangic,
        a.KasaAcilisDevri,
        IzleyiciSifreVarMi = a.IzleyiciSifreHash != null,
        // Yalnız editöre: kayıtlı izleyici şifresinin kurala (12+) uymadığı bir izleyici girişinde görüldüyse true
        // (hash uzunluk saklamaz) ve güvenilmeyen kaynaktan X-Forwarded-For geldiyse yanlış vekil ayarı uyarısı.
        IzleyiciSifreKisa = editor && izleyiciSifresi.KisaMi(a.IzleyiciSifreHash),
        VekilUyarisi = editor ? vekil.Uyari : null,
        // contract-6: başlangıç/açılış devri formunun sürümü (PUT /api/ayarlar geri gönderir).
        a.Surum,
    });
});
api.MapPut("/ayarlar", (AyarGuncelleDto dto, KasaDbContext db) =>
{
    using var transaction = db.Database.BeginTransaction();
    var v = new GirdiDogrulama();
    v.Tarih(dto.TakipBaslangic, "takipBaslangic");
    v.Para(dto.KasaAcilisDevri, "kasaAcilisDevri", negatifOlabilir: true);
    if (v.Sonuc() is { } hata)
        return hata;
    var a = db.Ayarlar.First();
    if (CekirdekSurum.Denetle(dto.Surum, a.Surum, CekirdekSurum.AyarIletisi) is { } eskiSurum)
        return eskiSurum;
    // gV5: gider üretmeyen mali kayıtlar da (takipli kart, ekstre geliri, kasa sayımı...) başlangıcı sabitler.
    if (a.TakipBaslangic != dto.TakipBaslangic && FinansHesaplari.IlkMaliKayitTuru(db) is { } kayit)
        return Results.Conflict(new { hata = $"Hareketler kaydedildikten sonra takip başlangıcı değiştirilemez; mevcut dönem bağlantıları korunmalıdır (kayıtlı: {kayit})." });
    a.TakipBaslangic = dto.TakipBaslangic;
    a.KasaAcilisDevri = dto.KasaAcilisDevri;
    db.SaveChanges();
    transaction.Commit();
    return Results.Ok();
}).RequireAuthorization("Editor");
api.MapPut("/ayarlar/izleyici-sifre", (IzleyiciSifreDto dto, KasaDbContext db, GuvenlikGunlugu gunluk) =>
{
    // Kural yalnız belirlerken/değiştirirken uygulanır; mevcut kısa hash ile giriş sürer.
    if (SifreKurallari.YeniSifreHatasi(dto.YeniSifre, "yeniSifre", "İzleyici şifresi") is { } hata)
        return hata;
    var a = db.Ayarlar.First();
    a.IzleyiciSifreHash = SifreHasher.Hashle(dto.YeniSifre);
    db.SaveChanges();
    gunluk.Yaz(GuvenlikGunlugu.IzleyiciSifresiDegisti);
    return Results.Ok();
}).RequireAuthorization("Editor");

// Raporlar (okuma — her iki rol). Salt okunur anlık görüntüde çalışır (yazma kilidi ve Sync yok); istemci isteği
// bırakırsa (RequestAborted) hesap sorgular ve döngüler arasında kesilir, anlık görüntü hemen bırakılır.
api.MapGet("/donemler", (HesapServisi svc, CancellationToken ct) => svc.Donemler(ct));
api.MapGet("/rapor/haftalik", (HesapServisi svc, CancellationToken ct) => svc.Haftalik(ct));
// Kilitli ayın raporu kilitlendiği andaki görüntüden döner ("dondurulmus": true); açık ay canlı hesaplanır.
api.MapGet("/rapor/aylik", (int yil, int ay, HesapServisi svc, CancellationToken ct) =>
    GirdiDogrulama.RaporAyi(yil, ay) ?? Results.Ok(svc.AylikYanit(yil, ay, ct)));
api.MapGet("/rapor/panel", (HesapServisi svc, CancellationToken ct) => svc.Panel(ct));
// Ana sayfanın panel + kasa eşikleri + takip özeti üçlüsü tek istekte, tek anlık görüntüde ve tek hesap bağlamıyla
// (kart verisi ve ödeme etkileri bir kez). Ayrı uçlar geriye uyum için aynen durur.
api.MapGet("/rapor/ana-sayfa", (int? gun, KasaDbContext db, HesapServisi svc, CancellationToken ct) =>
    AnaSayfaOzeti.Oku(db, svc, gun ?? 30, ct));

app.Run();

/// <param name="Surum">contract-6: istemcinin okuduğu ayarların sürümü (GET /api/ayarlar); uyuşmazsa 409. Eski istemci göndermez (null):
/// denetlenmez, son yazan kazanır.</param>
public record AyarGuncelleDto(DateOnly TakipBaslangic, decimal KasaAcilisDevri, int? Surum = null);

public partial class Program { }
