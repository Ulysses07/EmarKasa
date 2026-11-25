using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

// ERP12 satırının önerisi: kasadaki karşılığı, nakit sorusu ya da bekleme (docs/specs/2026-11-25-erp12-aktarimi.md).
public partial class EkstreSatirEditor
{
    private bool _erpden;

    /// <summary>Açıklamanın altında görünen ek bilgi (ERP12: işlem türü, belge no).</summary>
    public string EkBilgi => Kaynak.EkBilgi ?? "";
    private bool ErpOnerisi => Oneri?.Durum is ErpOneriDurumlari.Eslesme or ErpOneriDurumlari.Secim or ErpOneriDurumlari.Nakit or ErpOneriDurumlari.Bekle;

    /// <summary>ERP12 önerisini satıra yazar: kesin karşılık seçili gelir, kesin olmayan karşılıklar aday listesine dolar, nakitte tür dolar. Elle
    /// değiştirilmiş ya da kapanmış satır değişmez; öneri kalktıysa satır ilk hâline döner. Satırı seçmez.</summary>
    /// <returns>Satır değiştiyse true.</returns>
    public bool ErpOnerisiniUygula()
    {
        if (Kapali || !Secilebilir || ElleDegisti || !ErpOnerisi && !_erpden)
            return false;
        var once = ErpDurumu();
        var o = ErpOnerisi ? Oneri : null;
        _oneriUygulaniyor = true;
        try
        {
            IslemTuru = IslemTurleri.FirstOrDefault(t => t.Kod == (o is null ? Kaynak.OnerilenIslem : o.IslemTuru));
            Paylar.Clear();
            if (EslesmeMi)
            {
                var adaylar = o?.Adaylar ?? [];
                TakipMetni.Doldur(EslesmeAdaylari, adaylar.Select(a => new EslesmeAdayiSatiri(a)));
                SeciliAday = o?.Durum == ErpOneriDurumlari.Eslesme || adaylar.Count == 1 ? EslesmeAdaylari.FirstOrDefault() : null;
            }
            _erpden = o is not null;
        }
        finally { _oneriUygulaniyor = false; }
        return once != ErpDurumu();
    }

    private string ErpDurumu() => $"{Durum()}|{SeciliAday?.Veri.Tur}:{SeciliAday?.Veri.Id}|{string.Join(',', EslesmeAdaylari.Select(a => $"{a.Veri.Tur}:{a.Veri.Id}"))}";
}
