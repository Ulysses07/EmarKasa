using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Kasa.Api.Auth;

public static class JwtYardimci
{
    public static string Uret(string rol, string jwtKey)
    {
        var anahtar = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var kimlik = new SigningCredentials(anahtar, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: new[] { new Claim(ClaimTypes.Role, rol) },
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: kimlik);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
