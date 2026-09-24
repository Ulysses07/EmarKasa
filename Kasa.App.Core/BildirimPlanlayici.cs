using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Kısa (tek satır) bildirim: başlık, metin ve tıklanınca açılacak sayfa (<see cref="Yonlendirme.RotaCoz"/>).</summary>
public sealed record KisaBildirim(string Baslik, string Metin, string? Hedef);

/// <summary>Platform bildirimi (Windows toast). Test sahtesi gönderilenleri kaydeder.</summary>
public interface IKisaBildirim
{
    void Goster(KisaBildirim bildirim);
}

/// <summary>
/// Uygulama tarafı bildirimleri (e-posta yok). Uygulama açılışında (giriş sonrası) ve günlük arka plan
/// hatırlatıcısında çalışır; ikisi aynı yerel depoyu paylaştığından aynı bildirim iki kez gitmez.
/// <list type="bullet">
/// <item>Haftalık özet: her hafta Pazartesi 08:00'dan sonraki ilk çalıştırmada — "Geçen hafta kasa sonucu
/// +X, güncel kasa Y". Aynı anda (ayrı bildirim) vadesi geçmiş portföy çekleri.</item>
/// <item>Geçmişe dönük düzeltme: son bildirimden sonra geçmiş ayları etkileyen değişiklik olduysa; günde
/// en fazla bir kez. İlk çalıştırma yalnız başlangıç noktasını kaydeder (birikmiş eski satırlar bildirilmez).</item>
/// <item>Bugün yapılacaklar (yalnız editör): günün ilk çalıştırmasında — sabah uygulama açılınca ya da
/// 09:00 hatırlatıcısında, hangisi önce gelirse —, liste boş değilse; günde en fazla bir kez (ikisi aynı
/// depoyu paylaştığından biri gönderdiyse öbürü göndermez).</item>
/// </list>
/// Her tür Ayarlar'dan ayrı ayrı kapatılabilir (<see cref="BildirimAyarlariViewModel"/>). Okuma hatasında
/// o bildirim atlanır ve "gönderildi" işaretlenmez (sonraki çalıştırmada yeniden denenir).
/// </summary>
public sealed class BildirimPlanlayici
{
    /// <summary>Haftalık özetin en erken saati (Pazartesi, yerel saat).</summary>
    public static readonly TimeSpan HaftalikSaat = TimeSpan.FromHours(8);

    private readonly IKasaApi _api;
    private readonly IYerelDepo _depo;
    private readonly IKisaBildirim _bildirim;
    private readonly TimeProvider _zaman;

    public BildirimPlanlayici(IKasaApi api, IYerelDepo depo, IKisaBildirim bildirim, TimeProvider? zaman = null)
    {
        _api = api; _depo = depo; _bildirim = bildirim; _zaman = zaman ?? TimeProvider.System;
    }

    /// <param name="rol">Oturumdaki rol (bugün yapılacaklar yalnız editöre).</param>
    /// <returns>Gönderilen bildirimler.</returns>
    public async Task<IReadOnlyList<KisaBildirim>> CalistirAsync(Rol rol)
    {
        var gonderilen = new List<KisaBildirim>();
        var simdi = _zaman.GetLocalNow().DateTime;
        await Dene(() => HaftalikAsync(simdi, gonderilen));
        await Dene(() => GecmiseDonukAsync(DateOnly.FromDateTime(simdi), gonderilen));
        if (rol == Rol.Editor)
            await Dene(() => YapilacaklarAsync(DateOnly.FromDateTime(simdi), gonderilen));
        return gonderilen;
    }

    private static async Task Dene(Func<Task> is_)
    {
        try { await is_(); }
        catch (Exception) { /* ağ/oturum hatası: işaretlenmez, sonraki çalıştırmada yeniden denenir */ }
    }

    private void Gonder(KisaBildirim b, List<KisaBildirim> gonderilen)
    {
        _bildirim.Goster(b);
        gonderilen.Add(b);
    }

    /// <summary>Şu ana kadarki en son "Pazartesi 08:00"ın Pazartesi'si (haftalık özetin anahtarı).</summary>
    public static DateOnly HaftaAnahtari(DateTime yerelSimdi)
    {
        var bugun = DateOnly.FromDateTime(yerelSimdi);
        var pazartesi = bugun.AddDays(-(((int)bugun.DayOfWeek + 6) % 7));
        return yerelSimdi >= pazartesi.ToDateTime(TimeOnly.FromTimeSpan(HaftalikSaat)) ? pazartesi : pazartesi.AddDays(-7);
    }

    private async Task HaftalikAsync(DateTime simdi, List<KisaBildirim> gonderilen)
    {
        var hafta = HaftaAnahtari(simdi);
        if (_depo.OkuTarih(YerelAnahtarlar.SonHaftalikOzet) is { } son && son >= hafta) return;

        var ozetAcik = _depo.OkuBool(YerelAnahtarlar.BildirimHaftalikOzet, true);
        var cekAcik = _depo.OkuBool(YerelAnahtarlar.BildirimVadesiGecenCek, true);
        var gecenBas = hafta.AddDays(-7);
        var gecenSon = hafta.AddDays(-1);
        var bildirimler = new List<KisaBildirim>();

        if (ozetAcik)
        {
            var haftalikGorevi = _api.HaftalikAsync();
            var panelGorevi = _api.PanelAsync();
            await Task.WhenAll(haftalikGorevi, panelGorevi);
            // Ay sonunda ikiye bölünen hafta (29–30 Eyl + 1–5 Eki) iki dönemdir: ikisi toplanır.
            var sonuc = haftalikGorevi.Result.Where(h => h.Donem.Start >= gecenBas && h.Donem.Start <= gecenSon).Sum(h => h.KasaSonucu);
            bildirimler.Add(new KisaBildirim(
                $"Haftalık özet · {PanelMetin.Aralik(gecenBas, gecenSon)}",
                $"Geçen hafta kasa sonucu {PanelMetin.IsaretliTutar(sonuc)}, güncel kasa {PanelMetin.Tutar(panelGorevi.Result.GuncelKasa)}",
                "haftalik"));
        }
        if (cekAcik)
        {
            var ozet = await _api.CekOzetAsync();
            var bugun = DateOnly.FromDateTime(simdi);
            var gecen = ozet.VadesiGecenler.Where(c => c.VadeTarihi < bugun).OrderBy(c => c.VadeTarihi).ThenBy(c => c.Id).ToList();
            if (gecen.Count > 0)
            {
                var ilk = gecen[0];
                bildirimler.Add(new KisaBildirim(
                    $"Vadesi geçen {gecen.Count} çek",
                    $"{BuyukHarfle(PanelMetin.CekYonToplamlari(gecen))} · en eskisi {ilk.Kisi}, vade {PanelMetin.Gun(ilk.VadeTarihi, bugun)}",
                    "cekler"));
            }
        }
        // Okumaların hepsi başarılıysa gönderilir ve hafta işaretlenir (yarım bildirim tekrarlanmaz).
        foreach (var b in bildirimler) Gonder(b, gonderilen);
        _depo.YazTarih(YerelAnahtarlar.SonHaftalikOzet, hafta);
    }

    private static string BuyukHarfle(string metin)
        => metin.Length == 0 ? metin : char.ToUpper(metin[0], Kultur.Turkce) + metin[1..];

    private async Task GecmiseDonukAsync(DateOnly bugun, List<KisaBildirim> gonderilen)
    {
        if (!_depo.OkuBool(YerelAnahtarlar.BildirimGecmiseDonuk, true)) return;
        var sonId = _depo.OkuInt(YerelAnahtarlar.SonGecmiseDonukId);
        if (sonId is null)
        {
            // İlk çalıştırma: başlangıç noktası (eski satırlar bildirilmez).
            var ilk = await _api.GecmisOzetAsync(null);
            _depo.YazInt(YerelAnahtarlar.SonGecmiseDonukId, ilk.SonId);
            return;
        }
        if (_depo.OkuTarih(YerelAnahtarlar.SonGecmiseDonukGunu) == bugun) return;   // bugün zaten bildirildi

        var o = await _api.GecmisOzetAsync(sonId);
        if (sonId > o.SonId) { _depo.YazInt(YerelAnahtarlar.SonGecmiseDonukId, o.SonId); return; }   // geçmiş sıfırlandı
        if (o.GecmiseDonuk > 0)
        {
            var ilkSatir = o.GecmiseDonukSatirlar.FirstOrDefault();
            var metin = o.GecmiseDonuk == 1 && ilkSatir is not null
                ? ilkSatir.Ozet
                : $"{o.GecmiseDonuk} değişiklik geçmiş ayların rakamlarını değiştirdi" + (ilkSatir is null ? "" : $" · son: {ilkSatir.Ozet}");
            Gonder(new KisaBildirim("Geçmişe dönük düzeltme", metin, "gecmis"), gonderilen);
            _depo.YazTarih(YerelAnahtarlar.SonGecmiseDonukGunu, bugun);
        }
        _depo.YazInt(YerelAnahtarlar.SonGecmiseDonukId, o.SonId);
    }

    private async Task YapilacaklarAsync(DateOnly bugun, List<KisaBildirim> gonderilen)
    {
        if (!_depo.OkuBool(YerelAnahtarlar.BildirimBugunYapilacaklar, true)) return;
        if (_depo.OkuTarih(YerelAnahtarlar.SonYapilacaklar) == bugun) return;

        var bekleyenGorevi = TekrarlayanYukleme.Oku(_api.BekleyenGiderlerAsync);
        var cekGorevi = _api.CekOzetAsync();
        var kartGorevi = _api.KrediKartlariAsync();
        var sayimGorevi = _api.KasaSayimlariAsync();
        var eksikGorevi = EksikGelenlerAsync();
        await Task.WhenAll(bekleyenGorevi, cekGorevi, kartGorevi, sayimGorevi, eksikGorevi);
        var liste = YapilacakListesi.Olustur(bekleyenGorevi.Result, cekGorevi.Result, kartGorevi.Result,
            eksikGorevi.Result, sayimGorevi.Result, bugun);
        if (liste.Count > 0)
            Gonder(new KisaBildirim($"Bugün yapılacaklar ({liste.Count})", YapilacakListesi.BildirimMetni(liste), "panel"), gonderilen);
        _depo.YazTarih(YerelAnahtarlar.SonYapilacaklar, bugun);
    }

    // Eski sunucu eksik gelen ucunu bilmiyorsa (404) liste yine üretilir, yalnız o satırlar olmadan.
    private async Task<IReadOnlyList<EksikGelenDto>?> EksikGelenlerAsync()
    {
        try { return await _api.EksikGelenlerAsync(); }
        catch (KasaApiException ex) when (ex.DurumKodu == System.Net.HttpStatusCode.NotFound) { return null; }
    }
}

/// <summary>
/// Ayarlar → Bildirimler: her bildirim türü ayrı açılıp kapatılır (bu cihazda saklanır; varsayılan açık).
/// </summary>
public partial class BildirimAyarlariViewModel : ObservableObject
{
    private readonly IYerelDepo _depo;

    public BildirimAyarlariViewModel(IYerelDepo? depo = null)
    {
        _depo = depo ?? new BellekYerelDepo();
        _haftalikOzet = _depo.OkuBool(YerelAnahtarlar.BildirimHaftalikOzet, true);
        _vadesiGecenCek = _depo.OkuBool(YerelAnahtarlar.BildirimVadesiGecenCek, true);
        _gecmiseDonuk = _depo.OkuBool(YerelAnahtarlar.BildirimGecmiseDonuk, true);
        _bugunYapilacaklar = _depo.OkuBool(YerelAnahtarlar.BildirimBugunYapilacaklar, true);
        _kartHatirlatma = _depo.OkuBool(YerelAnahtarlar.BildirimKartHatirlatma, true);
    }

    /// <summary>Pazartesi sabahı geçen haftanın kasa sonucu ve güncel kasa.</summary>
    [ObservableProperty] private bool _haftalikOzet;
    /// <summary>Haftalık özetle birlikte: vadesi geçmiş, hâlâ portföyde bekleyen çekler.</summary>
    [ObservableProperty] private bool _vadesiGecenCek;
    /// <summary>Geçmiş ayların rakamlarını değiştiren düzeltmeler (günde en fazla bir kez).</summary>
    [ObservableProperty] private bool _gecmiseDonuk;
    /// <summary>Sabah "Bugün yapılacaklar" özeti (yalnız editör).</summary>
    [ObservableProperty] private bool _bugunYapilacaklar;
    /// <summary>Kredi kartı kesim / son ödeme hatırlatmaları.</summary>
    [ObservableProperty] private bool _kartHatirlatma;

    partial void OnHaftalikOzetChanged(bool value) => _depo.YazBool(YerelAnahtarlar.BildirimHaftalikOzet, value);
    partial void OnVadesiGecenCekChanged(bool value) => _depo.YazBool(YerelAnahtarlar.BildirimVadesiGecenCek, value);
    partial void OnGecmiseDonukChanged(bool value) => _depo.YazBool(YerelAnahtarlar.BildirimGecmiseDonuk, value);
    partial void OnBugunYapilacaklarChanged(bool value) => _depo.YazBool(YerelAnahtarlar.BildirimBugunYapilacaklar, value);
    partial void OnKartHatirlatmaChanged(bool value) => _depo.YazBool(YerelAnahtarlar.BildirimKartHatirlatma, value);
}
