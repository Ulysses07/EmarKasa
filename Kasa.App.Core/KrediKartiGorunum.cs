using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Kart + görünümde hesaplanan kalan limit (limit − borç, sunucuda saklanmaz).</summary>
public sealed class KrediKartiGorunum
{
    public int Id { get; }
    public string Ad { get; }
    public DateOnly KesimTarihi { get; }
    public DateOnly SonOdemeTarihi { get; }
    public decimal Limit { get; }
    public decimal Borc { get; }
    public decimal KalanLimit => Limit - Borc;

    public KrediKartiGorunum(KrediKartiDto d)
    {
        Id = d.Id; Ad = d.Ad; KesimTarihi = d.KesimTarihi;
        SonOdemeTarihi = d.SonOdemeTarihi; Limit = d.Limit; Borc = d.Borc;
    }
}
