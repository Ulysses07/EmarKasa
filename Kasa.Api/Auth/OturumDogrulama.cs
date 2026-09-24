using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Kasa.Api.Auth;

/// <summary>
/// Her istekte token'ın hâlâ geçerli olup olmadığına karar verir (JwtBearer OnTokenValidated):
/// <list type="bullet">
/// <item>Oturum sürümü: kişisel hesap token'ı genel sürüme (<see cref="AyarEntity.EditorOturumSurumu"/>)
///       bağlıdır — "Tüm oturumları kapat" herkesi çıkarır, ortak izleyici şifresinin değişmesi kişisel
///       hesapları etkilemez. Ortak izleyici token'ı eskisi gibi izleyici sürümüne bağlıdır.</item>
/// <item>Çıkışta / "Oturumu kapat" ile iptal edilmiş jti reddedilir.</item>
/// <item>Hesap: pasif hesap, kişinin oturum sürümü (şifre değişimi, oturumlarını kapatma) ya da rolü
///       değişmişse reddedilir. Kişi claim'i olmayan eski editör token'ı yerleşik editöre aittir.</item>
/// </list>
/// Hepsi bellekten okunur (istek başına DB sorgusu yok); yalnız "son görülme" birkaç dakikada bir yazılır.
/// </summary>
public static class OturumDogrulama
{
    public static void Dogrula(TokenValidatedContext ctx)
    {
        var sp = ctx.HttpContext.RequestServices;
        var oturum = sp.GetRequiredService<OturumOnbellegi>();
        var db = sp.GetRequiredService<KasaDbContext>();
        var u = ctx.Principal;
        if (u is null) { ctx.Fail("Kimlik yok."); return; }

        var rol = u.FindFirstValue(ClaimTypes.Role);
        var kisisel = u.FindFirstValue(KimlikClaimleri.KullaniciId) is not null;
        var gecerli = oturum.GecerliSurum(db, kisisel ? Roller.Editor : rol);
        if (gecerli is null || u.FindFirstValue(JwtYardimci.SurumClaim) != gecerli.Value.ToString())
        {
            ctx.Fail("Oturum geçersiz kılındı.");
            return;
        }
        if (oturum.IptalMi(db, ctx.SecurityToken?.Id))
        {
            ctx.Fail("Oturum kapatıldı.");
            return;
        }
        var kullanicilar = sp.GetRequiredService<KullaniciOnbellegi>();
        if (HesapHatasi(db, kullanicilar, u, rol) is string hata)
        {
            ctx.Fail(hata);
            return;
        }
        // Editör oturum süresi sonradan kısaltıldıysa daha önce verilmiş token'lar da ona uyar
        // (bu sürümden önceki token'lar dahil: veriliş anları bitişlerinden çıkarılır).
        if (rol == Roller.Editor && VerilisAni(u) is { } verilis
            && DateTimeOffset.UtcNow - verilis > TimeSpan.FromDays(kullanicilar.EditorOturumGun(db)))
        {
            ctx.Fail("Oturum süresi doldu.");
            return;
        }
        sp.GetRequiredService<OturumIzleyici>().Gorundu(ctx.HttpContext, u, ctx.SecurityToken);
    }

    /// <summary>
    /// Token'ın veriliş anı: iat claim'i; bu sürümden önce verilmiş token'larda iat yoktur, ömürleri
    /// sabit <see cref="JwtYardimci.Omur"/> (30 gün) olduğundan bitişten (exp) geri hesaplanır.
    /// </summary>
    public static DateTimeOffset? VerilisAni(ClaimsPrincipal u)
    {
        if (long.TryParse(u.FindFirstValue(JwtRegisteredClaimNames.Iat), out var iat))
            return DateTimeOffset.FromUnixTimeSeconds(iat);
        if (long.TryParse(u.FindFirstValue(JwtRegisteredClaimNames.Exp), out var exp))
            return DateTimeOffset.FromUnixTimeSeconds(exp) - JwtYardimci.Omur;
        return null;
    }

    /// <summary>Token'ın ait olduğu hesap artık bu token'ı kabul etmiyorsa nedeni.</summary>
    public static string? HesapHatasi(KasaDbContext db, KullaniciOnbellegi kullanicilar, ClaimsPrincipal u, string? rol)
    {
        KullaniciOnbellegi.Ozet? k;
        int kisiSurumu;
        var kul = u.FindFirstValue(KimlikClaimleri.KullaniciId);
        if (kul is not null)
        {
            if (!int.TryParse(kul, out var id) || (k = kullanicilar.Bul(db, id)) is null) return "Hesap bulunamadı.";
            kisiSurumu = int.TryParse(u.FindFirstValue(KimlikClaimleri.KullaniciSurumu), out var s) ? s : -1;
        }
        else if (rol == Roller.Editor)
        {
            // Bu sürümden önce verilmiş editör token'ı: yerleşik editörün sürüm 0'ına denk.
            k = kullanicilar.Yerlesik(db);
            if (k is null) return null;
            kisiSurumu = 0;
        }
        else
        {
            return null; // ortak izleyici şifresi
        }
        if (!k.Aktif) return "Hesap pasif.";
        if (k.OturumSurumu != kisiSurumu) return "Oturum geçersiz kılındı.";
        if (k.Rol != rol) return "Hesabın rolü değişti.";
        return null;
    }
}

/// <summary>
/// Açık oturumların "son görülme" zamanını tutar: aynı token için en fazla
/// <see cref="GuvenlikKurallari.SonGorulmeAraligi"/>'nda bir DB'ye yazar. Oturum satırı yoksa (bu
/// sürümden önce verilmiş token) token'daki bilgilerle "eski" satır ekler: açılış anı token'dan
/// (<see cref="OturumDogrulama.VerilisAni"/>), cihaz token'da yoksa isteğin <c>X-Kasa-Cihaz</c>
/// başlığından; cihazı boş satır başlık gelince cihazını alır. Hata isteği bozmaz.
/// </summary>
public sealed class OturumIzleyici(IServiceScopeFactory scopes, TimeProvider saat, ILogger<OturumIzleyici> log)
{
    private readonly ConcurrentDictionary<string, DateTime> _sonYazilan = new();

    /// <summary>Girişte oturum satırı yazıldı: ilk istekte yeniden yazmaya gerek yok.</summary>
    public void Yazildi(string jti, DateTime zamanUtc) => _sonYazilan[jti] = zamanUtc;

    public void Unut(string jti) => _sonYazilan.TryRemove(jti, out _);

    public void Gorundu(HttpContext http, ClaimsPrincipal u, SecurityToken? token)
    {
        var jti = token?.Id;
        if (string.IsNullOrEmpty(jti)) return;
        var simdi = saat.GetUtcNow().UtcDateTime;
        if (_sonYazilan.TryGetValue(jti, out var son) && simdi - son < GuvenlikKurallari.SonGorulmeAraligi && simdi >= son) return;
        _sonYazilan[jti] = simdi;
        if (_sonYazilan.Count > 2000)
            foreach (var (k, v) in _sonYazilan)
                if (simdi - v > GuvenlikKurallari.SonGorulmeAraligi) _sonYazilan.TryRemove(k, out _);
        try
        {
            // Bu sürümden önceki token'da cihaz claim'i yok: yeni uygulama adı her istekte başlıkta gönderir.
            var cihaz = u.FindFirstValue(KimlikClaimleri.Cihaz) ?? CihazAdi.Oku(http.Request);
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var n = db.OturumKayitlari.Where(o => o.Jti == jti)
                .ExecuteUpdate(s => s.SetProperty(o => o.SonGorulmeUtc, simdi).SetProperty(o => o.Cihaz, o => o.Cihaz ?? cihaz));
            if (n > 0) return;
            var verilis = OturumDogrulama.VerilisAni(u)?.UtcDateTime;
            db.OturumKayitlari.Add(new OturumKaydiEntity
            {
                Jti = jti,
                KullaniciId = int.TryParse(u.FindFirstValue(KimlikClaimleri.KullaniciId), out var id) ? id : null,
                AdSoyad = u.FindFirstValue(KimlikClaimleri.Ad),
                Rol = u.FindFirstValue(ClaimTypes.Role) ?? "",
                Cihaz = cihaz,
                Ip = http.Connection.RemoteIpAddress?.ToString(),
                OlusturmaUtc = verilis is { } v && v < simdi ? v : simdi,
                BitisUtc = token!.ValidTo == DateTime.MinValue ? simdi.AddDays(GuvenlikKurallari.VarsayilanOturumGun) : token.ValidTo,
                SonGorulmeUtc = simdi,
                Surum = int.TryParse(u.FindFirstValue(JwtYardimci.SurumClaim), out var sv) ? sv : 0,
                KullaniciSurumu = int.TryParse(u.FindFirstValue(KimlikClaimleri.KullaniciSurumu), out var ksv) ? ksv : null,
                Eski = true,
            });
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            // Aynı token'la eşzamanlı iki ilk istek aynı satırı eklemeye çalışabilir; önemsiz.
            log.LogDebug(ex, "Oturum son görülme zamanı yazılamadı");
        }
    }
}
