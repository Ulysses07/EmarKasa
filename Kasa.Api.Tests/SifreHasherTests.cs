using Kasa.Api.Auth;

namespace Kasa.Api.Tests;

public class SifreHasherTests
{
    [Fact]
    public void Dogru_sifre_dogrulanir_yanlis_reddedilir()
    {
        var hash = SifreHasher.Hashle("gizli123");

        Assert.True(SifreHasher.Dogrula("gizli123", hash));
        Assert.False(SifreHasher.Dogrula("yanlis", hash));
    }

    [Fact]
    public void Ayni_sifre_farkli_salt_ile_farkli_hash_uretir()
    {
        var h1 = SifreHasher.Hashle("aynisifre");
        var h2 = SifreHasher.Hashle("aynisifre");

        Assert.NotEqual(h1, h2);
        Assert.True(SifreHasher.Dogrula("aynisifre", h1));
        Assert.True(SifreHasher.Dogrula("aynisifre", h2));
    }

    [Fact]
    public void Bozuk_hash_dogrulamada_false_doner()
    {
        Assert.False(SifreHasher.Dogrula("x", "bozuk-veri"));
    }
}
