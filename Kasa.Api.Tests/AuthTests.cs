using System.Net;
using System.Net.Http.Json;

namespace Kasa.Api.Tests;

public class AuthTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public AuthTests(KasaWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Dogru_editor_giris_200_ve_cookie_doner()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { kullanici = "editor", sifre = "kasa123" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains(resp.Headers.GetValues("Set-Cookie"), v => v.StartsWith("kasa_auth="));
    }

    [Fact]
    public async Task Yanlis_sifre_401_doner()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { kullanici = "editor", sifre = "yanlis" });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Me_giris_yapmadan_401_giris_yapinca_rol_doner()
    {
        var anonim = _factory.CreateClient();
        var anonimResp = await anonim.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonimResp.StatusCode);

        var editor = await _factory.EditorClientAsync();
        var me = await editor.GetFromJsonAsync<RolYanit>("/api/auth/me");
        Assert.Equal("editor", me!.Rol);
    }

    private record RolYanit(string Rol);
}
