namespace Kasa.Api;

public static class SaglikEndpoints
{
    public static WebApplication MapSaglikEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { durum = "ok" }));
        return app;
    }
}
