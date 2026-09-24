using System.Globalization;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket B uçlarının sahtesi: canned yanıt + çağrı kaydı (varsayılanlar boş ama geçerli yanıtlardır).</summary>
public sealed partial class SahteApi
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    public static string AyEtiketi(int yil, int ay) => new DateOnly(yil, ay, 1).ToString("MMMM yyyy", Tr);

    // ---- Kasa dökümü ----
    public List<(DateOnly Bas, DateOnly Bit)> KasaDokumuCagrilari = new();
    /// <summary>Ayarlanırsa kasa dökümü yanıtını bu üretir.</summary>
    public Func<DateOnly, DateOnly, Task<KasaDokumuDto>>? KasaDokumuUret;
    public (DateOnly Bas, DateOnly Bit)? SonKasaDokumuCsv;

    public Task<KasaDokumuDto> KasaDokumuAsync(DateOnly baslangic, DateOnly bitis)
    {
        KasaDokumuCagrilari.Add((baslangic, bitis));
        if (KasaDokumuUret is not null) return KasaDokumuUret(baslangic, bitis);
        if (YuklemeHatasi is not null) return Task.FromException<KasaDokumuDto>(YuklemeHatasi);
        return Task.FromResult(new KasaDokumuDto(baslangic, bitis, 0m, 0m, 0m, 0m, new List<KasaDokumAdimiDto>()));
    }
    public Task<IndirilenDosya> KasaDokumuCsvAsync(DateOnly baslangic, DateOnly bitis)
    {
        SonKasaDokumuCsv = (baslangic, bitis);
        return CsvYanit();
    }

    // ---- İşlemler: tip süzgeci (sunucu gibi etkin tiple süzer: karta bağlı işlem K.K sayılır) ----
    /// <summary>Tipli sayfa çağrıları (tip, limit, offset) sırasıyla.</summary>
    public List<(IslemTipSuzgeci Tip, int Limit, int Offset)> TipliSayfaCagrilari = new();
    public (DateOnly? Bas, DateOnly? Bit, string? Kanal, IslemTipSuzgeci Tip)? SonTipliIslemCsv;

    public static bool TipeUyar(IslemDto i, IslemTipSuzgeci t) => t switch
    {
        IslemTipSuzgeci.Cari => i.Tip == GiderTipi.Cari && i.KrediKartiId is null,
        IslemTipSuzgeci.SabitGider => i.Tip == GiderTipi.SabitGider && i.KrediKartiId is null,
        IslemTipSuzgeci.KrediKarti => i.Tip == GiderTipi.KrediKarti || i.KrediKartiId is not null,
        _ => i.Tip != GiderTipi.KrediKarti && i.KrediKartiId is null,
    };

    public async Task<IslemSayfasi> IslemSayfasiTipeGoreAsync(DateOnly? baslangic, DateOnly? bitis, string? kanal, IslemTipSuzgeci tip, int limit, int offset)
    {
        TipliSayfaCagrilari.Add((tip, limit, offset));
        var hepsi = await IslemlerAsync(baslangic, bitis, kanal, null, limit, offset);
        var sirali = hepsi.Where(i => TipeUyar(i, tip)).OrderBy(i => i.Tarih).ThenBy(i => i.Id).ToList();
        return new IslemSayfasi(sirali.Skip(offset).Take(limit).ToList(), sirali.Count);
    }
    public Task<IndirilenDosya> IslemlerCsvTipeGoreAsync(DateOnly? baslangic, DateOnly? bitis, string? kanal, IslemTipSuzgeci tip)
    {
        SonTipliIslemCsv = (baslangic, bitis, kanal, tip);
        return CsvYanit();
    }

    // ---- Ay kapanışı ----
    /// <summary>Sunucu gibi: kilitli ve yayınlanmış aylar; AyKapanisiAsync bunlardan yanıt kurar.</summary>
    public HashSet<(int Yil, int Ay)> KilitliAylar = new();
    public HashSet<(int Yil, int Ay)> YayinlananAylar = new();
    /// <summary>Kilitlenebilir (bitmiş) son ay; bundan sonraki aylar kilitlenemez.</summary>
    public (int Yil, int Ay) SonBitmisAy = (2026, 8);
    public IReadOnlyList<AyFarkiDto> AyFarklari = new List<AyFarkiDto>();
    public IReadOnlyList<DegisiklikDto> AyDegisiklikleri = new List<DegisiklikDto>();
    public List<(int Yil, int Ay)> AyKapanisiCagrilari = new();
    public List<(string Eylem, int Yil, int Ay)> AyEylemleri = new();
    public Exception? AyEylemHatasi;
    public Exception? AyKapanisiHatasi;

    private AyKapanisDto Kapanis(int yil, int ay)
    {
        bool kilitli = KilitliAylar.Contains((yil, ay));
        bool yayin = YayinlananAylar.Contains((yil, ay));
        var zaman = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
        return new AyKapanisDto(yil, ay, AyEtiketi(yil, ay), kilitli, kilitli ? zaman : null,
            (yil, ay).CompareTo(SonBitmisAy) <= 0, yayin, yayin ? zaman : null,
            yayin ? AyFarklari : new List<AyFarkiDto>(), yayin ? AyDegisiklikleri : new List<DegisiklikDto>());
    }

    public Task<AyKapanisDto> AyKapanisiAsync(int yil, int ay)
    {
        AyKapanisiCagrilari.Add((yil, ay));
        var hata = AyKapanisiHatasi ?? YuklemeHatasi;
        return hata is not null ? Task.FromException<AyKapanisDto>(hata) : Task.FromResult(Kapanis(yil, ay));
    }
    public Task<IReadOnlyList<AyKilidiDto>> AyKilitleriAsync()
    {
        IReadOnlyList<AyKilidiDto> l = KilitliAylar.OrderByDescending(a => a)
            .Select(a => new AyKilidiDto(a.Yil, a.Ay, AyEtiketi(a.Yil, a.Ay), new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc))).ToList();
        return Task.FromResult(l);
    }
    private Task<AyKapanisDto> AyEylemi(string eylem, int yil, int ay, Action uygula)
    {
        AyEylemleri.Add((eylem, yil, ay));
        if (AyEylemHatasi is not null) return Task.FromException<AyKapanisDto>(AyEylemHatasi);
        uygula();
        return Task.FromResult(Kapanis(yil, ay));
    }
    public Task<AyKapanisDto> AyiKilitleAsync(int yil, int ay) => AyEylemi("kilitle", yil, ay, () => KilitliAylar.Add((yil, ay)));
    public Task<AyKapanisDto> AyKilidiniAcAsync(int yil, int ay) => AyEylemi("kilit-ac", yil, ay, () => KilitliAylar.Remove((yil, ay)));
    public Task<AyKapanisDto> AyiYayinlaAsync(int yil, int ay) => AyEylemi("yayinla", yil, ay, () =>
    {
        YayinlananAylar.Add((yil, ay));
        AyFarklari = new List<AyFarkiDto>();
        AyDegisiklikleri = new List<DegisiklikDto>();
    });

    // ---- Dosyalar ----
    public IndirilenDosya HtmlDosyasi = new("kasa-aylik-rapor-2026-08.html", "<!doctype html>"u8.ToArray());
    public IndirilenDosya ZipDosyasi = new("kasa-ay-paketi-2026-08.zip", [0x50, 0x4B, 0x03, 0x04]);
    public Exception? DosyaHatasi;
    public (int Yil, int Ay)? SonYazdir, SonAyPaketi;
    public (CekYonu? Yon, CekDurumu? Durum, DateOnly? Bas, DateOnly? Bit)? SonCekCsv;
    /// <summary>Son çek CSV'sinin paket D süzgeçleri (tür, konum).</summary>
    public (CekTuru? Tur, CekKonumu? Konum)? SonCekCsvEvrak;
    public int KasaSayimCsvCagri, GecmisCsvCagri;
    public string? SonGecmisCsvTur;

    public Task<IndirilenDosya> AylikYazdirAsync(int yil, int ay)
    {
        SonYazdir = (yil, ay);
        return DosyaHatasi is not null ? Task.FromException<IndirilenDosya>(DosyaHatasi) : Task.FromResult(HtmlDosyasi);
    }
    public Task<IndirilenDosya> AyPaketiAsync(int yil, int ay)
    {
        SonAyPaketi = (yil, ay);
        return DosyaHatasi is not null ? Task.FromException<IndirilenDosya>(DosyaHatasi) : Task.FromResult(ZipDosyasi);
    }
    public Task<IndirilenDosya> CeklerCsvAsync(CekYonu? yon = null, CekDurumu? durum = null, DateOnly? baslangic = null, DateOnly? bitis = null,
        CekTuru? tur = null, CekKonumu? konum = null)
    {
        SonCekCsv = (yon, durum, baslangic, bitis);
        SonCekCsvEvrak = (tur, konum);
        return CsvYanit();
    }
    public Task<IndirilenDosya> KasaSayimlariCsvAsync() { KasaSayimCsvCagri++; return CsvYanit(); }
    public Task<IndirilenDosya> GecmisCsvAsync(string? tur = null) { GecmisCsvCagri++; SonGecmisCsvTur = tur; return CsvYanit(); }

    // ---- Kurlar ve grafik ----
    /// <summary>Sunucu gibi: ay başına tek satır, en yeni önce; tüm değerler boşsa satır silinir.</summary>
    public List<KurDto> KurlarListe = new();
    public int KurlarCagri;
    public KurDto? SonKurKaydet;
    public Exception? KurYazHatasi;
    public DateOnly? SonTcmbAy;
    /// <summary>TCMB sonucunun USD/EUR'su (varsayılan 41,25 / 48,1234; 20 iş günü).</summary>
    public (decimal Usd, decimal Eur) TcmbKurlari = (41.25m, 48.1234m);
    public Exception? TcmbHatasi;
    public GrafikDto? Grafik;
    public (int Yil, int Ay)? SonGrafik;

    public Task<IReadOnlyList<KurDto>> KurlarAsync()
    {
        KurlarCagri++;
        return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KurDto>>(YuklemeHatasi)
            : Task.FromResult<IReadOnlyList<KurDto>>(KurlarListe.OrderByDescending(k => k.Ay).ToList());
    }
    public Task<KurDto> KurKaydetAsync(KurDto g)
    {
        if (KurYazHatasi is not null) return Task.FromException<KurDto>(KurYazHatasi);
        SonKurKaydet = g;
        var ay = new DateOnly(g.Ay.Year, g.Ay.Month, 1);
        var yeni = g with { Ay = ay };
        KurlarListe.RemoveAll(k => k.Ay == ay);
        if (g.TufeEndeksi is not null || g.UsdTry is not null || g.EurTry is not null || g.AltinGramTry is not null) KurlarListe.Add(yeni);
        return Task.FromResult(yeni);
    }
    public Task<KurTcmbSonucDto> KurTcmbDoldurAsync(DateOnly ay)
    {
        SonTcmbAy = ay;
        if (TcmbHatasi is not null) return Task.FromException<KurTcmbSonucDto>(TcmbHatasi);
        var bas = new DateOnly(ay.Year, ay.Month, 1);
        var eski = KurlarListe.FirstOrDefault(k => k.Ay == bas) ?? new KurDto(bas, null, null, null, null);
        var yeni = eski with { UsdTry = TcmbKurlari.Usd, EurTry = TcmbKurlari.Eur };
        KurlarListe.RemoveAll(k => k.Ay == bas);
        KurlarListe.Add(yeni);
        return Task.FromResult(new KurTcmbSonucDto(yeni, 20, bas.AddDays(2), bas.AddDays(27)));
    }
    public Task<GrafikDto> GrafikAsync(int yil, int ay)
    {
        SonGrafik = (yil, ay);
        if (YuklemeHatasi is not null) return Task.FromException<GrafikDto>(YuklemeHatasi);
        return Task.FromResult(Grafik ?? new GrafikDto(yil, ay, new List<string>(), new List<GrafikAyDto>()));
    }

    // ---- Hedef ve bütçe ----
    public HedefButceDto? HedefButce;
    public List<(int Yil, int Ay)> HedefButceCagrilari = new();
    public HedefButceYaz? SonHedefButceKaydet;
    public Exception? HedefYazHatasi;
    public DateOnly? SonKopyala;
    public KopyalaSonucDto KopyalaSonucu = new(2, 1);
    public Exception? KopyalaHatasi;

    public Task<HedefButceDto> HedefButceAsync(int yil, int ay)
    {
        HedefButceCagrilari.Add((yil, ay));
        if (YuklemeHatasi is not null) return Task.FromException<HedefButceDto>(YuklemeHatasi);
        return Task.FromResult(HedefButce ?? new HedefButceDto(yil, ay, new List<KanalHedefDto>(), new List<GiderButceDto>()));
    }
    /// <summary>Sunucu gibi: gönderilen satırları yazar (null siler), gerçekleşeni korur, yüzdeyi yeniden hesaplar.</summary>
    public Task<HedefButceDto> HedefButceKaydetAsync(HedefButceYaz g)
    {
        if (HedefYazHatasi is not null) return Task.FromException<HedefButceDto>(HedefYazHatasi);
        SonHedefButceKaydet = g;
        var d = HedefButce ?? new HedefButceDto(g.Ay.Year, g.Ay.Month, new List<KanalHedefDto>(), new List<GiderButceDto>());
        static decimal? Yuzde(decimal g, decimal? h) => h is > 0m ? decimal.Round(g / h.Value * 100m, 1, MidpointRounding.AwayFromZero) : null;
        var kanallar = d.Kanallar.Select(k => g.Kanallar?.FirstOrDefault(x => x.KanalId == k.KanalId) is { } y
            ? k with { Hedef = y.Tutar, Yuzde = Yuzde(k.Gerceklesen, y.Tutar) } : k).ToList();
        var kalemler = d.Kalemler.Select(k => g.Kalemler?.FirstOrDefault(x => x.GiderKalemiId == k.GiderKalemiId) is { } y
            ? k with { Butce = y.Tutar, Yuzde = Yuzde(k.Gerceklesen, y.Tutar) } : k).ToList();
        HedefButce = d with { Kanallar = kanallar, Kalemler = kalemler };
        return Task.FromResult(HedefButce);
    }
    public Task<KopyalaSonucDto> HedefButceKopyalaAsync(DateOnly ay)
    {
        SonKopyala = ay;
        return KopyalaHatasi is not null ? Task.FromException<KopyalaSonucDto>(KopyalaHatasi) : Task.FromResult(KopyalaSonucu);
    }

    // ---- Cari özeti ----
    public CariOzetiDto? CariOzeti;
    public List<(string Ad, int Yil, CariOzetiTuru Tur)> CariOzetiCagrilari = new();
    public Func<string, int, CariOzetiTuru, Task<CariOzetiDto>>? CariOzetiUret;

    public Task<CariOzetiDto> CariOzetiAsync(string ad, int yil, CariOzetiTuru tur)
    {
        CariOzetiCagrilari.Add((ad, yil, tur));
        if (CariOzetiUret is not null) return CariOzetiUret(ad, yil, tur);
        if (YuklemeHatasi is not null) return Task.FromException<CariOzetiDto>(YuklemeHatasi);
        return Task.FromResult(CariOzeti ?? new CariOzetiDto(ad, yil, tur == CariOzetiTuru.Kalem ? "kalem" : "cari",
            Enumerable.Range(1, 12).Select(a => new CariOzetiAyDto(a, 0m, 0m, 0m, 0m, 0, null, null)).ToList(), 0m, null));
    }
}

/// <summary>Gezinme çağrılarını kaydeden sahte.</summary>
public sealed class SahteGezinti : IGezinti
{
    public List<string> Rotalar = new();
    public Task GitAsync(string rota) { Rotalar.Add(rota); return Task.CompletedTask; }
}
