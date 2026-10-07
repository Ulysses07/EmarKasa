#if WINDOWS
using Kasa.App.Core;
using Velopack;
using Velopack.Sources;

namespace Kasa.App.Services;

/// <summary>Yalnız public kararlı yayınlar. ZIP çalışma biçiminde uygulama içi güncelleme yapılmaz.</summary>
public sealed class WindowsVelopackGuncelleyici : IUygulamaGuncelleyici
{
    public const string YayinAdresi = "https://github.com/Ulysses07/EmarKasa/releases";
    private readonly UpdateManager _manager = new(new GithubSource("https://github.com/Ulysses07/EmarKasa", accessToken: null, prerelease: false));
    private readonly SemaphoreSlim _kilit = new(1, 1);
    private UpdateInfo? _paket;
    private bool _indirildi;
    public bool UygulamaIciKurulum => _manager.IsInstalled;
    public string HariciKanalMetni => "Windows kurulum paketini aç";
    public string KanalAciklamasi => UygulamaIciKurulum
        ? "Windows güncellemeleri kararlı Emar Kasa yayın kanalından alınır. İndirme ve yeniden başlatma sizin onayınızla yapılır."
        : "Bu uygulama ZIP veya geliştirme sürümü olarak çalışıyor. Uygulama içi güncelleme için yayın sayfasındaki Windows kurulum paketini kullanın.";

    public async Task<UygulamaGuncelleme?> KontrolEtAsync(CancellationToken cancellationToken)
    {
        await _kilit.WaitAsync(cancellationToken);
        try
        {
            KuruluOldugunuDogrula();
            if (_manager.UpdatePendingRestart is { } hazir)
            {
                _paket = new UpdateInfo(hazir, false);
                _indirildi = true;
                return Bilgi(_paket, true);
            }
            var yeni = await _manager.CheckForUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _paket = yeni;
            _indirildi = false;
            return yeni is null ? null : Bilgi(yeni, false);
        }
        finally { _kilit.Release(); }
    }
    public async Task IndirAsync(UygulamaGuncelleme guncelleme, IProgress<int> ilerleme, CancellationToken cancellationToken)
    {
        await _kilit.WaitAsync(cancellationToken);
        try
        {
            var paket = PaketiDogrula(guncelleme);
            await _manager.DownloadUpdatesAsync(paket, ilerleme.Report, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _indirildi = true;
        }
        finally { _kilit.Release(); }
    }
    public async Task KurVeYenidenBaslatAsync(UygulamaGuncelleme guncelleme, CancellationToken cancellationToken)
    {
        await _kilit.WaitAsync(cancellationToken);
        try
        {
            var paket = PaketiDogrula(guncelleme);
            if (!_indirildi || !guncelleme.Indirildi)
                throw new InvalidOperationException("Güncelleme paketi henüz indirilmedi.");
            cancellationToken.ThrowIfCancellationRequested();
            _manager.ApplyUpdatesAndRestart(paket.TargetFullRelease);
        }
        finally { _kilit.Release(); }
    }
    public async Task HariciKanaliAcAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await Launcher.Default.OpenAsync(new Uri(YayinAdresi)))
            throw new InvalidOperationException("Windows yayın sayfası açılamadı.");
    }
    private void KuruluOldugunuDogrula()
    {
        if (!_manager.IsInstalled) throw new InvalidOperationException("Uygulama Velopack ile kurulmamış.");
    }
    private UpdateInfo PaketiDogrula(UygulamaGuncelleme guncelleme)
    {
        KuruluOldugunuDogrula();
        if (_paket is not { } paket || Bilgi(paket, _indirildi).Kimlik != guncelleme.Kimlik)
            throw new InvalidOperationException("Güncelleme seçimi değişti. Yeniden kontrol edin.");
        return paket;
    }
    private static UygulamaGuncelleme Bilgi(UpdateInfo paket, bool indirildi)
        => new(paket.TargetFullRelease.Version + "|" + paket.TargetFullRelease.FileName, paket.TargetFullRelease.Version.ToString(), indirildi);
}
#endif
