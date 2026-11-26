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
}
