using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core.Tests;

// Hareket penceresi (docs/specs/2026-11-26-ekstre-hareket-penceresi.md): hazır satırlar kendiliğinden işaretlenir; süzgeç üyeliği hesaplanınca
// donar; arama aynı satır nesnelerini gösterir.
public partial class EkstreAktarmaTests
{
    private static EkstreKuralOnerisi YzHazir(int no, string? dagilim = "Genel", bool hazir = true, string? neden = null) =>
        new(no, YapayZekaDurumlari.Oneri, [], EkstreIslemTurleri.Gider, dagilim, [], "Yapay zekâ (yüksek güven).", YapayZeka: new("Yuksek", "kargo", null, hazir, neden));

    [Fact]
    public async Task Hazir_satirlar_belge_acilinca_kendiliginden_isaretlenir()
    {
        var (vm, _, _) = await Hazir(new()
        {
            Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3)),
            Oneriler = [HafizaOnerisi(1), YzHazir(2), YzHazir(3, dagilim: null, hazir: false, neden: "kanal seçin")],
        });
        Assert.Equal([true, true, false], vm.Satirlar.Select(s => s.Secili));
        Assert.Equal([true, true, false], vm.Satirlar.Select(s => s.KendiligindenIsaretlendi));
        Assert.Equal([null, null, "kanal seçin"], vm.Satirlar.Select(s => s.KontrolNedeni));
        Assert.Equal((2, 1, 0), (vm.HazirSayisi, vm.KontrolSayisi, vm.KapananSayisi));
        Assert.Equal("2 satır işaretli · Giriş +0,00 ₺ (0 satır) · Çıkış −200,00 ₺ (2 satır)", vm.SecimOzeti);
    }

    /// <summary>Ekran, hücrenin son bildirimindeki değeri gösterir: öneri doldurulduktan sonra neden, kaynak ve tür bildirimleri güncel değeri vermeli
    /// (görsel denemede hazır satırda "kontrol edin" kalıyordu). Belge türü önermediği için yapay zekânın türü varsayılandan farklıdır, etiket görünür.</summary>
    [Fact]
    public async Task Doldurma_sonrasi_bildirimler_guncel_degeri_verir()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1) with { OnerilenIslem = EkstreIslemTurleri.Atla }) });
        var s = vm.Satirlar[0];
        var son = new Dictionary<string, object?>();
        s.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(s.KontrolNedeni) or nameof(s.KaynakEtiketi) or nameof(s.TurKanalMetni))
                son[e.PropertyName] = typeof(EkstreSatirEditor).GetProperty(e.PropertyName)!.GetValue(s);
        };
        api.Oneriler = [YzHazir(1)];
        await vm.KurallariYenileAsync();
        Assert.True(s.Secili);
        Assert.Equal(((object?)null, (object?)"yapay zekâ", (object?)"Gider · Genel"), (son[nameof(s.KontrolNedeni)], son[nameof(s.KaynakEtiketi)], son[nameof(s.TurKanalMetni)]));
    }

    /// <summary>Kayıt özeti açıkken seçim değişirse özet düşer; panel boş kalmasın diye yenilenmesi gerektiği bildirilir.</summary>
    [Fact]
    public async Task Secim_degisince_ozetin_yenilenmesi_gerektigi_bildirilir()
    {
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2)), Oneriler = [YzHazir(1), YzHazir(2)] });
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.Equal((true, false), (vm.OnizlemeVar, vm.OnizlemeYenilenmeli));
        var bildirildi = false;
        vm.PropertyChanged += (_, e) => bildirildi |= e.PropertyName == nameof(vm.OnizlemeYenilenmeli);
        vm.Satirlar[1].Secili = false;
        Assert.Equal((false, true, true), (vm.OnizlemeVar, vm.OnizlemeYenilenmeli, bildirildi));
    }

    [Fact]
    public async Task Kaldirilan_isaret_yeniden_konmaz()
    {
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1)), Oneriler = [YzHazir(1)] });
        Assert.True(vm.Satirlar[0].Secili);
        vm.Satirlar[0].Secili = false;
        await vm.KurallariYenileAsync();
        Assert.False(vm.Satirlar[0].Secili);
    }

    [Fact]
    public async Task Elle_degisen_satir_kendiliginden_isaretlenmez()
    {
        var (vm, api, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1)) });
        vm.Satirlar[0].Aciklama = "Benim açıklamam";
        api.Oneriler = [HafizaOnerisi(1)];
        await vm.KurallariYenileAsync();
        Assert.Equal((false, false), (vm.Satirlar[0].Secili, vm.Satirlar[0].KendiligindenIsaretlendi));
    }

    [Fact]
    public async Task Suzgec_uyeligi_calisirken_degismez()
    {
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2)), Oneriler = [YzHazir(1, dagilim: null, hazir: false, neden: "kanal seçin"), YzHazir(2)] });
        var s = vm.Satirlar[0];
        vm.Suzgec = EkstreSuzgec.Kontrol;
        Assert.Equal([s], vm.Gorunenler);
        // Kullanıcı eksiği tamamlar: neden hemen kalkar ama satır çalışırken süzgeçten kaybolmaz.
        s.DagilimTuru = s.DagilimTurleri.Single(d => d.Kod == DagilimBicimleri.Genel);
        Assert.Null(s.KontrolNedeni);
        Assert.Equal([s], vm.Gorunenler);
        vm.SuzgecleriHesapla();
        Assert.Empty(vm.Gorunenler);
        vm.Suzgec = EkstreSuzgec.Hazir;
        Assert.Contains(s, vm.Gorunenler);
        // Elle tamamlanan satırın işaretini kullanıcı koyar.
        Assert.False(s.Secili);
        Assert.Equal((2, 0), (vm.HazirSayisi, vm.KontrolSayisi));
    }

    [Fact]
    public async Task Suzgec_ve_arama_ayni_satir_nesnelerini_gosterir()
    {
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1) with { Aciklama = "KARGO ÖDEMESİ" }, Satir(2) with { Aciklama = "ELEKTRIK FATURASI" }) });
        var kargo = vm.Satirlar[0];
        vm.SatiriAcKapat(kargo);
        vm.Arama = "kargo odemesi";
        Assert.Same(kargo, Assert.Single(vm.Gorunenler));
        Assert.Same(kargo, vm.SeciliSatir);
        vm.Arama = "fatura";
        Assert.Same(vm.Satirlar[1], Assert.Single(vm.Gorunenler));
        vm.Arama = "";
        Assert.Equal(vm.Satirlar, vm.Gorunenler);
        vm.Suzgec = EkstreSuzgec.Kapanan;
        Assert.Empty(vm.Gorunenler);
    }

    [Fact]
    public async Task Bos_belgede_sayilar_sifir()
    {
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi() });
        Assert.Equal((0, 0, 0), (vm.HazirSayisi, vm.KontrolSayisi, vm.KapananSayisi));
        Assert.Empty(vm.Gorunenler);
        Assert.Equal("Hiç satır işaretli değil.", vm.SecimOzeti);
    }

    [Fact]
    public async Task Suzgec_cipleri_sayilari_gosterir_ve_secer()
    {
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2)), Oneriler = [YzHazir(1), YzHazir(2, dagilim: null, hazir: false, neden: "kanal seçin")] });
        Assert.Equal(["Tümü 2", "Hazır 1", "Kontrol edin 1", "Kapanan 0"], vm.SuzgecCipleri.Select(c => c.Ad));
        Assert.Equal([true, false, false, false], vm.SuzgecCipleri.Select(c => c.Secili));
        vm.SuzgecSecCommand.Execute(vm.SuzgecCipleri[2]);
        Assert.Equal(EkstreSuzgec.Kontrol, vm.Suzgec);
        Assert.Equal([false, false, true, false], vm.SuzgecCipleri.Select(c => c.Secili));
        Assert.Same(vm.Satirlar[1], Assert.Single(vm.Gorunenler));
    }

    /// <summary>Belge açılınca (geçmişten, yüklemeyle, kaynağa gitmeyle) pencere istenir; sayfa yenilemesi istemez.</summary>
    [Fact]
    public async Task Belge_acilinca_pencere_istenir_yenilemede_istenmez()
    {
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1)) });
        var istek = 0;
        vm.PencereAcIstendi += (_, _) => istek++;
        await vm.BelgeAcAsync(1);
        Assert.Equal(1, istek);
        await vm.YukleAsync();
        Assert.Equal(1, istek);
        vm.Kaynak = vm.Kaynaklar.Single(k => k.Kod == "Banka");
        vm.Banka = vm.Bankalar[0];
        vm.HesapAdi = "Ana hesap";
        await vm.PdfYukleAsync([1], "a.pdf", vm.YuklemeSecimi()!);
        Assert.Equal(2, istek);
    }

    [Fact]
    public async Task Pencere_basligi_belgeyi_tek_satirda_soyler_uyarilar_ayri()
    {
        var (vm, _, _) = await Hazir(new() { Veri = DurumBelgesi(Satir(1), Satir(2)) with { Uyarilar = ["Birinci uyarı.", "İkinci uyarı."] } });
        Assert.Equal(vm.Belge!.DosyaAdi, vm.PencereBasligi);
        Assert.EndsWith(" · 2 hareket", vm.PencereAltBilgisi);
        Assert.DoesNotContain("\n", vm.PencereAltBilgisi);
        Assert.Equal(("Belge uyarıları (2)", "Birinci uyarı.\nİkinci uyarı."), (vm.BelgeUyarilariBasligi, vm.BelgeUyarilariMetni));
    }

    [Fact]
    public async Task Kapanan_satir_turu_yerine_sonucunu_gosterir()
    {
        var belge = DurumBelgesi(Satir(1), Satir(2), Satir(3)) with { Kayitlar = [Kayit(7, 1)], YokSayilanlar = [2] };
        var (vm, _, _) = await Hazir(new() { Veri = belge });
        Assert.Equal(["Kaydedildi", "Yok sayıldı", "Gider"], vm.Satirlar.Select(s => s.TurKanalMetni));
        await vm.YokSayAsync([vm.Satirlar[2]], true);
        Assert.Equal("Satır 3 yok sayıldı; kasa değişmedi. Kapanan süzgecinden geri alabilirsiniz.", vm.SatirMesaji);
    }

    /// <summary>Tablonun hücreleri: tür · kanal, doldurmanın kaynağı ve işaretli tutar (giriş +, çıkış −, belirsiz işaretsiz).</summary>
    [Fact]
    public async Task Tablo_hucreleri_tur_kanal_kaynak_ve_tutari_gosterir()
    {
        var (vm, _, _) = await Hazir(new()
        {
            Veri = DurumBelgesi(Satir(1), Satir(2), Satir(3) with { Yon = "Giris", OnerilenIslem = EkstreIslemTurleri.Gelir, Tutar = 1500m }, Satir(4) with { Yon = "Belirsiz", OnerilenIslem = EkstreIslemTurleri.Atla }),
            Oneriler = [HafizaOnerisi(1, dagilim: "Esit", kanallar: [2]), YzHazir(2), new(3, "Oneri", ["Satış"], EkstreIslemTurleri.Gelir, "Genel", [], "Kural"), Yok(4)],
        });
        Assert.Equal(["Gider · PERAKENDE", "Gider · Genel", "Gelir · Genel", "—"], vm.Satirlar.Select(s => s.TurKanalMetni));
        // Yapay zekâ belgenin türünü genel kasaya yazdı (varsayılan): etiket yok.
        Assert.Equal(["hafıza", "", "kural", ""], vm.Satirlar.Select(s => s.KaynakEtiketi));
        Assert.Equal(["−100,00", "−100,00", "+1.500,00", "100,00"], vm.Satirlar.Select(s => s.TutarGosterimi));
        var s = vm.Satirlar[1];
        s.DagilimTuru = s.DagilimTurleri.Single(d => d.Kod == DagilimBicimleri.Esit);
        s.PayEkle();
        s.Paylar[0].Kanal = s.Kanallar[0];
        Assert.Equal("Gider · MEZAT", s.TurKanalMetni);
    }
}
