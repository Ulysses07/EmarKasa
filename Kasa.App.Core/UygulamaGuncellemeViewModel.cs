using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Kasa.App.Core;

/// <summary>Salt sürüm kontrolü, açık indirme ve onaylı kurulum. Oturumdan bağımsızdır; finansal işlem çağırmaz.</summary>
public sealed partial class UygulamaGuncellemeViewModel : ObservableObject
{
    private readonly IUygulamaGuncelleyici _guncelleyici;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private CancellationTokenSource? _iptal;
    private int _kilit, _nesil;
    public UygulamaGuncellemeViewModel(IUygulamaGuncelleyici guncelleyici)
    {
        _guncelleyici = guncelleyici;
        Durum = UygulamaIciKurulum ? "Henüz güncelleme kontrolü yapılmadı." : KanalAciklamasi;
    }
    public string MevcutSurum => GuvenlikViewModel.IstemciSurumu;
    public bool UygulamaIciKurulum => _guncelleyici.UygulamaIciKurulum;
    public string KanalAciklamasi => _guncelleyici.KanalAciklamasi;
    public string HariciKanalMetni => _guncelleyici.HariciKanalMetni;
    /// <summary>Kabuk açık formları, süren işleri ve kullanıcı onayını denetler. Bağlanmazsa kurulum yapılmaz.</summary>
    public Func<UygulamaGuncelleme, Task<bool>>? KurulumOnayi { get; set; }
    public Func<string?>? KurulumEngeli { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(KontrolCommand), nameof(IndirCommand), nameof(KurCommand), nameof(HariciKanalCommand), nameof(IptalCommand))]
    private bool _mesgul;
    [ObservableProperty] private string? _hata;
    [ObservableProperty] private string _durum = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuncellemeVar), nameof(GuncellemeMetni))]
    [NotifyCanExecuteChangedFor(nameof(IndirCommand), nameof(KurCommand))]
    private UygulamaGuncelleme? _guncelleme;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndirmeOrani), nameof(IlerlemeMetni))]
    private int _ilerleme;
    [ObservableProperty] private bool _indiriliyor;

    public bool GuncellemeVar => Guncelleme is not null;
    public string GuncellemeMetni => Guncelleme is { } g ? $"Sürüm {g.Surum}" + (g.Indirildi ? " · Kurulmaya hazır" : " · İndirilebilir") : "";
    public double IndirmeOrani => Ilerleme / 100d;
    public string IlerlemeMetni => $"İndirme: %{Ilerleme}";
    private bool KontrolEdilebilir() => !Mesgul && UygulamaIciKurulum;
    private bool Indirilebilir() => KontrolEdilebilir() && Guncelleme is { Indirildi: false };
    private bool Kurulabilir() => KontrolEdilebilir() && Guncelleme is { Indirildi: true };
    private bool Bosta() => !Mesgul;
    private bool IptalEdilebilir() => Mesgul;
    [RelayCommand(CanExecute = nameof(KontrolEdilebilir))] private Task Kontrol() => KontrolEtAsync();
    [RelayCommand(CanExecute = nameof(Indirilebilir))] private Task Indir() => IndirAsync();
    [RelayCommand(CanExecute = nameof(Kurulabilir))] private Task Kur() => KurAsync();
    [RelayCommand(CanExecute = nameof(Bosta))] private Task HariciKanal() => HariciKanaliAcAsync();
    [RelayCommand(CanExecute = nameof(IptalEdilebilir))] private void Iptal() => IptalEt();

    public Task KontrolEtAsync(CancellationToken cancellationToken = default)
    {
        if (!UygulamaIciKurulum)
        {
            Durum = KanalAciklamasi;
            return Task.CompletedTask;
        }
        return YurutAsync(async (n, ct) =>
        {
            Durum = "Güncellemeler kontrol ediliyor…";
            var yeni = await _guncelleyici.KontrolEtAsync(ct);
            if (!Gecerli(n, ct))
                return;
            Guncelleme = yeni;
            Ilerleme = yeni?.Indirildi == true ? 100 : 0;
            Durum = yeni is null ? "Bu yayın kanalında daha yeni bir sürüm bulunamadı." : yeni.Indirildi
                ? "İndirilmiş güncelleme hazır. Kurulumu istediğiniz zaman başlatabilirsiniz."
                : "Yeni sürüm bulundu. İndirme yalnız düğmeye bastığınızda başlar.";
        }, "Güncelleme kontrolü tamamlanamadı. Bağlantınızı kontrol edip yeniden deneyin.", cancellationToken);
    }
    public Task IndirAsync(CancellationToken cancellationToken = default)
    {
        if (!UygulamaIciKurulum || Guncelleme is not { Indirildi: false } paket)
            return Task.CompletedTask;
        return YurutAsync(async (n, ct) =>
        {
            Indiriliyor = true;
            Ilerleme = 0;
            Durum = "Güncelleme indiriliyor… Uygulama kendiliğinden yeniden başlamaz.";
            var ilerleme = new AnlikIlerleme(deger => Ui(() =>
            {
                if (Gecerli(n, ct))
                    Ilerleme = Math.Clamp(deger, 0, 100);
            }));
            await _guncelleyici.IndirAsync(paket, ilerleme, ct);
            if (!Gecerli(n, ct))
                return;
            Guncelleme = paket with { Indirildi = true };
            Ilerleme = 100;
            Durum = "İndirme tamamlandı. Kur ve yeniden başlat düğmesiyle uygulayabilirsiniz.";
        }, "İndirme tamamlanamadı. Paket seçimi korunuyor; yeniden deneyebilirsiniz.", cancellationToken);
    }
    public Task KurAsync(CancellationToken cancellationToken = default)
    {
        if (!UygulamaIciKurulum || Guncelleme is not { Indirildi: true } paket)
            return Task.CompletedTask;
        return YurutAsync(async (n, ct) =>
        {
            if (KurulumEngeli?.Invoke() is { } oncekiEngel)
            {
                Hata = oncekiEngel;
                return;
            }
            if (KurulumOnayi is not { } sor || !await sor(paket))
            {
                if (Gecerli(n, ct))
                    Durum = "Kurulum ertelendi. İndirilmiş paket hazır kalır.";
                return;
            }
            if (!Gecerli(n, ct))
                return;
            if (KurulumEngeli?.Invoke() is { } sonrakiEngel)
            {
                Hata = sonrakiEngel;
                return;
            }
            Durum = "Onaylanan güncelleme kuruluyor; uygulama yeniden başlatılacak.";
            await _guncelleyici.KurVeYenidenBaslatAsync(paket, ct);
        }, "Kurulum başlatılamadı. İndirilmiş paket korunuyor; yeniden deneyebilirsiniz.", cancellationToken);
    }
    public Task HariciKanaliAcAsync(CancellationToken cancellationToken = default)
        => YurutAsync(async (n, ct) =>
        {
            await _guncelleyici.HariciKanaliAcAsync(ct);
            if (Gecerli(n, ct))
                Durum = KanalAciklamasi;
        }, "Yayın kanalı açılamadı. Yeniden deneyin.", cancellationToken);

    public void IptalEt()
    {
        if (_iptal is not { } iptal)
            return;
        Interlocked.Increment(ref _nesil);
        iptal.Cancel();
        Durum = "İşlem iptal edildi. Paket seçimi korunuyor.";
        Ilerleme = 0;
    }
    private bool Gecerli(int nesil, CancellationToken ct) => nesil == Volatile.Read(ref _nesil) && !ct.IsCancellationRequested && _iptal is not null;
    private async Task YurutAsync(Func<int, CancellationToken, Task> islem, string hataIletisi, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _kilit, 1, 0) != 0)
            return;
        using var iptal = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var nesil = Interlocked.Increment(ref _nesil);
        _iptal = iptal;
        Mesgul = true;
        Hata = null;
        try
        {
            iptal.Token.ThrowIfCancellationRequested();
            await islem(nesil, iptal.Token);
        }
        catch (OperationCanceledException) when (iptal.IsCancellationRequested)
        {
            if (nesil == Volatile.Read(ref _nesil))
                Durum = "İşlem iptal edildi. Paket seçimi korunuyor.";
        }
        catch (Exception ex)
        {
            if (Gecerli(nesil, iptal.Token))
                Hata = hataIletisi;
            Debug.WriteLine($"Uygulama güncelleme işlemi tamamlanamadı: {ex}");
        }
        finally
        {
            _iptal = null;
            Indiriliyor = false;
            Mesgul = false;
            Volatile.Write(ref _kilit, 0);
        }
    }
    private void Ui(Action eylem)
    {
        if (_ui is not null && SynchronizationContext.Current != _ui)
            _ui.Post(_ => eylem(), null);
        else
            eylem();
    }
    private sealed class AnlikIlerleme(Action<int> bildir) : IProgress<int>
    {
        public void Report(int value) => bildir(value);
    }
}
