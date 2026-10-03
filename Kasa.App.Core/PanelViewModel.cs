using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

public partial class PanelViewModel : RaporViewModel
{
    private readonly IKasaApi _api;
    public PanelViewModel(IKasaApi api, BaglantiDurumu? baglanti = null, AuthViewModel? auth = null) : base(baglanti, auth) => _api = api;

    [ObservableProperty] private decimal _guncelKasa;
    [ObservableProperty] private decimal _buHaftaSonucu;
    [ObservableProperty] private decimal _buAySonucu;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DagilimBekliyor))]
    private decimal _dagilimBekleyenTutar;
    public bool DagilimBekliyor => DagilimBekleyenTutar > 0;
    public ObservableCollection<KanalKasaSatiri> Kanallar { get; } = new();

    public void KartBorclariniYansit(IReadOnlyList<TakipKanalPayi>? borclar)
    {
        for (var i = 0; i < Kanallar.Count; i++)
        {
            var satir = Kanallar[i];
            Kanallar[i] = satir with { KartBorcu = borclar is null || satir.KanalId is null ? null : borclar.Where(p => p.KanalId == satir.KanalId).Sum(p => Math.Max(0, p.Tutar)) };
        }
    }

    /// <summary>Son başarılı panel yüklemesinin başlattığı alt bölüm yüklemesi (<see cref="AltBolumleriBagla"/>).</summary>
    public Task AltBolumYuklemesi { get; private set; } = Task.CompletedTask;

    /// <summary>Kasalar alt bölümlerini (takip özeti, kasa kontrolü, çekler) panele bağlar. Takip özeti ve kanal eşikleri panelle aynı
    /// ana sayfa yanıtından gelir (bakiye, uyarı ve kart borcu aynı andan); eski sunucuda null'dır ve eski uçlardan ayrıca yüklenir.
    /// Her başarılı panel yüklemesinden sonra yenilenirler; panelin hatası onların son verisini silmez, kendi hataları da (tasarım
    /// 2026-10-02 §3). Kanal satırlarındaki kart borcu takip özetinin son başarılı kanal kart borçlarıdır: panel kanal satırlarını
    /// yeniledikten sonra (takip yüklemesi hata verse de) yeniden yazılır. Panelin kendi yüklemesi hata verince (panel kartları soluk)
    /// son verisi olan alt bölümler de eski işaretlenir; sonraki başarılı yükleme onları yeniler.</summary>
    public void AltBolumleriBagla(TakipOzetViewModel takip, KasaKontrolViewModel kontrol, CekOzetViewModel cekler)
    {
        Yuklendi += (_, _) => AltBolumYuklemesi = YukleAsync();
        // Panel son veri önbelleğinden gösterildi (yeniden kurulan sayfa, H-1): alt bölümler de kendi son verilerini gösterir;
        // istek atmazlar, panelin başarılı yüklemesi onları yeniler.
        OnbellektenGosterildi += (_, _) =>
        {
            takip.SonVeriyiGoster();
            kontrol.SonVeriyiGoster();
            cekler.SonVeriyiGoster();
            KartBorclariniYansit(takip.KanalKartBorclari);
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(VeriEski) || !VeriEski)
                return;
            foreach (OturumluViewModel bolum in new OturumluViewModel[] { takip, kontrol, cekler })
                if (bolum.SonGuncelleme is not null)
                    bolum.VeriEski = true;
        };
        takip.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(takip.KanalKartBorclari))
                KartBorclariniYansit(takip.KanalKartBorclari);
        };

        async Task YukleAsync()
        {
            await takip.PaneldenYukleAsync(TakipOzeti, TakipOzetiGunu);
            KartBorclariniYansit(takip.KanalKartBorclari);
            await kontrol.YukleAsync(KasaEsikleri);
            await cekler.YukleAsync();
        }
    }

    /// <summary>Ana sayfa isteğinin takip özeti ufku (gün); sayfa gün seçimini buraya da yazar.</summary>
    public int TakipGunu { get; set; } = 30;
    /// <summary>Son başarılı yüklemede panelle aynı anlık görüntüden gelen kanal eşikleri; eski sunucuda null (ayrıca yüklenir).</summary>
    public IReadOnlyList<KasaEsikDto>? KasaEsikleri { get; private set; }
    /// <summary>Son başarılı yüklemede panelle aynı anlık görüntüden gelen takip özeti; eski sunucuda null (ayrıca yüklenir).</summary>
    public TakipOzetDto? TakipOzeti { get; private set; }
    /// <summary><see cref="TakipOzeti"/>'nin istendiği gün ufku.</summary>
    public int TakipOzetiGunu { get; private set; }

    /// <summary>Takipte olmayan (geçişi yapılmamış) kart ve kredilerin kalıcı uyarısı (gap-tarihsel-spec-ve-emekli-web-7): hatırlatmaları
    /// eski kayıtlarla sınırlıdır. Kayıt yoksa (ya da eski sunucuda) boş.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TakipsizVar))]
    private string _takipsizUyari = "";
    public bool TakipsizVar => TakipsizUyari.Length > 0;

    public static string TakipsizMetni(IReadOnlyList<TakipsizKayitDto>? kayitlar) => kayitlar is not { Count: > 0 } ? ""
        : $"Kart ve kredi takibinde olmayan kayıtlar var: {string.Join(", ", kayitlar.Select(k => $"{k.Ad} ({(k.Kaynak == TakipKaynaklari.Kart ? "kart" : "kredi")})"))}. "
          + "Bu kayıtların hatırlatmaları eski kayıtlardan hesaplanır ve sınırlıdır; yeni ödeme ve güncel ekstre görünmez. Kartlar ve Krediler ekranından geçiş yapın.";

    /// <summary>Panel, kanal eşikleri ve takip özeti tek istekte (GET /api/rapor/ana-sayfa): bakiye, eşik uyarısı ve kart
    /// borcu aynı andan gelir. Değerler <see cref="RaporViewModel.VeriVar"/> true olmadan önce yazılır.</summary>
    public override Task YukleAsync()
    {
        var gun = TakipGunu;
        // Uyarı öbür panel alanları gibi son başarılı yüklemeden kalır (yenileme ve hata silmez).
        return RaporYukleAsync($"gun={gun}", ct => _api.AnaSayfaAsync(gun, ct), a =>
        {
            var p = a.Panel;
            GuncelKasa = p.GuncelKasa;
            BuHaftaSonucu = p.BuHaftaSonucu;
            BuAySonucu = p.BuAySonucu;
            DagilimBekleyenTutar = p.DagilimBekleyenTutar;
            Kanallar.Clear();
            foreach (var k in p.Kanallar)
                Kanallar.Add(new(k.Kanal, k.Bakiye, k.KanalId));
            KasaEsikleri = a.KasaEsikleri;
            TakipOzeti = a.TakipOzeti;
            TakipOzetiGunu = gun;
            TakipsizUyari = TakipsizMetni(a.TakipsizKayitlar);
        });
    }
}

public record KanalKasaSatiri(string Kanal, decimal Bakiye, int? KanalId, decimal? KartBorcu = null)
{
    public string KartBorcuMetni => KartBorcu is { } borc ? $"Kalan kart borcu {Bicim.Tl(borc)} ₺" : "Kart borcu bilgisi alınmadı.";
}
