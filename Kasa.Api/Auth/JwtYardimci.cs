using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Kasa.Api.Auth;

public static class JwtYardimci
{
    /// <summary>Oturum sürümü claim'i; AyarEntity'deki sürümle eşleşmeyen token reddedilir.</summary>
    public const string SurumClaim = "sv";

    public static readonly TimeSpan Omur = TimeSpan.FromDays(30);

    /// <summary>
    /// Rol + oturum sürümü + benzersiz kimlik (jti) taşıyan token üretir. jti, çıkışta
    /// yalnız bu token'ı iptal etmek için kullanılır.
    /// </summary>
    public static string Uret(string rol, string jwtKey, int surum = 0)
    {
        var anahtar = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var kimlik = new SigningCredentials(anahtar, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: new[]
            {
                new Claim(ClaimTypes.Role, rol),
                new Claim(SurumClaim, surum.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            },
            expires: DateTime.UtcNow.Add(Omur),
            signingCredentials: kimlik);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
