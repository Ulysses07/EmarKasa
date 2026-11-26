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
    /// <summary>Satırın yapay zekâ önerisi var (doldurulur; sunucu hazır dediyse kendiliğinden işaretlenir).</summary>
    public bool YapayZekaVar => !Kapali && Oneri?.YapayZeka is not null;
    /// <summary>Satır özetinin altındaki hafıza notu (ne yapılacağı ve dayanağı): "Gider · Yalnız genel kasa · Hafızadan: …"; yoksa boş.</summary>
    public string HafizaMetni => !Kapali && ErpOnerisi ? Oneri!.Aciklama
        // Yapay zekânın "kasa hareketi değil" ve yönü okunamayan satır önerisi yalnız yazıdır.
        : YapayZekaVar && Oneri!.IslemTuru is null or EkstreIslemTurleri.Atla ? Oneri!.Aciklama
        : !HafizaVar && !YapayZekaVar || Oneri is not { } o ? ""
        : string.Join(" · ", new[] { o.IslemTuru == EkstreIslemTurleri.Atla ? "Yok sayma önerisi" : IslemTurleri.FirstOrDefault(t => t.Kod == o.IslemTuru)?.Ad, DagilimNotu(o), o.Aciklama }
            .Where(s => !string.IsNullOrEmpty(s)));

    private string? DagilimNotu(Kasa.ApiClient.EkstreKuralOnerisi o) => o.IslemTuru == EkstreIslemTurleri.Atla ? null : o.DagilimTuru switch
    {
        DagilimBicimleri.Genel => "Yalnız genel kasa",
        DagilimBicimleri.Esit or DagilimBicimleri.Ozel => string.Join(", ", o.KanalIds.Select(id => Kanallar.FirstOrDefault(k => k.Id == id)?.Ad).OfType<string>()),
        _ => o.Hafiza?.KrediKartiId is { } kart ? Kartlar.FirstOrDefault(k => k.Id == kart)?.Ad : null
    };
    /// <summary>Daha önce yok sayılmış hareket: toplu yok sayma önerisine girer.</summary>
    public bool YokSayOnerisi => YokSayilabilir && !ElleDegisti && Oneri is { Durum: EkstreHafizaDurumlari.Hazir, IslemTuru: EkstreIslemTurleri.Atla };
    /// <summary>Kendiliğinden işaretlenir: hafızadan ya da hazır yapay zekâ önerisinden eksiksiz doldu, kesin ERP12 eşleşmesi ya da kesin kişisel
    /// kural önerisi var; elle değiştirilmedi (docs/specs/2026-11-26-ekstre-hareket-penceresi.md §3).</summary>
    public bool HazirOneri => Secilebilir && !ElleDegisti && Oneri is { } o && o.IslemTuru != EkstreIslemTurleri.Atla
        && (o.Durum == EkstreHafizaDurumlari.Hazir ? _hafizadan && Hazirla().Yaz is not null
            : o.Durum == YapayZekaDurumlari.Oneri ? o.YapayZeka is { Hazir: true } && _hafizadan && Hazirla().Yaz is not null
            : ErpOnerisi ? o.Durum == ErpOneriDurumlari.Eslesme && _erpden && Hazirla().Yaz is not null : OneriUygulanabilir);

    /// <summary>Satır bir kez kendiliğinden işaretlendi: kullanıcı işareti kaldırırsa bir daha konmaz.</summary>
    public bool KendiligindenIsaretlendi { get; internal set; }

    /// <summary>Bekleyen satırın "Kontrol edin" nedeni (canlı); hazır, kapalı ya da kullanıcının eksiksiz tamamladığı satırda null.</summary>
    public string? KontrolNedeni => Kapali || HazirOneri || ElleDegisti && Hazirla().Yaz is not null ? null
        : Kaynak.Yon is not ("Giris" or "Cikis") ? "yön belirsiz"
        : Oneri?.YapayZeka?.KontrolNedeni is { } neden ? neden
        : IslemTuru?.Kod is EkstreIslemTurleri.Gelir or EkstreIslemTurleri.Gider or EkstreIslemTurleri.KartHarcama && Hazirla().Yaz is null ? "kanal seçin"
        : Kaynak.Uyarilar.Count > 0 ? "uyarı var" : "kontrol edin";

    /// <summary>Gelen öneriyi (hafıza ya da ERP12 karşılığı) satıra yazar; kalkan önerinin izi önce silinir.</summary>
    public bool OneriyiKendiligindenUygula()
    {
        if (ErpOnerisi)
        {
            var hafiza = HafizayiUygula();
            return ErpOnerisiniUygula() || hafiza;
        }
        var erp = ErpOnerisiniUygula();
        return HafizayiUygula() || erp;
    }

    /// <summary>Hafıza önerisini satıra yazar; elle değiştirilmiş ya da kapanmış satır değişmez. Öneri kalktıysa (unutuldu) hafızadan dolan
    /// satır PDF'den okunan hâline döner. Satırı seçmez.</summary>
    /// <returns>Satır değiştiyse true.</returns>
    public bool HafizayiUygula()
    {
        if (Kapali || !Secilebilir || ElleDegisti)
            return false;
        var o = Oneri is { IslemTuru: not (null or EkstreIslemTurleri.Atla) } oneri && (oneri.Hafiza is not null || oneri.YapayZeka is not null) ? oneri : null;
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
            Aciklama = o?.Hafiza?.Aciklama ?? o?.YapayZeka?.Aciklama ?? Kaynak.Aciklama;
            _hafizadan = o is not null;
        }
        finally { _oneriUygulaniyor = false; }
        GosterimiYenile();
        return once != Durum();
    }

    /// <summary>Yenilenen modele taslak taşınırken alanların hafızadan geldiği ve PDF'ten okunan ilk tür de taşınır: hafıza bu arada
    /// unutulduysa satır ilk hâline dönebilir (Codex incelemesi, EmarKasa#62).</summary>
    private void HafizaDayanaginiAl(EkstreSatirEditor eski)
    {
        _hafizadan = eski._hafizadan;
        KendiligindenIsaretlendi = eski.KendiligindenIsaretlendi;
        _erpden = eski._erpden;
        _ilkTur = eski._ilkTur is { } tur ? IslemTurleri.FirstOrDefault(t => t.Kod == tur.Kod) : null;
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
