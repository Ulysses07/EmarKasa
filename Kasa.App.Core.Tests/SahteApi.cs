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

    /// <summary>Ayarlanırsa tüm okuma metotları bu istisnayı fırlatır (hata yüzeyi testi).</summary>
    public Exception? YuklemeHatasi;

    public AyarlarDto? AyarlarSonuc;
    public IReadOnlyList<KanalDto> KanallarListe = new List<KanalDto>();

    public PanelDto? Panel;
    public IReadOnlyList<KrediKartiDto> KrediKartlariListe = new List<KrediKartiDto>();
    public IReadOnlyList<KartOdemeDto> KartOdemelerListe = new List<KartOdemeDto>();
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

    public Task<PanelDto> PanelAsync() => YuklemeHatasi is not null ? Task.FromException<PanelDto>(YuklemeHatasi) : Task.FromResult(Panel!);
    public Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync() => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<HaftalikOzetDto>>(YuklemeHatasi) : Task.FromResult(HaftalikListe);
    public Task<AylikRaporDto> AylikAsync(int yil, int ay) { SonAylikYil = yil; SonAylikAy = ay; return YuklemeHatasi is not null ? Task.FromException<AylikRaporDto>(YuklemeHatasi) : Task.FromResult(AylikRapor!); }
    public Task<IReadOnlyList<DonemDto>> DonemlerAsync() => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<DonemDto>>(YuklemeHatasi) : Task.FromResult<IReadOnlyList<DonemDto>>(new List<DonemDto>());
    public Task<IReadOnlyList<KanalDto>> KanallarAsync() => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KanalDto>>(YuklemeHatasi) : Task.FromResult(KanallarListe);
    public Task<IReadOnlyList<CariDto>> CarilerAsync(string? ara = null) => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<CariDto>>(YuklemeHatasi) : Task.FromResult(CarilerListe);
    // İşlem listesi filtre çağrısının son argümanları (filtre testleri için).
    public DateOnly? SonFiltreBaslangic;
    public DateOnly? SonFiltreBitis;
    public string? SonFiltreKanal;
    public string? SonFiltreCari;
    public Task<IReadOnlyList<IslemDto>> IslemlerAsync(DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null)
    {
        SonFiltreBaslangic = baslangic; SonFiltreBitis = bitis; SonFiltreKanal = kanal; SonFiltreCari = cari;
        return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<IslemDto>>(YuklemeHatasi) : Task.FromResult(IslemlerListe);
    }
    public Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync() => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KrediKartiDto>>(YuklemeHatasi) : Task.FromResult(KrediKartlariListe);
    public Task<IReadOnlyList<GelenDto>> GelenlerAsync(DateOnly? donemStart = null) => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<GelenDto>>(YuklemeHatasi) : Task.FromResult<IReadOnlyList<GelenDto>>(new List<GelenDto>());
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
    public int? SonKartOdemelerId;
    public KartOdemeYaz? SonKartOdemeKaydet;
    public int? SonKartOdemeSil;

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

    public Task<IReadOnlyList<KartOdemeDto>> KartOdemelerAsync(int krediKartiId) { SonKartOdemelerId = krediKartiId; return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KartOdemeDto>>(YuklemeHatasi) : Task.FromResult(KartOdemelerListe); }
    public Task<KartOdemeDto> KartOdemeKaydetAsync(KartOdemeYaz g) { SonKartOdemeKaydet = g; return Task.FromResult(new KartOdemeDto(0, g.KrediKartiId, g.Tarih, g.Tutar, g.Not)); }
    public Task KartOdemeSilAsync(int id) { SonKartOdemeSil = id; return Task.CompletedTask; }

    public Task<AyarlarDto> AyarlarAsync() => YuklemeHatasi is not null ? Task.FromException<AyarlarDto>(YuklemeHatasi) : Task.FromResult(AyarlarSonuc!);
}
