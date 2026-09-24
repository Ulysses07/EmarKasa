
namespace Kasa.App.Views;

// Paket A — "Bugün yapılacaklar" → "Gelen gir": //islemler?donem=…&kanal=… gelen formunu o dönem ve kanalla
// açar (DerinBaglanti). Mantık VM'de; burada yalnız sorgu iletilir ve form sütunu gelen formuna kaydırılır.
public partial class IslemlerPage : IQueryAttributable
{
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!_vm.GelenIstegiUygula(query)) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Task.Delay(300);   // sayfa açılıp yerleşsin
                await FormKaydirici.ScrollToAsync(GelenKarti, ScrollToPosition.Start, true);
            }
            catch (Exception) { /* kaydırılamazsa form yine hazırdır */ }
        });
    }
}
