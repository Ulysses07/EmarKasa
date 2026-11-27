using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

public partial class PanelViewModel : RaporViewModel
{
    private readonly IKasaApi _api;
    public PanelViewModel(IKasaApi api, BaglantiDurumu? baglanti = null, AuthViewModel? auth = null) : base(baglanti, auth) => _api = api;

    /// <summary>K-4: oturum değişince takipte olmayan kayıtların uyarısı da temizlenir (başka kullanıcının kart ve kredi adları
    /// görünmesin).</summary>
    protected override void SonVeriyiSifirla()
    {
        base.SonVeriyiSifirla();
        TakipsizUyari = "";
    }

    [ObservableProperty][NotifyPropertyChangedFor(nameof(GuncelKasaEksi))] private decimal _guncelKasa;
    /// <summary>Genel kasa eksidedir: özet kutusunda tutar ayrı renkle (HeroNeg) yazılır.</summary>
    public bool GuncelKasaEksi => GuncelKasa < 0;
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

    /// <summary>Kasalar alt bölümlerini (takip özeti, çekler) panele bağlar. Takip özeti panelle aynı ana sayfa yanıtından gelir
    /// (bakiye ve kart borcu aynı andan); eski sunucuda null'dır ve eski uçtan ayrıca yüklenir. Her başarılı panel yüklemesinden sonra
    /// yenilenirler; panelin hatası onların son verisini silmez, kendi hataları da (tasarım 2026-10-02 §3). Kanal satırlarındaki kart
    /// borcu takip özetinin son başarılı kanal kart borçlarıdır: panel kanal satırlarını yeniledikten sonra (takip yüklemesi hata verse
    /// de) yeniden yazılır. Panelin kendi yüklemesi hata verince (panel kartları soluk) son verisi olan alt bölümler de eski
    /// işaretlenir; sonraki başarılı yükleme onları yeniler. Kasa kontrolü ayrı sayfadır (docs/specs/2026-11-26-slop-temizligi.md §8).</summary>
    public void AltBolumleriBagla(TakipOzetViewModel takip, CekOzetViewModel cekler)
    {
        Yuklendi += (_, _) => AltBolumYuklemesi = YukleAsync();
        // Panel son veri önbelleğinden gösterildi (yeniden kurulan sayfa, H-1): alt bölümler de kendi son verilerini gösterir;
        // istek atmazlar, panelin başarılı yüklemesi onları yeniler.
        OnbellektenGosterildi += (_, _) =>
        {
            takip.SonVeriyiGoster();
            cekler.SonVeriyiGoster();
            KartBorclariniYansit(takip.KanalKartBorclari);
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(VeriEski) || !VeriEski)
                return;
            foreach (OturumluViewModel bolum in new OturumluViewModel[] { takip, cekler })
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
            await cekler.YukleAsync();
        }
    }

    /// <summary>Ana sayfa isteğinin takip özeti ufku (gün); sayfa gün seçimini buraya da yazar.</summary>
    public int TakipGunu { get; set; } = 30;
    /// <summary>Son başarılı yüklemede panelle aynı anlık görüntüden gelen kanal eşikleri (bakiye ile uyarı çelişmez); eski sunucuda
    /// null, kanal satırında alt limit işareti olmaz. Uyarıların dökümü Kasa kontrolü sayfasındadır.</summary>
    public IReadOnlyList<KasaEsikDto>? KasaEsikleri { get; private set; }

    /// <summary>Kanalın alt limiti: uyarı açık ve bakiye limitin altındaysa limit tutarı, değilse (ya da eşik bilinmiyorsa) null.</summary>
    private static decimal? AltLimiti(IReadOnlyList<KasaEsikDto>? esikler, int? kanalId)
        => esikler?.FirstOrDefault(e => e.KanalId == kanalId) is { Etkin: true, EsikAltinda: true } e ? e.Tutar : null;
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
                Kanallar.Add(new(k.Kanal, k.Bakiye, k.KanalId, AltLimit: AltLimiti(a.KasaEsikleri, k.KanalId)));
            KasaEsikleri = a.KasaEsikleri;
            TakipOzeti = a.TakipOzeti;
            TakipOzetiGunu = gun;
            TakipsizUyari = TakipsizMetni(a.TakipsizKayitlar);
        });
    }
}

/// <param name="AltLimit">Bakiye açık alt limit uyarısının altındaysa limit tutarı (ana sayfa yanıtının eşiklerinden); değilse null.</param>
public record KanalKasaSatiri(string Kanal, decimal Bakiye, int? KanalId, decimal? KartBorcu = null, decimal? AltLimit = null)
{
    /// <summary>Kanal satırının alt satırı: sıfır borçta boştur (satır gizlenir), borç bilinmiyorsa söyler.</summary>
    public string KartBorcuMetni => KartBorcu switch { null => "Kart borcu bilgisi alınmadı.", 0 => "", var borc => $"Kalan kart borcu {Bicim.Tl(borc.Value)} ₺" };

    /// <summary>Alt limit uyarısı (kırmızı alt satır): yalnız bakiye açık uyarının limitinin altındayken dolu.</summary>
    public string AltLimitMetni => AltLimit is { } limit ? $"Alt limitin altında: limit {Bicim.Tl(limit)} ₺" : "";
}
