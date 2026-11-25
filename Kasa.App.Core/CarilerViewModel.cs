using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;
using CekKurallari = Kasa.Core.CekKurallari;

namespace Kasa.App.Core;

/// <summary>Cari listesinin satırı.</summary>
public sealed class CariSatiri(ErpCariDto veri)
{
    public ErpCariDto Veri { get; } = veri;
    public string Baslik => Veri.Kod is { } kod ? $"{Veri.Ad} · {kod}" : Veri.Ad;
    public string Ozet => $"Borç {Bicim.Tl(Veri.Borc)} ₺ · Alacak {Bicim.Tl(Veri.Alacak)} ₺ · {CariMetni.Bakiye(Veri.Bakiye)} · {Veri.HareketSayisi} hareket, son {Veri.SonTarih:dd.MM.yyyy}"
        + (Veri.Bekleyen > 0 ? $" · {Veri.Bekleyen} bekliyor" : "");
}

/// <summary>Carinin hareket satırı.</summary>
public sealed class CariHareketSatiri(ErpCariHareketDto veri)
{
    public ErpCariHareketDto Veri { get; } = veri;
    public string Baslik => string.Join(" · ", new[] { $"{Veri.Tarih:dd.MM.yyyy}", Veri.IslemTuru, Veri.BelgeNo is { } no ? "Belge " + no : null }.OfType<string>());
    public string Ozet => string.Join(" · ", new[]
    {
        Veri.Borc != 0 ? $"Borç {Bicim.Tl(Veri.Borc)} ₺" : null,
        Veri.Alacak != 0 ? $"Alacak {Bicim.Tl(Veri.Alacak)} ₺" : null,
        CariMetni.Durum(Veri),
        Veri.Aciklama,
    }.OfType<string>());
}

public static class CariMetni
{
    /// <summary>Artı bakiye carinin bize borcu, eksi bakiye bizim borcumuzdur.</summary>
    public static string Bakiye(decimal bakiye) => bakiye == 0 ? "Bakiye 0,00 ₺" : $"Bakiye {Bicim.Tl(Math.Abs(bakiye))} ₺ {(bakiye > 0 ? "borçlu" : "alacaklı")}";

    public static string Durum(ErpCariHareketDto h) => h.Durum switch
    {
        ErpHareketDurumlari.KasaDisi => "kasa hareketi değil",
        ErpHareketDurumlari.Bekliyor => "kasada karşılığı bekliyor",
        ErpHareketDurumlari.Eslesti => $"eşleşti ({EslesmeAdayiSatiri.TurAdi(h.EslesmeTuru)} #{h.EslesmeId})",
        ErpHareketDurumlari.Kaydedildi => "kasaya yazıldı",
        ErpHareketDurumlari.YokSayildi => "yok sayıldı",
        _ => h.Durum
    };
}

/// <summary>Cariler ekranı (docs/specs/2026-11-25-erp12-aktarimi.md): ERP12'den aktarılan hareketlerin cari listesi ve açılan carinin hareketleri.
/// Salt okunur; kayıtların aslı ERP12'dedir. Arama yüklü listede yapılır.</summary>
public partial class CarilerViewModel(IErpApi api, AuthViewModel auth) : OturumluViewModel(auth)
{
    private IReadOnlyList<ErpCariDto> _hepsi = [];
    public ObservableCollection<CariSatiri> Cariler { get; } = new();
    public ObservableCollection<CariHareketSatiri> Hareketler { get; } = new();
    [ObservableProperty] private string _ara = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CariAcik), nameof(AcikBasligi))] private CariSatiri? _acik;
    public bool CariAcik => Acik is not null;
    public string AcikBasligi => Acik is { } a ? $"{a.Baslik}\n{a.Ozet}" : "";
    /// <summary>Listenin toplamı; hiç aktarım yoksa ne yapılacağı.</summary>
    public string Ozet => _hepsi.Count == 0 ? "Henüz ERP12 hareketi aktarılmadı. Ekstre içe aktar ekranından \"ERP12 cari hareketleri\" yükleyin."
        : $"{_hepsi.Count} cari · Borç {Bicim.Tl(_hepsi.Sum(c => c.Borc))} ₺ · Alacak {Bicim.Tl(_hepsi.Sum(c => c.Alacak))} ₺ · {_hepsi.Sum(c => c.Bekleyen)} hareket kasada karşılığını bekliyor";

    partial void OnAraChanged(string value) => Suz();

    public Task YukleAsync() => VeriYukleAsync(async n =>
    {
        var liste = await api.ErpCarilerAsync();
        var acik = Acik?.Veri.Anahtar;
        var hareketler = acik is not null && liste.Any(c => c.Anahtar == acik) ? await api.ErpCariHareketleriAsync(acik) : null;
        if (!Gecerli(n))
            return;
        _hepsi = liste;
        Suz();
        Acik = hareketler is null ? null : new(liste.Single(c => c.Anahtar == acik));
        TakipMetni.Doldur(Hareketler, (hareketler ?? []).Select(h => new CariHareketSatiri(h)));
        OnPropertyChanged(nameof(Ozet));
        Tamamlandi();
    });

    private void Suz()
    {
        var aranan = CekKurallari.AramaAnahtari(Ara);
        TakipMetni.Doldur(Cariler, _hepsi.Where(c => aranan.Length == 0 || CekKurallari.AramaAnahtari(c.Ad).Contains(aranan, StringComparison.Ordinal)
            || CekKurallari.AramaAnahtari(c.Kod).Contains(aranan, StringComparison.Ordinal)).Select(c => new CariSatiri(c)));
    }

    /// <summary>Cariyi açar; açık cariye yeniden basılırsa kapatır.</summary>
    [RelayCommand]
    private Task SecAsync(CariSatiri satir)
    {
        if (Acik?.Veri.Anahtar == satir.Veri.Anahtar)
        {
            Acik = null;
            Hareketler.Clear();
            return Task.CompletedTask;
        }
        return YurutAsync(async n =>
        {
            var hareketler = await api.ErpCariHareketleriAsync(satir.Veri.Anahtar);
            if (!Gecerli(n))
                return;
            Acik = satir;
            TakipMetni.Doldur(Hareketler, hareketler.Select(h => new CariHareketSatiri(h)));
        }, mesgulkenBildir: true);
    }

    protected override void OturumTemizle()
    {
        _hepsi = [];
        Ara = "";
        Acik = null;
        Cariler.Clear();
        Hareketler.Clear();
        OnPropertyChanged(nameof(Ozet));
    }
}
