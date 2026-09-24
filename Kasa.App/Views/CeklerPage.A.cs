namespace Kasa.App.Views;

// Paket A — "Bugün yapılacaklar" → "Çeki aç": //cekler?id=… çeki düzenleme formunda açar (DerinBaglanti).
public partial class CeklerPage : IQueryAttributable
{
    public void ApplyQueryAttributes(IDictionary<string, object> query) => _ = _vm.CekIstegiUygulaAsync(query);
}
