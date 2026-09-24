using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket D uçlarının sahtesi (çek durumu/risk, atlananlar, hazır şablonlar, kart mutabakatı, sayım farkı).</summary>
public sealed partial class SahteApi
{
    // ---- Çek: tek dokunuş + risk ----
    public List<(int Id, CekDurumYaz G)> CekDurumCagrilari = new();
    public Exception? CekDurumHatasi;
    public CekRiskDto CekRiski = new(0m, 0, new List<CekRiskKalemiDto>(), new List<CekRiskKalemiDto>());
    public List<CekTuru?> CekRiskCagrilari = new();
    public Exception? CekRiskHatasi;

    /// <summary>Sunucu gibi: listedeki çeğin durumunu ve (tahsil/ödeme/ciroda) işlem tarihini değiştirir.</summary>
    public Task<CekDto> CekDurumAsync(int id, CekDurumYaz g)
    {
        if (CekDurumHatasi is not null) return Task.FromException<CekDto>(CekDurumHatasi);
        CekDurumCagrilari.Add((id, g));
        var c = CeklerListe.First(x => x.Id == id);
        DateOnly? tarih = g.Durum == CekDurumu.Karsiliksiz ? null : g.Tarih ?? new DateOnly(2026, 9, 24);
        var yeni = c with { Durum = g.Durum, IslemTarihi = tarih, CiroEdilenCari = g.CiroEdilenCari };
        CeklerListe = CeklerListe.Select(x => x.Id == id ? yeni : x).ToList();
        return Task.FromResult(yeni);
    }

    public Task<CekRiskDto> CekRiskAsync(CekTuru? tur = null)
    {
        CekRiskCagrilari.Add(tur);
        if (CekRiskHatasi is not null) return Task.FromException<CekRiskDto>(CekRiskHatasi);
        return YuklemeHatasi is not null ? Task.FromException<CekRiskDto>(YuklemeHatasi) : Task.FromResult(CekRiski);
    }

    // ---- Tekrarlayan: atlananlar + hazır şablonlar ----
    public IReadOnlyList<TekrarlayanAtlananDto> AtlananListe = new List<TekrarlayanAtlananDto>();
    public int AtlananCagri;
    public Exception? AtlananHatasi;
    public (int Id, DateOnly Ay)? SonAtlamaGeriAl;
    public IReadOnlyList<TekrarlayanHazirDto> HazirListe = new List<TekrarlayanHazirDto>();
    public string? SonHazirEkle;
    public Exception? HazirEkleHatasi;

    public Task<IReadOnlyList<TekrarlayanAtlananDto>> AtlananGiderlerAsync()
    {
        AtlananCagri++;
        if (AtlananHatasi is not null) return Task.FromException<IReadOnlyList<TekrarlayanAtlananDto>>(AtlananHatasi);
        return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<TekrarlayanAtlananDto>>(YuklemeHatasi) : Task.FromResult(AtlananListe);
    }

    public Task TekrarlayanAtlamayiGeriAlAsync(int id, DateOnly ay)
    {
        if (TekrarlayanYazHatasi is not null) return Task.FromException(TekrarlayanYazHatasi);
        SonAtlamaGeriAl = (id, ay);
        var a = AtlananListe.FirstOrDefault(x => x.TekrarlayanGiderId == id && x.Ay == ay);
        AtlananListe = AtlananListe.Where(x => x != a).ToList();
        if (a is not null)
            BekleyenListe = BekleyenListe.Append(new BekleyenGiderDto(id, a.Kalem, a.Kanal, 0m, a.Ay, a.Vade)).ToList();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TekrarlayanHazirDto>> TekrarlayanHazirlarAsync()
        => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<TekrarlayanHazirDto>>(YuklemeHatasi) : Task.FromResult(HazirListe);

    public Task<IReadOnlyList<TekrarlayanGiderDto>> TekrarlayanHazirEkleAsync(string kod)
    {
        if (HazirEkleHatasi is not null) return Task.FromException<IReadOnlyList<TekrarlayanGiderDto>>(HazirEkleHatasi);
        SonHazirEkle = kod;
        var h = HazirListe.First(x => x.Kod == kod);
        HazirListe = HazirListe.Select(x => x.Kod == kod ? x with { Eklendi = true } : x).ToList();
        var yeni = new TekrarlayanGiderDto(700 + TekrarlayanListe.Count, h.Ad, "Ortak", 0m, 28, true, new DateOnly(2026, 9, 1),
            TekrarSikligi.Aylik, null, true);
        TekrarlayanListe = TekrarlayanListe.Append(yeni).ToList();
        return Task.FromResult<IReadOnlyList<TekrarlayanGiderDto>>(new[] { yeni });
    }

    // ---- Kart mutabakatı ----
    public IReadOnlyList<KartDonemDto> KartDonemleriListe = new List<KartDonemDto>();
    public List<int> KartDonemleriCagrilari = new();
    /// <summary>Dönem detayı üreticisi (kart, kesim); ayarlanmadıysa <see cref="KartMutabakatDetay"/>.</summary>
    public Func<int, DateOnly, KartMutabakatDetayDto>? KartMutabakatUret;
    public KartMutabakatDetayDto? KartMutabakatDetay;
    public List<(int KartId, DateOnly Kesim)> KartMutabakatCagrilari = new();
    public KartMutabakatYaz? SonKartMutabakatKaydet;
    public Exception? KartMutabakatYazHatasi;
    public int? SonKartMutabakatSil;

    public Task<IReadOnlyList<KartDonemDto>> KartDonemleriAsync(int krediKartiId, int? adet = null)
    {
        KartDonemleriCagrilari.Add(krediKartiId);
        return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KartDonemDto>>(YuklemeHatasi) : Task.FromResult(KartDonemleriListe);
    }

    public Task<KartMutabakatDetayDto> KartMutabakatAsync(int krediKartiId, DateOnly kesim)
    {
        KartMutabakatCagrilari.Add((krediKartiId, kesim));
        if (YuklemeHatasi is not null) return Task.FromException<KartMutabakatDetayDto>(YuklemeHatasi);
        return Task.FromResult(KartMutabakatUret?.Invoke(krediKartiId, kesim) ?? KartMutabakatDetay!);
    }

    /// <summary>Sunucu gibi: tutarı ve tikleri detaya yazar; fark sıfırsa Mutabik, değilse FarkKabul ya da Acik.</summary>
    public Task<KartMutabakatDetayDto> KartMutabakatKaydetAsync(KartMutabakatYaz g)
    {
        if (KartMutabakatYazHatasi is not null) return Task.FromException<KartMutabakatDetayDto>(KartMutabakatYazHatasi);
        SonKartMutabakatKaydet = g;
        var d = KartMutabakatUret?.Invoke(g.KrediKartiId, g.Kesim) ?? KartMutabakatDetay!;
        var tikli = g.TikliIslemIdleri ?? Array.Empty<int>();
        var fark = g.EkstreTutari - d.HesaplananBorc;
        var durum = fark == 0m ? KartMutabakatDurumu.Mutabik : g.FarkKabul ? KartMutabakatDurumu.FarkKabul : KartMutabakatDurumu.Acik;
        KartMutabakatDetay = d with
        {
            MutabakatId = d.MutabakatId ?? 900,
            EkstreTutari = g.EkstreTutari,
            Fark = fark,
            Not = g.Not,
            Durum = durum,
            KayittakiHesaplanan = d.HesaplananBorc,
            Islemler = d.Islemler.Select(i => i with { Tikli = tikli.Contains(i.Id) }).ToList(),
            TiksizToplam = d.Islemler.Where(i => !tikli.Contains(i.Id)).Sum(i => i.Tutar),
        };
        KartMutabakatUret = null;
        return Task.FromResult(KartMutabakatDetay);
    }

    public Task KartMutabakatSilAsync(int id)
    {
        SonKartMutabakatSil = id;
        if (KartMutabakatDetay is { } d && d.MutabakatId == id)
            KartMutabakatDetay = d with { MutabakatId = null, EkstreTutari = null, Fark = null, Not = null, Durum = null, TiksizToplam = null, KayittakiHesaplanan = null };
        return Task.CompletedTask;
    }

    // ---- Kasa sayımı: fark durumu, neden değişti, son sayım ----
    public (int Id, SayimFarkYaz G)? SonSayimFarki;
    public Exception? SayimFarkiHatasi;
    public Func<int, NedenDegistiDto>? NedenDegistiUret;
    public List<int> NedenDegistiCagrilari = new();
    public SonSayimDto SonSayim = new(null, null, null);

    public Task<KasaSayimDto> SayimFarkiAsync(int id, SayimFarkYaz g)
    {
        if (SayimFarkiHatasi is not null) return Task.FromException<KasaSayimDto>(SayimFarkiHatasi);
        SonSayimFarki = (id, g);
        var s = KasaSayimlariListe.First(x => x.Id == id) with { FarkDurumu = g.Durum, FarkAciklamasi = g.Aciklama };
        KasaSayimlariListe = KasaSayimlariListe.Select(x => x.Id == id ? s : x).ToList();
        return Task.FromResult(s);
    }

    public Task<NedenDegistiDto> SayimNedenDegistiAsync(int id)
    {
        NedenDegistiCagrilari.Add(id);
        if (YuklemeHatasi is not null) return Task.FromException<NedenDegistiDto>(YuklemeHatasi);
        var s = KasaSayimlariListe.First(x => x.Id == id);
        return Task.FromResult(NedenDegistiUret?.Invoke(id)
            ?? new NedenDegistiDto(id, s.Tarih, s.HesaplananTutar, s.GuncelHesaplanan, s.GuncelHesaplanan - s.HesaplananTutar, new List<SayimDegisikligiDto>()));
    }

    public Task<SonSayimDto> SonSayimAsync()
        => YuklemeHatasi is not null ? Task.FromException<SonSayimDto>(YuklemeHatasi) : Task.FromResult(SonSayim);
}
