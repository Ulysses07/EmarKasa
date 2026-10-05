using System.Collections.ObjectModel;
using Kasa.ApiClient;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core;

public partial class EkstreAktarmaViewModel
{
    public ObservableCollection<EkstreKuralDto> Kurallar { get; } = new();
    [ObservableProperty] private string _kuralDurumu = "";
    [ObservableProperty] private bool _kurallarDestekleniyor;
    public Task KurallariYenileAsync() => YurutAsync(KurallariAlAsync);
    private async Task KurallariAlAsync(int n)
    {
        if (!EditorMu || !Gecerli(n))
            return;
        var belge = Belge;
        try
        {
            var list = await api.EkstreKurallarAsync();
            var oneriler = belge is null ? [] : await api.EkstreOnerilerAsync(belge.Id);
            if (!Gecerli(n) || Belge != belge)
                return;
            TakipMetni.Doldur(Kurallar, list);
            foreach (var satir in Satirlar)
                satir.Oneri = oneriler.FirstOrDefault(o => o.SatirNo == satir.Kaynak.No);
            KurallarDestekleniyor = true;
            KuralDurumu = $"{list.Count} kişisel kural. Öneriler satır seçmez veya kayıt oluşturmaz.";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException || e is KasaApiException { DurumKodu: not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) })
        {
            if (!Gecerli(n) || Belge != belge)
                return;
            KurallarDestekleniyor = false;
            Kurallar.Clear();
            foreach (var satir in Satirlar)
                satir.Oneri = null;
            KuralDurumu = e is KasaApiException { DurumKodu: HttpStatusCode.NotFound } ? "Sunucu kişisel kuralları desteklemiyor. PDF satırlarını elle işlemeye devam edebilirsiniz." : "Kurallar alınamadı; yenileyerek tekrar deneyin. " + (e is KasaApiException apiError ? OkumaHataMesaji(apiError) : "Bağlantı kesildi veya istek zaman aşımına uğradı.");
        }
    }
    public void OnerileriUygula()
    {
        if (!EditorMu || Mesgul || !VeriHazir)
            return;
        var count = Satirlar.Count(s => s.OneriyiUygula(toplu: true));
        Mesaj = $"{count} öneri uygulandı. Elle değiştirilen satırlar korundu; satırları seçip önizleyin.";
    }
    public Task KuralKaydetAsync(int? id, EkstreKuralYaz yaz, int oturum) => YurutAsync(async n =>
    {
        if (!EditorMu || OturumNesli != oturum || !KurallarDestekleniyor)
            return;
        if (id is { } key)
            await api.EkstreKuralDuzenleAsync(key, yaz);
        else
            await api.EkstreKuralEkleAsync(yaz);
        if (!Gecerli(n))
            return;
        await KurallariAlAsync(n);
        if (Gecerli(n))
            Mesaj = "Kural kaydedildi. Önerileri kontrol ederek uygulayabilirsiniz.";
    });
    public Task KuralSilAsync(EkstreKuralDto kural, int oturum) => YurutAsync(async n =>
    {
        if (!EditorMu || OturumNesli != oturum || !KurallarDestekleniyor)
            return;
        await api.EkstreKuralSilAsync(kural.Id, kural.Surum);
        if (Gecerli(n))
            await KurallariAlAsync(n);
    });
}
