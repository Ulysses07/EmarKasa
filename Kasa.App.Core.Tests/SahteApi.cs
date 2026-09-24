using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Elle IKasaApi sahtesi — VM testleri için canned yanıt + çağrı kaydı.</summary>
public sealed partial class SahteApi : IKasaApi
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
    // İşlem kaydı carinin kayıtlı olmasını ister: editör testlerinin kullandığı adlar varsayılan olarak kayıtlı.
    public IReadOnlyList<CariDto> CarilerListe = new List<CariDto>
    {
        new(901, "MEZAT alış", true), new(902, "Market", true), new(903, "K.K", true), new(904, "a", true),
    };
    // Sabit gider işleminin adı kayıtlı bir kalem olmalı: testlerin kullandığı adlar varsayılan olarak kayıtlı.
    public IReadOnlyList<GiderKalemiDto> GiderKalemleriListe = new List<GiderKalemiDto>
    {
        new(801, "SGK", true), new(802, "Kira", true), new(803, "Market", true), new(804, "a", true),
    };
    public int GiderKalemleriCagri;
    public GiderKalemiYaz? SonKalemOlustur;
    public (int Id, GiderKalemiYaz G)? SonKalemGuncelle;
    public int? SonKalemSil;
    public Task<IReadOnlyList<GiderKalemiDto>> GiderKalemleriAsync()
    {
        GiderKalemleriCagri++;
        return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<GiderKalemiDto>>(YuklemeHatasi) : Task.FromResult(GiderKalemleriListe);
    }
    public Task<GiderKalemiDto> GiderKalemiOlusturAsync(GiderKalemiYaz g)
    {
        SonKalemOlustur = g;
        var yeni = new GiderKalemiDto(700 + GiderKalemleriListe.Count, g.Ad, g.Aktif);
        GiderKalemleriListe = GiderKalemleriListe.Append(yeni).ToList();
        return Task.FromResult(yeni);
    }
    public Task<GiderKalemiDto> GiderKalemiGuncelleAsync(int id, GiderKalemiYaz g) { SonKalemGuncelle = (id, g); return Task.FromResult(new GiderKalemiDto(id, g.Ad, g.Aktif)); }
    public Task GiderKalemiSilAsync(int id) { SonKalemSil = id; return Task.CompletedTask; }

    // Tekrarlayan giderler: sunucu gibi davranır (onay/atla bekleyenden düşer, silme kayıtla bekleyenini kaldırır).
    public IReadOnlyList<TekrarlayanGiderDto> TekrarlayanListe = new List<TekrarlayanGiderDto>();
    public IReadOnlyList<BekleyenGiderDto> BekleyenListe = new List<BekleyenGiderDto>();
    public int TekrarlayanCagri, BekleyenCagri;
    public TekrarlayanGiderYaz? SonTekrarOlustur;
    public (int Id, TekrarlayanGiderYaz G)? SonTekrarGuncelle;
    public int? SonTekrarSil;
    public (int Id, TekrarlayanOnayYaz G)? SonOnay;
    public (int Id, DateOnly Ay)? SonAtla;
    /// <summary>Ayarlanırsa tekrarlayan yazma çağrıları (kaydet/sil/onayla/atla) bu istisnayı fırlatır.</summary>
    public Exception? TekrarlayanYazHatasi;
    /// <summary>Ayarlanırsa yalnız bekleyen okuması bu istisnayı fırlatır.</summary>
    public Exception? BekleyenHatasi;
    /// <summary>Ayarlanırsa yalnız tekrarlayan gider listesi okuması bu istisnayı fırlatır.</summary>
    public Exception? TekrarlayanOkumaHatasi;
    public Task<IReadOnlyList<TekrarlayanGiderDto>> TekrarlayanGiderlerAsync()
    {
        TekrarlayanCagri++;
        var hata = TekrarlayanOkumaHatasi ?? YuklemeHatasi;
        return hata is not null ? Task.FromException<IReadOnlyList<TekrarlayanGiderDto>>(hata) : Task.FromResult(TekrarlayanListe);
    }
    public Task<IReadOnlyList<BekleyenGiderDto>> BekleyenGiderlerAsync()
    {
        BekleyenCagri++;
        var hata = BekleyenHatasi ?? YuklemeHatasi;
        return hata is not null ? Task.FromException<IReadOnlyList<BekleyenGiderDto>>(hata) : Task.FromResult(BekleyenListe);
    }
    public Task<TekrarlayanGiderDto> TekrarlayanGiderOlusturAsync(TekrarlayanGiderYaz g)
    {
        if (TekrarlayanYazHatasi is not null) return Task.FromException<TekrarlayanGiderDto>(TekrarlayanYazHatasi);
        SonTekrarOlustur = g;
        var yeni = new TekrarlayanGiderDto(600 + TekrarlayanListe.Count, g.Kalem, g.Kanal, g.Tutar, g.AyinGunu, g.Aktif,
            g.BaslangicAyi ?? new DateOnly(2026, 9, 1), g.Siklik, g.KrediKartiId, g.TutarDegisken);
        TekrarlayanListe = TekrarlayanListe.Append(yeni).ToList();
        return Task.FromResult(yeni);
    }
    public Task<TekrarlayanGiderDto> TekrarlayanGiderGuncelleAsync(int id, TekrarlayanGiderYaz g)
    {
        if (TekrarlayanYazHatasi is not null) return Task.FromException<TekrarlayanGiderDto>(TekrarlayanYazHatasi);
        SonTekrarGuncelle = (id, g);
        var eski = TekrarlayanListe.FirstOrDefault(t => t.Id == id);
        var yeni = new TekrarlayanGiderDto(id, g.Kalem, g.Kanal, g.Tutar, g.AyinGunu, g.Aktif,
            g.BaslangicAyi ?? eski?.BaslangicAyi ?? new DateOnly(2026, 9, 1), g.Siklik, g.KrediKartiId, g.TutarDegisken);
        TekrarlayanListe = TekrarlayanListe.Select(t => t.Id == id ? yeni : t).ToList();
        return Task.FromResult(yeni);
    }
    public Task TekrarlayanGiderSilAsync(int id)
    {
        if (TekrarlayanYazHatasi is not null) return Task.FromException(TekrarlayanYazHatasi);
        SonTekrarSil = id;
        TekrarlayanListe = TekrarlayanListe.Where(t => t.Id != id).ToList();
        BekleyenListe = BekleyenListe.Where(b => b.TekrarlayanGiderId != id).ToList();
        return Task.CompletedTask;
    }
    public Task<IslemDto> TekrarlayanOnaylaAsync(int id, TekrarlayanOnayYaz g)
    {
        if (TekrarlayanYazHatasi is not null) return Task.FromException<IslemDto>(TekrarlayanYazHatasi);
        SonOnay = (id, g);
        var b = BekleyenListe.FirstOrDefault(x => x.TekrarlayanGiderId == id && x.Ay == g.Ay);
        BekleyenListe = BekleyenListe.Where(x => x != b).ToList();
        return Task.FromResult(new IslemDto(500, g.Tarih, b?.Kalem ?? "", g.Tutar, b?.Kanal ?? "", GiderTipi.SabitGider, "Tekrarlayan gider"));
    }
    public Task TekrarlayanAtlaAsync(int id, DateOnly ay)
    {
        if (TekrarlayanYazHatasi is not null) return Task.FromException(TekrarlayanYazHatasi);
        SonAtla = (id, ay);
        BekleyenListe = BekleyenListe.Where(x => !(x.TekrarlayanGiderId == id && x.Ay == ay)).ToList();
        return Task.CompletedTask;
    }
    public IReadOnlyList<IslemDto> IslemlerListe = new List<IslemDto>();
    public IReadOnlyList<HaftalikOzetDto> HaftalikListe = new List<HaftalikOzetDto>();
    public AylikRaporDto? AylikRapor;
    public int SonAylikYil, SonAylikAy;

    public Task<LoginYanit> LoginAsync(string? kullanici, string sifre)
        => LoginHatasi is not null ? Task.FromException<LoginYanit>(LoginHatasi)
                                   : Task.FromResult(LoginYaniti!);
    public Task<string?> BenKimAsync()
        => MeHatasi is not null ? Task.FromException<string?>(MeHatasi) : Task.FromResult(MeRol);
    public Exception? CikisHatasi;
    public Task CikisAsync() { CikisCagrildi = true; return CikisHatasi is not null ? Task.FromException(CikisHatasi) : Task.CompletedTask; }

    public event EventHandler<OturumBitisNedeni>? OturumSonaErdi;
    /// <summary>Gerçek istemcinin 401'de yaptığını taklit eder.</summary>
    public void OturumuBitir(OturumBitisNedeni neden = OturumBitisNedeni.Yetkisiz) => OturumSonaErdi?.Invoke(this, neden);

    /// <summary>Ayarlanırsa işlem listesi yanıtını bu üretir (yarış testleri; argümanlar: baslangic, bitis, kanal).</summary>
    public Func<DateOnly?, DateOnly?, string?, Task<IReadOnlyList<IslemDto>>>? IslemlerUret;
    /// <summary>Ayarlanırsa aylık rapor yanıtını bu üretir (yarış testleri).</summary>
    public Func<int, int, Task<AylikRaporDto>>? AylikUret;
    /// <summary>Ayarlanırsa cari arama yanıtını bu üretir (yarış testleri).</summary>
    public Func<string?, Task<IReadOnlyList<CariDto>>>? CarilerUret;
    public IReadOnlyList<DonemDto> DonemlerListe = new List<DonemDto>();
    public int DonemlerCagri, IslemlerCagri, KanallarCagri, KrediKartlariCagri, KartOdemelerCagri, TumKartOdemeleriCagri;
    public string? SonAra;
    public int? SonLimit, SonOffset;

    public Task<PanelDto> PanelAsync() => YuklemeHatasi is not null ? Task.FromException<PanelDto>(YuklemeHatasi) : Task.FromResult(Panel!);
    public Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync() => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<HaftalikOzetDto>>(YuklemeHatasi) : Task.FromResult(HaftalikListe);
    public Task<AylikRaporDto> AylikAsync(int yil, int ay)
    {
        SonAylikYil = yil; SonAylikAy = ay;
        if (AylikUret is not null) return AylikUret(yil, ay);
        return YuklemeHatasi is not null ? Task.FromException<AylikRaporDto>(YuklemeHatasi) : Task.FromResult(AylikRapor!);
    }
    public Task<IReadOnlyList<DonemDto>> DonemlerAsync() { DonemlerCagri++; return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<DonemDto>>(YuklemeHatasi) : Task.FromResult(DonemlerListe); }
    public Task<IReadOnlyList<KanalDto>> KanallarAsync() { KanallarCagri++; return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KanalDto>>(YuklemeHatasi) : Task.FromResult(KanallarListe); }
    public Task<IReadOnlyList<CariDto>> CarilerAsync(string? ara = null)
    {
        SonAra = ara;
        if (CarilerUret is not null) return CarilerUret(ara);
        return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<CariDto>>(YuklemeHatasi) : Task.FromResult(CarilerListe);
    }
    // İşlem listesi filtre çağrısının son argümanları (filtre testleri için).
    public DateOnly? SonFiltreBaslangic;
    public DateOnly? SonFiltreBitis;
    public string? SonFiltreKanal;
    public string? SonFiltreCari;
    public Task<IReadOnlyList<IslemDto>> IslemlerAsync(DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null,
        int? limit = null, int? offset = null)
    {
        IslemlerCagri++;
        SonFiltreBaslangic = baslangic; SonFiltreBitis = bitis; SonFiltreKanal = kanal; SonFiltreCari = cari;
        SonLimit = limit; SonOffset = offset;
        if (IslemlerUret is not null) return IslemlerUret(baslangic, bitis, kanal);
        return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<IslemDto>>(YuklemeHatasi) : Task.FromResult(IslemlerListe);
    }
    /// <summary>Sayfa çağrıları (limit, offset) sırasıyla.</summary>
    public List<(int Limit, int Offset)> SayfaCagrilari = new();
    /// <summary>Sunucu gibi: tüm eşleşenleri (IslemlerAsync üretir) tarih, id artan sıralar ve dilimler.</summary>
    public async Task<IslemSayfasi> IslemSayfasiAsync(DateOnly? baslangic, DateOnly? bitis, string? kanal, string? cari, int limit, int offset)
    {
        SayfaCagrilari.Add((limit, offset));
        var hepsi = await IslemlerAsync(baslangic, bitis, kanal, cari, limit, offset);
        var sirali = hepsi.OrderBy(i => i.Tarih).ThenBy(i => i.Id).ToList();
        return new IslemSayfasi(sirali.Skip(offset).Take(limit).ToList(), sirali.Count);
    }
    public Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync() { KrediKartlariCagri++; return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KrediKartiDto>>(YuklemeHatasi) : Task.FromResult(KrediKartlariListe); }
    public Task<IReadOnlyList<GelenDto>> GelenlerAsync(DateOnly? donemStart = null) => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<GelenDto>>(YuklemeHatasi)
        : Task.FromResult<IReadOnlyList<GelenDto>>(GelenDeposu.Where(g => donemStart is null || g.Key.Item1 == donemStart)
            .Select(g => new GelenDto(0, g.Key.Item1, g.Key.Item2, g.Value)).ToList());   // GelenDeposu: SahteApi.C.cs
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
    public Exception? IslemYazHatasi;
    public Task<IslemDto> IslemOlusturAsync(IslemYaz g) { if (IslemYazHatasi is not null) return Task.FromException<IslemDto>(IslemYazHatasi); SonIslemOlustur = g; return Task.FromResult(new IslemDto(0, g.Tarih, g.Cari, g.TutarTl, g.Kanal, g.Tip, g.Not)); }
    public Task<IslemDto> IslemGuncelleAsync(int id, IslemYaz g) { SonIslemGuncelle = (id, g); return Task.FromResult(new IslemDto(id, g.Tarih, g.Cari, g.TutarTl, g.Kanal, g.Tip, g.Not)); }
    public Task IslemSilAsync(int id) { SonIslemSil = id; return Task.CompletedTask; }
    public Task<KrediKartiDto> KrediKartiOlusturAsync(KrediKartiYaz g) { SonKartOlustur = g; return Task.FromResult(new KrediKartiDto(0, g.Ad, g.KesimTarihi, g.SonOdemeTarihi, g.Limit, g.Borc)); }
    public Task<KrediKartiDto> KrediKartiGuncelleAsync(int id, KrediKartiYaz g) { SonKartGuncelle = (id, g); return Task.FromResult(new KrediKartiDto(id, g.Ad, g.KesimTarihi, g.SonOdemeTarihi, g.Limit, g.Borc)); }
    public Task KrediKartiSilAsync(int id) { SonKartSil = id; return Task.CompletedTask; }
    public Task<GelenDto> GelenKaydetAsync(GelenYaz g) { SonGelen = g; return Task.FromResult(new GelenDto(0, g.DonemStart, g.Kanal, g.TutarTl)); }
    public Task AyarGuncelleAsync(AyarYaz g) { SonAyar = g; return Task.CompletedTask; }
    public Task IzleyiciSifreAsync(string yeniSifre) { SonIzleyiciSifre = yeniSifre; return Task.CompletedTask; }
    public int OturumKapatSayisi;
    public Task OturumlariKapatAsync() { OturumKapatSayisi++; return Task.CompletedTask; }

    public Task<IReadOnlyList<KartOdemeDto>> KartOdemelerAsync(int krediKartiId) { KartOdemelerCagri++; SonKartOdemelerId = krediKartiId; return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KartOdemeDto>>(YuklemeHatasi) : Task.FromResult<IReadOnlyList<KartOdemeDto>>(KartOdemelerListe.Where(o => o.KrediKartiId == krediKartiId).ToList()); }
    public Task<IReadOnlyList<KartOdemeDto>> TumKartOdemeleriAsync() { TumKartOdemeleriCagri++; return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KartOdemeDto>>(YuklemeHatasi) : Task.FromResult(KartOdemelerListe); }
    public Task<KartOdemeDto> KartOdemeKaydetAsync(KartOdemeYaz g) { SonKartOdemeKaydet = g; return Task.FromResult(new KartOdemeDto(0, g.KrediKartiId, g.Tarih, g.Tutar, g.Not)); }
    public Task KartOdemeSilAsync(int id) { SonKartOdemeSil = id; return Task.CompletedTask; }

    public Task<AyarlarDto> AyarlarAsync() => YuklemeHatasi is not null ? Task.FromException<AyarlarDto>(YuklemeHatasi) : Task.FromResult(AyarlarSonuc!);

    // ---- Çekler ----
    public IReadOnlyList<CekDto> CeklerListe = new List<CekDto>();
    public CekOzetDto CekOzeti = new(0m, 0, 0m, 0, 30, new List<CekDto>(), new List<CekDto>());
    public int CeklerCagri, CekOzetCagri;
    /// <summary>Son çek listesi çağrısının filtresi.</summary>
    public (CekYonu? Yon, CekDurumu? Durum)? SonCekFiltre;
    /// <summary>Ayarlanırsa çek listesi yanıtını bu üretir (yarış testleri).</summary>
    public Func<CekYonu?, CekDurumu?, Task<IReadOnlyList<CekDto>>>? CeklerUret;
    public Exception? CekYazHatasi;
    public CekYaz? SonCekOlustur;
    public (int Id, CekYaz G)? SonCekGuncelle;
    public int? SonCekSil;

    /// <summary>Sunucu gibi: yön/durum/vade filtresi uygular.</summary>
    public Task<IReadOnlyList<CekDto>> CeklerAsync(CekYonu? yon = null, CekDurumu? durum = null, DateOnly? baslangic = null, DateOnly? bitis = null)
    {
        CeklerCagri++;
        SonCekFiltre = (yon, durum);
        if (CeklerUret is not null) return CeklerUret(yon, durum);
        if (YuklemeHatasi is not null) return Task.FromException<IReadOnlyList<CekDto>>(YuklemeHatasi);
        IReadOnlyList<CekDto> l = CeklerListe
            .Where(c => (yon is null || c.Yon == yon) && (durum is null || c.Durum == durum)
                        && (baslangic is null || c.VadeTarihi >= baslangic) && (bitis is null || c.VadeTarihi <= bitis))
            .ToList();
        return Task.FromResult(l);
    }
    public Task<CekOzetDto> CekOzetAsync()
    {
        CekOzetCagri++;
        return YuklemeHatasi is not null ? Task.FromException<CekOzetDto>(YuklemeHatasi) : Task.FromResult(CekOzeti);
    }
    public Task<CekDto> CekOlusturAsync(CekYaz g)
    {
        if (CekYazHatasi is not null) return Task.FromException<CekDto>(CekYazHatasi);
        SonCekOlustur = g;
        return Task.FromResult(new CekDto(0, g.Yon, g.CekNo, g.Banka, g.Kisi, g.Tutar, g.DuzenlemeTarihi, g.VadeTarihi, g.Kanal, g.Durum, g.IslemTarihi, g.Not, g.Tur, g.Konum, g.CiroEdilenCari));
    }
    public Task<CekDto> CekGuncelleAsync(int id, CekYaz g)
    {
        if (CekYazHatasi is not null) return Task.FromException<CekDto>(CekYazHatasi);
        SonCekGuncelle = (id, g);
        return Task.FromResult(new CekDto(id, g.Yon, g.CekNo, g.Banka, g.Kisi, g.Tutar, g.DuzenlemeTarihi, g.VadeTarihi, g.Kanal, g.Durum, g.IslemTarihi, g.Not, g.Tur, g.Konum, g.CiroEdilenCari));
    }
    public Task CekSilAsync(int id) { SonCekSil = id; return Task.CompletedTask; }

    // ---- Excel'e aktar ----
    /// <summary>İndirme çağrılarının döndüğü dosya.</summary>
    public IndirilenDosya CsvDosyasi = new("kasa-rapor.csv", [0xEF, 0xBB, 0xBF, (byte)'a']);
    /// <summary>Ayarlanırsa CSV indirmeleri bu istisnayı fırlatır.</summary>
    public Exception? CsvHatasi;
    public (DateOnly? Baslangic, DateOnly? Bitis, string? Kanal, string? Cari)? SonIslemCsv;
    public int HaftalikCsvCagri;
    public (int Yil, int Ay)? SonAylikCsv;
    private Task<IndirilenDosya> CsvYanit() => CsvHatasi is not null ? Task.FromException<IndirilenDosya>(CsvHatasi) : Task.FromResult(CsvDosyasi);
    public Task<IndirilenDosya> IslemlerCsvAsync(DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null)
    {
        SonIslemCsv = (baslangic, bitis, kanal, cari);
        return CsvYanit();
    }
    public Task<IndirilenDosya> HaftalikCsvAsync() { HaftalikCsvCagri++; return CsvYanit(); }
    public Task<IndirilenDosya> AylikCsvAsync(int yil, int ay) { SonAylikCsv = (yil, ay); return CsvYanit(); }

    // ---- Kasa sayımı ----
    public IReadOnlyList<KasaSayimDto> KasaSayimlariListe = new List<KasaSayimDto>();
    public int KasaSayimlariCagri;
    /// <summary>Defter değeri yanıtı (tarih → tutar); ayarlanmadıysa <see cref="KasaHesapSonuc"/>.</summary>
    public Func<DateOnly, Task<KasaHesapDto>>? KasaHesaplaUret;
    public decimal KasaHesapSonuc;
    public List<DateOnly> KasaHesaplaCagrilari = new();
    public KasaSayimYaz? SonKasaSayimKaydet;
    public Exception? KasaSayimYazHatasi;
    public int? SonKasaSayimSil;
    public Task<IReadOnlyList<KasaSayimDto>> KasaSayimlariAsync()
    {
        KasaSayimlariCagri++;
        return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KasaSayimDto>>(YuklemeHatasi) : Task.FromResult(KasaSayimlariListe);
    }
    public Task<KasaHesapDto> KasaHesaplaAsync(DateOnly tarih)
    {
        KasaHesaplaCagrilari.Add(tarih);
        if (KasaHesaplaUret is not null) return KasaHesaplaUret(tarih);
        return YuklemeHatasi is not null ? Task.FromException<KasaHesapDto>(YuklemeHatasi) : Task.FromResult(new KasaHesapDto(tarih, KasaHesapSonuc));
    }
    public Task<KasaSayimDto> KasaSayimKaydetAsync(KasaSayimYaz g)
    {
        if (KasaSayimYazHatasi is not null) return Task.FromException<KasaSayimDto>(KasaSayimYazHatasi);
        SonKasaSayimKaydet = g;
        var d = new KasaSayimDto(100 + KasaSayimlariListe.Count, g.Tarih, g.SayilanTutar, KasaHesapSonuc,
            g.SayilanTutar - KasaHesapSonuc, KasaHesapSonuc, g.Not, new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc), g.Satirlar);
        KasaSayimlariListe = KasaSayimlariListe.Prepend(d).ToList();
        return Task.FromResult(d);
    }
    public Task KasaSayimSilAsync(int id)
    {
        SonKasaSayimSil = id;
        KasaSayimlariListe = KasaSayimlariListe.Where(s => s.Id != id).ToList();
        return Task.CompletedTask;
    }

    // ---- Değişiklik geçmişi ----
    /// <summary>Sunucu sırasıyla (en yeni önce) geçmiş satırları; GecmisAsync tür filtreler ve dilimler.</summary>
    public IReadOnlyList<DegisiklikDto> GecmisListe = new List<DegisiklikDto>();
    public IReadOnlyList<string> GecmisTurleriListe = new List<string>();
    /// <summary>Ayarlanırsa geçmiş yanıtını bu üretir (yarış testleri; argümanlar: tur, limit, offset).</summary>
    public Func<string?, int, int, Task<DegisiklikSayfasi>>? GecmisUret;
    /// <summary>Geçmiş sayfa çağrıları (tur, limit, offset) sırasıyla.</summary>
    public List<(string? Tur, int Limit, int Offset)> GecmisCagrilari = new();
    public int? SonGeriAl;
    public Exception? GeriAlHatasi;

    public Task<DegisiklikSayfasi> GecmisAsync(string? tur, int limit, int offset)
    {
        GecmisCagrilari.Add((tur, limit, offset));
        if (GecmisUret is not null) return GecmisUret(tur, limit, offset);
        if (YuklemeHatasi is not null) return Task.FromException<DegisiklikSayfasi>(YuklemeHatasi);
        var eslesen = GecmisListe.Where(d => tur is null || d.Tur == tur).ToList();
        return Task.FromResult(new DegisiklikSayfasi(eslesen.Skip(offset).Take(limit).ToList(), eslesen.Count));
    }
    public Task<IReadOnlyList<string>> GecmisTurleriAsync()
        => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<string>>(YuklemeHatasi) : Task.FromResult(GecmisTurleriListe);
    public Task GeriAlAsync(int degisiklikId)
    {
        if (GeriAlHatasi is not null) return Task.FromException(GeriAlHatasi);
        SonGeriAl = degisiklikId;
        return Task.CompletedTask;
    }
}
