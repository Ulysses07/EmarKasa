using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Elle IKasaApi sahtesi — VM testleri için canned yanıt + çağrı kaydı.</summary>
public sealed class SahteApi : IKasaApi, IOturumBildirimleri
{
    public event EventHandler? OturumSonlandi;
    public void OturumuSonlandir() => OturumSonlandi?.Invoke(this, EventArgs.Empty);
    public void OturumuSonlandir(OturumSonuNedeni neden) => OturumSonlandi?.Invoke(this, new OturumSonlandiEventArgs(neden));
    public Func<int, int, Task<AylikRaporDto>>? AylikGetir;
    public Func<Task<PanelDto>>? PanelGetir;
    public LoginYanit? LoginYaniti;
    public Exception? LoginHatasi;
    public string? MeRol;
    public Exception? MeHatasi;
    public bool CikisCagrildi;

    /// <summary>Ayarlanırsa tüm okuma metotları bu istisnayı fırlatır (hata yüzeyi testi).</summary>
    public Exception? YuklemeHatasi;

    public AyarlarDto? AyarlarSonuc;
    public IReadOnlyList<KanalDto> KanallarListe = new List<KanalDto>();
    public IReadOnlyList<DonemDto> DonemlerListe = new List<DonemDto>();
    // Gelir formu: dönem gelirleri listesi / gecikmeli yanıt kancası / hata ve istenen dönemler.
    public IReadOnlyList<GelenDto> GelenlerListe = new List<GelenDto>();
    public Func<DateOnly?, Task<IReadOnlyList<GelenDto>>>? GelenlerGetir;
    public Exception? GelenlerHatasi;
    public List<DateOnly?> GelenlerIstekleri = new();
    public DateOnly? SonGelenlerDonem => GelenlerIstekleri.LastOrDefault();

    public PanelDto? Panel;
    public IReadOnlyList<KrediKartiDto> KrediKartlariListe = new List<KrediKartiDto>();
    public IReadOnlyList<KartOdemeDto> KartOdemelerListe = new List<KartOdemeDto>();
    public IReadOnlyList<KrediDto> KredilerListe = new List<KrediDto>();
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

    public Task<PanelDto> PanelAsync() => PanelGetir?.Invoke() ?? (YuklemeHatasi is not null ? Task.FromException<PanelDto>(YuklemeHatasi) : Task.FromResult(Panel!));
    public Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync() => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<HaftalikOzetDto>>(YuklemeHatasi) : Task.FromResult(HaftalikListe);
    public Task<AylikRaporDto> AylikAsync(int yil, int ay) { SonAylikYil = yil; SonAylikAy = ay; return AylikGetir?.Invoke(yil, ay) ?? (YuklemeHatasi is not null ? Task.FromException<AylikRaporDto>(YuklemeHatasi) : Task.FromResult(AylikRapor!)); }
    public Task<IReadOnlyList<DonemDto>> DonemlerAsync() => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<DonemDto>>(YuklemeHatasi) : Task.FromResult(DonemlerListe);
    public Task<IReadOnlyList<KanalDto>> KanallarAsync() => KanallarGetir?.Invoke() ?? (YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KanalDto>>(YuklemeHatasi) : Task.FromResult(KanallarListe));
    // İşlem listesi filtre çağrısının son argümanları (filtre testleri için).
    public DateOnly? SonFiltreBaslangic;
    public DateOnly? SonFiltreBitis;
    public string? SonFiltreKanal;
    public string? SonFiltreCari;
    /// <summary>Ayarlanırsa işlem listesi yanıtını verir (baslangic, bitis, kanal): gecikmeli/sırasız yanıt testleri için.</summary>
    public Func<DateOnly?, DateOnly?, string?, Task<IReadOnlyList<IslemDto>>>? IslemlerGetir;
    public int IslemlerCagri;
    public Task<IReadOnlyList<IslemDto>> IslemlerAsync(DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null)
    {
        IslemlerCagri++;
        SonFiltreBaslangic = baslangic; SonFiltreBitis = bitis; SonFiltreKanal = kanal; SonFiltreCari = cari;
        if (IslemlerGetir is not null) return IslemlerGetir(baslangic, bitis, kanal);
        return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<IslemDto>>(YuklemeHatasi) : Task.FromResult(IslemlerListe);
    }
    public Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync() => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KrediKartiDto>>(YuklemeHatasi) : Task.FromResult(KrediKartlariListe);
    public Task<IReadOnlyList<GelenDto>> GelenlerAsync(DateOnly? donemStart = null)
    {
        GelenlerIstekleri.Add(donemStart);
        if ((YuklemeHatasi ?? GelenlerHatasi) is { } hata) return Task.FromException<IReadOnlyList<GelenDto>>(hata);
        return GelenlerGetir?.Invoke(donemStart) ?? Task.FromResult(GelenlerListe);
    }
    // Mutasyon çağrı kayıtları (son çağrıyı tutar)
    public KanalYaz? SonKanalOlustur;
    public (int Id, KanalYaz G)? SonKanalGuncelle;
    public int? SonKanalSil;
    public IslemYaz? SonIslemOlustur;
    public Task<IslemDto>? IslemKayitYaniti;
    public int IslemOlusturCagri;
    public (int Id, IslemYaz G)? SonIslemGuncelle;
    public int? SonIslemSil;
    public KrediKartiYaz? SonKartOlustur;
    public (int Id, KrediKartiYaz G)? SonKartGuncelle;
    public int? SonKartSil;
    public GelenYaz? SonGelen;
    public int GelenKaydetCagri;
    public Exception? GelenKaydetHatasi;
    public AyarYaz? SonAyar;
    public string? SonIzleyiciSifre;
    public int? SonKartOdemelerId;
    public KartOdemeYaz? SonKartOdemeKaydet;
    public int? SonKartOdemeSil;
    public KrediDto? SonKrediEkle;
    public (int Id, KrediDto K)? SonKrediGuncelle;
    public int? SonKrediSil;

    public Task<KanalDto> KanalOlusturAsync(KanalYaz g) { SonKanalOlustur = g; return Task.FromResult(new KanalDto(0, g.Ad, g.Aktif, g.Sira, g.AcilisDevri)); }
    /// <summary>Ayarlanırsa kanal düzenlemesi, gider düzenlemesi ve ayar kaydı bu hatayla biter (ör. contract-6 sürüm çakışması 409).</summary>
    public Exception? KanalGuncelleHatasi, IslemGuncelleHatasi, AyarGuncelleHatasi;
    public Task<KanalDto> KanalGuncelleAsync(int id, KanalYaz g)
    {
        SonKanalGuncelle = (id, g);
        return KanalGuncelleHatasi is { } hata ? Task.FromException<KanalDto>(hata) : Task.FromResult(new KanalDto(id, g.Ad, g.Aktif, g.Sira, g.AcilisDevri, g.Surum + 1));
    }
    public Task KanalSilAsync(int id) { SonKanalSil = id; return Task.CompletedTask; }
    public List<IslemYaz> IslemOlusturmalari = new();
    /// <summary>Ayarlanırsa bir sonraki gider oluşturma bu hatayla biter (bir kez): zaman aşımı sonrası tekrar testleri için.</summary>
    public Exception? IslemOlusturHatasi;
    public Task<IslemDto> IslemOlusturAsync(IslemYaz g)
    {
        SonIslemOlustur = g; IslemOlusturCagri++; IslemOlusturmalari.Add(g);
        if (IslemOlusturHatasi is { } hata) { IslemOlusturHatasi = null; return Task.FromException<IslemDto>(hata); }
        return IslemKayitYaniti ?? Task.FromResult(new IslemDto(0, g.Tarih, g.Cari, g.TutarTl, g.Kanal, g.Tip, g.Not));
    }
    public Task<IslemDto> IslemGuncelleAsync(int id, IslemYaz g)
    {
        SonIslemGuncelle = (id, g);
        return IslemGuncelleHatasi is { } hata ? Task.FromException<IslemDto>(hata) : Task.FromResult(new IslemDto(id, g.Tarih, g.Cari, g.TutarTl, g.Kanal, g.Tip, g.Not, Surum: g.Surum + 1));
    }
    public Task IslemSilAsync(int id) { SonIslemSil = id; return Task.CompletedTask; }
    public Task<KrediKartiDto> KrediKartiOlusturAsync(KrediKartiYaz g) { SonKartOlustur = g; return Task.FromResult(new KrediKartiDto(0, g.Ad, g.KesimTarihi, g.SonOdemeTarihi, g.Limit, g.Borc)); }
    public Task<KrediKartiDto> KrediKartiGuncelleAsync(int id, KrediKartiYaz g) { SonKartGuncelle = (id, g); return Task.FromResult(new KrediKartiDto(id, g.Ad, g.KesimTarihi, g.SonOdemeTarihi, g.Limit, g.Borc)); }
    public Task KrediKartiSilAsync(int id) { SonKartSil = id; return Task.CompletedTask; }
    public Task<IReadOnlyList<KrediDto>> KredilerAsync() => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KrediDto>>(YuklemeHatasi) : Task.FromResult(KredilerListe);
    public Task KrediEkleAsync(KrediDto kredi) { SonKrediEkle = kredi; return Task.CompletedTask; }
    public Task KrediGuncelleAsync(int id, KrediDto kredi) { SonKrediGuncelle = (id, kredi); return Task.CompletedTask; }
    public Task KrediSilAsync(int id) { SonKrediSil = id; return Task.CompletedTask; }
    public Task<GelenDto> GelenKaydetAsync(GelenYaz g) { SonGelen = g; GelenKaydetCagri++; return GelenKaydetHatasi is { } hata ? Task.FromException<GelenDto>(hata) : Task.FromResult(new GelenDto(0, g.DonemStart, g.Kanal, g.TutarTl)); }
    /// <summary>Ayarlanırsa ayar kaydı ve izleyici şifre kaydı bu görevlerle biter (bekleyen kayıt testleri için).</summary>
    public Task? AyarGuncelleYaniti, IzleyiciSifreYaniti;
    /// <summary>Gerçek sunucu gibi kayıt, sonraki ayar okumasına yansır ve sürümü artırır (masaüstü kayıttan sonra ayarları yeniden okur).</summary>
    public Task AyarGuncelleAsync(AyarYaz g)
    {
        SonAyar = g;
        if (AyarGuncelleHatasi is { } hata) return Task.FromException(hata);
        if (AyarlarSonuc is { } a) AyarlarSonuc = a with { TakipBaslangic = g.TakipBaslangic, KasaAcilisDevri = g.KasaAcilisDevri, Surum = a.Surum + 1 };
        return AyarGuncelleYaniti ?? Task.CompletedTask;
    }
    public Task IzleyiciSifreAsync(string yeniSifre) { SonIzleyiciSifre = yeniSifre; return IzleyiciSifreYaniti ?? Task.CompletedTask; }

    public Task<IReadOnlyList<KartOdemeDto>> KartOdemelerAsync(int krediKartiId) { SonKartOdemelerId = krediKartiId; return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KartOdemeDto>>(YuklemeHatasi) : Task.FromResult(KartOdemelerListe); }
    public Task<KartOdemeDto> KartOdemeKaydetAsync(KartOdemeYaz g) { SonKartOdemeKaydet = g; return Task.FromResult(new KartOdemeDto(0, g.KrediKartiId, g.Tarih, g.Tutar, g.Not)); }
    public Task KartOdemeSilAsync(int id) { SonKartOdemeSil = id; return Task.CompletedTask; }

    public Task<AyarlarDto> AyarlarAsync() => YuklemeHatasi is not null ? Task.FromException<AyarlarDto>(YuklemeHatasi) : Task.FromResult(AyarlarSonuc!);

    /// <summary>Ayarlanırsa kanal listesi yanıtını verir (eski tam yüklemenin geç kaynakları testleri için).</summary>
    public Func<Task<IReadOnlyList<KanalDto>>>? KanallarGetir;

    // Ana sayfa özeti: kanca yoksa panel (Panel / PanelGetir / YuklemeHatasi) ile ayarlanan eşik ve takip özeti döner.
    public Func<int, CancellationToken, Task<AnaSayfaDto>>? AnaSayfaGetir;
    public List<int> AnaSayfaIstekleri = new();
    public IReadOnlyList<KasaEsikDto>? AnaSayfaEsikleri;
    public TakipOzetDto? AnaSayfaTakipOzeti;
    public async Task<AnaSayfaDto> AnaSayfaAsync(int gun = 30, CancellationToken ct = default)
    {
        AnaSayfaIstekleri.Add(gun);
        if (AnaSayfaGetir is not null) return await AnaSayfaGetir(gun, ct);
        return new(await PanelAsync(), AnaSayfaEsikleri, AnaSayfaTakipOzeti);
    }

    /// <summary>Ayarlanırsa iptal edilebilir haftalık rapor yanıtını verir; yoksa <see cref="HaftalikListe"/>.</summary>
    public Func<CancellationToken, Task<IReadOnlyList<HaftalikOzetDto>>>? HaftalikGetir;
    public Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync(CancellationToken ct) => HaftalikGetir?.Invoke(ct) ?? HaftalikAsync();
}
