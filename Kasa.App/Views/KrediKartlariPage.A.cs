namespace Kasa.App.Views;

// Paket A — "Bugün yapılacaklar" → "Ödeme gir": //kartlar?id=… kartı vurgular ve ödeme girişini ekstre
// borcuyla doldurur (DerinBaglanti); sayfa açılışındaki yüklemede uygulanır.
public partial class KrediKartlariPage : IQueryAttributable
{
    public void ApplyQueryAttributes(IDictionary<string, object> query) => _vm.KartIstegiUygula(query);
}
