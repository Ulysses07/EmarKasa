namespace Kasa.App.Core;

/// <summary>Kurulum uygulamayı kapatacağı için süren işi ve kaydedilmemiş formları korur; hiçbir formu sıfırlamaz.</summary>
public static class UygulamaKurulumKarari
{
    public static string? Engel(bool guvenliEkran, IEnumerable<object?> baglamlar)
    {
        if (!guvenliEkran)
            return "Açık formunuzu kaydedip Haftalık rapor ya da Giriş ekranına dönün; ardından kurulumu başlatın.";
        return Engel(baglamlar);
    }
    public static string? Engel(IEnumerable<object?> baglamlar)
    {
        var durumlar = baglamlar.ToArray();
        if (durumlar.Any(b => b is TemelViewModel { Mesgul: true } or AuthViewModel { Mesgul: true }))
            return "Devam eden işlem tamamlanmadan güncelleme kurulamaz. İşlem bittikten sonra yeniden deneyin.";
        if (durumlar.Any(b => b is IKaydedilmemisForm { KaydedilmemisDegisiklikVar: true }))
            return "Açık formda kaydedilmemiş değişiklik var. Pencereyi kapatıp formu kaydedin; ardından güncellemeyi kurun.";
        return null;
    }
}
