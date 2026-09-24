namespace Kasa.App.Core;

// Paket A — "Bugün yapılacaklar" → "Gelen gir" derin bağlantısı (DerinBaglanti.GelenGir).
public partial class IslemlerViewModel
{
    /// <summary>
    /// Gelen formunu eksik dönemin başlangıcı ve kanalıyla hazırlar (tutar boşaltılır); kaydetmez. Kanal
    /// yüklemeden sonra da seçili görünür (<c>SenkronSecim</c>). Sorguda geçerli dönem yoksa (ör. menüden boş
    /// sorguyla gelindi) form olduğu gibi kalır.
    /// </summary>
    /// <returns>Form hazırlandıysa true (sayfa gelen formuna kaydırır).</returns>
    public bool GelenIstegiUygula(IDictionary<string, object>? sorgu)
    {
        if (DerinBaglanti.Donem(sorgu) is not { } donem) return false;
        GelenTarih = donem.ToDateTime(TimeOnly.MinValue);
        GelenKanal = DerinBaglanti.Oku(sorgu, DerinBaglanti.KanalAnahtari) ?? "";
        GelenTutar = 0m;
        return true;
    }
}
