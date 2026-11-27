using Kasa.ApiClient;
using Kasa.Core.Kodlar;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

// Kayıt özeti (docs/specs/2026-11-26-slop-temizligi.md §9): satır dökümü yerine toplamlar, kanal bazında kasa etkisi ve yalnız varsa uyarılar;
// tablodaki "yapay zekâ" kaynak etiketi yalnız öneri varsayılandan farklıyken.
public partial class EkstreAktarmaTests
{
    private const string TurFarkli = "İşlem türü belgeden okunan öneriden farklı; yönünü ve kasaya etkisini kontrol edin.";

    private static EkstreSatirOnizleme Etki(int no, string tur, decimal tutar, decimal kasa, TakipKanalPayi[] paylar, params string[] uyarilar) =>
        new(no, Tarih, $"Hareket {no}", tutar, tur, kasa, paylar, uyarilar);

    /// <summary>Gelir, kanallara bölünmüş gider, genel kasa gideri, kısmen dağılım bekleyen kart ödemesi ve kasa etkisi olmayan eşleştirme.</summary>
    private static EkstreOnizlemeDto KarisikOnizleme() => new("ozet", -100m,
    [
        Etki(1, EkstreIslemTurleri.Gelir, 500m, 500m, []),
        Etki(2, EkstreIslemTurleri.Gider, 300m, -300m, [new(2, "PERAKENDE", 150m), new(1, "MEZAT", 150m)], TurFarkli),
        Etki(3, EkstreIslemTurleri.Gider, 100m, -100m, [], TurFarkli),
        Etki(4, EkstreIslemTurleri.KartOdemesi, 200m, -200m, [new(2, "PERAKENDE", 120m), new(null, "Dağılım bekliyor", 80m)], "Benzer kayıt: Kart ödemesi · 27.09.2026 · 200,00 TL"),
        Etki(5, EkstreIslemTurleri.Eslestir, 100m, 0m, []),
    ], ["Önceden kaydedilen kart avansının kanal dağılımı bu harcamalarla belirlenir."], true);

    private static async Task<EkstreAktarmaViewModel> OzetiGosterilmis(EkstreOnizlemeDto onizleme)
    {
        var (vm, api, _) = await Hazir(new()
        {
            Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3), Satir(4), Satir(5)),
            Oneriler = [YzHazir(1), YzHazir(2), YzHazir(3), YzHazir(4), YzHazir(5)],
        });
        api.OnizlemeYaniti = Task.FromResult(onizleme);
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.True(vm.OnizlemeVar);
        return vm;
    }

    [Fact]
    public async Task Kayit_ozeti_once_toplamlari_sonra_kanal_etkisini_sonra_uyarilari_verir()
    {
        var vm = await OzetiGosterilmis(KarisikOnizleme());
        // Toplamlar: kaydedilecek satır ve genel kasa etkisi, sonra satır formundaki tür sırasıyla her türün kasa etkisi.
        Assert.Equal(
        [
            new("5 satır kaydedilecek", "Genel kasa etkisi", -100m),
            new("Gelir", "1 satır", 500m),
            new("Gider", "2 satır", -400m),
            new("Karta ödeme", "1 satır", -200m),
            new EkstreOzetSatiri("Mevcut kayıtla eşleştir", "1 satır · kasa etkisi yok", 0m),
        ], vm.OzetToplamlari);
        // Kanal etkisi işaretlidir (gider eksi): önce yalnız genel kasa, sonra kanallar kendi sırasıyla, listede olmayan pay sonda.
        Assert.Equal(
        [
            new(EkstreKayitOzeti.GenelKasa, "2 satır", 400m),
            new("MEZAT", "1 satır", -150m),
            new("PERAKENDE", "2 satır", -270m),
            new EkstreOzetSatiri("Dağılım bekliyor", "1 satır", -80m),
        ], vm.KanalEtkileri);
        Assert.Equal(-100m, vm.KanalEtkileri.Sum(k => k.SatirTutari));
        // Aynı uyarı bir kez ve satır numaralarıyla; seçimin tümüne ait uyarı sonda.
        Assert.Equal(
        [
            ("Satır 2, 3", TurFarkli, (int?)2),
            ("Satır 4", "Benzer kayıt: Kart ödemesi · 27.09.2026 · 200,00 TL", (int?)4),
            ("Tüm seçim", "Önceden kaydedilen kart avansının kanal dağılımı bu harcamalarla belirlenir.", null),
        ], vm.OzetUyarilari.Select(u => (u.Baslik, u.Ozet, u.SatirNo)));
        Assert.Equal((true, true, true), (vm.KanalEtkisiVar, vm.OzetUyarisiVar, vm.TekrarOnayGerekli));
    }

    /// <summary>Kasa etkisi olmayan seçimde kanal tablosu ve uyarı bölümü görünmez; kart harcamasının kanal payı kart borcunundur, sayılmaz.
    /// Seçim değişince özet boşalır.</summary>
    [Fact]
    public async Task Kasa_etkisi_ve_uyarisi_yoksa_bolumleri_gorunmez_secim_degisince_ozet_bosalir()
    {
        var kart = EkstreKayitOzeti.Kur(new("ozet", 0m, [Etki(1, EkstreIslemTurleri.KartHarcama, 100m, 0m, [new(1, "MEZAT", 100m)])], [], false),
            [new(EkstreIslemTurleri.KartHarcama, "Kart harcaması")], []);
        Assert.Equal([new("1 satır kaydedilecek", "Genel kasa etkisi", 0m), new EkstreOzetSatiri("Kart harcaması", "1 satır · kasa etkisi yok", 0m)], kart.Toplamlar);
        Assert.Equal((0, 0), (kart.Kanallar.Count, kart.Uyarilar.Count));

        var vm = await OzetiGosterilmis(new("ozet", 0m, [Etki(1, EkstreIslemTurleri.Eslestir, 100m, 0m, [])], [], false));
        Assert.Equal((2, false, false), (vm.OzetToplamlari.Count, vm.KanalEtkisiVar, vm.OzetUyarisiVar));
        vm.Satirlar[0].Secili = false;
        Assert.Equal((false, 0, 0, 0), (vm.OnizlemeVar, vm.OzetToplamlari.Count, vm.KanalEtkileri.Count, vm.OzetUyarilari.Count));
    }

    [Fact]
    public void Uzun_uyari_listesi_bes_satir_numarasi_ve_kalan_sayisini_yazar() =>
        Assert.Equal("Satır 1, 2, 3, 4, 5 ve 2 satır daha", new EkstreOzetUyarisi("Uyarı", [1, 2, 3, 4, 5, 6, 7]).Baslik);

    /// <summary>Pencerede özet tablo düzenindedir: düz metin paragrafı yok, tutar işaretli ve kasa etkisinin rengiyle sağda; uyarıdan satıra gidilir.
    /// Mali onay kutusu yerinde kalır, ekranda tek ana düğme özetin "Kaydet"idir.</summary>
    [Fact]
    public async Task Pencerede_kayit_ozeti_tablo_duzeninde_ve_uyaridan_satira_gidilir()
    {
        GorunumOrtami.Kur();
        var (vm, api, _) = await Hazir(new()
        {
            Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3), Satir(4), Satir(5)),
            Oneriler = [YzHazir(1), YzHazir(2), YzHazir(3), YzHazir(4), YzHazir(5)],
        });
        var pencere = Pencere(vm);
        api.OnizlemeYaniti = Task.FromResult(KarisikOnizleme());
        await vm.OnizleCommand.ExecuteAsync(null);

        var etiketler = pencere.GetVisualTreeDescendants().OfType<Label>().Where(Gorunur).ToList();
        string[] metinler = [.. etiketler.Select(l => l.Text ?? "")];
        Assert.Contains("5 satır kaydedilecek", metinler);
        Assert.Contains("Yalnız genel kasa", metinler);
        Assert.Contains("Satır 2, 3", metinler);
        Assert.DoesNotContain(metinler, m => m.Contains("Kasa etkisi:") || m.Contains('\n'));
        Color Renk(string tutar) => etiketler.First(l => l.Text == tutar).TextColor;
        Assert.Equal((Color)Application.Current!.Resources["Neg"], Renk("−100,00"));
        Assert.Equal((Color)Application.Current!.Resources["Pos"], Renk("+500,00"));
        Assert.Equal((Color)Application.Current!.Resources["Sub"], Renk("0,00"));
        Assert.Contains("Satırları ve kasa etkisini kontrol ettim.", metinler);
        // Eylemsiz tablo satırında boş eylem sütunu yok: tutar sütunu başlığın "Kasa etkisi"yle aynı sağ kenarda biter.
        Assert.All(pencere.GetVisualTreeDescendants().OfType<Kasa.App.Controls.UyumluIzgara>().Where(r => r.BindingContext is EkstreOzetSatiri),
            r => Assert.Equal(2, r.ColumnDefinitions.Count));

        var git = pencere.GetVisualTreeDescendants().OfType<Button>().Where(b => b.Text == "Satıra git" && Gorunur(b)).ToList();
        Assert.Equal(2, git.Count);
        ((IButtonController)git[0]).SendClicked();
        Assert.Same(vm.Satirlar[1], vm.SeciliSatir);
    }

    /// <summary>"yapay zekâ" etiketi satırların çoğunda aynı olduğunda bilgi taşımaz: yalnız öneri belgenin türünü değiştirdiyse ya da kanal
    /// seçtiyse görünür. Yapay zekânın düzelttiği açıklama varsayılandan farklı sayılmaz (açıklama sütununda görünür).</summary>
    [Fact]
    public async Task Yapay_zeka_etiketi_yalniz_oneri_varsayilandan_farkliyken_gorunur()
    {
        EkstreKuralOnerisi Yz(int no, string dagilim, int[] kanallar, string? aciklama = null) =>
            new(no, YapayZekaDurumlari.Oneri, [], EkstreIslemTurleri.Gider, dagilim, kanallar, "Yapay zekâ (yüksek güven).", YapayZeka: new("Yuksek", "kargo", aciklama, true, null));
        var (vm, _, _) = await Hazir(new()
        {
            Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3) with { OnerilenIslem = EkstreIslemTurleri.Atla }, Satir(4)),
            Oneriler = [Yz(1, DagilimBicimleri.Genel, [], "Kargo ödemesi"), Yz(2, DagilimBicimleri.Esit, [2]), Yz(3, DagilimBicimleri.Genel, []), HafizaOnerisi(4)],
        });
        Assert.Equal(["Gider · Genel", "Gider · PERAKENDE", "Gider · Genel", "Gider · Genel"], vm.Satirlar.Select(s => s.TurKanalMetni));
        Assert.Equal("Kargo ödemesi", vm.Satirlar[0].AciklamaGosterimi);
        Assert.Equal(["", "yapay zekâ", "yapay zekâ", "hafıza"], vm.Satirlar.Select(s => s.KaynakEtiketi));
        Assert.All(vm.Satirlar, s => Assert.True(s.Secili));
    }
}
