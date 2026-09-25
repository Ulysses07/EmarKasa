namespace Kasa.App.Views;

internal static class SilmeOnayi
{
    public static async Task GosterAsync(Page sayfa, Button dugme, string kayit, Func<Task> sil)
    {
        dugme.IsEnabled = false;
        try
        {
            if (await sayfa.DisplayAlertAsync("Kaydı sil", $"{kayit}\n\nBu kayıt silinecek. Bu işlem geri alınamaz.", "Sil", "Vazgeç"))
                await sil();
        }
        finally { dugme.IsEnabled = true; }
    }
}
