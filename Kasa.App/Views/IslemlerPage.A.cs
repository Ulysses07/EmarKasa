using Kasa.App.Core;

namespace Kasa.App.Views;

// İşlemler'e sorguyla gelinir:
// - Paket B · 22 "Rapordan iniş": //islemler?baslangic=…&bitis=…&kanal=…(&tip=…) listeyi o dönem, kanal ve
//   tiple süzer. Sorgu bir kez uygulanır (menüden yeniden gelince tekrar uygulanmaz).
// - Paket A "Bugün yapılacaklar" → "Gelen gir": //islemler?donem=…&kanal=… gelen formunu o dönem ve kanalla
//   açar (DerinBaglanti). Mantık VM'de; burada yalnız sorgu iletilir ve form sütunu gelen formuna kaydırılır.
public partial class IslemlerPage : IQueryAttributable
{
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (IslemSuzgeci.Coz(query) is { } suzgec)
        {
            try { query.Clear(); }
            catch (NotSupportedException) { /* salt okunur sözlük */ }
            _ = _vm.SuzgecUygulaAsync(suzgec);
            return;
        }
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
