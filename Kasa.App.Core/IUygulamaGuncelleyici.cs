namespace Kasa.App.Core;

/// <summary>Finansal API ve oturumdan bağımsız uygulama yayın kanalı. İndirme ve kurulum yalnız açık kullanıcı eylemidir.</summary>
public interface IUygulamaGuncelleyici
{
    bool UygulamaIciKurulum { get; }
    string KanalAciklamasi { get; }
    string HariciKanalMetni { get; }
    Task<UygulamaGuncelleme?> KontrolEtAsync(CancellationToken cancellationToken);
    Task IndirAsync(UygulamaGuncelleme guncelleme, IProgress<int> ilerleme, CancellationToken cancellationToken);
    Task KurVeYenidenBaslatAsync(UygulamaGuncelleme guncelleme, CancellationToken cancellationToken);
    Task HariciKanaliAcAsync(CancellationToken cancellationToken);
}

public sealed record UygulamaGuncelleme(string Kimlik, string Surum, bool Indirildi = false);
