using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket E üyeleri: giriş (iki adımlı), hesabım, kullanıcılar, oturumlar, sorular, risk kartı.</summary>
public sealed partial class SahteApi
{
    // ── Giriş ──
    /// <summary>true: kodsuz giriş "kod isteniyor" döner; yalnız <see cref="GecerliKod"/> kabul edilir.</summary>
    public bool IkiAdimGerekli;
    public string GecerliKod = "123456";
    /// <summary>Kod isteğinde sunucunun mesajı (null değilse ilk yanıtta bu döner; ör. kilit mesajı).</summary>
    public string? KodIstekMesaji;
    public string? GirisAd;
    public int? GirisKullaniciId;
    public List<(string? Kullanici, string Sifre, string? Kod)> GirisCagrilari = new();

    public Task<GirisSonucu> GirisAsync(string? kullanici, string sifre, string? kod)
    {
        GirisCagrilari.Add((kullanici, sifre, kod));
        if (LoginHatasi is not null) return Task.FromException<GirisSonucu>(LoginHatasi);
        if (IkiAdimGerekli)
        {
            if (string.IsNullOrWhiteSpace(kod))
                return Task.FromResult(GirisSonucu.KodIsteniyor(KodIstekMesaji ?? "İki adımlı giriş kodunu girin."));
            if (kod.Trim() != GecerliKod)
                return Task.FromResult(GirisSonucu.KodIsteniyor("Kod hatalı. Uygulamadaki güncel kodu girin."));
        }
        return Task.FromResult(new GirisSonucu(LoginYaniti!.Rol, GirisAd, GirisKullaniciId));
    }

    public string? MeAd;
    public int? MeKullaniciId;
    public Task<BenDto> BenAsync()
        => MeHatasi is not null ? Task.FromException<BenDto>(MeHatasi) : Task.FromResult(new BenDto(MeRol, MeAd, MeKullaniciId));

    /// <summary>Ayarlanırsa paket E yazma çağrıları bu istisnayı fırlatır.</summary>
    public Exception? GuvenlikYazHatasi;

    private Task<T> PaketEOku<T>(Func<T> uret) => YuklemeHatasi is not null ? Task.FromException<T>(YuklemeHatasi) : Task.FromResult(uret());
    private Task PaketEYaz(Action eylem)
    {
        if (GuvenlikYazHatasi is not null) return Task.FromException(GuvenlikYazHatasi);
        eylem();
        return Task.CompletedTask;
    }
    private Task<T> PaketEYaz<T>(Func<T> uret)
        => GuvenlikYazHatasi is not null ? Task.FromException<T>(GuvenlikYazHatasi) : Task.FromResult(uret());

    // ── Hesabım ──
    public HesapDto Hesap = new(1, "EMAR", "emar", "editor", false, false, false, 0, 30);
    public (string Mevcut, string Yeni)? SonSifreDegistir;
    public IkiAdimKurulumDto Kurulum = new("JBSWY3DPEHPK3PXP", "otpauth://totp/Emar%20Kasa:emar?secret=JBSWY3DPEHPK3PXP&issuer=Emar%20Kasa");
    public IReadOnlyList<string> UretilecekKodlar = ["23456-789AB", "CDEFG-HJKLM", "NPQRS-TUVWX"];
    public int IkiAdimBaslatCagri;
    public (string Sifre, string Kod)? SonIkiAdimKapat;
    public string? SonKurtarmaYenileKodu;
    public string? SonKurtarmaYenileSifre;
    public string? SonIkiAdimOnaySifre;

    public Task<HesapDto> HesabimAsync() => PaketEOku(() => Hesap);

    public Task SifremiDegistirAsync(string mevcutSifre, string yeniSifre) => PaketEYaz(() =>
    {
        SonSifreDegistir = (mevcutSifre, yeniSifre);
        Hesap = Hesap with { EnvSifresi = false };
    });

    public Task<IkiAdimKurulumDto> IkiAdimBaslatAsync() => PaketEYaz(() => { IkiAdimBaslatCagri++; return Kurulum; });

    public Task<IReadOnlyList<string>> IkiAdimOnaylaAsync(string sifre, string kod)
    {
        if (GuvenlikYazHatasi is not null) return Task.FromException<IReadOnlyList<string>>(GuvenlikYazHatasi);
        SonIkiAdimOnaySifre = sifre;
        if (kod.Trim() != GecerliKod)
            return Task.FromException<IReadOnlyList<string>>(new KasaApiException(HttpStatusCode.BadRequest, "Kod hatalı. Uygulamadaki güncel kodu girin."));
        Hesap = Hesap with { IkiAdimAcik = true, KurtarmaKoduKalan = UretilecekKodlar.Count };
        return Task.FromResult(UretilecekKodlar);
    }

    public Task IkiAdimKapatAsync(string sifre, string kod) => PaketEYaz(() =>
    {
        SonIkiAdimKapat = (sifre, kod);
        Hesap = Hesap with { IkiAdimAcik = false, KurtarmaKoduKalan = 0 };
    });

    public Task<IReadOnlyList<string>> KurtarmaKodlariYenileAsync(string sifre, string kod) => PaketEYaz(() =>
    {
        SonKurtarmaYenileKodu = kod;
        SonKurtarmaYenileSifre = sifre;
        Hesap = Hesap with { KurtarmaKoduKalan = UretilecekKodlar.Count };
        return UretilecekKodlar;
    });

    // ── Kullanıcılar ──
    public List<KullaniciDto> KullanicilarListe = new()
    {
        new(1, "EMAR", "editor", "editor", true, true, false, new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), null, null, 1),
    };
    public KullaniciEkle? SonKullaniciEkle;
    public (int Id, KullaniciGuncelle G)? SonKullaniciGuncelle;
    public (int Id, string Sifre)? SonKullaniciSifre;
    public int? SonOturumlariKapatilan, SonIkiAdimKapatilan, SonSilinenKullanici;
    public bool IzleyiciSifresiKaldirildi;
    public int KullanicilarCagri;

    public Task<IReadOnlyList<KullaniciDto>> KullanicilarAsync()
    {
        KullanicilarCagri++;
        return PaketEOku<IReadOnlyList<KullaniciDto>>(() => KullanicilarListe.ToList());
    }

    public Task<KullaniciDto> KullaniciEkleAsync(KullaniciEkle g) => PaketEYaz(() =>
    {
        SonKullaniciEkle = g;
        var yeni = new KullaniciDto(KullanicilarListe.Count == 0 ? 1 : KullanicilarListe.Max(k => k.Id) + 1, g.AdSoyad, g.KullaniciAdi,
            g.Rol, true, false, false, DateTime.UtcNow, null, null, 0);
        KullanicilarListe.Add(yeni);
        return yeni;
    });

    public Task KullaniciGuncelleAsync(int id, KullaniciGuncelle g) => PaketEYaz(() =>
    {
        SonKullaniciGuncelle = (id, g);
        var i = KullanicilarListe.FindIndex(k => k.Id == id);
        if (i >= 0) KullanicilarListe[i] = KullanicilarListe[i] with { AdSoyad = g.AdSoyad, Rol = g.Rol, Aktif = g.Aktif };
    });

    public Task KullaniciSifreAsync(int id, string yeniSifre) => PaketEYaz(() => SonKullaniciSifre = (id, yeniSifre));
    public Task KullaniciOturumlariniKapatAsync(int id) => PaketEYaz(() => SonOturumlariKapatilan = id);
    public Task KullaniciIkiAdimKapatAsync(int id) => PaketEYaz(() =>
    {
        SonIkiAdimKapatilan = id;
        var i = KullanicilarListe.FindIndex(k => k.Id == id);
        if (i >= 0) KullanicilarListe[i] = KullanicilarListe[i] with { IkiAdimAcik = false };
    });
    public Task KullaniciSilAsync(int id) => PaketEYaz(() =>
    {
        SonSilinenKullanici = id;
        KullanicilarListe.RemoveAll(k => k.Id == id);
    });
    public Task IzleyiciSifresiniKaldirAsync() => PaketEYaz(() => IzleyiciSifresiKaldirildi = true);

    // ── Oturumlar ve giriş günlüğü ──
    public List<OturumDto> OturumlarListe = new();
    public string? SonKapatilanOturum;
    public List<GirisKaydiDto> GirisKayitlariListe = new();
    public List<(bool YalnizBasarisiz, int Limit, int Offset)> GirisKaydiCagrilari = new();
    public GuvenlikAyariDto GuvenlikAyari = new(30, 30, 180);
    public int? SonEditorOturumGun;

    public Task<IReadOnlyList<OturumDto>> OturumlarAsync() => PaketEOku<IReadOnlyList<OturumDto>>(() => OturumlarListe.ToList());
    public Task OturumKapatAsync(string oturumId) => PaketEYaz(() =>
    {
        SonKapatilanOturum = oturumId;
        OturumlarListe.RemoveAll(o => o.Id == oturumId);
    });

    public Task<GirisKaydiSayfasi> GirisKayitlariAsync(bool yalnizBasarisiz, int limit, int offset) => PaketEOku(() =>
    {
        GirisKaydiCagrilari.Add((yalnizBasarisiz, limit, offset));
        var liste = GirisKayitlariListe.Where(g => !yalnizBasarisiz || (!g.Basarili && g.Neden != "Kod bekleniyor")).ToList();
        return new GirisKaydiSayfasi(liste.Skip(offset).Take(limit).ToList(), liste.Count);
    });

    public Task<GuvenlikAyariDto> GuvenlikAyariAsync() => PaketEOku(() => GuvenlikAyari);
    public Task GuvenlikAyariKaydetAsync(int editorOturumGun) => PaketEYaz(() =>
    {
        SonEditorOturumGun = editorOturumGun;
        GuvenlikAyari = GuvenlikAyari with { EditorOturumGun = editorOturumGun };
    });

    // ── Sorular ──
    public List<SoruDto> SorularListe = new();
    public List<(SoruDurumu? Durum, SoruHedefTuru? HedefTur, int? HedefId, DateOnly? Hafta)> SoruCagrilari = new();
    public SoruYaz? SonSoru;
    public (int Id, string Cevap, bool Kapat)? SonCevap;
    public int? SonKapatilanSoru, SonAcilanSoru, SonSilinenSoru;
    /// <summary>Soru yazanın adı ve rolü (sunucu token'dan alır).</summary>
    public string SoranAd = "Ortak (izleyici)";
    public string SoranRol = "viewer";

    public Task<IReadOnlyList<SoruDto>> SorularAsync(SoruDurumu? durum = null, SoruHedefTuru? hedefTur = null, int? hedefId = null, DateOnly? hafta = null)
        => PaketEOku<IReadOnlyList<SoruDto>>(() =>
        {
            SoruCagrilari.Add((durum, hedefTur, hedefId, hafta));
            return SorularListe
                .Where(s => durum is null || s.Durum == durum)
                .Where(s => hedefTur is null || s.HedefTur == hedefTur)
                .Where(s => hedefId is null || s.HedefId == hedefId)
                .Where(s => hafta is null || s.Hafta == hafta)
                .OrderBy(s => s.Durum).ThenByDescending(s => s.Id).ToList();
        });

    public Task<SoruOzetDto> SoruOzetAsync() => PaketEOku(() =>
    {
        var acik = SorularListe.Where(s => s.Durum == SoruDurumu.Acik).ToList();
        return new SoruOzetDto(acik.Count, acik.Count(s => s.Cevap is null), acik.OrderByDescending(s => s.Id).Take(5).ToList());
    });

    public Task<SoruDto> SoruSorAsync(SoruYaz g) => PaketEYaz(() =>
    {
        SonSoru = g;
        var ozet = g.HedefTur switch
        {
            SoruHedefTuru.Islem => $"İşlem #{g.HedefId}",
            SoruHedefTuru.Cek => $"Çek #{g.HedefId}",
            SoruHedefTuru.Hafta => $"Hafta {g.Hafta:dd.MM.yyyy}",
            _ => null,
        };
        var yeni = new SoruDto(SorularListe.Count == 0 ? 1 : SorularListe.Max(s => s.Id) + 1, g.HedefTur, g.HedefId, g.Hafta, ozet,
            g.Metin.Trim(), null, SoranAd, SoranRol, DateTime.UtcNow, null, null, null, SoruDurumu.Acik, null);
        SorularListe.Add(yeni);
        return yeni;
    });

    private SoruDto SoruDegistir(int id, Func<SoruDto, SoruDto> f)
    {
        var i = SorularListe.FindIndex(s => s.Id == id);
        if (i < 0) throw new KasaApiException(HttpStatusCode.NotFound);
        return SorularListe[i] = f(SorularListe[i]);
    }

    public Task<SoruDto> SoruCevaplaAsync(int id, string cevap, bool kapat) => PaketEYaz(() =>
    {
        SonCevap = (id, cevap, kapat);
        return SoruDegistir(id, s => s with
        {
            Cevap = cevap.Trim(), CevaplayanAd = "EMAR", CevaplanmaUtc = DateTime.UtcNow,
            Durum = kapat ? SoruDurumu.Kapali : s.Durum, KapanmaUtc = kapat ? DateTime.UtcNow : s.KapanmaUtc,
        });
    });

    public Task<SoruDto> SoruKapatAsync(int id) => PaketEYaz(() =>
    {
        SonKapatilanSoru = id;
        return SoruDegistir(id, s => s with { Durum = SoruDurumu.Kapali, KapanmaUtc = DateTime.UtcNow });
    });

    public Task<SoruDto> SoruAcAsync(int id) => PaketEYaz(() =>
    {
        SonAcilanSoru = id;
        return SoruDegistir(id, s => s with { Durum = SoruDurumu.Acik, KapanmaUtc = null });
    });

    public Task SoruSilAsync(int id) => PaketEYaz(() =>
    {
        SonSilinenSoru = id;
        SorularListe.RemoveAll(s => s.Id == id);
    });

    // ── Sistem ve risk ──
    public SistemRiskDto SistemRiski = new("ok", [], new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc), null, 50_000, 0);
    public int SistemRiskiCagri;
    public Task<SistemRiskDto> SistemRiskiAsync() => PaketEOku(() => { SistemRiskiCagri++; return SistemRiski; });
}
