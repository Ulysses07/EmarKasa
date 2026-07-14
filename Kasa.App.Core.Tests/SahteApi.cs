using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Elle IKasaApi sahtesi — VM testleri için canned yanıt + çağrı kaydı.</summary>
public sealed class SahteApi : IKasaApi
{
    public LoginYanit? LoginYaniti;
    public Exception? LoginHatasi;
    public string? MeRol;
    public Exception? MeHatasi;
    public bool CikisCagrildi;

    public PanelDto? Panel;
    public IReadOnlyList<KrediKartiDto> KrediKartlariListe = new List<KrediKartiDto>();
    public IReadOnlyList<CariDto> CarilerListe = new List<CariDto>();
    public IReadOnlyList<IslemDto> IslemlerListe = new List<IslemDto>();
    public IReadOnlyList<HaftalikOzetDto> HaftalikListe = new List<HaftalikOzetDto>();
    public AylikRaporDto? AylikRapor;
    public int SonAylikYil, SonAylikAy;

    public Task<LoginYanit> LoginAsync(string? kullanici, string sifre)
        => LoginHatasi is not null ? Task.FromException<LoginYanit>(LoginHatasi)
                                   : Task.FromResult(LoginYaniti!);
    public Task<string?> BenKimAsync()
        => MeHatasi is not null ? Task.FromException<string?>(MeHatasi) : Task.FromResult(MeRol);
    public Task CikisAsync() { CikisCagrildi = true; return Task.CompletedTask; }

    public Task<PanelDto> PanelAsync() => Task.FromResult(Panel!);
    public Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync() => Task.FromResult(HaftalikListe);
    public Task<AylikRaporDto> AylikAsync(int yil, int ay) { SonAylikYil = yil; SonAylikAy = ay; return Task.FromResult(AylikRapor!); }
    public Task<IReadOnlyList<DonemDto>> DonemlerAsync() => Task.FromResult<IReadOnlyList<DonemDto>>(new List<DonemDto>());
    public Task<IReadOnlyList<KanalDto>> KanallarAsync() => Task.FromResult<IReadOnlyList<KanalDto>>(new List<KanalDto>());
    public Task<IReadOnlyList<CariDto>> CarilerAsync(string? ara = null) => Task.FromResult(CarilerListe);
    public Task<IReadOnlyList<IslemDto>> IslemlerAsync(DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null) => Task.FromResult(IslemlerListe);
    public Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync() => Task.FromResult(KrediKartlariListe);
    public Task<IReadOnlyList<GelenDto>> GelenlerAsync(DateOnly? donemStart = null) => Task.FromResult<IReadOnlyList<GelenDto>>(new List<GelenDto>());
    public Task<AyarlarDto> AyarlarAsync() => throw new NotImplementedException();
}
