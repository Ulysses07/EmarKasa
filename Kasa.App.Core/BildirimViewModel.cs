using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public record BildirimSatiri(BildirimDto Veri)
{
    public string Baslik => (Veri.Okundu ? "" : "● ") + Veri.Baslik;
    public string Ozet => $"{Veri.Tarih:dd.MM.yyyy} · {Veri.Mesaj}";
}
public record BildirimCihaziSatiri(BildirimCihaziDto Veri)
{
    public string Baslik => Veri.CihazAdi + (Veri.Etkin ? " · açık" : " · kapalı");
    public string Ozet => Veri.SonBasarili is { } t ? $"Son iletim: {t.ToLocalTime():dd.MM.yyyy HH:mm}" : "Henüz başarılı iletim kaydı yok.";
}
public partial class BildirimViewModel(IBildirimApi api, AuthViewModel auth) : OturumluViewModel(auth)
{
    private int _surum;
    public ObservableCollection<BildirimSatiri> Bildirimler { get; } = new();
    public ObservableCollection<BildirimCihaziSatiri> Cihazlar { get; } = new();
    [ObservableProperty] private bool _etkin;
    [ObservableProperty] private int _saat = 9;
    [ObservableProperty] private int _dakika;
    [ObservableProperty] private string _cihazDurumu = "Cihaz bildirimlerinin durumu alınmadı.";
    [ObservableProperty] private bool _cihazBildirimiEtkin;
    public Task YukleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu) return;
        VeriHazir = false;
        var ayar = await api.BildirimAyarlariAsync();
        var bildirimler = await api.BildirimlerAsync();
        var cihazlar = await api.BildirimCihazlariAsync();
        var anahtar = await api.BildirimAnahtariAsync();
        if (!Gecerli(n)) return;
        AyariYansit(ayar); TakipMetni.Doldur(Bildirimler, bildirimler.Select(x => new BildirimSatiri(x)));
        TakipMetni.Doldur(Cihazlar, cihazlar.Select(x => new BildirimCihaziSatiri(x)));
        CihazBildirimiEtkin = anahtar.Etkin;
        CihazDurumu = anahtar.Etkin ? "Cihaz bildirimleri kullanılabilir. Bu bilgisayarda izin vermek veya denemek için aşağıdaki düğmeyi kullanın." : "Cihaz bildirimleri henüz açılmamış. Hatırlatmalar bu ekranda görünmeye devam eder.";
        Tamamlandi();
    });
    private void AyariYansit(BildirimAyarDto a) { Etkin = a.Etkin; Saat = a.Saat; Dakika = a.Dakika; _surum = a.Surum; }
    [RelayCommand] private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || _surum == 0) return;
        if (Saat is < 0 or > 23 || Dakika is < 0 or > 59) { Hata = "Saati 0–23, dakikayı 0–59 arasında girin."; return; }
        var a = await api.BildirimAyarKaydetAsync(new(Etkin, Saat, Dakika, _surum));
        if (!Gecerli(n)) return;
        AyariYansit(a); Tamamlandi(); Mesaj = "Bildirim ayarları kaydedildi. Saat Türkiye saatidir.";
    });
    public Task OkunduAsync(BildirimSatiri satir) => YurutAsync(async n =>
    {
        if (!EditorMu || satir.Veri.Okundu) return;
        await api.BildirimOkunduAsync(satir.Veri.Id);
        if (!Gecerli(n)) return;
        var index = Bildirimler.IndexOf(satir); if (index >= 0) Bildirimler[index] = new(satir.Veri with { Okundu = true });
    });
    public Task CihaziKaldirAsync(BildirimCihaziSatiri satir) => YurutAsync(async n =>
    {
        if (!EditorMu || !satir.Veri.Etkin) return;
        await api.BildirimCihaziKaldirAsync(satir.Veri.Id);
        if (!Gecerli(n)) return;
        var index = Cihazlar.IndexOf(satir); if (index >= 0) Cihazlar[index] = new(satir.Veri with { Etkin = false });
        Mesaj = "Bu cihazın bildirimleri kapatıldı.";
    });
    public static Uri KurulumAdresi(string? apiAdresi) => new(ApiAdresi.Coz(apiAdresi), "#notifications");
    protected override void OturumTemizle() { Bildirimler.Clear(); Cihazlar.Clear(); _surum = 0; Etkin = CihazBildirimiEtkin = false; CihazDurumu = "Cihaz bildirimlerinin durumu alınmadı."; }
}
