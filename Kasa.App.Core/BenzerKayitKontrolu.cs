using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Onay yalnız gösterilen kayıt gövdesi içindir; form değişikliği yeni kontrol ister.</summary>
public partial class BenzerKayitKontrolu(IBenzerKayitApi? api) : ObservableObject
{
    private string? _bekleyen;
    private string? _onaylanan;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UyariVar))]
    private string? _uyari;
    public bool UyariVar => !string.IsNullOrWhiteSpace(Uyari);

    public async Task<bool> DevamEdilebilirAsync(BenzerlikYaz arama, object govde, Func<bool> gecerli)
    {
        if (api is null) return gecerli();
        var anahtar = JsonSerializer.Serialize(govde);
        if (_onaylanan == anahtar) return gecerli();
        Temizle();
        var eslesmeler = await api.BenzerKayitlarAsync(arama);
        if (!gecerli()) return false;
        if (eslesmeler.Count == 0) return true;
        _bekleyen = anahtar;
        Uyari = "Benzer kayıt bulundu. " + KuralMetni + " Aynı işlemi yeniden girmediğinizi kontrol edin; ayrı bir işlemse yine kaydedebilirsiniz:\n" +
            string.Join("\n", eslesmeler.Select(k => $"{KaynakAdi(k.Kaynak)} #{k.Id} · {k.Tarih:dd.MM.yyyy} · {Bicim.Tl(k.Tutar)} ₺ · {k.Aciklama}"));
        return false;
    }
    /// <summary>Sunucunun benzer kayıt kuralı (BenzerKayitServisi; web SIMILAR_RULE_TEXT ile aynı metin): aynı tutar ve ±3 gün;
    /// kanal süzgeci yalnız kesin başka kanala düşen kaydı eler.</summary>
    public const string KuralMetni = "Aynı tutarda ve ±3 gün içindeki kayıtlar gösterilir; kartlı kayıtta aynı kartın kayıtları aranır. Kanal yalnız kesin olarak başka kanala düşen kaydı eler: kanalı belirsiz, Ortak, yalnız genel kasa ya da dağılım bekleyen kayıtlar, seçilen kanalı da içeren çok kanallı kayıtlar ve kart ödemeleri her kanalda görünür.";
    private static string KaynakAdi(string kaynak) => kaynak switch
    {
        "KartHarcama" => "Kart harcaması", "KartOdeme" or "EskiKartOdeme" => "Kart ödemesi",
        "KrediTaksidi" => "Kredi taksidi", "EskiKrediTaksidi" => "Eski kredi taksidi", _ => "Gider",
    };
    public bool Onayla()
    {
        if (_bekleyen is null) return false;
        _onaylanan = _bekleyen; _bekleyen = null; Uyari = null; return true;
    }
    public void Temizle() { _bekleyen = _onaylanan = null; Uyari = null; }
}
