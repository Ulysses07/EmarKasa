using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Sorunun sorulduğu kayıt: işlem, çek, hafta ya da genel. <see cref="Ozet"/> formda gösterilen
/// kısa metindir (sunucu kendi özetini ayrıca kaydeder).
/// </summary>
public sealed record SoruHedefi(SoruHedefTuru Tur, int? Id, DateOnly? Hafta, string Ozet)
{
    public static SoruHedefi Genel { get; } = new(SoruHedefTuru.Genel, null, null, "Genel soru");

    public static string TurAdi(SoruHedefTuru tur) => tur switch
    {
        SoruHedefTuru.Islem => "İşlem",
        SoruHedefTuru.Hafta => "Hafta",
        SoruHedefTuru.Cek => "Çek",
        _ => "Genel",
    };

    /// <summary>Satırdaki kayıttan hedef (işlem, çek, hafta satırı); tanınmayan kayıtta null.</summary>
    public static SoruHedefi? Olustur(object? kayit) => kayit switch
    {
        SoruHedefi h => h,
        IslemDto i => new(SoruHedefTuru.Islem, i.Id, null,
            $"{i.Tarih.ToString("dd.MM.yyyy", Kultur.Turkce)} · {i.Cari} · {Bicim.Tl(i.TutarTl)} ₺ · {i.Kanal}"),
        CekGorunum c => Olustur(c.Dto),
        CekDto c => new(SoruHedefTuru.Cek, c.Id, null,
            $"{CekMetin.YonAdi(c.Yon)} çek · {c.Kisi} · {Bicim.Tl(c.Tutar)} ₺ · vade {c.VadeTarihi.ToString("dd.MM.yyyy", Kultur.Turkce)}"),
        HaftalikOzetDto h => Olustur(h.Donem),
        DonemDto d => new(SoruHedefTuru.Hafta, null, d.Start,
            $"Hafta {d.Start.ToString("dd.MM", Kultur.Turkce)} – {d.End.ToString("dd.MM.yyyy", Kultur.Turkce)}"),
        _ => null,
    };
}

/// <summary>
/// Satırlardaki "Soru sor" düğmesinden Sorular sayfasına geçiş: hedef burada bekler, Sorular sayfası
/// açılınca formu bu hedefle açar (<see cref="Al"/>). Sayfaya gitme <see cref="Yonlendirme"/> ile olur
/// (oturum açık değilse gidilmez).
/// </summary>
public sealed class SoruYonlendirme
{
    private readonly Yonlendirme? _yonlendirme;
    private readonly object _kilit = new();
    private SoruHedefi? _bekleyen;

    public SoruYonlendirme(Yonlendirme? yonlendirme = null) => _yonlendirme = yonlendirme;

    /// <summary>
    /// Uygulamanın örneği (açılışta atanır): satır şablonları sayfa VM'lerine dokunmadan
    /// <see cref="SorKomutu"/>'na bağlanır.
    /// </summary>
    public static SoruYonlendirme? Varsayilan { get; set; }

    /// <summary>XAML: <c>Command="{x:Static core:SoruYonlendirme.SorKomutu}" CommandParameter="{Binding .}"</c>.</summary>
    public static ICommand SorKomutu { get; } = new RelayCommand<object?>(k => Varsayilan?.Sor(k));

    /// <summary>Kayıt için soru formunu açtırır; tanınmayan kayıtta genel soru.</summary>
    public void Sor(object? kayit)
    {
        var hedef = SoruHedefi.Olustur(kayit) ?? SoruHedefi.Genel;
        lock (_kilit) _bekleyen = hedef;
        _yonlendirme?.Iste("sorular");
    }

    /// <summary>Bekleyen hedefi alır ve temizler (yoksa null).</summary>
    public SoruHedefi? Al()
    {
        lock (_kilit)
        {
            var h = _bekleyen;
            _bekleyen = null;
            return h;
        }
    }
}
