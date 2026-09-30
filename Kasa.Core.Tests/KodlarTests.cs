using Kasa.Core.Kodlar;

namespace Kasa.Core.Tests;

/// <summary>Kasa.Core.Kodlar değerleri API sözleşmesi ve veritabanı değeridir: burada dize olarak sabitlenir, sabit yanlışlıkla
/// değişirse test kırmızı olur. Beklenen değerler bilerek literal yazılır.</summary>
public class KodlarTests
{
    [Fact]
    public void Kanal_etiketleri() => Esit(
        ("Ortak", KanalEtiketleri.Ortak), ("Dağılım bekliyor", KanalEtiketleri.DagilimBekliyor), ("Genel kasa", KanalEtiketleri.GenelKasa));

    [Fact]
    public void Alis_durumlari() => Esit(
        ("Taslak", AlisDurumlari.Taslak), ("Incelemede", AlisDurumlari.Incelemede), ("Onaylandi", AlisDurumlari.Onaylandi));

    [Fact]
    public void Dagilim_bicimleri() => Esit(
        ("Genel", DagilimBicimleri.Genel), ("Esit", DagilimBicimleri.Esit), ("Ozel", DagilimBicimleri.Ozel),
        ("Otomatik", DagilimBicimleri.Otomatik), ("Eslesme", DagilimBicimleri.Eslesme));

    [Fact]
    public void Ekstre_islem_turleri() => Esit(
        ("Gelir", EkstreIslemTurleri.Gelir), ("Gider", EkstreIslemTurleri.Gider), ("KartHarcama", EkstreIslemTurleri.KartHarcama),
        ("KartIade", EkstreIslemTurleri.KartIade), ("KartOdemesi", EkstreIslemTurleri.KartOdemesi), ("Eslestir", EkstreIslemTurleri.Eslestir),
        ("Atla", EkstreIslemTurleri.Atla));

    [Fact]
    public void Ekstre_kaynaklari() => Esit(("Kart", EkstreKaynaklari.Kart), ("Banka", EkstreKaynaklari.Banka));

    [Fact]
    public void Eslesme_turleri() => Esit(
        ("Gider", EslesmeTurleri.Gider), ("KartHarcama", EslesmeTurleri.KartHarcama), ("KartTaksidi", EslesmeTurleri.KartTaksidi),
        ("KartOdeme", EslesmeTurleri.KartOdeme));

    [Fact]
    public void Eslesme_durumlari() => Esit(("Eslesti", EslesmeDurumlari.Eslesti), ("KayitYok", EslesmeDurumlari.KayitYok));

    [Fact]
    public void Taksit_durumlari() => Esit(
        ("Bekliyor", TaksitDurumlari.Bekliyor), ("KasayaIslendi", TaksitDurumlari.KasayaIslendi), ("Iptal", TaksitDurumlari.Iptal));

    [Fact]
    public void Aylik_gider_durumlari() => Esit(
        ("Planlandi", AylikGiderDurumlari.Planlandi), ("Odendi", AylikGiderDurumlari.Odendi), ("Iptal", AylikGiderDurumlari.Iptal));

    [Fact]
    public void Takip_kaynaklari() => Esit(("Kart", TakipKaynaklari.Kart), ("Kredi", TakipKaynaklari.Kredi));

    [Fact]
    public void Takip_olay_turleri() => Esit(
        ("Kesim", TakipOlayTurleri.Kesim), ("SonOdeme", TakipOlayTurleri.SonOdeme), ("Taksit", TakipOlayTurleri.Taksit));

    [Fact]
    public void Benzer_arama_turleri() => Esit(
        ("Gider", BenzerAramaTurleri.Gider), ("AylikGider", BenzerAramaTurleri.AylikGider), ("AlisOdeme", BenzerAramaTurleri.AlisOdeme),
        ("KartHarcama", BenzerAramaTurleri.KartHarcama), ("KartOdeme", BenzerAramaTurleri.KartOdeme));

    [Fact]
    public void Benzer_kayit_kaynaklari() => Esit(
        ("Islem", BenzerKayitKaynaklari.Islem), ("KartHarcama", BenzerKayitKaynaklari.KartHarcama), ("KartOdeme", BenzerKayitKaynaklari.KartOdeme),
        ("EskiKartOdeme", BenzerKayitKaynaklari.EskiKartOdeme), ("KrediTaksidi", BenzerKayitKaynaklari.KrediTaksidi),
        ("EskiKrediTaksidi", BenzerKayitKaynaklari.EskiKrediTaksidi));

    private static void Esit(params (string Beklenen, string Gercek)[] ciftler) =>
        Assert.All(ciftler, c => Assert.Equal(c.Beklenen, c.Gercek));
}
