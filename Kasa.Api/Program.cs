var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { durum = "ok" }));

app.Run();

// Entegrasyon testlerinin WebApplicationFactory<Program> kullanabilmesi için:
public partial class Program { }
