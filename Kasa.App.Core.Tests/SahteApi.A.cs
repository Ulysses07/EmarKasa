using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

// Paket A — nakit tahmini, eksik gelen ve geçmiş özeti sahteleri.
public sealed partial class SahteApi
{
    /// <summary>Tahmin yanıtı; null ise panel kasasıyla düz bir tahmin üretilir.</summary>
    public NakitTahminDto? NakitTahmin;
    /// <summary>Ayarlanırsa tahmin yanıtını bu üretir (argümanlar: gün, hariç çekler).</summary>
    public Func<int, IReadOnlyCollection<int>?, Task<NakitTahminDto>>? NakitTahminUret;
    /// <summary>Ayarlanırsa yalnız tahmin okuması bu istisnayı fırlatır.</summary>
    public Exception? NakitTahminHatasi;
    public List<(int Gun, IReadOnlyList<int> Haric)> NakitTahminCagrilari = new();

    public IReadOnlyList<EksikGelenDto> EksikGelenlerListe = new List<EksikGelenDto>();
    public Exception? EksikGelenHatasi;
    public int EksikGelenCagri;

    /// <summary>Özet yanıtı; null ise <see cref="GecmisListe"/>'den sunucu gibi hesaplanır.</summary>
    public GecmisOzetDto? GecmisOzetSonuc;
    public Exception? GecmisOzetHatasi;
    public List<int?> GecmisOzetCagrilari = new();

    public Task<NakitTahminDto> NakitTahminAsync(int gun, IReadOnlyCollection<int>? haricCekler = null)
    {
        NakitTahminCagrilari.Add((gun, haricCekler?.Order().ToList() ?? new List<int>()));
        if (NakitTahminUret is not null) return NakitTahminUret(gun, haricCekler);
        var hata = NakitTahminHatasi ?? YuklemeHatasi;
        if (hata is not null) return Task.FromException<NakitTahminDto>(hata);
        return Task.FromResult(NakitTahmin ?? DuzTahmin(gun, Panel?.GuncelKasa ?? 0m, new DateOnly(2026, 9, 24)));
    }

    public static NakitTahminDto DuzTahmin(int gun, decimal kasa, DateOnly bugun)
    {
        var gunler = Enumerable.Range(0, gun + 1)
            .Select(i => new TahminGunuDto(bugun.AddDays(i), 0m, 0m, kasa, new List<TahminKalemiDto>())).ToList();
        return new NakitTahminDto(bugun, gun, kasa, gunler, bugun, kasa, kasa, 0m, 0m, new List<TahminKalemiDto>());
    }

    public Task<IReadOnlyList<EksikGelenDto>> EksikGelenlerAsync()
    {
        EksikGelenCagri++;
        var hata = EksikGelenHatasi ?? YuklemeHatasi;
        return hata is not null ? Task.FromException<IReadOnlyList<EksikGelenDto>>(hata) : Task.FromResult(EksikGelenlerListe);
    }

    public Task<GecmisOzetDto> GecmisOzetAsync(int? sonId = null)
    {
        GecmisOzetCagrilari.Add(sonId);
        var hata = GecmisOzetHatasi ?? YuklemeHatasi;
        if (hata is not null) return Task.FromException<GecmisOzetDto>(hata);
        if (GecmisOzetSonuc is not null) return Task.FromResult(GecmisOzetSonuc);
        var enYeni = GecmisListe.OrderByDescending(d => d.Id).FirstOrDefault();
        if (enYeni is null) return Task.FromResult(new GecmisOzetDto(0, null, 0, 0, new List<GecmisOzetSatiriDto>()));
        if (sonId is not { } s) return Task.FromResult(new GecmisOzetDto(enYeni.Id, enYeni.ZamanUtc, 0, 0, new List<GecmisOzetSatiriDto>()));
        var yeni = GecmisListe.Where(d => d.Id > s).OrderByDescending(d => d.Id).ToList();
        var donuk = yeni.Where(d => d.GecmiseDonuk).ToList();
        return Task.FromResult(new GecmisOzetDto(enYeni.Id, enYeni.ZamanUtc, yeni.Count, donuk.Count,
            donuk.Take(5).Select(d => new GecmisOzetSatiriDto(d.Id, d.ZamanUtc, d.Tur, d.Ozet)).ToList()));
    }
}
