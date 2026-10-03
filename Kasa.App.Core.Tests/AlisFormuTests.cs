using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Alışlar formu (tasarım 2026-10-02 §1-2; AL-01): tedarikçi hatası alanın altında, kalem kuralları formun genel hatasında;
/// sunucu alanları eşlenir; başka kayda geçiş ve Yeni onay ister; kaydedilmemiş değişiklik form açıldığı andaki değerlere göredir.</summary>
public class AlisFormuTests
{
    private static AlisDto Alis(int id, string tedarikci) => new(
        id, 2, 3, "Ayşe", new(2026, 9, 21), tedarikci, null, "Taslak", null, 100m, 0m, 100m,
        [new AlisKalemDto(1, "Mal alımı", 100m, [new AlisDagilimDto(1, "MEZAT", 60m), new AlisDagilimDto(2, "PERAKENDE", 40m)])],
        Array.Empty<AlisOdemeDto>());

    private static async Task<(AlislarViewModel Vm, AlislarViewModelTests.SahteAlisApi Api)> Kur()
    {
        var api = new AlislarViewModelTests.SahteAlisApi { Liste = [Alis(7, "Ege Gıda"), Alis(8, "Akdeniz")] };
        var vm = new AlislarViewModel(api, new SahteApi(), TestOturumu.Ac());
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Bos_taslak_tedarikciyi_alanda_kalem_kuralini_formun_ustunde_soyler()
    {
        var (vm, api) = await Kur();

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonYaz);
        Assert.Equal("Tedarikçi adını yazın.", vm.Hatalar[nameof(vm.Tedarikci)]);
        Assert.Equal("Her kaleme açıklama ve sıfırdan büyük, kuruş hassasiyetinde tutar girin.", vm.Hatalar.Genel);
        Assert.Null(vm.Hata);
        Assert.Equal(("Yeni alış", "Kaydet"), (vm.FormBasligi, vm.KaydetMetni));
    }

    [Fact]
    public async Task Sunucu_alan_hatasi_tedarikciye_kalem_hatasi_genel_hataya_gider()
    {
        var alanlar = new Dictionary<string, string> { ["tedarikci"] = "En fazla 200 karakter girilebilir.", ["kalemler[0].aciklama"] = "En fazla 500 karakter girilebilir." };
        var (vm, api) = await Kur();
        api.OlusturmaHatasi = new KasaApiException(HttpStatusCode.BadRequest, "birleşik", alanHatalari: alanlar);
        vm.Tedarikci = "Firma";
        vm.Kalemler[0].Aciklama = "Mal";
        vm.Kalemler[0].Tutar = 10m;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal("En fazla 200 karakter girilebilir.", vm.Hatalar[nameof(vm.Tedarikci)]);
        Assert.Equal("En fazla 500 karakter girilebilir.", vm.Hatalar.Genel);
    }

    [Fact]
    public async Task Baska_kayda_gecis_onay_ister_birakinca_hata_ve_degisiklik_kalkar()
    {
        var (vm, _) = await Kur();
        await vm.SecCommand.ExecuteAsync(vm.Alislar.Single(a => a.Veri.Id == 7));
        Assert.Equal(("Düzenleniyor: 21.09.2026 · Ege Gıda", "Değişiklikleri kaydet"), (vm.FormBasligi, vm.KaydetMetni));
        vm.Tedarikci = "";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.True(vm.Hatalar.Var);
        Assert.True(vm.KaydedilmemisDegisiklikVar);

        var sorulan = 0;
        var cevap = false;
        vm.BirakmaOnayi = _ => { sorulan++; return Task.FromResult(cevap); };
        await vm.SecCommand.ExecuteAsync(vm.Alislar.Single(a => a.Veri.Id == 8));
        Assert.Equal(7, vm.Secili!.Id);

        cevap = true;
        await vm.SecCommand.ExecuteAsync(vm.Alislar.Single(a => a.Veri.Id == 8));
        Assert.Equal(8, vm.Secili!.Id);
        Assert.False(vm.Hatalar.Var);
        Assert.False(vm.KaydedilmemisDegisiklikVar);

        vm.Tedarikci = "x";
        await vm.YeniCommand.ExecuteAsync(null);
        Assert.Null(vm.Secili);
        Assert.Equal(3, sorulan);
    }

    [Fact]
    public async Task Deger_geri_alininca_kaydedilmemis_degisiklik_kalkar()
    {
        var (vm, _) = await Kur();
        await vm.SecCommand.ExecuteAsync(vm.Alislar.Single(a => a.Veri.Id == 7));

        vm.Tedarikci = "Başka";
        Assert.True(vm.KaydedilmemisDegisiklikVar);
        vm.Tedarikci = "Ege Gıda";
        Assert.False(vm.KaydedilmemisDegisiklikVar);

        vm.Kalemler[0].Tutar = 90m;
        Assert.True(vm.KaydedilmemisDegisiklikVar);
        vm.DegisiklikleriBirak();
        Assert.Equal(100m, vm.Kalemler[0].Tutar);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
    }

    /// <summary>AlislarPage.xaml, KaydedilmemisDegisiklikVar'a dört yerde bağlıdır (uyarı metni, "Değişiklikleri bırak" düğmesi,
    /// ödeme formu uyarısı ve kilidi). Ortak yapıya taşınırken (<see cref="KaydedilmemisDegisiklik"/>) bu değer hâlâ
    /// [ObservableProperty] olmalı ve her değişiminde PropertyChanged duyurmalı; aksi halde bu bağlamalar donar.</summary>
    [Fact]
    public async Task KaydedilmemisDegisiklikVar_her_degisiminde_property_changed_duyurur()
    {
        var (vm, _) = await Kur();
        await vm.SecCommand.ExecuteAsync(vm.Alislar.Single(a => a.Veri.Id == 7));

        var duyurulanlar = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.KaydedilmemisDegisiklikVar))
                duyurulanlar.Add(vm.KaydedilmemisDegisiklikVar);
        };

        vm.Tedarikci = "Başka";
        vm.Tedarikci = "Ege Gıda";
        await vm.YeniCommand.ExecuteAsync(null);

        Assert.Equal([true, false], duyurulanlar);
    }
}
