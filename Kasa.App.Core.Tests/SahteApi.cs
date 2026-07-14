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
    // Mutasyon çağrı kayıtları (son çağrıyı tutar)
    public KanalYaz? SonKanalOlustur;
    public (int Id, KanalYaz G)? SonKanalGuncelle;
    public int? SonKanalSil;
    public CariYaz? SonCariOlustur;
    public (int Id, CariYaz G)? SonCariGuncelle;
    public int? SonCariSil;
    public IslemYaz? SonIslemOlustur;
    public (int Id, IslemYaz G)? SonIslemGuncelle;
    public int? SonIslemSil;
    public KrediKartiYaz? SonKartOlustur;
    public (int Id, KrediKartiYaz G)? SonKartGuncelle;
    public int? SonKartSil;
    public GelenYaz? SonGelen;
    public AyarYaz? SonAyar;
    public string? SonIzleyiciSifre;

    public Task<KanalDto> KanalOlusturAsync(KanalYaz g) { SonKanalOlustur = g; return Task.FromResult(new KanalDto(0, g.Ad, g.Aktif, g.Sira, g.AcilisDevri)); }
    public Task<KanalDto> KanalGuncelleAsync(int id, KanalYaz g) { SonKanalGuncelle = (id, g); return Task.FromResult(new KanalDto(id, g.Ad, g.Aktif, g.Sira, g.AcilisDevri)); }
    public Task KanalSilAsync(int id) { SonKanalSil = id; return Task.CompletedTask; }
    public Task<CariDto> CariOlusturAsync(CariYaz g) { SonCariOlustur = g; return Task.FromResult(new CariDto(0, g.Ad, g.Aktif)); }
    public Task<CariDto> CariGuncelleAsync(int id, CariYaz g) { SonCariGuncelle = (id, g); return Task.FromResult(new CariDto(id, g.Ad, g.Aktif)); }
    public Task CariSilAsync(int id) { SonCariSil = id; return Task.CompletedTask; }
    public Task<IslemDto> IslemOlusturAsync(IslemYaz g) { SonIslemOlustur = g; return Task.FromResult(new IslemDto(0, g.Tarih, g.Cari, g.TutarTl, g.Kanal, g.Tip, g.Not)); }
    public Task<IslemDto> IslemGuncelleAsync(int id, IslemYaz g) { SonIslemGuncelle = (id, g); return Task.FromResult(new IslemDto(id, g.Tarih, g.Cari, g.TutarTl, g.Kanal, g.Tip, g.Not)); }
    public Task IslemSilAsync(int id) { SonIslemSil = id; return Task.CompletedTask; }
    public Task<KrediKartiDto> KrediKartiOlusturAsync(KrediKartiYaz g) { SonKartOlustur = g; return Task.FromResult(new KrediKartiDto(0, g.Ad, g.KesimTarihi, g.SonOdemeTarihi, g.Limit, g.Borc)); }
    public Task<KrediKartiDto> KrediKartiGuncelleAsync(int id, KrediKartiYaz g) { SonKartGuncelle = (id, g); return Task.FromResult(new KrediKartiDto(id, g.Ad, g.KesimTarihi, g.SonOdemeTarihi, g.Limit, g.Borc)); }
    public Task KrediKartiSilAsync(int id) { SonKartSil = id; return Task.CompletedTask; }
    public Task<GelenDto> GelenKaydetAsync(GelenYaz g) { SonGelen = g; return Task.FromResult(new GelenDto(0, g.DonemStart, g.Kanal, g.TutarTl)); }
    public Task AyarGuncelleAsync(AyarYaz g) { SonAyar = g; return Task.CompletedTask; }
    public Task IzleyiciSifreAsync(string yeniSifre) { SonIzleyiciSifre = yeniSifre; return Task.CompletedTask; }

    public Task<AyarlarDto> AyarlarAsync() => throw new NotImplementedException();
}
