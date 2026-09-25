using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api;

/// <summary>Alan hatalarını, kayıt üzerinde değişiklik yapmadan toplar.</summary>
public sealed class GirdiDogrulama
{
    private readonly Dictionary<string, string[]> _hatalar = new();
    private const decimal EnBuyukTutar = 999_999_999_999.99m;

    public void Kontrol(bool gecerli, string alan, string mesaj)
    {
        if (!gecerli) _hatalar[alan] = [mesaj];
    }

    public void Metin(string? deger, string alan, int sinir = 200, bool zorunlu = true)
    {
        Kontrol(!zorunlu || !string.IsNullOrWhiteSpace(deger), alan, "Bu alan boş olamaz.");
        Kontrol(deger is null || deger.Length <= sinir, alan, $"En fazla {sinir} karakter girilebilir.");
    }

    public void Para(decimal deger, string alan, bool negatifOlabilir = false)
    {
        Kontrol(deger >= (negatifOlabilir ? -EnBuyukTutar : 0m) && deger <= EnBuyukTutar,
            alan, negatifOlabilir ? "Tutar izin verilen aralığın dışında." : "Tutar negatif olamaz veya izin verilen sınırı aşamaz.");
        Kontrol(decimal.Round(deger, 2) == deger, alan, "Tutar en fazla iki ondalık basamak içerebilir.");
    }

    public void Tarih(DateOnly tarih, string alan)
        => Kontrol(tarih != default && tarih.Year < 9999, alan, "Geçerli bir tarih seçin.");

    public KanalEntity? Kanal(KasaDbContext db, string? ad, bool ortakOlabilir = true)
    {
        Metin(ad, "kanal");
        var temiz = ad?.Trim();
        if (ortakOlabilir && temiz == Kanallar.Ortak) return null;
        var kanal = db.Kanallar.FirstOrDefault(k => k.Ad == temiz);
        Kontrol(kanal is not null, "kanal", "Kayıtlı bir kanal seçin.");
        return kanal;
    }

    public void Kart(KasaDbContext db, int? id, bool zorunlu = false)
    {
        Kontrol(id is null ? !zorunlu : db.KrediKartlari.Any(k => k.Id == id), "krediKartiId", "Kayıtlı bir kredi kartı seçin.");
        if (id is not null) Kontrol(!db.TakipKartlar.Any(k => k.KrediKartiId == id && !k.Aktif), "krediKartiId", "Bu kart yeni kullanıma kapalı.");
    }

    public IResult? Sonuc() => _hatalar.Count == 0 ? null : Results.ValidationProblem(_hatalar);

    public static bool AyrilmisKanalAdi(string ad)
        => string.Equals(ad, Kanallar.Ortak, StringComparison.OrdinalIgnoreCase)
           || string.Equals(ad, Kanallar.DagilimBekliyor, StringComparison.OrdinalIgnoreCase)
           || string.Equals(ad, KrediTuretici.KrediKanal, StringComparison.OrdinalIgnoreCase);
}
