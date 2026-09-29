namespace Kasa.App.Core;

/// <summary>Önizleme → onay akışı (kart ödemesi, kart faiz / masrafı, kart ve kredi geçişi): onaylanan gövde, gösterilen
/// önizlemenin gövdesiyle aynı olmalıdır. Önizleme istenirken önceki önizleme kalkar; yanıt geldiğinde oturum ya da seçili kayıt
/// değiştiyse (<c>gecerli</c>) veya girdi (gövde) değiştiyse yanıt uygulanmaz. Gövde tekrar anahtarını (IstekId) da taşır:
/// onay, önizlenen isteğin kimliğiyle gider.</summary>
/// <typeparam name="TGovde">Önizlenen ve onaylanan istek gövdesi.</typeparam>
/// <typeparam name="TOnizleme">Sunucunun önizleme yanıtı.</typeparam>
public sealed class OnizlemeOnay<TGovde, TOnizleme> where TGovde : class where TOnizleme : class
{
    /// <summary>Gösterilen önizlemenin gövdesi; önizleme yoksa ya da kalktıysa null.</summary>
    public TGovde? Govde { get; private set; }

    /// <summary>Gösterilen önizlemenin sunucu yanıtı; önizleme yoksa ya da kalktıysa null.</summary>
    public TOnizleme? Onizleme { get; private set; }

    /// <summary>Gösterilen (güncel) bir önizleme var mı.</summary>
    public bool Var => Govde is not null;

    public void Temizle()
    {
        Govde = null;
        Onizleme = null;
    }

    /// <summary>Önizlemeyi ister: önce önceki önizleme kalkar. Yanıt geldiğinde <paramref name="gecerli"/> tutmuyorsa (oturum ya da
    /// seçili kayıt değişti) veya <paramref name="govde"/> artık başka gövde üretiyorsa (girdi değişti) yanıt uygulanmaz.</summary>
    /// <returns>Önizleme uygulandı mı (<see cref="Onizleme"/> doldu mu).</returns>
    public async Task<bool> IsteAsync(Func<TGovde> govde, Func<TGovde, Task<TOnizleme>> iste, Func<bool> gecerli)
    {
        Temizle();
        var g = govde();
        var onizleme = await iste(g);
        if (!gecerli() || !TakipMetni.Ayni(g, govde()))
            return false;
        Govde = g;
        Onizleme = onizleme;
        return true;
    }

    /// <summary>Onaylanacak gövde gösterilen önizlemenin gövdesi mi (önizlemeden sonra girdi değişmedi mi).</summary>
    public bool Gecerli(TGovde govde) => Govde is not null && TakipMetni.Ayni(govde, Govde);
}
