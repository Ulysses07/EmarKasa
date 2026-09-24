using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

// Paket C (hızlı ve hatasız giriş) uçlarının sahtesi: canned yanıt + çağrı kaydı.
public sealed partial class SahteApi
{
    // ---- Gelişmiş arama ----
    public List<(IslemAramasi Arama, int Limit, int Offset)> AramaCagrilari = new();
    public IslemAramasi? SonAramaCsv;

    /// <summary>Sunucu gibi: IslemlerAsync'in ürettiği listeye gelişmiş süzgeci uygular ve dilimler.</summary>
    public async Task<IslemSayfasi> IslemAraAsync(IslemAramasi a, int limit, int offset)
    {
        AramaCagrilari.Add((a, limit, offset));
        var hepsi = await IslemlerAsync(a.Baslangic, a.Bitis, a.Kanal, a.Cari, limit, offset);
        var suzulen = hepsi
            .Where(i => a.NotAra is null || (i.Not ?? "").Contains(a.NotAra, StringComparison.CurrentCultureIgnoreCase))
            .Where(i => a.Tip is null || (a.Tip == GiderTipi.KrediKarti ? i.Tip == GiderTipi.KrediKarti || i.KrediKartiId is not null
                                                                        : i.Tip == a.Tip && i.KrediKartiId is null))
            .Where(i => a.KartId is null || i.KrediKartiId == a.KartId)
            .Where(i => a.MinTutar is null || i.TutarTl >= a.MinTutar)
            .Where(i => a.MaxTutar is null || i.TutarTl <= a.MaxTutar)
            .OrderBy(i => i.Tarih).ThenBy(i => i.Id).ToList();
        return new IslemSayfasi(suzulen.Skip(offset).Take(limit).ToList(), suzulen.Count);
    }

    public Task<IndirilenDosya> IslemAramaCsvAsync(IslemAramasi a)
    {
        SonAramaCsv = a;
        return CsvHatasi is not null ? Task.FromException<IndirilenDosya>(CsvHatasi) : Task.FromResult(CsvDosyasi);
    }

    // ---- Kayıt öncesi uyarılar ----
    public IReadOnlyList<IslemUyariDto> UyariListe = new List<IslemUyariDto>();
    public Exception? UyariHatasi;
    public List<(IslemYaz G, int? HaricId)> UyariCagrilari = new();

    public Task<IReadOnlyList<IslemUyariDto>> IslemUyarilariAsync(IslemYaz g, int? haricId = null)
    {
        UyariCagrilari.Add((g, haricId));
        return UyariHatasi is not null ? Task.FromException<IReadOnlyList<IslemUyariDto>>(UyariHatasi) : Task.FromResult(UyariListe);
    }

    // ---- Toplu yükleme ----
    public (IReadOnlyList<IslemYaz> Satirlar, bool YeniCariler)? SonToplu;
    /// <summary>Ayarlanırsa toplu yükleme bu sonucu döner (satır hatası senaryosu).</summary>
    public TopluIslemSonucu? TopluSonuc;

    public Task<TopluIslemSonucu> TopluIslemKaydetAsync(IReadOnlyList<IslemYaz> satirlar, bool yeniCarileriEkle)
    {
        SonToplu = (satirlar, yeniCarileriEkle);
        if (TopluSonuc is { } s) return Task.FromResult(s);
        var islemler = satirlar.Select((g, i) => new IslemDto(1000 + i, g.Tarih, g.Cari, g.TutarTl, g.Kanal, g.Tip, g.Not, g.KrediKartiId)).ToList();
        var yeni = yeniCarileriEkle
            ? satirlar.Select(g => g.Cari).Where(c => !CarilerListe.Any(k => string.Equals(k.Ad, c, StringComparison.CurrentCultureIgnoreCase)))
                .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList()
            : new List<string>();
        return Task.FromResult(new TopluIslemSonucu(true, islemler.Count, islemler.Sum(i => i.TutarTl), yeni, islemler, null, []));
    }

    // ---- Cariye göre öneri ----
    public Dictionary<string, IslemOneriDto> OneriListe = new(StringComparer.CurrentCultureIgnoreCase);
    public List<string> OneriCagrilari = new();
    public Exception? OneriHatasi;

    public Task<IslemOneriDto?> IslemOnerisiAsync(string cari)
    {
        OneriCagrilari.Add(cari);
        if (OneriHatasi is not null) return Task.FromException<IslemOneriDto?>(OneriHatasi);
        return Task.FromResult(OneriListe.TryGetValue(cari, out var o) ? o : null);
    }

    // ---- Korumalı gelen ----
    /// <summary>Sunucudaki gelen tutarları (dönem, kanal); korumalı kayıt beklenen tutarı buna göre denetler.</summary>
    public Dictionary<(DateOnly, string), decimal> GelenDeposu = new();
    public List<(GelenYaz G, decimal Beklenen)> KorumaliGelenCagrilari = new();
    /// <summary>Ayarlanırsa korumalı gelen yazımı bunu fırlatır (ör. kilitli ay 409'u: istemci çakışma saymaz).</summary>
    public Exception? GelenKorumaliHatasi;

    public Task<GelenKayitSonucu> GelenKorumaliKaydetAsync(GelenYaz g, decimal beklenenTutar)
    {
        KorumaliGelenCagrilari.Add((g, beklenenTutar));
        if (GelenKorumaliHatasi is not null) return Task.FromException<GelenKayitSonucu>(GelenKorumaliHatasi);
        var mevcut = GelenDeposu.TryGetValue((g.DonemStart, g.Kanal), out var m) ? m : 0m;
        if (mevcut != beklenenTutar)
            return Task.FromResult(new GelenKayitSonucu(false, null, mevcut, "Bu kanalın geleni siz açtıktan sonra değişmiş."));
        SonGelen = g;
        GelenDeposu[(g.DonemStart, g.Kanal)] = g.TutarTl;
        return Task.FromResult(new GelenKayitSonucu(true, new GelenDto(77, g.DonemStart, g.Kanal, g.TutarTl), g.TutarTl, null));
    }

    // ---- Gelen tablosu ----
    public Func<DateOnly?, GelenTablosuDto>? GelenTablosuUret;
    public List<DateOnly?> GelenTablosuCagrilari = new();
    public EksikGelenSayfasi EksikGelenler = new(new List<EksikGelenSatiriDto>(), 0);
    public int EksikGelenListesiCagri;

    public Task<GelenTablosuDto> GelenTablosuAsync(DateOnly? donemStart = null)
    {
        GelenTablosuCagrilari.Add(donemStart);
        if (YuklemeHatasi is not null) return Task.FromException<GelenTablosuDto>(YuklemeHatasi);
        return Task.FromResult(GelenTablosuUret?.Invoke(donemStart)
            ?? new GelenTablosuDto(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 14), null, []));
    }

    public Task<EksikGelenSayfasi> EksikGelenListesiAsync()
    {
        EksikGelenListesiCagri++;
        return YuklemeHatasi is not null ? Task.FromException<EksikGelenSayfasi>(YuklemeHatasi) : Task.FromResult(EksikGelenler);
    }

    // ---- Son silme ----
    public List<(string Tur, int KayitId)> SonSilmeCagrilari = new();
    /// <summary>Ayarlanırsa son silme araması bunu üretir; yoksa geri alınabilir bir satır (id 900 + kayıt).</summary>
    public Func<string, int, DegisiklikDto?>? SonSilmeUret;

    public Task<DegisiklikDto?> SonSilmeAsync(string tur, int kayitId)
    {
        SonSilmeCagrilari.Add((tur, kayitId));
        var d = SonSilmeUret is not null
            ? SonSilmeUret(tur, kayitId)
            : new DegisiklikDto(900 + kayitId, new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc), "editor", tur, kayitId,
                "Silindi", "silindi", "{}", null, false, null, GeriAlinabilir: true);
        return Task.FromResult(d);
    }
}
