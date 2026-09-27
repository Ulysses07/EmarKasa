using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Kasa.Api.Auth;

public static class JwtYardimci
{
    /// <summary>Oturumun (JWT ve kasa_auth çerezi) ömrü, gün. Tanıdık cihaz belirteci bundan uzun yaşamalıdır
    /// (<see cref="HizSiniriAyarlari.Hatalar"/>): oturumu dolan kullanıcı yeniden girerken cihazı hâlâ tanınır.</summary>
    public const int OturumGun = 30;

    public static string Uret(string rol, string jwtKey, string oturumDamgasi, int? aliciId = null)
    {
        var anahtar = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var kimlik = new SigningCredentials(anahtar, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim> { new(ClaimTypes.Role, rol), new(OturumDamgasi.ClaimAdi, oturumDamgasi) };
        if (aliciId is { } id) claims.Add(new Claim("alici_id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(OturumGun),
            signingCredentials: kimlik);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
