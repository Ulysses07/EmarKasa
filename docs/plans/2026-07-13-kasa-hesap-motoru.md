# Kasa Hesap Motoru Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Kasa defterinin saf hesap çekirdeğini (dönem üretimi, haftalık kanal/kasa devir zincirleri, aylık AY SONUCU) TDD ile bir C# kütüphanesi olarak yaz.

**Architecture:** Bağımlılıksız bir `Kasa.Core` sınıf kütüphanesi. Domain tipleri değişmez (immutable) `record`'lar. Hesaplar saf fonksiyonlardır — girdi olarak işlemler/gelenler/kanallar + açılış bakiyeleri alır, özet nesneleri döner. DB/API yok; bu plan sadece test edilebilir çekirdek üretir.

**Tech Stack:** .NET 10 (`net10.0`), C#, xUnit test.

Bu, 4 planlık setin **1. planıdır** (sonraki planlar: kalıcılık+API, React arayüz, dağıtım). Tasarım: `docs/specs/2026-07-13-kasa-defteri-design.md`.

---

## Dosya Yapısı

- `Kasa.sln` — çözüm dosyası
- `Kasa.Core/Kasa.Core.csproj` — sınıf kütüphanesi (`net10.0`)
- `Kasa.Core/Domain.cs` — domain tipleri (Kanal, Cari, Islem, Gelen, Donem, enum)
- `Kasa.Core/DonemUretici.cs` — dönem takvimi üretimi (hafta + ay sınırı)
- `Kasa.Core/HesapMotoru.cs` — haftalık + aylık hesap fonksiyonları + sonuç tipleri
- `Kasa.Core.Tests/Kasa.Core.Tests.csproj` — xUnit test projesi
- `Kasa.Core.Tests/DonemUreticiTests.cs`
- `Kasa.Core.Tests/HaftalikHesapTests.cs`
- `Kasa.Core.Tests/AylikHesapTests.cs`
- `Kasa.Core.Tests/HaziranSenaryoTests.cs` — Excel doğrulaması

---

### Task 0: Çözümü ve projeleri oluştur

**Files:**
- Create: `Kasa.sln`, `Kasa.Core/Kasa.Core.csproj`, `Kasa.Core.Tests/Kasa.Core.Tests.csproj`

- [ ] **Step 1: Projeleri oluştur**

Run (repo kökünde `<repo>`):
```bash
dotnet new sln -n Kasa
dotnet new classlib -n Kasa.Core -f net10.0
dotnet new xunit -n Kasa.Core.Tests -f net10.0
dotnet sln add Kasa.Core/Kasa.Core.csproj Kasa.Core.Tests/Kasa.Core.Tests.csproj
dotnet add Kasa.Core.Tests/Kasa.Core.Tests.csproj reference Kasa.Core/Kasa.Core.csproj
```

- [ ] **Step 2: Şablon dosyalarını sil**

`dotnet new classlib` `Class1.cs`, `dotnet new xunit` `UnitTest1.cs` üretir. Sil:
```bash
rm Kasa.Core/Class1.cs Kasa.Core.Tests/UnitTest1.cs
```

- [ ] **Step 3: Nullable + ImplicitUsings açık mı doğrula**

`Kasa.Core/Kasa.Core.csproj` içinde `<Nullable>enable</Nullable>` ve `<ImplicitUsings>enable</ImplicitUsings>` bulunmalı (şablon varsayılanı). Yoksa ekle.

- [ ] **Step 4: Derlemeyi doğrula**

Run: `dotnet build`
Expected: `Build succeeded`, 0 error.

- [ ] **Step 5: Commit**

```bash
git add Kasa.sln Kasa.Core Kasa.Core.Tests
git commit -m "chore: kasa çözümü ve proje iskeleti"
```

---

### Task 1: Domain tipleri

**Files:**
- Create: `Kasa.Core/Domain.cs`
- Test: `Kasa.Core.Tests/DomainTests.cs`

- [ ] **Step 1: Failing test yaz**

`Kasa.Core.Tests/DomainTests.cs`:
```csharp
using Kasa.Core;

namespace Kasa.Core.Tests;

public class DomainTests
{
    [Fact]
    public void Islem_alanlari_dogru_kurulur()
    {
        var islem = new Islem(
            Tarih: new DateOnly(2026, 6, 30),
            Cari: "PORT KARGO",
            TutarTl: 3874.03m,
            Kanal: "MEZAT",
            Tip: GiderTipi.Cari,
            Not: null);

        Assert.Equal("MEZAT", islem.Kanal);
        Assert.Equal(GiderTipi.Cari, islem.Tip);
        Assert.Equal(3874.03m, islem.TutarTl);
    }

    [Fact]
    public void Ortak_kanal_sabiti_dogru()
    {
        Assert.Equal("Ortak", Kanallar.Ortak);
    }
}
```

- [ ] **Step 2: Test başarısız olsun (derlenmez)**

Run: `dotnet test --filter DomainTests`
Expected: derleme hatası — `Islem`, `GiderTipi`, `Kanallar` tanımlı değil.

- [ ] **Step 3: Domain tiplerini yaz**

`Kasa.Core/Domain.cs`:
```csharp
namespace Kasa.Core;

/// <summary>Giden işleminin muhasebe tipi.</summary>
public enum GiderTipi
{
    Cari,        // tedarikçi/kişi ödemesi — haftalık kanal devrine girer
    SabitGider,  // SGK, maaş, vergi vb. — yalnız aylık kârlılığa girer
    KrediKarti   // K.K — yalnız aylık kârlılığa girer
}

public static class Kanallar
{
    /// <summary>Belirli bir kanala ait olmayan ortak gider için kanal etiketi.</summary>
    public const string Ortak = "Ortak";
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
    string? Not = null);

/// <summary>Haftalık gelen — dönem başına, kanal başına tek rakam.</summary>
public record Gelen(DateOnly DonemStart, string Kanal, decimal TutarTl);

/// <summary>Devir segmenti. End dahildir (inclusive).</summary>
public record Donem(DateOnly Start, DateOnly End)
{
    public int Yil => Start.Year;
    public int Ay => Start.Month;
    public bool Icerir(DateOnly d) => d >= Start && d <= End;
}
```

- [ ] **Step 4: Test geçsin**

Run: `dotnet test --filter DomainTests`
Expected: PASS (2 test).

- [ ] **Step 5: Commit**

```bash
git add Kasa.Core/Domain.cs Kasa.Core.Tests/DomainTests.cs
git commit -m "feat: domain tipleri (Kanal, Cari, Islem, Gelen, Donem)"
```

---

### Task 2: Dönem üretici (hafta + ay sınırı)

Haftalar Pazartesi başlar (Mon–Sun). Bir hafta ay sonunu geçerse ay sonunda kesilir; sonraki dönem ayın 1'inde başlar ve aynı haftanın Pazar'ına kadar sürer.

**Files:**
- Create: `Kasa.Core/DonemUretici.cs`
- Test: `Kasa.Core.Tests/DonemUreticiTests.cs`

- [ ] **Step 1: Failing test yaz**

`Kasa.Core.Tests/DonemUreticiTests.cs`:
```csharp
using Kasa.Core;

namespace Kasa.Core.Tests;

public class DonemUreticiTests
{
    [Fact]
    public void Pazartesi_baslayan_tam_haftalar_ve_ay_sonu_bolunmesi()
    {
        // 15 Haziran 2026 = Pazartesi
        var donemler = DonemUretici.Uret(
            baslangic: new DateOnly(2026, 6, 15),
            bitis: new DateOnly(2026, 7, 12));

        var beklenen = new (int, int, int, int)[]
        {
            (6, 15, 6, 21), // Pzt-Paz
            (6, 22, 6, 28),
            (6, 29, 6, 30), // ay sonunda kesildi
            (7, 1,  7, 5),  // aynı haftanın kalanı
            (7, 6,  7, 12),
        };

        Assert.Equal(beklenen.Length, donemler.Count);
        for (int i = 0; i < beklenen.Length; i++)
        {
            var (sa, sg, ea, eg) = beklenen[i];
            Assert.Equal(new DateOnly(2026, sa, sg), donemler[i].Start);
            Assert.Equal(new DateOnly(2026, ea, eg), donemler[i].End);
        }
    }

    [Fact]
    public void Hafta_ortasi_baslangic_kismi_ilk_donem_uretir()
    {
        // 17 Haziran 2026 = Çarşamba → ilk dönem 17-21 (kısmi)
        var donemler = DonemUretici.Uret(
            baslangic: new DateOnly(2026, 6, 17),
            bitis: new DateOnly(2026, 6, 28));

        Assert.Equal(new DateOnly(2026, 6, 17), donemler[0].Start);
        Assert.Equal(new DateOnly(2026, 6, 21), donemler[0].End);
        Assert.Equal(new DateOnly(2026, 6, 22), donemler[1].Start);
        Assert.Equal(new DateOnly(2026, 6, 28), donemler[1].End);
    }
}
```

- [ ] **Step 2: Test başarısız olsun**

Run: `dotnet test --filter DonemUreticiTests`
Expected: derleme hatası — `DonemUretici` tanımlı değil.

- [ ] **Step 3: Dönem üreticiyi yaz**

`Kasa.Core/DonemUretici.cs`:
```csharp
namespace Kasa.Core;

public static class DonemUretici
{
    /// <summary>
    /// [baslangic, bitis] aralığı için dönemleri üretir. Haftalar Pazartesi
    /// başlar (Mon–Sun); her dönem hafta-sonu veya ay-sonundan hangisi önce
    /// gelirse orada kapanır. Böylece her dönem tek bir takvim ayına aittir.
    /// </summary>
    public static IReadOnlyList<Donem> Uret(DateOnly baslangic, DateOnly bitis)
    {
        var sonuc = new List<Donem>();
        var imlec = baslangic;
        while (imlec <= bitis)
        {
            var haftaSonu = HaftaninPazari(imlec);
            var aySonu = AyinSonGunu(imlec);
            var donemSonu = haftaSonu <= aySonu ? haftaSonu : aySonu;
            if (donemSonu > bitis) donemSonu = bitis;
            sonuc.Add(new Donem(imlec, donemSonu));
            imlec = donemSonu.AddDays(1);
        }
        return sonuc;
    }

    // Verilen tarihin içinde bulunduğu Mon–Sun haftasının Pazar günü.
    private static DateOnly HaftaninPazari(DateOnly d)
    {
        // DayOfWeek: Sunday=0, Monday=1 ... Saturday=6
        int gun = (int)d.DayOfWeek;
        int pazaraKalan = gun == 0 ? 0 : 7 - gun;
        return d.AddDays(pazaraKalan);
    }

    private static DateOnly AyinSonGunu(DateOnly d)
        => new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
}
```

- [ ] **Step 4: Test geçsin**

Run: `dotnet test --filter DonemUreticiTests`
Expected: PASS (2 test).

- [ ] **Step 5: Commit**

```bash
git add Kasa.Core/DonemUretici.cs Kasa.Core.Tests/DonemUreticiTests.cs
git commit -m "feat: dönem üretici (Pazartesi haftası + ay sınırı bölme)"
```

---

### Task 3: Haftalık hesap (kanal devri + kasa devri)

**Files:**
- Create: `Kasa.Core/HesapMotoru.cs`
- Test: `Kasa.Core.Tests/HaftalikHesapTests.cs`

- [ ] **Step 1: Failing test yaz**

`Kasa.Core.Tests/HaftalikHesapTests.cs`:
```csharp
using Kasa.Core;

namespace Kasa.Core.Tests;

public class HaftalikHesapTests
{
    private static readonly Kanal[] IkiKanal =
    {
        new("MEZAT", AcilisDevri: 1000m),
        new("TOPTAN", AcilisDevri: 0m),
    };

    [Fact]
    public void Kanal_sonucu_gelen_eksi_cari_giden()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 21));
        var gelenler = new[] { new Gelen(new DateOnly(2026, 6, 15), "MEZAT", 500m) };
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 16), "A", 200m, "MEZAT", GiderTipi.Cari),
            // SabitGider kanal devrine GİRMEMELİ:
            new Islem(new DateOnly(2026, 6, 16), "SGK", 999m, "MEZAT", GiderTipi.SabitGider),
        };

        var ozetler = HesapMotoru.HaftalikHesapla(
            kasaAcilisDevri: 0m, IkiKanal, islemler, gelenler, donemler);

        var mezat = ozetler[0].Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal(500m, mezat.Gelen);
        Assert.Equal(200m, mezat.Giden);          // yalnız Cari
        Assert.Equal(300m, mezat.Sonuc);          // 500 - 200
        Assert.Equal(1300m, mezat.Devir);         // açılış 1000 + 300
    }

    [Fact]
    public void Kasa_devri_tum_gidenleri_sayar_ve_zincirlenir()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 28));
        var gelenler = new[]
        {
            new Gelen(new DateOnly(2026, 6, 15), "MEZAT", 500m),
            new Gelen(new DateOnly(2026, 6, 22), "MEZAT", 100m),
        };
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 16), "A", 200m, "MEZAT", GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 16), "SGK", 50m, "MEZAT", GiderTipi.SabitGider),
            new Islem(new DateOnly(2026, 6, 23), "Kira", 30m, Kanallar.Ortak, GiderTipi.SabitGider),
        };

        var ozetler = HesapMotoru.HaftalikHesapla(
            kasaAcilisDevri: 2000m, IkiKanal, islemler, gelenler, donemler);

        // 1. dönem: gelen 500, giden 250 (200+50) → sonuç 250 → devir 2250
        Assert.Equal(500m, ozetler[0].ToplamGelen);
        Assert.Equal(250m, ozetler[0].ToplamGiden);
        Assert.Equal(2250m, ozetler[0].KasaDevir);
        // 2. dönem: gelen 100, giden 30 (ortak) → sonuç 70 → devir 2320
        Assert.Equal(100m, ozetler[1].ToplamGelen);
        Assert.Equal(30m, ozetler[1].ToplamGiden);
        Assert.Equal(2320m, ozetler[1].KasaDevir);
    }
}
```

- [ ] **Step 2: Test başarısız olsun**

Run: `dotnet test --filter HaftalikHesapTests`
Expected: derleme hatası — `HesapMotoru` tanımlı değil.

- [ ] **Step 3: HesapMotoru haftalık kısmını yaz**

`Kasa.Core/HesapMotoru.cs`:
```csharp
namespace Kasa.Core;

public record KanalHaftalik(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir);

public record HaftalikOzet(
    Donem Donem,
    IReadOnlyList<KanalHaftalik> Kanallar,
    decimal ToplamGelen,
    decimal ToplamGiden,
    decimal KasaSonucu,
    decimal KasaDevir);

public static class HesapMotoru
{
    /// <summary>
    /// Dönem dönem haftalık özet üretir. Kanal devri yalnız o kanalın Cari tipli
    /// gidenini sayar; kasa devri TÜM gidenleri (her kanal + Ortak, her tip) sayar.
    /// Devirler tarih sırasına göre zincirlenir; ilk dönemin girişi açılış bakiyeleridir.
    /// </summary>
    public static IReadOnlyList<HaftalikOzet> HaftalikHesapla(
        decimal kasaAcilisDevri,
        IReadOnlyList<Kanal> kanallar,
        IReadOnlyList<Islem> islemler,
        IReadOnlyList<Gelen> gelenler,
        IReadOnlyList<Donem> donemler)
    {
        var sirali = donemler.OrderBy(d => d.Start).ToList();
        var kanalDevir = kanallar.ToDictionary(k => k.Ad, k => k.AcilisDevri);
        var kasaDevir = kasaAcilisDevri;
        var sonuc = new List<HaftalikOzet>();

        foreach (var donem in sirali)
        {
            var donemIslem = islemler.Where(i => donem.Icerir(i.Tarih)).ToList();
            var donemGelen = gelenler.Where(g => g.DonemStart == donem.Start).ToList();

            var kanalSatirlari = new List<KanalHaftalik>();
            foreach (var kanal in kanallar)
            {
                decimal gelen = donemGelen.Where(g => g.Kanal == kanal.Ad).Sum(g => g.TutarTl);
                decimal gidenCari = donemIslem
                    .Where(i => i.Kanal == kanal.Ad && i.Tip == GiderTipi.Cari)
                    .Sum(i => i.TutarTl);
                decimal kanalSonuc = gelen - gidenCari;
                kanalDevir[kanal.Ad] += kanalSonuc;
                kanalSatirlari.Add(new KanalHaftalik(kanal.Ad, gelen, gidenCari, kanalSonuc, kanalDevir[kanal.Ad]));
            }

            decimal toplamGelen = donemGelen.Sum(g => g.TutarTl);
            decimal toplamGiden = donemIslem.Sum(i => i.TutarTl);
            decimal kasaSonucu = toplamGelen - toplamGiden;
            kasaDevir += kasaSonucu;

            sonuc.Add(new HaftalikOzet(donem, kanalSatirlari, toplamGelen, toplamGiden, kasaSonucu, kasaDevir));
        }
        return sonuc;
    }
}
```

- [ ] **Step 4: Test geçsin**

Run: `dotnet test --filter HaftalikHesapTests`
Expected: PASS (2 test).

- [ ] **Step 5: Commit**

```bash
git add Kasa.Core/HesapMotoru.cs Kasa.Core.Tests/HaftalikHesapTests.cs
git commit -m "feat: haftalık hesap (kanal + kasa devir zincirleri)"
```

---

### Task 4: Aylık hesap (AY SONUCU)

**Files:**
- Modify: `Kasa.Core/HesapMotoru.cs` (aylık tipler + `AylikHesapla` ekle)
- Test: `Kasa.Core.Tests/AylikHesapTests.cs`

- [ ] **Step 1: Failing test yaz**

`Kasa.Core.Tests/AylikHesapTests.cs`:
```csharp
using Kasa.Core;

namespace Kasa.Core.Tests;

public class AylikHesapTests
{
    private static readonly Kanal[] UcKanal =
    {
        new("MEZAT"), new("PERAKENDE"), new("TOPTAN"),
    };

    [Fact]
    public void Ay_sonucu_tum_bilesenleri_ve_ortak_payini_dusiyor()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var ilkDonemStart = donemler[0].Start; // 1 Haziran (Pazartesi)

        var gelenler = new[] { new Gelen(ilkDonemStart, "MEZAT", 1000m) };
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 3), "Tedarik", 100m, "MEZAT", GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 4), "Maaş",    200m, "MEZAT", GiderTipi.SabitGider),
            new Islem(new DateOnly(2026, 6, 5), "K.K",     50m,  "MEZAT", GiderTipi.KrediKarti),
            new Islem(new DateOnly(2026, 6, 6), "Kira",    300m, Kanallar.Ortak, GiderTipi.SabitGider),
        };

        var rapor = HesapMotoru.AylikHesapla(2026, 6, UcKanal, islemler, gelenler, donemler);
        var mezat = rapor.Kanallar.Single(k => k.Kanal == "MEZAT");

        Assert.Equal(1000m, mezat.Gelen);
        Assert.Equal(100m, mezat.CariGiden);
        Assert.Equal(200m, mezat.SabitGider);
        Assert.Equal(50m, mezat.KrediKarti);
        Assert.Equal(100m, mezat.OrtakPay);            // 300 / 3 aktif kanal
        // 1000 - 100 - 200 - 50 - 100 = 550
        Assert.Equal(550m, mezat.AySonucu);
    }

    [Fact]
    public void Ortak_pay_yalniz_aktif_kanal_sayisina_bolunur()
    {
        var kanallar = new[]
        {
            new Kanal("MEZAT"), new Kanal("PERAKENDE"),
            new Kanal("ESKI", Aktif: false),   // pasif → bölmeye dahil değil
        };
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 6), "Kira", 300m, Kanallar.Ortak, GiderTipi.SabitGider),
        };

        var rapor = HesapMotoru.AylikHesapla(2026, 6, kanallar, islemler, Array.Empty<Gelen>(), donemler);
        Assert.Equal(150m, rapor.Kanallar.Single(k => k.Kanal == "MEZAT").OrtakPay); // 300/2
    }
}
```

- [ ] **Step 2: Test başarısız olsun**

Run: `dotnet test --filter AylikHesapTests`
Expected: derleme hatası — `AylikHesapla` tanımlı değil.

- [ ] **Step 3: Aylık hesap ekle**

`Kasa.Core/HesapMotoru.cs` dosyasının SONUNA (aynı namespace içinde) ekle:
```csharp
public record KanalAylik(
    string Kanal,
    decimal Gelen,
    decimal CariGiden,
    decimal SabitGider,
    decimal KrediKarti,
    decimal OrtakPay,
    decimal AySonucu);

public record AylikRapor(int Yil, int Ay, IReadOnlyList<KanalAylik> Kanallar);
```

Ve `HesapMotoru` sınıfının içine metodu ekle:
```csharp
    /// <summary>
    /// Bir takvim ayı için kanal başına AY SONUCU üretir.
    /// Aylık gelen = o aya düşen dönemlerin geleni. Ortak giderler (Kanallar.Ortak)
    /// aktif kanal sayısına bölünüp her kanaldan düşülür.
    /// </summary>
    public static AylikRapor AylikHesapla(
        int yil,
        int ay,
        IReadOnlyList<Kanal> kanallar,
        IReadOnlyList<Islem> islemler,
        IReadOnlyList<Gelen> gelenler,
        IReadOnlyList<Donem> donemler)
    {
        var ayinDonemleri = donemler.Where(d => d.Yil == yil && d.Ay == ay).ToList();
        var ayinDonemStartlari = ayinDonemleri.Select(d => d.Start).ToHashSet();
        var ayinIslemleri = islemler.Where(i => i.Tarih.Year == yil && i.Tarih.Month == ay).ToList();

        decimal ortakToplam = ayinIslemleri.Where(i => i.Kanal == Kanallar.Ortak).Sum(i => i.TutarTl);
        int aktifKanalSayisi = kanallar.Count(k => k.Aktif);
        decimal ortakPay = aktifKanalSayisi > 0 ? ortakToplam / aktifKanalSayisi : 0m;

        var satirlar = new List<KanalAylik>();
        foreach (var kanal in kanallar)
        {
            decimal gelen = gelenler
                .Where(g => g.Kanal == kanal.Ad && ayinDonemStartlari.Contains(g.DonemStart))
                .Sum(g => g.TutarTl);
            decimal cari = ayinIslemleri.Where(i => i.Kanal == kanal.Ad && i.Tip == GiderTipi.Cari).Sum(i => i.TutarTl);
            decimal sabit = ayinIslemleri.Where(i => i.Kanal == kanal.Ad && i.Tip == GiderTipi.SabitGider).Sum(i => i.TutarTl);
            decimal kk = ayinIslemleri.Where(i => i.Kanal == kanal.Ad && i.Tip == GiderTipi.KrediKarti).Sum(i => i.TutarTl);
            decimal aySonucu = gelen - cari - sabit - kk - ortakPay;
            satirlar.Add(new KanalAylik(kanal.Ad, gelen, cari, sabit, kk, ortakPay, aySonucu));
        }
        return new AylikRapor(yil, ay, satirlar);
    }
```

- [ ] **Step 4: Test geçsin**

Run: `dotnet test --filter AylikHesapTests`
Expected: PASS (2 test).

- [ ] **Step 5: Commit**

```bash
git add Kasa.Core/HesapMotoru.cs Kasa.Core.Tests/AylikHesapTests.cs
git commit -m "feat: aylık hesap (AY SONUCU + ortak pay dağıtımı)"
```

---

### Task 5: Haziran senaryosu — Excel doğrulaması

Spec §4.1'deki bilinen rakamları uçtan uca doğrula. İşlemler, ekran görüntüsündeki kanal-bazlı toplamları verecek şekilde kurgulanır (birkaç temsili işlem toplamı = gerçek toplam).

**Files:**
- Test: `Kasa.Core.Tests/HaziranSenaryoTests.cs`

- [ ] **Step 1: Doğrulama testini yaz**

`Kasa.Core.Tests/HaziranSenaryoTests.cs`:
```csharp
using Kasa.Core;

namespace Kasa.Core.Tests;

public class HaziranSenaryoTests
{
    // Ekran görüntüsündeki 29-30 Haziran dönemi (image 1).
    // Kanal açılış devirleri = "GEÇEN HAFTA DEVİR", kasa açılışı = 2.907.053,21.
    private static readonly Kanal[] Kanallar3 =
    {
        new("MEZAT",     AcilisDevri: 4_991_052m),
        new("PERAKENDE", AcilisDevri: 2_013_516m),
        new("TOPTAN",    AcilisDevri: 619_647m),
    };

    [Fact]
    public void Haziran_29_30_donemi_excel_rakamlarini_uretir()
    {
        var start = new DateOnly(2026, 6, 29); // Pazartesi
        var donemler = DonemUretici.Uret(start, new DateOnly(2026, 6, 30)); // tek dönem 29-30

        var gelenler = new[]
        {
            new Gelen(start, "MEZAT",     289_425m),
            new Gelen(start, "PERAKENDE", 271_006m),
            new Gelen(start, "TOPTAN",    207_000m),
        };

        // Kanal-bazlı Cari giden toplamları (screenshot):
        // MEZAT 1.308.800, PERAKENDE 1.221.374, TOPTAN 360.000
        // Ayrıca kanalsız/Ortak giderler: TOPLAM GİDEN 3.345.495 - 2.890.174 = 455.321
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 29), "MEZAT-cari",     1_308_800m, "MEZAT",     GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 29), "PERAKENDE-cari", 1_221_374m, "PERAKENDE", GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 29), "TOPTAN-cari",    360_000m,   "TOPTAN",    GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 30), "SGK/Vergi/vb.",  455_321m,   Kanallar.Ortak, GiderTipi.SabitGider),
        };

        var ozetler = HesapMotoru.HaftalikHesapla(2_907_053.21m, Kanallar3, islemler, gelenler, donemler);
        var d = ozetler.Single();

        var mezat = d.Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal(-1_019_375m, mezat.Sonuc);   // 289.425 - 1.308.800
        Assert.Equal(3_971_677m, mezat.Devir);     // 4.991.052 - 1.019.375

        Assert.Equal(767_431m, d.ToplamGelen);
        Assert.Equal(3_345_495m, d.ToplamGiden);
        Assert.Equal(328_989.21m, d.KasaDevir);    // 2.907.053,21 + 767.431 - 3.345.495
    }
}
```

- [ ] **Step 2: Testi çalıştır**

Run: `dotnet test --filter HaziranSenaryoTests`
Expected: PASS. Geçmezse önceki task'lardaki hesap fonksiyonlarında hata var — düzelt.

- [ ] **Step 3: Tüm testleri çalıştır**

Run: `dotnet test`
Expected: tüm testler PASS.

- [ ] **Step 4: Commit**

```bash
git add Kasa.Core.Tests/HaziranSenaryoTests.cs
git commit -m "test: Haziran Excel rakamlarıyla uçtan uca doğrulama"
```

---

## Self-Review (yazar kontrolü — tamamlandı)

**Spec kapsamı:** §2.1 dönem üretimi → Task 2. §2.2 iki devir zinciri → Task 3.
§4 haftalık formüller → Task 3; aylık AY SONUCU → Task 4; §4.1 doğrulama → Task 5.
§3 veri modeli → Task 1. (API/kalıcılık/arayüz/dağıtım bu planın kapsamı dışı —
Plan 2-4.)

**Placeholder taraması:** Yok — her adımda tam kod ve komut mevcut.

**Tip tutarlılığı:** `HesapMotoru.HaftalikHesapla` imzası Task 3 ve Task 5'te aynı
(`decimal kasaAcilisDevri, IReadOnlyList<Kanal>, IReadOnlyList<Islem>,
IReadOnlyList<Gelen>, IReadOnlyList<Donem>`). `AylikHesapla` Task 4'te tanımlı,
imza tutarlı. `Kanallar.Ortak`, `GiderTipi`, `Donem.Icerir`, `Donem.Yil/Ay`
Task 1'de tanımlanıp sonraki task'larda tutarlı kullanıldı.

**Not:** Bu plan `net10.0` hedefler (kullanıcının SDK'sı .NET 10). Farklı bir
sürüm gerekirse Task 0'daki `-f net10.0` bayrakları değiştirilir.
