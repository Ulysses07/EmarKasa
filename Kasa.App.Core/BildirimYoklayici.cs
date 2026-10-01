using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Masaüstü Windows bildirimlerinin çekirdeği (tasarım 2026-09-30 masaüstü bildirimleri §1): sunucunun bildirim listesini
/// (IBildirimApi.BildirimlerAsync) çeker; bugünün okunmamış bildirimlerinden bu bilgisayarda henüz gösterilmemiş olanları depodan
/// ayırıp gösterir ve okunmamış sayısını (menü rozeti) tutar. Kendi hatırlatma hesabı yapmaz. Göstermek okundu yapmaz; yalnız tıklama
/// okundu işaretler. Liste bugünle sınırlanır: sunucu web push'ta da yalnız bugünün bildirimlerini gönderir, eski okunmamışlar ilk
/// kurulumda bir kerede gösterilmez. İptal bilgisi listede yoktur (BildirimDto'da alan yok): sunucu iptal edilmiş bildirimi yalnız
/// okunmuş ya da bir tarayıcıya gönderilmişse döndürür. Uygulama içinde (BildirimNobetcisi, 5 dakikada bir) ve pencere açmadan çalışan
/// görevde (BildirimKontrolu) aynı sınıf kullanılır. Hiçbir hata dışarı çıkmaz.
/// </summary>
public sealed partial class BildirimYoklayici(IBildirimApi api, IBildirimGosterici gosterici, IGosterilenBildirimDeposu depo,
    IBildirimAyari ayar, TimeProvider? saat = null) : ObservableObject
{
    private readonly TimeProvider _saat = saat ?? TimeProvider.System;

    /// <summary>Son listedeki okunmamış bildirim sayısı (menü rozeti); oturum kapanınca ya da ayar kapanınca 0.</summary>
    [ObservableProperty] private int _okunmamis;

    /// <summary>Son bakmanın sonucu (durum satırı); henüz bakılmadıysa null.</summary>
    [ObservableProperty] private YoklamaSonucu? _sonSonuc;

    /// <summary>Editör oturumunda ve ayar açıkken bir kez bakar; aksi halde hiçbir şey yapmaz ve null döner.</summary>
    public async Task<YoklamaSonucu?> YoklaAsync(bool editorOturumu)
    {
        if (!editorOturumu || !ayar.Acik)
            return null;
        IReadOnlyList<BildirimDto> liste;
        try
        {
            liste = await api.BildirimlerAsync();
        }
        catch (KasaApiException e) when (e.DurumKodu is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return Sonuc(YoklamaDurumu.OturumGecersiz, 0);
        }
        catch (KasaApiException)
        {
            return Sonuc(YoklamaDurumu.SunucuHatasi, 0);
        }
        catch (Exception)
        {
            return Sonuc(YoklamaDurumu.SunucuyaUlasilamadi, 0);
        }
        Okunmamis = liste.Count(b => !b.Okundu);
        var bugun = DateOnly.FromDateTime(_saat.GetLocalNow().DateTime);
        var adaylar = liste.Where(b => !b.Okundu && b.Tarih == bugun).OrderBy(b => b.Id).ToList();
        IReadOnlyList<int> yeniler;
        try
        {
            yeniler = adaylar.Count == 0 ? [] : depo.YenileriAyir(adaylar.Select(b => b.Id).ToList());
        }
        catch (Exception)
        {
            return Sonuc(YoklamaDurumu.YerelKayitHatasi, 0);
        }
        foreach (var bildirim in adaylar.Where(b => yeniler.Contains(b.Id)))
        {
            try
            {
                gosterici.Goster(bildirim);
            }
            catch (Exception)
            {
                // Tek bildirimin gösterilememesi ötekileri durdurmaz; bildirim listede ve telefonda görünmeye devam eder.
            }
        }
        return Sonuc(YoklamaDurumu.Basarili, yeniler.Count);
    }

    /// <summary>Tıklanan bildirimi sunucuda okundu işaretler (hata yutulur; sonraki bakmada sayı düzelir) ve açılacak Shell rotasını
    /// döner. Editör oturumu yoksa hiçbir şey yapmaz ve null döner.</summary>
    public async Task<string?> TiklandiAsync(BildirimTiklamasi tiklama, bool editorOturumu)
    {
        if (!editorOturumu)
            return null;
        if (tiklama.BildirimId is { } kimlik)
        {
            try
            {
                await api.BildirimOkunduAsync(kimlik);
                Okunmamis = Math.Max(0, Okunmamis - 1);
            }
            catch (Exception)
            {
                // Okundu işareti bir sonraki tıklamada ya da Bildirimler ekranında verilebilir; sayfa yine açılır.
            }
        }
        return BildirimHedefi.Rota(tiklama.Hedef);
    }

    /// <summary>Bildirimler ekranı listeyi yükleyince ya da okundu işaretleyince okunmamış sayısını bildirir.</summary>
    public void OkunmamisBildir(int sayi) => Okunmamis = Math.Max(0, sayi);

    /// <summary>Oturum kapandı, rol editör değil ya da ayar kapandı: rozet gizlenir, durum satırı boşalır.</summary>
    public void Sifirla()
    {
        Okunmamis = 0;
        SonSonuc = null;
    }

    private YoklamaSonucu Sonuc(YoklamaDurumu durum, int yeni)
    {
        var sonuc = new YoklamaSonucu(_saat.GetLocalNow(), durum, yeni);
        SonSonuc = sonuc;
        return sonuc;
    }
}
