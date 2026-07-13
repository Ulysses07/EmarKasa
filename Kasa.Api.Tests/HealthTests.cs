using Microsoft.AspNetCore.Mvc.Testing;

namespace Kasa.Api.Tests;

public class HealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public HealthTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Health_ok_doner()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        Assert.True(resp.IsSuccessStatusCode);
    }
}
