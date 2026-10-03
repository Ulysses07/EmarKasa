using System.Net;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core.Tests;

/// <summary>Çekler formları (tasarım 2026-10-02 §1-2; ÇK-01): çek formu ve hareket formu hataları formun içinde ve alanın altında,
/// sunucunun alan adsız iletisi genel hataya; düzenleme başlığı; Vazgeç ve Yeni çek kaydedilmemiş değişikliği sorar.</summary>
public class CekFormuTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 25);

    private static async Task<(CekTakipViewModel Vm, CekTakipViewModelTests.Sahte Api)> Kur(params CekDto[] cekler)
    {
        var api = new CekTakipViewModelTests.Sahte { Liste = [.. cekler] };
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };
        var vm = new CekTakipViewModel(api, finans, new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor }, new IslemEditorTests.SabitZaman(Bugun));
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Bos_cek_formu_istek_gondermez_alanlari_formun_icinde_soyler()
    {
        var (vm, api) = await Kur();
        await vm.YeniCekCommand.ExecuteAsync(null);
        var gosterim = 0;
        vm.Hatalar.GosterIstendi += (_, _) => gosterim++;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Empty(api.Kayitlar);
        Assert.Equal("Çek / senet numarası boş olamaz.", vm.Hatalar[nameof(vm.No)]);
        Assert.Equal("Banka boş olamaz.", vm.Hatalar[nameof(vm.Banka)]);
        Assert.Equal("Kişi boş olamaz.", vm.Hatalar[nameof(vm.Kisi)]);
        Assert.Equal("Tutar sıfırdan büyük olmalı.", vm.Hatalar[nameof(vm.Tutar)]);
        Assert.Null(vm.Hata);
        Assert.True(vm.FormAcik);
        Assert.Equal(1, gosterim);

        vm.FormYon = vm.YonSecenekleri[1];
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal("Ödeneceği kasayı (kanal ya da Ortak) seçin.", vm.Hatalar[nameof(vm.CekKasasi)]);
    }

    [Fact]
    public async Task Sunucunun_alan_adsiz_reddi_formun_genel_hatasina_yazilir()
    {
        var (vm, api) = await Kur();
        await vm.YeniCekCommand.ExecuteAsync(null);
        vm.No = "1";
        vm.Banka = "Ziraat";
        vm.Kisi = "Ayşe";
        vm.Tutar = 100m;
        api.KayitHatasi = new KasaApiException(HttpStatusCode.BadRequest, "Vade tarihi 01.01.2000 tarihinden önce olamaz.");

        await vm.YineDeKaydetCommand.ExecuteAsync(null);

        Assert.Equal("Vade tarihi 01.01.2000 tarihinden önce olamaz.", vm.Hatalar.Genel);
        Assert.Null(vm.Hata);
        Assert.True(vm.FormAcik);
    }

    [Fact]
    public async Task Duzeltme_basligi_kaydet_metni_yeni_cekte_onay_vazgecte_onaysiz_kapanis()
    {
        var (vm, _) = await Kur(CekTakipViewModelTests.Cek(1));
        vm.SecCommand.Execute(vm.Cekler[0]);
        await vm.DuzeltCommand.ExecuteAsync(null);
        Assert.Equal(("Düzenleniyor: 30.09.2026 · Ahmet Yılmaz", "Değişikliği kaydet"), (vm.FormBasligi, vm.KaydetMetni));
        Assert.False(vm.KaydedilmemisDegisiklikVar);

        vm.Kisi = "Başka";
        Assert.True(vm.KaydedilmemisDegisiklikVar);
        var cevap = false;
        vm.BirakmaOnayi = _ => Task.FromResult(cevap);
        await vm.YeniCekCommand.ExecuteAsync(null);
        Assert.Equal(("Başka", 1), (vm.Kisi, vm.Duzenlenen));   // "Forma dön"

        cevap = true;                                            // "Bırak"
        await vm.YeniCekCommand.ExecuteAsync(null);
        Assert.Equal(("Yeni çek / senet", "Kaydet", ""), (vm.FormBasligi, vm.KaydetMetni, vm.Kisi));

        vm.Kisi = "Yazıldı";
        vm.FormuKapatCommand.Execute(null);                     // "Vazgeç" sormaz
        Assert.False(vm.FormAcik);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
    }

    [Fact]
    public async Task Hareket_formunda_kasa_secilmeden_istek_gitmez_hata_alanin_altinda()
    {
        var (vm, api) = await Kur(CekTakipViewModelTests.Cek(1));
        vm.SecCommand.Execute(vm.Cekler[0]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri.Single(c => c.Kod == CekHareketTurleri.Tahsilat));
        vm.HareketKasasi = null;

        await vm.HareketKaydetCommand.ExecuteAsync(null);

        Assert.Empty(api.Hareketler);
        Assert.Equal("Kasa (kanal) seçin.", vm.HareketHatalari[nameof(vm.HareketKasasi)]);
        vm.HareketKasasi = "MEZAT";
        Assert.False(vm.HareketHatalari.Var);
    }

    [Fact]
    public async Task Hareket_formu_izlenir_ayni_cekin_yenilenmesi_kapatmaz_baska_satira_geciste_onay_sorulur()
    {
        var (vm, api) = await Kur(CekTakipViewModelTests.Cek(1), CekTakipViewModelTests.Cek(2));
        await vm.SecCommand.ExecuteAsync(vm.Cekler[0]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri.Single(c => c.Kod == CekHareketTurleri.Tahsilat));
        Assert.False(vm.KaydedilmemisDegisiklikVar);

        vm.HareketTutari = 1_000m;
        Assert.True(vm.KaydedilmemisDegisiklikVar);

        // Sunucudan aynı sürümün yeni örneği gelir (liste yenilemesi): hareket formu ve yazılan tutar kalır.
        api.Liste = [.. api.Liste.Select(c => c with { Hareketler = [] })];
        await vm.YukleAsync();
        Assert.Equal((CekHareketTurleri.Tahsilat, 1_000m), (vm.HareketTuru, vm.HareketTutari));
        Assert.True(vm.HareketFormuAcik);
        Assert.True(vm.HareketCipleri.Single(c => c.Kod == CekHareketTurleri.Tahsilat).Secili);
        Assert.True(vm.KaydedilmemisDegisiklikVar);

        var sorulan = 0;
        var cevap = false;
        vm.BirakmaOnayi = _ => { sorulan++; return Task.FromResult(cevap); };
        await vm.SecCommand.ExecuteAsync(vm.SonrakiSatirlar[0]);
        Assert.Equal((1, 1, true), (sorulan, vm.Acik?.Id, vm.HareketFormuAcik));

        cevap = true;
        await vm.SecCommand.ExecuteAsync(vm.SonrakiSatirlar[0]);
        Assert.Equal((2, 2, false), (sorulan, vm.Acik?.Id, vm.HareketFormuAcik));
        Assert.False(vm.KaydedilmemisDegisiklikVar);
    }

    [Fact]
    public async Task Degisiklikleri_birakmak_hareket_formunu_da_kapatir()
    {
        var (vm, _) = await Kur(CekTakipViewModelTests.Cek(1));
        await vm.SecCommand.ExecuteAsync(vm.Cekler[0]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri.Single(c => c.Kod == CekHareketTurleri.Tahsilat));
        vm.HareketTutari = 1_000m;

        vm.DegisiklikleriBirak();

        Assert.False(vm.HareketFormuAcik);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        Assert.DoesNotContain(vm.HareketCipleri, c => c.Secili);
    }

    [Fact]
    public async Task Ciroda_karsi_taraf_bos_ise_istek_gitmez_hata_alanin_altinda()
    {
        var (vm, api) = await Kur(CekTakipViewModelTests.Cek(1));
        await vm.SecCommand.ExecuteAsync(vm.Cekler[0]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri.Single(c => c.Kod == CekHareketTurleri.Ciro));
        vm.Karsi = "  ";

        await vm.HareketKaydetCommand.ExecuteAsync(null);

        Assert.Empty(api.Hareketler);
        Assert.Equal("Karşı taraf boş olamaz.", vm.HareketHatalari[nameof(vm.Karsi)]);
    }

    [Fact]
    public async Task Duzenlenen_cekin_kimligi_yalniz_duzeltme_formu_acikken_verilir()
    {
        var (vm, _) = await Kur(CekTakipViewModelTests.Cek(1));
        Assert.Null(vm.DuzenlenenCekId);
        await vm.SecCommand.ExecuteAsync(vm.Cekler[0]);
        await vm.DuzeltCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.DuzenlenenCekId);

        vm.FormuKapatCommand.Execute(null);
        Assert.Null(vm.DuzenlenenCekId);
        await vm.YeniCekCommand.ExecuteAsync(null);
        Assert.Null(vm.DuzenlenenCekId);
    }
}
