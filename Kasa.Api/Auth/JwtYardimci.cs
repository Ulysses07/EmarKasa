using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Kasa.Api.Auth;

public static class JwtYardimci
{
    public static string Uret(string rol, string jwtKey, string oturumDamgasi, int? aliciId = null)
    {
        var anahtar = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var kimlik = new SigningCredentials(anahtar, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim> { new(ClaimTypes.Role, rol), new(OturumDamgasi.ClaimAdi, oturumDamgasi) };
        if (aliciId is { } id) claims.Add(new Claim("alici_id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: kimlik);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
