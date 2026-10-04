using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Kasa.Api;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Api.Denetim;
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
builder.Services.AddSingleton<GuvenlikYedekSeriKilidi>();
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
    KasaVeritabaniBaslatici.Baslat(db, yedekServisi, belgeDeposu, scope.ServiceProvider.GetRequiredService<IDiskAlani>());
    // Yedekten geri yüklenmiş dosya: oturumlar, kurtarma kodu, izleyici girişi ve bildirim kayıtları kapanır, yedekten sonraki
    // güvenlik kararları güvenlik günlüğünden yeniden uygulanır, kimlikler ileri alınır (HTTP açılmadan).
    var guvenlikGunlugu = scope.ServiceProvider.GetRequiredService<GuvenlikGunlugu>();
    GeriYuklemeIsleyici.Isle(db, guvenlikGunlugu);
    guvenlikGunlugu.Hazirla();
    // Operatörün editör şifresi sıfırlaması (Kasa:EditorSifreSifirla): geri yüklemenin kilitlediği girişi de açar. Bayrak kapalıysa
    // hiçbir şey değişmez; aynı ortam şifresiyle ikinci kez uygulanmaz.
    EditorSifreSifirlama.Uygula(db, scope.ServiceProvider.GetRequiredService<IConfiguration>(), guvenlikGunlugu);
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
app.Use(async (http, next) =>
{
    if (IstemciSurumKapisi.YazmaEngellenmeli(http))
    {
        http.Response.StatusCode = StatusCodes.Status409Conflict;
        await http.Response.WriteAsJsonAsync(new { hata = IstemciSurumKapisi.Ileti });
        return;
    }
    await next();
});
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
app.MapEkstreAktarmaEndpoints();
app.MapBildirimEndpoints();
app.MapAliciEndpoints();
app.MapGuvenlikEndpoints();
app.MapBelgeEndpoints();
app.MapBaglanabilirGiderler();
app.MapAlisIncelemeOzeti();
app.MapYonetimEndpoints();

// '/api' finans uçları: her dosya kendi grubunu "Finans" politikasıyla açar (okuma editör ve izleyiciye, yazma yalnız editöre).
app.MapKanalEndpoints();
app.MapKrediKartiEndpoints();
app.MapKrediEndpoints();
app.MapIslemEndpoints();
app.MapKartOdemeEndpoints();
app.MapGelenEndpoints();
app.MapAyarEndpoints();
app.MapRaporEndpoints();

app.Run();

public partial class Program { }
