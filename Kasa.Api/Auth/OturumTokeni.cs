using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Kasa.Api.Data;
using Microsoft.IdentityModel.Tokens;

namespace Kasa.Api.Auth;

/// <summary>Token'daki kişi claim'leri (kişisel hesaplarla eklendi; eski token'larda yok).</summary>
public static class KimlikClaimleri
{
    /// <summary>Kullanıcı Id'si (<see cref="KullaniciEntity.Id"/>).</summary>
    public const string KullaniciId = "kul";
    /// <summary>Kişinin oturum sürümü (<see cref="KullaniciEntity.OturumSurumu"/>); eşleşmeyen token reddedilir.</summary>
    public const string KullaniciSurumu = "kulsv";
    /// <summary>Giriş anındaki ad soyad (geçmişte önbellekteki güncel ad tercih edilir).</summary>
    public const string Ad = "kulad";
    /// <summary>Giriş yapılan cihazın adı (uygulamanın <c>X-Kasa-Cihaz</c> başlığı).</summary>
    public const string Cihaz = "cihaz";
}

/// <summary>Token üretim isteği.</summary>
/// <param name="Surum">sv claim'i: kişisel hesapta genel sürüm (EditorOturumSurumu), ortak izleyicide izleyici sürümü.</param>
public sealed record TokenIstegi(string Rol, int Surum, int? KullaniciId, int? KullaniciSurumu, string? Ad, string? Cihaz, TimeSpan Omur);

public sealed record UretilenToken(string Token, string Jti, DateTime BitisUtc);

/// <summary>
/// Oturum token'ı: rol + oturum sürümü + jti (<see cref="JwtYardimci"/> ile aynı) ve varsa kişi
/// claim'leri. Süre role göre değişir (editör isteğe bağlı 7 gün, izleyici 30 gün).
/// Token'ın bitişi gerçek saatle yazılır: JWT doğrulaması da gerçek saate bakar.
/// </summary>
public static class OturumTokeni
{
    public static UretilenToken Uret(TokenIstegi i, string jwtKey)
    {
        var jti = Guid.NewGuid().ToString("N");
        // exp saniye hassasiyetindedir; kayıttaki bitiş de aynı olsun.
        var simdi = DateTimeOffset.UtcNow;
        var bitis = DateTimeOffset.FromUnixTimeSeconds(simdi.Add(i.Omur).ToUnixTimeSeconds()).UtcDateTime;
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, i.Rol),
            new(JwtYardimci.SurumClaim, i.Surum.ToString()),
            new(JwtRegisteredClaimNames.Jti, jti),
            // Veriliş anı: editör oturum süresi sonradan kısaltılırsa eski token'lar da ona uyar.
            new(JwtRegisteredClaimNames.Iat, simdi.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };
        if (i.KullaniciId is { } id)
        {
            claims.Add(new Claim(KimlikClaimleri.KullaniciId, id.ToString()));
            claims.Add(new Claim(KimlikClaimleri.KullaniciSurumu, (i.KullaniciSurumu ?? 0).ToString()));
        }
        if (!string.IsNullOrWhiteSpace(i.Ad)) claims.Add(new Claim(KimlikClaimleri.Ad, i.Ad));
        if (!string.IsNullOrWhiteSpace(i.Cihaz)) claims.Add(new Claim(KimlikClaimleri.Cihaz, i.Cihaz));

        var anahtar = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var token = new JwtSecurityToken(
            claims: claims,
            expires: bitis,
            signingCredentials: new SigningCredentials(anahtar, SecurityAlgorithms.HmacSha256));
        return new UretilenToken(new JwtSecurityTokenHandler().WriteToken(token), jti, bitis);
    }
}
