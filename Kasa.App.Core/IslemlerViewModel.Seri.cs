using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket C · 15: işlemi kopyala, cariye göre kanal/tip önerisi, seri giriş ve kaydedilen satır şeridi.
public partial class IslemlerViewModel
{
    /// <summary>
    /// Seri giriş: kayıttan sonra tarih, kanal, tip (kart) ve not kalır; yalnız cari ve tutar temizlenir
    /// (aynı fiş yığınının ortak notu her satırda yeniden yazılmasın).
    /// </summary>
    [ObservableProperty] private bool _seriGiris;

    /// <summary>Son kaydedilen işlem (yeşil şerit: Düzelt / Sil).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonKaydedilenMetni), nameof(SonKaydedilenGorunur))]
    private IslemDto? _sonKaydedilen;

    public bool SonKaydedilenGorunur => SonKaydedilen is not null;

    public string? SonKaydedilenMetni => SonKaydedilen is { } i
        ? $"Kaydedildi: {i.Tarih.ToString("d MMM", Kultur.Turkce)} · {i.Cari} · {Bicim.Tl(i.TutarTl)} ₺ · {i.Kanal}"
        : null;

    /// <summary>Cari seçilince doldurulan önerinin açıklaması ("Son işlem: 10 Eyl · TOPTAN").</summary>
    [ObservableProperty] private string? _cariOnerisiMetni;

    private int _oneriSurumu;

    /// <summary>Kayıttan sonra: seri girişte yalnız cari ve tutar temizlenir, değilse form sıfırlanır. Şerit güncellenir.</summary>
    private void KayitSonrasi(IslemDto kaydedilen)
    {
        SonKaydedilen = kaydedilen;
        if (!SeriGiris)
        {
            Yeni();
            return;
        }
        Programla(() =>
        {
            DuzenId = 0;
            DuzenCari = "";
            DuzenTutar = 0;
        });
        _duzenEskiKartsizKK = false;
        IleriTarihUyarisi = null;
        UyarilariTemizle();
        BelgeFormunuSifirla();                // belge no ve ekler her kayda ayrıdır (paket F)
        _kanalElle = DuzenKanal.Length > 0;   // kalan kanal/tip bu seride kullanıcının seçimi sayılır
        _tipElle = true;
        OdakIste(OdakCari);
    }

    /// <summary>İşlemi yeni kayıt olarak forma kopyalar; tarih bugün olur (yalnız tarihi değiştirip kaydetmek için).</summary>
    [RelayCommand]
    private void Kopyala(IslemDto i)
    {
        Programla(() =>
        {
            Yeni();
            DuzenCari = i.Cari;
            DuzenTutar = i.TutarTl;
            DuzenKanal = i.Kanal;
            DuzenTip = i.Tip;
            DuzenKrediKartiId = i.KrediKartiId;
            DuzenNot = i.Not;
            DuzenTarih = Bugun;
        });
        _duzenEskiKartsizKK = i.Tip == GiderTipi.KrediKarti && i.KrediKartiId is null;
        _kanalElle = true;
        _tipElle = true;
        OdakIste(OdakTarih);
    }

    [RelayCommand]
    private void SonKaydedileniDuzelt()
    {
        if (SonKaydedilen is { } i) Duzenle(i);
        OdakIste(OdakTutar);
    }

    [RelayCommand]
    private Task SonKaydedileniSilAsync() => SonKaydedilen is { } i ? OnayliSilAsync(i) : Task.CompletedTask;

    [RelayCommand]
    private void SonKaydedilenKapat() => SonKaydedilen = null;

    /// <summary>
    /// Yeni kayıtta kayıtlı bir cari (sabit giderde kalem) seçilince son işleminin kanalını ve tipini
    /// doldurur. Kullanıcının bu girişte kendisi seçtiği kanal/tip ezilmez; düzenlemede öneri yoktur.
    /// </summary>
    private async Task CariOnerisiUygulaAsync(string yazilan)
    {
        var surum = ++_oneriSurumu;
        var ad = yazilan?.Trim() ?? "";
        if (DuzenId != 0 || ad.Length == 0) return;
        var kayitli = SabitGiderMi ? KayitliKalem(ad) : KayitliCari(ad);
        if (kayitli is null || (_kanalElle && _tipElle)) return;

        IslemOneriDto? o;
        try { o = await _api.IslemOnerisiAsync(kayitli); }
        catch (Exception) { return; }   // öneri bir kolaylık: hata kaydı etkilemez
        if (o is null || surum != _oneriSurumu || DuzenId != 0) return;
        if (!string.Equals((DuzenCari ?? "").Trim(), ad, StringComparison.Ordinal)) return;

        var uygulanan = new List<string>();
        Programla(() =>
        {
            if (!_kanalElle && GiderKanallari.Any(k => k.Ad == o.Kanal) && DuzenKanal != o.Kanal)
            {
                DuzenKanal = o.Kanal;
                uygulanan.Add(o.Kanal);
            }
            if (!_tipElle && !SabitGiderMi && o.Tip != GiderTipi.SabitGider)
            {
                if (o.Tip == GiderTipi.KrediKarti && o.KrediKartiId is { } kart && KartCipleri.Any(k => k.Id == kart))
                {
                    DuzenTip = GiderTipi.KrediKarti;
                    DuzenKrediKartiId = kart;
                    uygulanan.Add("Kredi kartı · " + KartCipleri.First(k => k.Id == kart).Ad);
                }
                else if (o.Tip == GiderTipi.Cari && DuzenTip != GiderTipi.Cari)
                {
                    DuzenTip = GiderTipi.Cari;
                    uygulanan.Add("Cari");
                }
            }
        });
        CariOnerisiMetni = $"Son işlem: {o.Tarih.ToString("d MMM yyyy", Kultur.Turkce)} · {o.Kanal} · {Bicim.Tl(o.TutarTl)} ₺"
                           + (uygulanan.Count > 0 ? $" (dolduruldu: {string.Join(", ", uygulanan)})" : "");
    }
}
