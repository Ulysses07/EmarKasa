namespace Kasa.App.Core;

/// <summary>Platform bildirim servisi (Windows toast). Kayıt + gösterme.</summary>
public interface IBildirimServisi
{
    /// <summary>Uygulama açılışında bir kez: bildirim altyapısını kaydeder.</summary>
    void KayitOl();

    /// <summary>Verilen hatırlatmalar için bildirim gösterir.</summary>
    Task GosterAsync(IReadOnlyList<Hatirlatma> hatirlatmalar);
}
