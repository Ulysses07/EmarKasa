namespace Kasa.ApiClient.Tests;

public class TokenStoreTests
{
    [Fact]
    public async Task Yaz_oku_temizle_dongusu()
    {
        var store = new BellekTokenStore();
        Assert.Null(await store.OkuAsync());

        await store.YazAsync("abc");
        Assert.Equal("abc", await store.OkuAsync());

        await store.TemizleAsync();
        Assert.Null(await store.OkuAsync());
    }
}
