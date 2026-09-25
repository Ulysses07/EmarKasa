namespace Kasa.Core;

/// <summary>Giden işleminin muhasebe tipi.</summary>
public enum GiderTipi
{
    Cari,        // tedarikçi/kişi ödemesi — haftalık kanal devrine girer
    SabitGider,  // SGK, maaş, vergi vb. — yalnız aylık kârlılığa girer
    KrediKarti   // K.K — bir sonraki ay sonunda kasadan/kanaldan düşülür (ertelemeli)
}

public static class Kanallar
{
    /// <summary>Belirli bir kanala ait olmayan ortak gider için kanal etiketi.</summary>
    public const string Ortak = "Ortak";
    /// <summary>Gerçek giderin kanalı henüz kesinleşmedi; ortak paya dağıtılmaz.</summary>
    public const string DagilimBekliyor = "Dağılım bekliyor";
}

/// <summary>Gelir kanalı ve kümülatif devir başlangıcı.</summary>
public record Kanal(string Ad, decimal AcilisDevri = 0m, bool Aktif = true, int Sira = 0);

/// <summary>Cari (kişi/firma) — yalnız isim; bakiye tutulmaz.</summary>
public record Cari(string Ad, bool Aktif = true);

/// <summary>Kalem kalem giden (nakit çıkışı).</summary>
public record Islem(
    DateOnly Tarih,
    string Cari,
    decimal TutarTl,
    string Kanal,          // "MEZAT" | "PERAKENDE" | "TOPTAN" | Kanallar.Ortak
    GiderTipi Tip,
    string? Not = null,
    bool DagilimBekliyor = false,
    bool NakitKartOdemesi = false,
    bool AylikGider = false,
    bool YalnizGenelKasa = false);

/// <summary>Haftalık gelen — dönem başına, kanal başına tek rakam.</summary>
public record Gelen(DateOnly DonemStart, string Kanal, decimal TutarTl, bool KrediGirisi = false, bool GenelGelir = false);

/// <summary>Devir segmenti. End dahildir (inclusive).</summary>
public record Donem(DateOnly Start, DateOnly End)
{
    public int Yil => Start.Year;
    public int Ay => Start.Month;
    public bool Icerir(DateOnly d) => d >= Start && d <= End;
}
