using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

// Paket F: belge, ekler, fatura takibi, muhasebeci listesi ve POS sahteleri.
public sealed partial class SahteApi
{
    // ---- ekler
    public Dictionary<int, List<EkDto>> EklerSozluk = new();
    public List<(int IslemId, string Ad, byte[] Icerik)> EkYuklemeleri = new();
    /// <summary>Adı bu kümede olan dosyanın yüklemesi hata verir.</summary>
    public HashSet<string> YuklenemeyenEkler = new();
    public List<int> SilinenEkler = new();
    public List<int> IndirilenEkler = new();
    public int EklerCagri;
    private int _ekId = 500;

    public Task<IReadOnlyList<EkDto>> EklerAsync(int islemId)
    {
        EklerCagri++;
        if (YuklemeHatasi is not null) return Task.FromException<IReadOnlyList<EkDto>>(YuklemeHatasi);
        return Task.FromResult<IReadOnlyList<EkDto>>(EklerSozluk.GetValueOrDefault(islemId)?.ToList() ?? new List<EkDto>());
    }

    public Task<EkDto> EkYukleAsync(int islemId, string dosyaAdi, byte[] icerik)
    {
        if (YuklenemeyenEkler.Contains(dosyaAdi))
            return Task.FromException<EkDto>(new KasaApiException(System.Net.HttpStatusCode.BadRequest, "Dosyanın içeriği uzantısıyla uyuşmuyor."));
        EkYuklemeleri.Add((islemId, dosyaAdi, icerik));
        var ek = new EkDto(++_ekId, islemId, dosyaAdi, dosyaAdi.EndsWith(".pdf") ? "application/pdf" : "image/jpeg", icerik.Length,
            new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc));
        if (!EklerSozluk.TryGetValue(islemId, out var l)) EklerSozluk[islemId] = l = new List<EkDto>();
        l.Add(ek);
        return Task.FromResult(ek);
    }

    public Task<IndirilenDosya> EkIndirAsync(int ekId)
    {
        IndirilenEkler.Add(ekId);
        var ek = EklerSozluk.Values.SelectMany(x => x).FirstOrDefault(e => e.Id == ekId);
        return Task.FromResult(new IndirilenDosya(ek?.Ad ?? $"ek-{ekId}", [1, 2, 3]));
    }

    public Task EkSilAsync(int ekId)
    {
        SilinenEkler.Add(ekId);
        foreach (var l in EklerSozluk.Values) l.RemoveAll(e => e.Id == ekId);
        return Task.CompletedTask;
    }

    // ---- belge / fatura takibi
    public List<(int IslemId, BelgeBilgisi Belge)> BelgeGuncellemeleri = new();
    public Task<IslemDto> BelgeGuncelleAsync(int islemId, BelgeBilgisi belge)
    {
        BelgeGuncellemeleri.Add((islemId, belge));
        return Task.FromResult(new IslemDto(islemId, new DateOnly(2026, 9, 1), "X", 1m, "MEZAT", GiderTipi.Cari, null)
        { BelgeTuru = belge.Tur, BelgeNo = belge.No, FaturaBekleniyor = belge.FaturaBekleniyor });
    }

    public FaturaTakibiDto? FaturaTakibi;
    public List<(int Yil, int Ay)> FaturaTakibiCagrilari = new();
    public Task<FaturaTakibiDto> FaturaTakibiAsync(int yil, int ay)
    {
        FaturaTakibiCagrilari.Add((yil, ay));
        if (YuklemeHatasi is not null) return Task.FromException<FaturaTakibiDto>(YuklemeHatasi);
        return Task.FromResult(FaturaTakibi ?? new FaturaTakibiDto(yil, ay, [], 0m, 0, [], 0m, 0, 0m, 0));
    }

    public (int Yil, int Ay)? SonMuhasebeciCsv;
    public Task<IndirilenDosya> MuhasebeciCsvAsync(int yil, int ay)
    {
        SonMuhasebeciCsv = (yil, ay);
        return Task.FromResult(new IndirilenDosya($"kasa-muhasebeci-{yil:D4}-{ay:D2}.csv", [0xEF, 0xBB, 0xBF]));
    }

    // ---- POS
    public List<PosTanimDto> PosTanimlariListe = new();
    public List<PosSatisDto> PosSatislariListe = new();
    public PosOzetDto? PosOzet;
    public PosTanimYaz? SonPosTanimOlustur;
    public (int Id, PosTanimYaz G)? SonPosTanimGuncelle;
    public int? SonPosTanimSil;
    public PosSatisYaz? SonPosSatisOlustur;
    public (int Id, PosSatisYaz G)? SonPosSatisGuncelle;
    public int? SonPosSatisSil;
    public (DateOnly? Bas, DateOnly? Bit, int? PosId)? SonPosSatisFiltresi;
    public (int Yil, int Ay)? SonPosOzet;
    public Exception? PosYazHatasi;

    public Task<IReadOnlyList<PosTanimDto>> PosTanimlariAsync()
        => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<PosTanimDto>>(YuklemeHatasi) : Task.FromResult<IReadOnlyList<PosTanimDto>>(PosTanimlariListe.ToList());

    public Task<PosTanimDto> PosTanimOlusturAsync(PosTanimYaz g)
    {
        if (PosYazHatasi is not null) return Task.FromException<PosTanimDto>(PosYazHatasi);
        SonPosTanimOlustur = g;
        var t = new PosTanimDto(100 + PosTanimlariListe.Count, g.Ad, g.Saglayici, g.KanalId, null, g.KomisyonOrani, g.BlokajGunu, g.Aktif);
        PosTanimlariListe.Add(t);
        return Task.FromResult(t);
    }

    public Task<PosTanimDto> PosTanimGuncelleAsync(int id, PosTanimYaz g)
    {
        if (PosYazHatasi is not null) return Task.FromException<PosTanimDto>(PosYazHatasi);
        SonPosTanimGuncelle = (id, g);
        return Task.FromResult(new PosTanimDto(id, g.Ad, g.Saglayici, g.KanalId, null, g.KomisyonOrani, g.BlokajGunu, g.Aktif));
    }

    public Task PosTanimSilAsync(int id) { SonPosTanimSil = id; PosTanimlariListe.RemoveAll(t => t.Id == id); return Task.CompletedTask; }

    public Task<IReadOnlyList<PosSatisDto>> PosSatislariAsync(DateOnly? baslangic = null, DateOnly? bitis = null, int? posId = null)
    {
        SonPosSatisFiltresi = (baslangic, bitis, posId);
        return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<PosSatisDto>>(YuklemeHatasi) : Task.FromResult<IReadOnlyList<PosSatisDto>>(PosSatislariListe.ToList());
    }

    public Task<PosSatisDto> PosSatisOlusturAsync(PosSatisYaz g)
    {
        if (PosYazHatasi is not null) return Task.FromException<PosSatisDto>(PosYazHatasi);
        SonPosSatisOlustur = g;
        return Task.FromResult(new PosSatisDto(900, g.Tarih, g.PosId, "POS", "MEZAT", g.BrutTutar, g.KomisyonOrani ?? 0m, 0m, g.BrutTutar,
            g.BlokajGunu ?? 0, g.Tarih, false, g.Not));
    }

    public Task<PosSatisDto> PosSatisGuncelleAsync(int id, PosSatisYaz g)
    {
        if (PosYazHatasi is not null) return Task.FromException<PosSatisDto>(PosYazHatasi);
        SonPosSatisGuncelle = (id, g);
        return Task.FromResult(new PosSatisDto(id, g.Tarih, g.PosId, "POS", "MEZAT", g.BrutTutar, g.KomisyonOrani ?? 0m, 0m, g.BrutTutar,
            g.BlokajGunu ?? 0, g.Tarih, false, g.Not));
    }

    public Task PosSatisSilAsync(int id) { SonPosSatisSil = id; return Task.CompletedTask; }

    public Task<PosOzetDto> PosOzetAsync(int yil, int ay)
    {
        SonPosOzet = (yil, ay);
        if (YuklemeHatasi is not null) return Task.FromException<PosOzetDto>(YuklemeHatasi);
        return Task.FromResult(PosOzet ?? new PosOzetDto(yil, ay, new DateOnly(2026, 9, 24), 0m, 0, [], [], 0m, 0m, 0m));
    }
}
