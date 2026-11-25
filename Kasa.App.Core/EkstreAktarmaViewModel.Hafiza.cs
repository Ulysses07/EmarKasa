using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

/// <summary>Hafıza listesinin satırı (anahtar, yön ve son karar).</summary>
public sealed class EkstreHafizaSatiri(EkstreHafizaDto veri, string islem)
{
    public EkstreHafizaDto Veri { get; } = veri;
    public string Baslik => $"{Veri.Anahtar} · {EkstreSatirEditor.YonAdi(Veri.Yon)}";
    public string Ozet => $"{Veri.Kaynak switch { EkstreKaynaklari.Kart => "Kart ekstresi", EkstreKaynaklari.Erp => "ERP12", _ => "Banka hareketi" }} · {(Veri.Celiskili ? "son iki karar farklı, önerilmez; son: " : "")}{islem} · {Veri.Adet} kez, son {Veri.SonTarih:dd.MM.yyyy}"
        + (Veri.Aciklama is { } aciklama ? $" · Açıklama: {aciklama}" : "");
}

// İşlem hafızası: kendiliğinden doldurma, hazır önerileri toplu işaretleme, yok sayma önerisi, liste ve unutma
// (docs/specs/2026-11-25-islem-hafizasi.md). Hiçbir satır kendiliğinden kaydedilmez.
public partial class EkstreAktarmaViewModel
{
    private readonly TekrarAnahtari _unutKey = new();
    public ObservableCollection<EkstreHafizaSatiri> Hafiza { get; } = new();
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HafizaDugmesi))] private bool _hafizaAcik;
    [ObservableProperty] private string _hafizaDurumu = "";
    public string HafizaDugmesi => HafizaAcik ? "Hafıza listesini gizle" : "Hafıza listesini göster";

    private List<EkstreSatirEditor> Hazirlar => [.. Satirlar.Where(s => s.HazirOneri)];
    private List<EkstreSatirEditor> YokSayOnerileri => [.. Satirlar.Where(s => s.YokSayOnerisi)];
    public bool HazirVar => Satirlar.Any(s => s.HazirOneri);
    /// <summary>Hafızadan (ve kesin kişisel kuraldan) hazır satırlar ve toplamları.</summary>
    public string HazirOzeti => Hazirlar is { Count: > 0 } h ? $"{h.Count} satır hazır · {Toplam(h)}" : "";
    public string HazirAciklamasi => (Belge?.Kaynak == EkstreKaynaklari.Erp ? "Kasadaki karşılığı kesin olan (ya da hafızanın bildiği nakit) satırlar; eşleşme kasayı değiştirmez."
        : "Bu satırlar daha önce kaydettiğiniz gibi dolduruldu.") + " Düğme onları işaretler ve etkilerini gösterir; kayıt ancak siz onaylayınca olur.";
    public bool YokSayOneriVar => YokSayDestekleniyor && Satirlar.Any(s => s.YokSayOnerisi);
    public int YokSayOneriSayisi => YokSayOnerileri.Count;
    public string YokSayOneriDugmesi => $"Daha önce yok sayılanları yok say ({YokSayOneriSayisi})";

    private void HazirlariBildir()
    {
        foreach (var ad in new[] { nameof(HazirVar), nameof(HazirOzeti), nameof(HazirAciklamasi), nameof(YokSayOneriVar), nameof(YokSayOneriDugmesi) })
            OnPropertyChanged(ad);
        YapayZekayiBildir();
    }

    /// <summary>Gelen öneriler satırlara yazıldıktan sonra hafızadan doldurur; değişen satır varsa önizleme düşer.</summary>
    private void HafizayiYansit()
    {
        var degisen = 0;
        _topluDegisiklik = true;
        try
        { degisen = Satirlar.Count(s => s.OneriyiKendiligindenUygula()); }
        finally { _topluDegisiklik = false; }
        if (degisen > 0)
            GirdiDegisti();
        else
            HazirlariBildir();
    }

    /// <summary>Hazır satırları işaretler ve etkilerini gösterir; kayıt, önizleme onaylanınca olur.</summary>
    [RelayCommand]
    private async Task HazirlariIsaretleAsync()
    {
        if (!EditorMu || Mesgul || !VeriHazir || Belge is null)
            return;
        var hazir = Hazirlar;
        _topluDegisiklik = true;
        try
        {
            foreach (var s in hazir)
            {
                if (s.OneriUygulanabilir && !s.OneriyiUygula(toplu: true))
                    continue;
                s.Secili = s.Hazirla().Yaz is not null;
            }
        }
        finally { _topluDegisiklik = false; }
        GirdiDegisti();
        await OnizleCommand.ExecuteAsync(null);
    }

    /// <summary>Daha önce yok sayılmış hareketlerin aynılarını yok sayar (sayfa önce onay ister; geri alınabilir).</summary>
    public Task OnerilenleriYokSayAsync() => YokSayAsync(YokSayOnerileri, true);

    [RelayCommand]
    private Task HafizaAcKapatAsync()
    {
        HafizaAcik = !HafizaAcik;
        return HafizaAcik ? YurutAsync(HafizayiAlAsync, mesgulkenBildir: true) : Task.CompletedTask;
    }

    private async Task HafizayiAlAsync(int n)
    {
        if (!EditorMu)
            return;
        try
        {
            var liste = await api.EkstreHafizaAsync();
            if (!Gecerli(n))
                return;
            TakipMetni.Doldur(Hafiza, liste.Select(h => new EkstreHafizaSatiri(h, HafizaIslemi(h))));
            HafizaDurumu = liste.Count == 0 ? "Hafıza boş. Kaydettiğiniz ve yok saydığınız satırlar kendiliğinden öğrenilir." : $"{liste.Count} hafıza kaydı, en yeni önce.";
        }
        catch (KasaApiException e) when (e.DurumKodu == HttpStatusCode.NotFound)
        {
            if (!Gecerli(n))
                return;
            Hafiza.Clear();
            HafizaDurumu = "Sunucu işlem hafızasını desteklemiyor; sunucu güncellenmeli.";
        }
    }

    /// <summary>Kararın okunur hâli: "Gider · Seçilen kanallara eşit: MEZAT".</summary>
    private string HafizaIslemi(EkstreHafizaDto h)
    {
        var tur = h.IslemTuru switch
        {
            EkstreIslemTurleri.Atla => "Yok sayıldı",
            EkstreIslemTurleri.KartHarcama => "Kart harcaması",
            EkstreIslemTurleri.KartIade => "Kart iadesi",
            EkstreIslemTurleri.KartOdemesi => "Karta ödeme" + (h.KrediKartiId is { } kart ? $" ({Kartlar.FirstOrDefault(k => k.Id == kart)?.Ad ?? $"kart #{kart}"})" : ""),
            EkstreIslemTurleri.Eslestir => "Mevcut kayıtla eşleştirildi",
            _ => h.IslemTuru
        };
        var kanallar = string.Join(", ", h.KanalIds.Select(id => Kanallar.FirstOrDefault(k => k.Id == id)?.Ad ?? $"kanal #{id}"));
        return h.DagilimTuru switch
        {
            DagilimBicimleri.Esit => $"{tur} · Eşit: {kanallar}",
            DagilimBicimleri.Ozel => $"{tur} · Özel tutarlar: {kanallar}",
            DagilimBicimleri.Genel when h.IslemTuru != EkstreIslemTurleri.Atla => $"{tur} · Yalnız genel kasa",
            _ => tur
        };
    }

    /// <summary>Anahtarı hafızadan çıkarır (sayfa önce onay ister). Kayıtlar ve raporlar değişmez.</summary>
    public Task HafizaUnutAsync(string kaynak, string anahtar, string yon) => YurutAsync(async n =>
    {
        if (!EditorMu)
            return;
        var g = new EkstreHafizaUnutYaz(Guid.Empty, kaynak, anahtar, yon);
        await api.EkstreHafizaUnutAsync(g with { IstekId = _unutKey.Al(g) });
        if (!Gecerli(n))
            return;
        _unutKey.Temizle();
        if (HafizaAcik)
            await HafizayiAlAsync(n);
        if (!Gecerli(n))
            return;
        await KurallariAlAsync(n);
        if (Gecerli(n))
            Mesaj = $"\"{anahtar}\" hafızadan çıktı; kayıtlar değişmedi.";
    }, mesgulkenBildir: true);

    private void HafizayiTemizle()
    {
        HafizaAcik = false;
        HafizaDurumu = "";
        Hafiza.Clear();
        _unutKey.Temizle();
    }
}
