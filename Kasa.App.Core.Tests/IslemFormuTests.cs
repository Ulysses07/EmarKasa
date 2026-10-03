using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>İşlemler gider formu (tasarım 2026-10-02 §1-3; İŞ-02, İŞ-03, HD-02): ön doğrulama, sunucu alan eşlemesi, temizleme,
/// düzenleme başlığı, kaydedilmemiş değişiklik onayı ve kopukken kaydetme.</summary>
public class IslemFormuTests
{
    private static readonly IslemDto Kargo = new(5, new DateOnly(2026, 3, 5), "Kargo", 75m, "MEZAT", GiderTipi.Cari, null);

    private static IslemlerViewModel Vm(SahteApi? api = null) => new(api ?? new SahteApi(), TestOturumu.Ac());

    private static void Doldur(IslemlerViewModel vm)
    {
        vm.DuzenTarih = new DateTime(2026, 3, 5);
        vm.DuzenCari = "Ege Gıda";
        vm.DuzenTutar = 150m;
        vm.DuzenKanal = "MEZAT";
    }

    [Fact]
    public async Task Bos_form_istek_gondermez_alanlari_adiyla_soyler_ilk_alan_aciklamadir()
    {
        var api = new SahteApi();
        var vm = Vm(api);
        var gosterim = 0;
        vm.Hatalar.GosterIstendi += (_, _) => gosterim++;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(0, api.IslemOlusturCagri);
        Assert.Equal("Açıklama boş olamaz.", vm.Hatalar[nameof(vm.DuzenCari)]);
        Assert.Equal("Tutar sıfır olamaz.", vm.Hatalar[nameof(vm.DuzenTutar)]);
        Assert.Equal("Kanal seçin.", vm.Hatalar[nameof(vm.DuzenKanal)]);
        Assert.Equal(nameof(vm.DuzenCari), vm.Hatalar.IlkAlan);
        Assert.Null(vm.Hatalar.Genel);
        Assert.Null(vm.Hata);
        Assert.Equal(1, gosterim);
    }

    [Fact]
    public async Task Sunucu_alan_hatasi_alana_eslenmeyen_genel_hataya_gider()
    {
        var alanlar = new Dictionary<string, string> { ["cari"] = "En fazla 200 karakter girilebilir.", ["kredikartiid"] = "Bu kart yeni kullanıma kapalı.", ["istekid"] = "Geçerli bir istek kimliği gerekir." };
        var api = new SahteApi { IslemOlusturHatasi = new KasaApiException(HttpStatusCode.BadRequest, "birleşik", alanHatalari: alanlar) };
        var vm = Vm(api);
        Doldur(vm);

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal("En fazla 200 karakter girilebilir.", vm.Hatalar[nameof(vm.DuzenCari)]);
        Assert.Equal("Bu kart yeni kullanıma kapalı.", vm.Hatalar[nameof(vm.DuzenKrediKartiId)]);
        Assert.Equal("Geçerli bir istek kimliği gerekir.", vm.Hatalar.Genel);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Hata_alan_degisince_kayit_degisince_yenide_ve_basarida_kalkar()
    {
        var vm = Vm();
        await vm.KaydetCommand.ExecuteAsync(null);

        vm.DuzenCari = "Ege";
        Assert.Null(vm.Hatalar[nameof(vm.DuzenCari)]);
        Assert.NotNull(vm.Hatalar[nameof(vm.DuzenTutar)]);

        vm.Duzenle(Kargo);
        Assert.False(vm.Hatalar.Var);

        vm.DuzenCari = "";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.True(vm.Hatalar.Var);
        vm.DegisiklikleriBirak();
        Assert.False(vm.Hatalar.Var);

        vm.Hatalar.Genel = "eski";
        await vm.YeniCommand.ExecuteAsync(null);
        Assert.False(vm.Hatalar.Var);

        Doldur(vm);
        vm.Hatalar.Genel = "eski";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.False(vm.Hatalar.Var);
        Assert.Equal("Gider kaydedildi.", vm.Mesaj);
    }

    [Fact]
    public void Duzenleme_basligi_ve_dugmeleri_modu_soyler()
    {
        var vm = Vm();
        Assert.Equal(("Yeni işlem", "Kaydet", false), (vm.FormBasligi, vm.KaydetMetni, vm.DuzenlemeModu));

        vm.Duzenle(Kargo);
        vm.DuzenCari = "Kargo (düzeltildi)";

        Assert.Equal(("Düzenleniyor: 05.03.2026 · Kargo", "Değişikliği kaydet", true), (vm.FormBasligi, vm.KaydetMetni, vm.DuzenlemeModu));
    }

    [Fact]
    public async Task Yazilmis_form_baska_kayda_gecmeden_ve_yeniden_once_onay_ister()
    {
        var vm = Vm();
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        vm.DuzenCari = "Deneme gideri";
        Assert.True(vm.KaydedilmemisDegisiklikVar);

        var sorulan = 0;
        var cevap = false;
        vm.BirakmaOnayi = ileti => { sorulan++; Assert.Equal(KaydedilmemisDegisiklik.Ileti, ileti); return Task.FromResult(cevap); };

        await vm.DuzenlemeyeGecCommand.ExecuteAsync(Kargo);
        Assert.Equal(("Deneme gideri", 0), (vm.DuzenCari, vm.DuzenId));   // "Forma dön"
        await vm.YeniCommand.ExecuteAsync(null);
        Assert.Equal("Deneme gideri", vm.DuzenCari);

        cevap = true;                                                      // "Bırak"
        await vm.DuzenlemeyeGecCommand.ExecuteAsync(Kargo);
        Assert.Equal((5, "Kargo"), (vm.DuzenId, vm.DuzenCari));
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        Assert.Equal(3, sorulan);

        vm.DuzenTutar = 80m;
        vm.DegisiklikleriBirak();
        Assert.Equal(75m, vm.DuzenTutar);
        vm.DuzenTutar = 90m;
        vm.VazgecCommand.Execute(null);   // "Vazgeç" bilerek bırakmaktır: sorulmaz
        Assert.Equal((0, 0m), (vm.DuzenId, vm.DuzenTutar));
        Assert.Equal(3, sorulan);
    }

    [Fact]
    public async Task Kopukken_kaydetme_formu_korur_ve_kayit_yapilmadigini_soyler()
    {
        var api = new SahteApi { IslemOlusturHatasi = new HttpRequestException() };
        var vm = Vm(api);
        Doldur(vm);

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal("Sunucuya ulaşılamadı. Kayıt yapılmadı; bağlantı gelince yeniden kaydedin.", vm.Hatalar.Genel);
        Assert.Equal(("Ege Gıda", 150m, "MEZAT"), (vm.DuzenCari, vm.DuzenTutar, vm.DuzenKanal));
        Assert.True(vm.KaydedilmemisDegisiklikVar);
    }

    /// <summary>Not alanına yazıp silmek formu kirli bırakmaz (boş metin null'dan farklı sayılmaz): aksi halde "Yeni",
    /// "Düzenle" ve menüden çıkış gereksiz onay sorar.</summary>
    [Fact]
    public void Not_alanina_yazip_silmek_formu_kirletmez()
    {
        var vm = Vm();
        Assert.False(vm.KaydedilmemisDegisiklikVar);

        vm.DuzenNot = "x";
        Assert.True(vm.KaydedilmemisDegisiklikVar);
        vm.DuzenNot = "";
        Assert.False(vm.KaydedilmemisDegisiklikVar);
    }

    [Fact]
    public async Task Tip_secenekleri_sabit_kanal_secenekleri_son_basarili_yuklemeden_gelir()
    {
        var api = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0m)] };
        var vm = Vm(api);
        Assert.Equal(["Diğer gider", "Sabit gider", "Kredi kartı"], vm.TipCipleri.Select(c => c.Ad));
        Assert.True(vm.TipCipleri[0].Secili);

        await vm.YukleAsync();
        api.YuklemeHatasi = new HttpRequestException();
        await vm.YukleAsync();

        Assert.Equal(3, vm.TipCipleri.Count);
        Assert.Equal(["MEZAT", "Ortak"], vm.GiderKanallari.Select(c => c.Ad));
    }
}
