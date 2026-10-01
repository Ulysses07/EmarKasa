using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Kasa.ApiClient;
using Xunit.v3;

namespace Kasa.Sozlesme.Tests;

/// <summary>Testin gerçek sunucuya karşı çağırdığı istemci metotları. Kapsam testi, istemci arayüzlerindeki her metodun
/// bir testte işaretli olmasını; <see cref="SozlesmeKapsamiDenetimiAttribute"/> de işaretli metodun o testte gerçekten
/// çağrılmasını ister.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class SozlesmeKapsamiAttribute(params string[] metotlar) : Attribute
{
    public IReadOnlyList<string> Metotlar { get; } = metotlar;
}

/// <summary>xUnit her sözleşme testinin sonunda (test metodu döndükten sonra, örnek atılmadan önce) kapsamı denetler:
/// testin [SozlesmeKapsami] işaretlerindeki her metot o testte vekil üzerinden çağrılmış olmalı. Denetim test metoduna
/// bırakılmadığı için unutulamaz; <see cref="SozlesmeTemeli"/>'nden türeyen her sınıf onu devralır.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class SozlesmeKapsamiDenetimiAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest, IXunitTest test) => SozlesmeTemeli.KapsamiDenetle(methodUnderTest);
}

/// <summary>
/// Her test kendi sunucusunu kurar. İstemci arayüzleri bir vekil üzerinden çağrılır: her çağrıdan sonra o çağrının
/// yanıtı istemcinin döndürdüğü türe (<see cref="SozlesmeDenetimi.YanitHatalari"/>), gönderdiği gövde de ucun bağladığı
/// sunucu türüne (<see cref="SozlesmeDenetimi.IstekHatalari"/>) karşı denetlenir; 400/409/429 gibi iletili hatalarda
/// istemcinin sunucu iletisini okuyabildiği doğrulanır. Test bittiğinde kapsam denetimi kendiliğinden çalışır
/// (<see cref="SozlesmeKapsamiDenetimiAttribute"/>).
/// </summary>
[SozlesmeKapsamiDenetimi]
public abstract class SozlesmeTemeli : IDisposable
{
    protected static readonly DateOnly Bugun = SozlesmeFabrikasi.Bugun;
    /// <summary>Takip başlangıcı bugünün ayından 8 ay önce: geçmiş kesim ekstreleri ve taksitler oluşur.</summary>
    protected static readonly DateOnly Baslangic = new DateOnly(Bugun.Year, Bugun.Month, 1).AddMonths(-8);

    // xUnit aynı sınıfın testlerini sırayla koşar (sınıf başına bir test koleksiyonu): sınıfın kayıtlı örneği o anda
    // koşan testin örneğidir. Örnek kurucuda kaydolur, Dispose'ta (kapsam denetiminden sonra) çıkar.
    private static readonly ConcurrentDictionary<Type, SozlesmeTemeli> Kosan = new();

    private readonly List<SozlesmeFabrikasi> _fabrikalar = [];
    private readonly HashSet<string> _cagrilan = [];
    protected SozlesmeFabrikasi F { get; }

    protected SozlesmeTemeli()
    {
        F = Fabrika();
        Kosan[GetType()] = this;
    }

    protected SozlesmeFabrikasi Fabrika(Dictionary<string, string?>? ayarlar = null)
    {
        var f = new SozlesmeFabrikasi { EkAyarlar = ayarlar ?? [] };
        _fabrikalar.Add(f);
        return f;
    }

    /// <summary>Oturumsuz istemci (vekilli); giriş testte yapılır.</summary>
    protected Oturum Istemci(SozlesmeFabrikasi? f = null, ITokenStore? depo = null) => new(f ?? F, _cagrilan, depo);

    protected async Task<Oturum> Editor(SozlesmeFabrikasi? f = null)
    {
        var o = Istemci(f);
        await o.Kasa.LoginAsync("editor", SozlesmeFabrikasi.EditorSifresi);
        return o;
    }

    /// <summary>Koşan testin kapsam denetimi (<see cref="SozlesmeKapsamiDenetimiAttribute"/> çağırır). Kurucu hata
    /// verdiyse kayıtlı örnek yoktur; kurucunun hatası zaten bildirilmiştir.</summary>
    internal static void KapsamiDenetle(MethodInfo test)
    {
        if (!Kosan.TryGetValue(test.ReflectedType ?? test.DeclaringType!, out var ornek))
            return;
        HashSet<string> cagrilan;
        lock (ornek._cagrilan)
            cagrilan = [.. ornek._cagrilan];
        var hatalar = KapsamHatalari(test, cagrilan);
        Assert.True(hatalar.Count == 0, string.Join("\n", hatalar));
    }

    /// <summary>Testin [SozlesmeKapsami] işaretlerindeki her metot bu testte vekil üzerinden çağrılmış olmalı; işaretsiz
    /// sözleşme testi de hatadır.</summary>
    internal static List<string> KapsamHatalari(MethodInfo test, IReadOnlySet<string> cagrilan)
    {
        var isaretli = test.GetCustomAttributes<SozlesmeKapsamiAttribute>().SelectMany(a => a.Metotlar).ToHashSet();
        if (isaretli.Count == 0)
            return [$"{test.Name}: [SozlesmeKapsami] işareti yok."];
        var cagrilmayan = isaretli.Where(m => !cagrilan.Contains(m)).Order(StringComparer.Ordinal).ToList();
        return cagrilmayan.Count == 0 ? []
            : [$"{test.Name}: işaretli ama çağrılmayan metotlar: {string.Join(", ", cagrilmayan)} (test bu çağrılardan önce başka bir hatayla bittiyse önce o hatayı düzeltin)."];
    }

    public void Dispose()
    {
        Kosan.TryRemove(KeyValuePair.Create(GetType(), this));
        foreach (var f in _fabrikalar)
            f.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Tek istemci (tek token deposu) ve arayüz vekilleri.</summary>
public sealed class Oturum
{
    public KasaApiClient Istemci { get; }
    public YanitKaydedici Kayit { get; } = new();
    public ITokenStore Depo { get; }
    public IKasaApi Kasa { get; }
    public IFinansTakipApi Takip { get; }
    public IAlisApi Alis { get; }
    public IAlisOdemeApi AlisOdeme { get; }
    public IYonetimApi Yonetim { get; }
    public IEkstreAktarmaApi Ekstre { get; }
    public IAylikGiderApi AylikGider { get; }
    public IKasaKontrolApi Kontrol { get; }
    public IBildirimApi Bildirim { get; }
    public IBenzerKayitApi Benzer { get; }
    public ICekApi Cek { get; }

    internal Oturum(SozlesmeFabrikasi f, HashSet<string> cagrilan, ITokenStore? depo)
    {
        Depo = depo ?? new BellekTokenStore();
        Istemci = f.Istemci(Kayit, Depo);
        T Vekil<T>() where T : class
        {
            var v = DispatchProxy.Create<T, SozlesmeVekili>();
            ((SozlesmeVekili)(object)v).Kur(Istemci, Kayit, f.Istekler, cagrilan);
            return v;
        }
        Kasa = Vekil<IKasaApi>();
        Takip = Vekil<IFinansTakipApi>();
        Alis = Vekil<IAlisApi>();
        AlisOdeme = Vekil<IAlisOdemeApi>();
        Yonetim = Vekil<IYonetimApi>();
        Ekstre = Vekil<IEkstreAktarmaApi>();
        AylikGider = Vekil<IAylikGiderApi>();
        Kontrol = Vekil<IKasaKontrolApi>();
        Bildirim = Vekil<IBildirimApi>();
        Benzer = Vekil<IBenzerKayitApi>();
        Cek = Vekil<ICekApi>();
    }

    /// <summary>Son istemci yanıtı (durum kodu denetimleri için).</summary>
    public IstemciYaniti SonYanit => Kayit.Kayitlar[^1];
}

/// <summary>Arayüz çağrısını gerçek istemciye iletir ve çağrının trafiğini sözleşmeye karşı denetler.</summary>
public class SozlesmeVekili : DispatchProxy
{
    /// <summary>İstemcinin yanıtı kendi özel türüyle okuduğu metotlar: aynı şekil burada tanımlıdır.</summary>
    private sealed record RolYaniti(string Rol, string? Cihaz = null);
    private static readonly MethodInfo SarGenel = typeof(SozlesmeVekili).GetMethod(nameof(Sar), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private KasaApiClient _istemci = null!;
    private YanitKaydedici _kayit = null!;
    private IstekKaydedici _istekler = null!;
    private HashSet<string> _cagrilan = null!;

    internal void Kur(KasaApiClient istemci, YanitKaydedici kayit, IstekKaydedici istekler, HashSet<string> cagrilan)
    { _istemci = istemci; _kayit = kayit; _istekler = istekler; _cagrilan = cagrilan; }

    protected override object? Invoke(MethodInfo? metot, object?[]? arguman)
    {
        ArgumentNullException.ThrowIfNull(metot);
        lock (_cagrilan)
            _cagrilan.Add(metot.Name);
        var yanitSayisi = _kayit.Kayitlar.Count;
        var istekSayisi = _istekler.Kayitlar.Count;
        object? sonuc;
        try
        { sonuc = metot.Invoke(_istemci, arguman); }
        catch (TargetInvocationException e) when (e.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(e.InnerException); throw; }
        if (sonuc is not Task gorev)
            return sonuc;
        var tur = metot.ReturnType.IsGenericType ? metot.ReturnType.GetGenericArguments()[0] : null;
        return tur is null
            ? Bekle(gorev, metot.Name, yanitSayisi, istekSayisi)
            : SarGenel.MakeGenericMethod(tur).Invoke(this, [gorev, metot.Name, yanitSayisi, istekSayisi]);
    }

    private async Task Bekle(Task gorev, string ad, int yanitSayisi, int istekSayisi)
    {
        try
        { await gorev; }
        catch (KasaApiException e) { Denetle(ad, null, yanitSayisi, istekSayisi); HataIletisiOkunur(ad, e, yanitSayisi); throw; }
        Denetle(ad, null, yanitSayisi, istekSayisi);
    }

    private async Task<T> Sar<T>(Task<T> gorev, string ad, int yanitSayisi, int istekSayisi)
    {
        T sonuc;
        try
        { sonuc = await gorev; }
        catch (KasaApiException e) { Denetle(ad, null, yanitSayisi, istekSayisi); HataIletisiOkunur(ad, e, yanitSayisi); throw; }
        Denetle(ad, ad == nameof(IKasaApi.BenKimAsync) ? typeof(RolYaniti) : typeof(T) == typeof(IndirmeBilgisi) ? null : typeof(T), yanitSayisi, istekSayisi);
        return sonuc;
    }

    private void Denetle(string ad, Type? tur, int yanitSayisi, int istekSayisi)
    {
        var hatalar = _istekler.Kayitlar.Skip(istekSayisi).SelectMany(SozlesmeDenetimi.IstekHatalari).ToList();
        if (tur is not null)
        {
            var yanit = _kayit.Kayitlar.Skip(yanitSayisi).LastOrDefault();
            if (yanit is null)
                hatalar.Add($"{ad}: yanıt kaydı yok.");
            else
                hatalar.AddRange(SozlesmeDenetimi.YanitHatalari(tur, yanit));
        }
        Assert.True(hatalar.Count == 0, $"{ad} sözleşme farkları:\n" + string.Join("\n", hatalar));
    }

    /// <summary>İletili hata yanıtında istemci sunucunun iletisini okumalı; genel yedek iletiye düşmemeli.</summary>
    private void HataIletisiOkunur(string ad, KasaApiException e, int yanitSayisi)
    {
        if (e.DurumKodu is not (HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests))
            return;
        var yanit = _kayit.Kayitlar.Skip(yanitSayisi).LastOrDefault();
        Assert.True(yanit?.Json is not null, $"{ad}: {(int)e.DurumKodu} yanıtı JSON gövde taşımıyor.");
        Assert.True(e.Message != new KasaApiException(e.DurumKodu).Message, $"{ad}: {(int)e.DurumKodu} iletisi okunamadı, istemci genel iletiye düştü. Gövde: {yanit!.Json}");
    }
}
