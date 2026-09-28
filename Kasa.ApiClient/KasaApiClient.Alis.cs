namespace Kasa.ApiClient;

public sealed partial class KasaApiClient : IAlisApi
{
    public Task<IReadOnlyList<AlisKanalDto>> AlisKanallariAsync() => GetAsync<IReadOnlyList<AlisKanalDto>>("api/alis/kanallar");
    public Task<IReadOnlyList<AlisDto>> AlislarAsync() => GetAsync<IReadOnlyList<AlisDto>>("api/alis");
    public Task<AlisDto> AlisOlusturAsync(AlisYaz g) => GonderJsonAsync<AlisDto>(HttpMethod.Post, "api/alis", g);
    public Task<AlisDto> AlisGuncelleAsync(int id, AlisYaz g) => GonderJsonAsync<AlisDto>(HttpMethod.Put, $"api/alis/{id}", g);
    public Task<AlisDto> AlisGonderAsync(int id, AlisDurumYaz g) => GonderJsonAsync<AlisDto>(HttpMethod.Post, $"api/alis/{id}/gonder", g);
    public Task<AlisDto> AlisOnaylaAsync(int id, AlisDurumYaz g) => GonderJsonAsync<AlisDto>(HttpMethod.Post, $"api/alis/{id}/onayla", g);
    public Task<AlisDto> AlisIadeAsync(int id, AlisDurumYaz g) => GonderJsonAsync<AlisDto>(HttpMethod.Post, $"api/alis/{id}/iade", g);
    public Task<AlisDto> AlisOdemeKaydetAsync(int id, AlisOdemeYaz g) => GonderJsonAsync<AlisDto>(HttpMethod.Post, $"api/alis/{id}/odemeler", g);
    /// <summary>Uç bir kez yok yanıtı verdiyse (eski sunucu) sonraki aramalar doğrudan eski gider listesine gider; uygulama
    /// yeniden başlatılınca yeniden denenir.</summary>
    private volatile bool _baglanabilirGiderUcuYok;

    public async Task<BaglanabilirGiderSayfasi> BaglanabilirGiderlerAsync(string? arama = null, decimal? tutar = null, DateOnly? baslangic = null, DateOnly? bitis = null, string? imlec = null, int? limit = null, decimal? aramaTutari = null)
    {
        if (!_baglanabilirGiderUcuYok)
        {
            var q = new List<string>();
            if (!string.IsNullOrWhiteSpace(arama)) q.Add("arama=" + Uri.EscapeDataString(arama.Trim()));
            if (aramaTutari is { } at) q.Add("aramaTutari=" + at.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            if (tutar is { } t) q.Add("tutar=" + t.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            if (baslangic is { } b) q.Add($"baslangic={b:yyyy-MM-dd}");
            if (bitis is { } s) q.Add($"bitis={s:yyyy-MM-dd}");
            if (!string.IsNullOrEmpty(imlec)) q.Add("imlec=" + Uri.EscapeDataString(imlec));
            if (limit is { } l) q.Add("limit=" + l.ToString(System.Globalization.CultureInfo.InvariantCulture));
            try { return await GetAsync<BaglanabilirGiderSayfasi>("api/alis/baglanabilir-giderler" + (q.Count > 0 ? "?" + string.Join("&", q) : "")); }
            // Eski sunucu yolu tanımaz: 404 ya da yalnız PUT kabul eden /api/alis/{id:int} deseni yüzünden 405.
            catch (KasaApiException e) when (e.DurumKodu is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed) { _baglanabilirGiderUcuYok = true; }
        }
        // Eski sunucu (uç yok): yeni masaüstü editörün Alışlar ekranını düşürmez. Eski davranışla bütün gider listesi okunur;
        // ödeme ucunun kabul etmeyeceği giderler (alışa, aylık gidere ya da ekstre kaydına bağlı, pozitif olmayan, cari ya da kart
        // dışı) ile arama (metin ya da arama tutarı) ve tutar süzgeci istemcide uygulanır. Hepsi tek sayfadır (devam yok).
        if (!string.IsNullOrEmpty(imlec)) return new(Array.Empty<BaglanabilirGiderDto>(), null, false);
        var aranan = arama?.Trim();
        var ogeler = (await IslemlerAsync(baslangic, bitis))
            .Where(i => i.TutarTl > 0 && i.AlisId is null && i.AylikGiderOdemeId is null && i.EkstreKayitId is null && i.Tip is GiderTipi.Cari or GiderTipi.KrediKarti)
            .Where(i => tutar is null || i.TutarTl == tutar)
            .Where(i => string.IsNullOrEmpty(aranan) && aramaTutari is null || i.TutarTl == aramaTutari
                || !string.IsNullOrEmpty(aranan) && (i.Cari.Contains(aranan, StringComparison.OrdinalIgnoreCase) || i.Not?.Contains(aranan, StringComparison.OrdinalIgnoreCase) == true))
            .OrderByDescending(i => i.Tarih).ThenByDescending(i => i.Id)
            .Select(i => new BaglanabilirGiderDto(i.Id, i.Tarih, i.Cari, i.TutarTl, i.Kanal, null, i.Tip, i.Not, i.KrediKartiId))
            .ToList();
        return new(ogeler, null, false);
    }
    public Task<IReadOnlyList<AliciDto>> AlicilarAsync() => GetAsync<IReadOnlyList<AliciDto>>("api/alicilar");
    public Task<AliciDto> AliciOlusturAsync(AliciYaz g) => GonderJsonAsync<AliciDto>(HttpMethod.Post, "api/alicilar", g);
    public Task<AliciDto> AliciGuncelleAsync(int id, AliciYaz g) => GonderJsonAsync<AliciDto>(HttpMethod.Put, $"api/alicilar/{id}", g);
}
