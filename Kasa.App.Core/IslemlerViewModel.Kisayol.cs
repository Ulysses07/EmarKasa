namespace Kasa.App.Core;

// Paket C · 26: klavye kısayolları ve odak istekleri (eşleme KlavyeKisayollari'nda).
public partial class IslemlerViewModel
{
    public const string OdakTarih = "Tarih";
    public const string OdakCari = "Cari";
    public const string OdakTutar = "Tutar";

    /// <summary>Sayfa bu alanı odaklasın ("Tarih", "Cari", "Tutar").</summary>
    public event EventHandler<string>? OdakIstendi;

    private void OdakIste(string alan) => OdakIstendi?.Invoke(this, alan);

    public string KisayolYardimi => KlavyeKisayollari.Yardim;

    /// <summary>
    /// Tutar kutusunda şu an yazılı metin (sayfa her değişiklikte iter; null = bilinmiyor). Geçersiz
    /// metin bağlı tutarı değiştirmez, son geçerli tutar kalır; kutu ancak odak çıkınca ona döner.
    /// Odak çıkmadan kaydeden klavye yolları (Enter, Ctrl+S) bu yüzden ekrandaki metni denetler.
    /// </summary>
    public string? TutarKutusuMetni { get; set; }

    /// <summary>Gelen tutarı kutusunda şu an yazılı metin (bkz. <see cref="TutarKutusuMetni"/>).</summary>
    public string? GelenTutarKutusuMetni { get; set; }

    public static string TutarKutusuHatasi(string alan, string? neden)
        => $"{alan} geçersiz: {neden} Kaydedilmedi; tutarı düzeltin.";

    /// <summary>Kutudaki metin geçersizse <see cref="TemelViewModel.Hata"/>'ya yazar ve true döner.</summary>
    private bool KutuGecersiz(string? metin, string alan)
    {
        if (metin is null || ParaGiris.Ayristir(metin) is not { Gecerli: false } p) return false;
        Hata = TutarKutusuHatasi(alan, p.Hata);
        return true;
    }

    /// <summary>
    /// Klavye kısayolunu uygular; işlendiyse true (sayfa tuşu yutar). Yalnız editörde çalışır.
    /// Kaydet, bekleyen bir uyarı/onay varken hiçbir şey yapmaz (uyarı klavyeyle geçilemez); tutar
    /// kutusunda geçersiz bir metin varken kaydetmez (ekrandaki tutarla kaydedilecek tutar ayrışmasın).
    /// </summary>
    public bool KisayolCalistir(KisayolEylemi eylem)
    {
        if (!EditorMu) return false;
        switch (eylem)
        {
            case KisayolEylemi.Kaydet:
                if (UyariBekliyor || IleriTarihOnayBekliyor || BenzerCariSoruluyor) return true;
                if (KutuGecersiz(TutarKutusuMetni, "Tutar")) return true;
                if (KaydetCommand.CanExecute(null)) KaydetCommand.Execute(null);
                return true;
            case KisayolEylemi.YeniSatir:
                Yeni();
                OdakIste(OdakCari);
                return true;
            case KisayolEylemi.Vazgec:
                return Vazgec();
            default:
                if (KlavyeKisayollari.KanalSirasi(eylem) is { } i && i < GiderKanallari.Count)
                {
                    DuzenKanal = GiderKanallari[i].Ad;
                    return true;
                }
                return false;
        }
    }

    /// <summary>
    /// Esc: önce açık uyarıyı / onayı / soruyu kapatır; hiçbiri yoksa düzenlenen kaydı bırakır (form
    /// yeni kayda döner). Yeni kayıt yazılırken Esc formu silmez (yazılan kaybolmasın).
    /// </summary>
    private bool Vazgec()
    {
        if (UyariBekliyor) { UyarilariTemizle(); return true; }
        if (IleriTarihOnayBekliyor) { IleriTarihUyarisi = null; return true; }
        if (BenzerCariSoruluyor) { BenzerCariKapat(); return true; }
        if (GelenCakismaVar) { GelenCakismaKapat(); return true; }
        if (Silme.OnayBekliyor) { Silme.Vazgec(); return true; }
        if (DuzenId != 0) { Yeni(); return true; }
        return false;
    }

    /// <summary>İşlem tarihi kutusunda B (bugün) / D (dün). İşlendiyse true.</summary>
    public bool TarihKisayolu(string? tus)
    {
        if (KlavyeKisayollari.TarihTusu(tus, BugunTarih) is not { } t) return false;
        DuzenTarih = t.ToDateTime(TimeOnly.MinValue);
        return true;
    }

    /// <summary>Gelen tarihi kutusunda B (bugün) / D (dün). İşlendiyse true.</summary>
    public bool GelenTarihKisayolu(string? tus)
    {
        if (KlavyeKisayollari.TarihTusu(tus, BugunTarih) is not { } t) return false;
        GelenTarih = t.ToDateTime(TimeOnly.MinValue);
        return true;
    }

    /// <summary>Tutar ve not kutularında Enter: Ctrl+S ile aynı (bekleyen uyarı/onay varken kaydetmez).</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void EnterKaydet() => KisayolCalistir(KisayolEylemi.Kaydet);

    /// <summary>
    /// Cari kutusunda Enter: yazılan kayıtlı bir ad değilse ve öneri varsa ilk öneriyi seçer. Tutar
    /// henüz girilmediyse (0) kaydetmez, tutara geçer (kayıtlı ad kayıtlı yazımına çevrilir); tutar
    /// varsa kaydeder. Böylece Tarih → Cari (Enter) → Tutar (Enter) akışı fare olmadan yürür.
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void CariEnter()
    {
        var ad = DuzenCari?.Trim() ?? "";
        var kayitli = SabitGiderMi ? KayitliKalem(ad) : KayitliCari(ad);
        if (kayitli is null && CariOnerileri.Count > 0)
        {
            DuzenCari = CariOnerileri[0];
            OdakIste(OdakTutar);
            return;
        }
        if (DuzenTutar <= 0m && EditorMu)
        {
            if (kayitli is not null && kayitli != DuzenCari) DuzenCari = kayitli;
            OdakIste(OdakTutar);
            return;
        }
        KisayolCalistir(KisayolEylemi.Kaydet);
    }

    /// <summary>
    /// Gelen tutarı kutusunda Enter: kutudaki metin geçersizse kaydetmez; "Üzerine yaz / Üstüne ekle"
    /// sorusu açıkken hiçbir şey yapmaz (soru düğmeyle yanıtlanır).
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void GelenEnter()
    {
        if (!EditorMu || GelenCakismaVar) return;
        if (KutuGecersiz(GelenTutarKutusuMetni, "Gelen tutarı")) return;
        if (GelenKaydetCommand.CanExecute(null)) GelenKaydetCommand.Execute(null);
    }
}
