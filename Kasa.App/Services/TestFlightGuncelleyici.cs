using Kasa.App.Core;

namespace Kasa.App.Services;

/// <summary>iOS binary güncellemesi TestFlight tarafından yönetilir. Bu servis yeni build var/yok iddiası üretmez.</summary>
public sealed class TestFlightGuncelleyici : IUygulamaGuncelleyici
{
    public const string YayinAdresi = "https://testflight.apple.com/";
    public bool UygulamaIciKurulum => false;
    public string HariciKanalMetni => "TestFlight kanalını aç";
    public string KanalAciklamasi => "iOS güncellemeleri TestFlight üzerinden yüklenir. "
        + "Yeni build olup olmadığını TestFlight uygulamasında kontrol edin. "
        + "TestFlight'te Emar Kasa'nın görünmesi için test davetini kabul etmiş olmalısınız.";
    public Task<UygulamaGuncelleme?> KontrolEtAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<UygulamaGuncelleme?>(null);
    }
    public Task IndirAsync(UygulamaGuncelleme guncelleme, IProgress<int> ilerleme, CancellationToken cancellationToken)
        => throw new InvalidOperationException("iOS uygulama güncellemeleri TestFlight üzerinden yüklenir.");
    public Task KurVeYenidenBaslatAsync(UygulamaGuncelleme guncelleme, CancellationToken cancellationToken)
        => throw new InvalidOperationException("iOS uygulama güncellemeleri TestFlight üzerinden yüklenir.");
    public async Task HariciKanaliAcAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await Launcher.Default.OpenAsync(new Uri(YayinAdresi)))
            throw new InvalidOperationException("TestFlight kanalı açılamadı.");
    }
}
