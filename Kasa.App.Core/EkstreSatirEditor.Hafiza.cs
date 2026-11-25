using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

// Satırın işlem hafızasından doldurulması (docs/specs/2026-11-25-islem-hafizasi.md).
public partial class EkstreSatirEditor
{
    /// <summary>Satırın alanları hafızadan doldu (elle değiştirilmedi).</summary>
    private bool _hafizadan;
    private EkstreSecenek? _ilkTur;

    /// <summary>Satırın hafıza önerisi var (hazır ya da kontrol).</summary>
    public bool HafizaVar => !Kapali && Oneri?.Hafiza is not null;
    /// <summary>Satır özetinin altındaki hafıza notu; yoksa boş.</summary>
    public string HafizaMetni => HafizaVar ? Oneri!.Aciklama : "";
    /// <summary>Daha önce yok sayılmış hareket: toplu yok sayma önerisine girer.</summary>
    public bool YokSayOnerisi => YokSayilabilir && !ElleDegisti && Oneri is { Durum: EkstreHafizaDurumlari.Hazir, IslemTuru: EkstreIslemTurleri.Atla };
    /// <summary>Toplu işaretlemeye girer: hafızadan eksiksiz doldu ya da kesin kişisel kural önerisi var; elle değiştirilmedi.</summary>
    public bool HazirOneri => Secilebilir && !ElleDegisti && Oneri is { } o && o.IslemTuru != EkstreIslemTurleri.Atla
        && (o.Durum == EkstreHafizaDurumlari.Hazir ? _hafizadan && Hazirla().Yaz is not null : OneriUygulanabilir);

    /// <summary>Hafıza önerisini satıra yazar; elle değiştirilmiş ya da kapanmış satır değişmez. Öneri kalktıysa (unutuldu) hafızadan dolan
    /// satır PDF'den okunan hâline döner. Satırı seçmez.</summary>
    /// <returns>Satır değiştiyse true.</returns>
    public bool HafizayiUygula()
    {
        if (Kapali || !Secilebilir || ElleDegisti)
            return false;
        var o = Oneri is { Hafiza: not null, IslemTuru: not (null or EkstreIslemTurleri.Atla) } oneri ? oneri : null;
        if (o is null && !_hafizadan)
            return false;
        var once = Durum();
        _oneriUygulaniyor = true;
        try
        {
            var tur = o is null ? _ilkTur : IslemTurleri.FirstOrDefault(t => t.Kod == o.IslemTuru);
            if (o is not null && tur is null)
                return false;
            if (!_hafizadan)
                _ilkTur = IslemTuru;
            IslemTuru = tur;
            if (DagilimTurleri.FirstOrDefault(d => d.Kod == o?.DagilimTuru) is { } dagilim)
                DagilimTuru = dagilim;
            else if (o is null)
                VarsayilanDagilim();
            Paylar.Clear();
            foreach (var id in o?.KanalIds ?? [])
                if (Kanallar.FirstOrDefault(k => k.Id == id && k.Aktif) is { } kanal)
                    Paylar.Add(new(Kanallar) { Kanal = kanal });
            if (KartSecimiGorunur)
                Kart = o?.Hafiza?.KrediKartiId is { } kart ? Kartlar.FirstOrDefault(k => k.Id == kart) : null;
            Aciklama = o?.Hafiza?.Aciklama ?? Kaynak.Aciklama;
            _hafizadan = o is not null;
        }
        finally { _oneriUygulaniyor = false; }
        return once != Durum();
    }

    /// <summary>Tür seçilince gelen varsayılan dağılım (tür değişmediyse yeniden kurulur).</summary>
    private void VarsayilanDagilim()
    {
        var tur = IslemTuru;
        IslemTuru = null;
        IslemTuru = tur;
    }

    private string Durum() => $"{IslemTuru?.Kod}|{DagilimTuru?.Kod}|{Kart?.Id}|{Aciklama}|{string.Join(',', Paylar.Select(p => $"{p.Kanal?.Id}:{p.Tutar}"))}";
}
