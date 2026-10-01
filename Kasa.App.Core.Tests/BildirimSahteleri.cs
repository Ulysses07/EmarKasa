using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Masaüstü bildirim testlerinin sunucusu: liste, ayar, okundu işareti; hata ve bekleyen yanıt kancaları.</summary>
internal sealed class SahteBildirimApi : IBildirimApi
{
    public List<BildirimDto> Liste { get; set; } = [];
    public Exception? ListeHatasi { get; set; }
    /// <summary>Ayarlanırsa liste yanıtı bu görevi bekler (süre sınırı testleri).</summary>
    public Task<IReadOnlyList<BildirimDto>>? Bekleyen { get; set; }
    public int ListeCagri { get; private set; }
    public BildirimAyarDto Ayar { get; set; } = new(true, 9, 0, "Europe/Istanbul", 3);
    public Exception? AyarHatasi { get; set; }
    public Exception? OkunduHatasi { get; set; }
    public List<int> Okunanlar { get; } = [];

    public static BildirimDto Bildirim(int id, DateOnly tarih, bool okundu = false, string hedef = "/#cards/1")
        => new(id, $"Başlık {id}", $"Mesaj {id}", tarih, okundu, hedef, "SonOdeme", 1);

    public Task<IReadOnlyList<BildirimDto>> BildirimlerAsync()
    {
        ListeCagri++;
        if (Bekleyen is not null)
            return Bekleyen;
        return ListeHatasi is not null
            ? Task.FromException<IReadOnlyList<BildirimDto>>(ListeHatasi)
            : Task.FromResult<IReadOnlyList<BildirimDto>>([.. Liste]);
    }

    public Task<BildirimAyarDto> BildirimAyarlariAsync()
        => AyarHatasi is not null ? Task.FromException<BildirimAyarDto>(AyarHatasi) : Task.FromResult(Ayar);

    public Task<BildirimAyarDto> BildirimAyarKaydetAsync(BildirimAyarYaz g)
    {
        Ayar = new(g.Etkin, g.Saat, g.Dakika, "Europe/Istanbul", g.Surum + 1);
        return Task.FromResult(Ayar);
    }

    public Task BildirimOkunduAsync(int id)
    {
        if (OkunduHatasi is not null)
            return Task.FromException(OkunduHatasi);
        Okunanlar.Add(id);
        return Task.CompletedTask;
    }

    public Task<PushAnahtarDto> BildirimAnahtariAsync() => Task.FromResult(new PushAnahtarDto(true, "public"));
    public Task<IReadOnlyList<BildirimCihaziDto>> BildirimCihazlariAsync() => Task.FromResult<IReadOnlyList<BildirimCihaziDto>>([]);
    public Task BildirimCihaziKaldirAsync(int id) => Task.CompletedTask;
}

/// <summary>Windows göstericisinin sahtesi: gösterilenleri ve deneme sayısını kaydeder.</summary>
internal sealed class SahteGosterici : IBildirimGosterici
{
    public List<BildirimDto> Gosterilenler { get; } = [];
    public int DenemeSayisi { get; private set; }
    /// <summary>Bu kimlikteki bildirim gösterilirken hata fırlatılır.</summary>
    public int? HataliKimlik { get; set; }
    public bool DenemeBasarili { get; set; } = true;
    /// <summary>Ayarlanırsa Windows ayarı okunurken ve deneme bildiriminde bu istisna fırlatılır.</summary>
    public Exception? Hata { get; set; }

    public bool WindowsAyarindaKapali
    {
        get => Hata is null ? field : throw Hata;
        set;
    }

    public void Goster(BildirimDto bildirim)
    {
        if (bildirim.Id == HataliKimlik)
            throw new InvalidOperationException("gösterilemedi");
        Gosterilenler.Add(bildirim);
    }

    public bool DenemeGoster()
    {
        if (Hata is not null)
            throw Hata;
        DenemeSayisi++;
        return DenemeBasarili;
    }
}

/// <summary>Bellek içi gösterilenler deposu (dosya davranışı GosterilenBildirimDeposuTests'te sınanır).</summary>
internal sealed class SahteDepo : IGosterilenBildirimDeposu
{
    public HashSet<int> Kayitli { get; } = [];
    public Exception? Hata { get; set; }

    public IReadOnlyList<int> YenileriAyir(IReadOnlyCollection<int> adaylar)
    {
        if (Hata is not null)
            throw Hata;
        return adaylar.Where(Kayitli.Add).ToList();
    }
}

/// <summary>Zamanlanmış görevin sahtesi: çağrıları "kur HH:mm" ve "sil" olarak kaydeder; kurulunca <see cref="Kurulu"/> olur,
/// başarılı silmede kurulu olmaktan çıkar.</summary>
internal sealed class SahteGorev : IBildirimGorevi
{
    public List<string> Cagrilar { get; } = [];
    public bool Kurulu { get; set; }
    public bool SilmeBasarili { get; set; } = true;
    /// <summary>Ayarlanırsa kurma (çağrı kaydedildikten sonra) bu görev tamamlanana dek sürer (sıra testleri).</summary>
    public TaskCompletionSource? Kapi { get; set; }

    public async Task<bool> GuncelleAsync(int saat, int dakika)
    {
        Cagrilar.Add($"kur {saat:00}:{dakika:00}");
        if (Kapi is { } kapi)
            await kapi.Task;
        Kurulu = true;
        return true;
    }

    public Task<bool> SilAsync()
    {
        Cagrilar.Add("sil");
        if (SilmeBasarili)
            Kurulu = false;
        return Task.FromResult(SilmeBasarili);
    }
}

internal sealed class SahteAyar : IBildirimAyari
{
    public bool Acik { get; set; } = true;
}

/// <summary>Sabit an; yerel saat dilimi verilmezse UTC (yerel saat = UTC saati).</summary>
internal sealed class SabitBildirimSaati(DateTimeOffset an, TimeZoneInfo? dilim = null) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => an;
    public override TimeZoneInfo LocalTimeZone => dilim ?? TimeZoneInfo.Utc;
}

/// <summary>Bir testin bildirim ortamı: bugün 30.09.2026, saat 14:05 (başka saat verilmezse); oturum editör ve açık.</summary>
internal sealed class BildirimOrtami
{
    public static readonly DateOnly Bugun = new(2026, 9, 30);

    public BildirimOrtami(IBildirimApi? api = null, AuthViewModel? auth = null, TimeProvider? saat = null)
    {
        Sunucu = api ?? Api;
        Auth = auth ?? new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor, GirisYapildi = true };
        Saat = saat ?? new SabitBildirimSaati(new DateTimeOffset(2026, 9, 30, 14, 5, 0, TimeSpan.Zero));
        Yoklayici = new BildirimYoklayici(Sunucu, Gosterici, Depo, Ayar, Saat);
        Nobetci = new BildirimNobetcisi(Yoklayici, Sunucu, Gorev, Ayar, Gosterici, Auth);
    }

    /// <summary>Varsayılan sunucu sahtesi; kurucuya başka API verilirse kullanılmaz.</summary>
    public SahteBildirimApi Api { get; } = new();
    /// <summary>Yoklayıcının kullandığı API (verilen ya da <see cref="Api"/>).</summary>
    public IBildirimApi Sunucu { get; }
    public AuthViewModel Auth { get; }
    public SahteGosterici Gosterici { get; } = new();
    public SahteDepo Depo { get; } = new();
    public SahteAyar Ayar { get; } = new();
    public TimeProvider Saat { get; }
    public BildirimYoklayici Yoklayici { get; }
    public SahteGorev Gorev { get; } = new();
    public BildirimNobetcisi Nobetci { get; }
}
