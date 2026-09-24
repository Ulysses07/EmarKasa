using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.GuvenlikUcYardimci;

namespace Kasa.Api;

/// <summary>
/// Giriş (POST /api/auth/login). Sırayla:
/// <list type="number">
/// <item>Kişisel hesap (kullanıcı adı Türkçe büyük/küçük harf duyarsız),</item>
/// <item>.env editörü (kullanıcı adı birebir, eskisi gibi) — uygulamadan şifre değiştirildiyse o şifre,</item>
/// <item>ortak izleyici şifresi (kullanıcı adı ne olursa olsun, eskisi gibi).</item>
/// </list>
/// Hesapta iki adımlı giriş açıksa şifreden sonra 6 haneli kod (ya da bir kurtarma kodu) istenir:
/// kod yoksa 401 + <c>kodGerekli: true</c>. Her deneme giriş günlüğüne yazılır (başarısızlar saatlik
/// tek satırda sayılır, bkz. <see cref="GirisKaydiEntity.Tekrar"/>); başarılı girişte oturum satırı
/// eklenir. Var olan şifreler ve yanıt biçimi (<c>rol</c>, <c>token</c>) değişmedi.
/// </summary>
internal static class GirisIslemi
{
    /// <summary>
    /// İki adımlı kodun doğrulanması ile harcanması (kurtarma kodu listeden düşer, TOTP adımı günlüğe
    /// yazılır) tek adım olsun diye: aynı kodla eşzamanlı iki giriş ikisi birden geçmesin, hatalı kod
    /// sayısı da eşzamanlı denemelerle aşılmasın. Sunucu tek süreçtir (bkz. <see cref="OturumOnbellegi"/>);
    /// kilit yalnız iki adımlı hesapların kod adımında tutulur.
    /// </summary>
    private static readonly Lock KodKilidi = new();

    public static IResult Calistir(LoginDto dto, HttpContext http, KasaDbContext db, IConfiguration cfg,
        OturumOnbellegi oturum, KullaniciOnbellegi kullanicilar, OturumIzleyici izleyici, TimeProvider saat,
        string jwtKey, bool cerezSecure)
    {
        var simdi = saat.GetUtcNow();
        var simdiUtc = simdi.UtcDateTime;
        var ip = http.Connection.RemoteIpAddress?.ToString();
        var cihaz = CihazAdi.Oku(http.Request);
        var yazilan = (dto.Kullanici ?? "").Trim();
        // Günlüğe en fazla geçerli bir kullanıcı adı uzunluğu yazılır (eşleştirme tam adla yapılır).
        var gunlukAdi = yazilan.Length > GuvenlikKurallari.GunlukAdEnCok ? yazilan[..GuvenlikKurallari.GunlukAdEnCok] : yazilan;
        var sifre = dto.Sifre ?? "";

        void Gunluk(bool basarili, string? neden, KullaniciEntity? k, string? rol, long? adim = null)
        {
            if (!basarili && neden != GirisNedenleri.HataliKod && Topla(db, simdiUtc, ip, k?.Id, neden, gunlukAdi, cihaz)) return;
            db.GirisKayitlari.Add(new GirisKaydiEntity
            {
                ZamanUtc = simdiUtc, KullaniciAdi = gunlukAdi, KullaniciId = k?.Id, AdSoyad = k?.AdSoyad, Rol = rol,
                Basarili = basarili, Neden = neden, Ip = ip, Cihaz = cihaz, TotpAdim = adim,
            });
            db.SaveChanges();
        }

        var ayar = db.Ayarlar.AsNoTracking().OrderBy(x => x.Id).First();
        var hesaplar = db.Kullanicilar.AsNoTracking().ToList();
        var envKullanici = cfg["Kasa:EditorKullanici"];
        var envSifre = cfg["Kasa:EditorSifre"];
        // Editör bilgileri config'te tanımlı DEĞİLSE .env editörü girişi kapalıdır
        // (aksi halde eksik config null==null ile şifresiz editör erişimine yol açar).
        var envAcik = !string.IsNullOrEmpty(envKullanici) && !string.IsNullOrEmpty(envSifre);

        KullaniciEntity? hesap = null;
        var sifreDogru = false;
        var envEditoru = false;
        var kisisel = yazilan.Length == 0 ? null
            : hesaplar.FirstOrDefault(k => !k.Yerlesik && Metin.EsitBuyukKucukDuyarsiz.Equals(k.KullaniciAdi, yazilan));
        if (kisisel is not null)
        {
            hesap = kisisel;
            sifreDogru = SifreDogruMu(kisisel, sifre, cfg);
        }
        else if (envAcik && dto.Kullanici == envKullanici)
        {
            envEditoru = true;
            hesap = hesaplar.FirstOrDefault(k => k.Yerlesik);
            sifreDogru = hesap is not null ? SifreDogruMu(hesap, sifre, cfg) : SabitZamanEsitMi(sifre, envSifre!);
        }
        // Hesap bilindiğinde başarısız denemede de rol yazılır (editör hesabına mı, ortağa mı deneniyor).
        var hesapRolu = hesap?.Rol ?? (envEditoru ? Roller.Editor : null);

        if (!(sifreDogru && (hesap is not null || envEditoru)))
        {
            // Ortak izleyici şifresi (eskisi gibi kullanıcı adından bağımsız).
            if (ayar.IzleyiciSifreHash is string h && SifreHasher.Dogrula(sifre, h))
                return Basarili(null, Roller.Izleyici, ayar.IzleyiciOturumSurumu, GirisNedenleri.OrtakSifre, null);
            Gunluk(false, GirisNedenleri.HataliSifre, hesap, hesapRolu);
            return Results.Unauthorized();
        }

        if (hesap is { Aktif: false })
        {
            Gunluk(false, GirisNedenleri.HesapPasif, hesap, hesapRolu);
            return Results.Json(new { hata = "Bu hesap pasif. Editörle görüşün." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (hesap?.TotpSir is null)
            return Basarili(hesap, hesap?.Rol ?? Roller.Editor, ayar.EditorOturumSurumu, null, null);

        // İki adımlı giriş: hesap kilit içinde yeniden okunur (kurtarma kodları ya da ayar başka bir
        // istekte az önce değişmiş olabilir); kod doğrulanıp harcanana kadar kilit bırakılmaz.
        lock (KodKilidi)
        {
            var guncel = db.Kullanicilar.Single(k => k.Id == hesap.Id);
            if (guncel.TotpSir is not string sir)   // bu arada kapatıldı
                return Basarili(guncel, guncel.Rol, ayar.EditorOturumSurumu, null, null);

            var kilitBasi = simdiUtc - GuvenlikKurallari.KodKilitSuresi;
            // Hatalı kod satırları toplanmaz (her biri bir deneme): sayım satır sayısıdır.
            var hataliKod = db.GirisKayitlari.Count(g => g.KullaniciId == guncel.Id && g.ZamanUtc > kilitBasi && g.Neden == GirisNedenleri.HataliKod);
            if (hataliKod >= GuvenlikKurallari.KodKilitDenemesi)
            {
                Gunluk(false, GirisNedenleri.KodKilitli, guncel, guncel.Rol);
                return KodYaniti($"Çok fazla hatalı kod denendi; {GuvenlikKurallari.KodKilitSuresi.TotalMinutes:0} dakika sonra tekrar deneyin.");
            }
            if (string.IsNullOrWhiteSpace(dto.Kod))
            {
                Gunluk(false, GirisNedenleri.KodBekleniyor, guncel, guncel.Rol);
                return KodYaniti("İki adımlı giriş kodunu girin.");
            }
            var sonAdim = db.GirisKayitlari.Where(g => g.KullaniciId == guncel.Id && g.TotpAdim != null).Max(g => g.TotpAdim);
            if (Totp.Dogrula(sir, dto.Kod, simdi, sonAdim) is { } adim)
                return Basarili(guncel, guncel.Rol, ayar.EditorOturumSurumu, null, adim);   // adım günlüğe kilit içinde yazılır

            if (Totp.Normallestir(dto.Kod) is null && KurtarmaKodlari.Kullan(guncel.KurtarmaKodlari, dto.Kod, out var kalan))
            {
                guncel.KurtarmaKodlari = kalan;
                // Henüz token yok: geçmişte kişinin kendisi görünsün.
                db.DegistirenRol = guncel.Rol;
                db.DegistirenAd = guncel.AdSoyad;
                db.DegistirenCihaz = cihaz;
                db.SaveChanges();
                kullanicilar.Gecersiz();
                return Basarili(guncel, guncel.Rol, ayar.EditorOturumSurumu, GirisNedenleri.KurtarmaKodu, null);
            }
            Gunluk(false, GirisNedenleri.HataliKod, guncel, guncel.Rol);
            return KodYaniti("Kod hatalı. Uygulamadaki güncel kodu girin.");
        }

        IResult Basarili(KullaniciEntity? k, string rol, int surum, string? nedenMetni, long? adim)
        {
            oturum.SurumleriAyarla(ayar);
            var gun = rol == Roller.Editor ? kullanicilar.EditorOturumGun(db) : GuvenlikKurallari.IzleyiciOturumGun;
            var t = OturumTokeni.Uret(new TokenIstegi(rol, surum, k?.Id, k?.OturumSurumu, k?.AdSoyad, cihaz, TimeSpan.FromDays(gun)), jwtKey);
            OturumEkle(db, t, k, rol, surum, cihaz, ip, simdiUtc);
            Gunluk(true, nedenMetni, k, rol, adim);
            izleyici.Yazildi(t.Jti, simdiUtc);
            CerezYaz(http, t.Token, t.BitisUtc, cerezSecure);
            return Results.Ok(new GirisYanitiDto(rol, t.Token, k?.AdSoyad, k?.Id));
        }
    }

    /// <summary>
    /// Başarısız denemeyi bu saatin aynı IP + hesap + neden satırına sayar; öyle satır yoksa false
    /// (çağıran yeni satır ekler). Tanınmayan adlar tek hesap sayılır; farklı adlar denendiyse satırdaki
    /// ad "<see cref="GuvenlikKurallari.CesitliAdlar"/>" olur. Böylece giriş yapmadan uydurma adlarla
    /// gönderilen denemeler tabloyu şişiremez (satır sayısı IP × hesap × neden × saat ile sınırlı).
    /// </summary>
    private static bool Topla(KasaDbContext db, DateTime simdiUtc, string? ip, int? kullaniciId, string? neden, string ad, string? cihaz)
    {
        var saatBasi = new DateTime(simdiUtc.Ticks - simdiUtc.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc);
        var id = db.GirisKayitlari.AsNoTracking()
            .Where(g => !g.Basarili && g.ZamanUtc >= saatBasi && g.Ip == ip && g.KullaniciId == kullaniciId && g.Neden == neden)
            .OrderByDescending(g => g.Id).Select(g => (int?)g.Id).FirstOrDefault();
        if (id is null) return false;
        return db.GirisKayitlari.Where(g => g.Id == id).ExecuteUpdate(s => s
            .SetProperty(g => g.Tekrar, g => g.Tekrar + 1)
            .SetProperty(g => g.SonZamanUtc, simdiUtc)
            .SetProperty(g => g.KullaniciAdi, g => g.KullaniciAdi == ad ? g.KullaniciAdi : GuvenlikKurallari.CesitliAdlar)
            .SetProperty(g => g.Cihaz, g => cihaz ?? g.Cihaz)) > 0;
    }

    private static IResult KodYaniti(string hata)
        => Results.Json(new { hata, kodGerekli = true }, statusCode: StatusCodes.Status401Unauthorized);

    /// <summary>Verilen token için oturum satırı (SaveChanges çağıran yapar).</summary>
    public static void OturumEkle(KasaDbContext db, UretilenToken t, KullaniciEntity? k, string rol, int surum,
        string? cihaz, string? ip, DateTime simdiUtc)
        => db.OturumKayitlari.Add(new OturumKaydiEntity
        {
            Jti = t.Jti, KullaniciId = k?.Id, AdSoyad = k?.AdSoyad, Rol = rol, Cihaz = cihaz, Ip = ip,
            OlusturmaUtc = simdiUtc, BitisUtc = t.BitisUtc, SonGorulmeUtc = simdiUtc,
            Surum = surum, KullaniciSurumu = k?.OturumSurumu,
        });
}
