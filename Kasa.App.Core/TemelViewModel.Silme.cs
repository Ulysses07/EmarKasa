namespace Kasa.App.Core;

// Paket C · 32: silme ile liste tazelemesi ayrı değerlendirilir. Silme ucu başarılı döndüyse,
// ardından gelen tazeleme hata verse bile "Silindi · Geri al" şeridi gösterilebilsin (kayıt
// sunucuda silinmiştir; geri alma imkânı kaybolmamalı).
public partial class TemelViewModel
{
    /// <summary>
    /// <paramref name="sil"/> ve ardından <paramref name="tazele"/> tek Mesgul/Hata sarmalayıcısında
    /// çalışır. Silme başarılıysa true döner; tazeleme hatası <see cref="Hata"/>'da kalır ama sonucu
    /// değiştirmez. Silme hata verirse false (tazeleme çalışmaz).
    /// </summary>
    protected async Task<bool> SilVeTazeleAsync(Func<Task> sil, Func<Task> tazele)
    {
        var silindi = false;
        await CalistirAsync(async () =>
        {
            await sil();
            silindi = true;
            await tazele();
        });
        return silindi;
    }
}
