using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket D — Kasa sayımının devamı (35): satırlar, küpür, fark durumu, "Neden değişti?", aylık özet.</summary>
public class PaketDSayimTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    private static KasaSayimDto Sayim(int id, DateOnly t, decimal sayilan, decimal hesaplanan,
        SayimFarkDurumu durum = SayimFarkDurumu.Acik, string? aciklama = null, IReadOnlyList<SayimSatiriDto>? satirlar = null)
        => new(id, t, sayilan, hesaplanan, sayilan - hesaplanan, hesaplanan, null,
            new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), satirlar, durum, aciklama);

    private static (SahteApi api, KasaSayimiViewModel vm) Kur(Action<SahteApi>? ayar = null, bool editor = true)
    {
        var api = new SahteApi { KasaHesapSonuc = 10_000m };
        ayar?.Invoke(api);
        return (api, new KasaSayimiViewModel(api, new SabitSaat(Bugun.AddHours(10))) { EditorMu = editor });
    }

    [Fact]
    public async Task Varsayilan_satirlar_nakit_bankalar_ve_pos()
    {
        var (_, vm) = Kur();
        await vm.YukleAsync();

        Assert.Equal(["Nakit", "İş Bankası", "Ziraat", "POS'ta bekleyen"], vm.SayimSatirlari.Select(s => s.Ad));
        Assert.Equal([SayimSatirTuru.Nakit, SayimSatirTuru.Banka, SayimSatirTuru.Banka, SayimSatirTuru.Pos], vm.SayimSatirlari.Select(s => s.Tur));
        Assert.True(vm.SayimSatirlari[0].NakitMi);
        Assert.Equal(11, vm.SayimSatirlari[0].Kupurler.Count);
        Assert.Empty(vm.SayimSatirlari[1].Kupurler);
        Assert.False(vm.SatirlarKullaniliyor);
    }

    [Fact]
    public async Task Satir_tutarlari_sayilan_toplami_ve_canli_farki_belirler_kayit_satirlari_gonderir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();

        vm.SayimSatirlari[0].Tutar = 2_000m;
        vm.SayimSatirlari[1].Tutar = 7_500m;
        vm.SayimSatirlari[3].Tutar = 450.50m;

        Assert.True(vm.SatirlarKullaniliyor);
        Assert.Equal(9_950.50m, vm.SayilanTutar);
        Assert.Equal("9.950,50", vm.SatirToplamiYazi);
        Assert.Equal(-49.50m, vm.Fark);

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        var g = api.SonKasaSayimKaydet!;
        Assert.Equal(9_950.50m, g.SayilanTutar);
        Assert.Equal(g.SayilanTutar, g.Satirlar!.Sum(s => s.Tutar));   // sunucu kuralı: toplam = satırlar
        Assert.Equal(["Nakit", "İş Bankası", "Ziraat", "POS'ta bekleyen"], g.Satirlar!.Select(s => s.Ad));
        Assert.Equal(0m, g.Satirlar![2].Tutar);                  // sayıldı, boş
        Assert.Null(g.Satirlar![0].Kupurler);

        // Kayıttan sonra satır adları kalır, tutarlar sıfırlanır.
        Assert.Equal(4, vm.SayimSatirlari.Count);
        Assert.All(vm.SayimSatirlari, s => Assert.Equal(0m, s.Tutar));
        Assert.Equal(0m, vm.SayilanTutar);
        Assert.False(vm.SatirlarKullaniliyor);
    }

    [Fact]
    public async Task Tek_tutarli_sayim_eskisi_gibi_satirsiz_gider()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();

        vm.SayilanTutar = 10_250m;
        await vm.YukleAsync();                                    // yeniden yükleme tek tutarı ezmez
        Assert.Equal(10_250m, vm.SayilanTutar);
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(new KasaSayimYaz(new DateOnly(2026, 9, 24), 10_250m, null), api.SonKasaSayimKaydet);
        Assert.Null(api.SonKasaSayimKaydet!.Satirlar);
    }

    [Fact]
    public async Task Kupur_sayaci_nakit_satirini_doldurur_ve_kupurler_gonderilir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        var nakit = vm.SayimSatirlari[0];
        Assert.True(nakit.TutarDuzenlenebilir);

        vm.KupurAcKapatCommand.Execute(nakit);
        Assert.True(nakit.KupurAcik);
        nakit.Kupurler.Single(k => k.Kurus == 20000).Adet = 3;   // 600
        nakit.Kupurler.Single(k => k.Kurus == 500).Adet = 4;     // 20
        nakit.Kupurler.Single(k => k.Kurus == 50).Adet = 3;      // 1,50

        Assert.Equal("200 ₺", nakit.Kupurler[0].Ad);
        Assert.Equal("50 kr", nakit.Kupurler.Single(k => k.Kurus == 50).Ad);
        Assert.Equal(621.50m, nakit.Tutar);
        Assert.False(nakit.TutarDuzenlenebilir);
        Assert.Equal("Küpürlerden: 621,50 ₺", nakit.KupurOzeti);
        Assert.Equal(621.50m, vm.SayilanTutar);

        await vm.KaydetCommand.ExecuteAsync(null);

        var satir = api.SonKasaSayimKaydet!.Satirlar![0];
        Assert.Equal([new KupurAdetDto(20000, 3), new KupurAdetDto(500, 4), new KupurAdetDto(50, 3)], satir.Kupurler);
        Assert.All(nakit.Kupurler, k => Assert.Equal(0, k.Adet));
    }

    [Fact]
    public async Task Satir_ekle_sil_ve_adsiz_satir_uyarisi()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();

        vm.SatirEkleCommand.Execute("Banka");
        Assert.Equal((SayimSatirTuru.Banka, "Banka"), (vm.SayimSatirlari[^1].Tur, vm.SayimSatirlari[^1].Ad));
        vm.SatirEkleCommand.Execute(null);
        Assert.Equal(SayimSatirTuru.Diger, vm.SayimSatirlari[^1].Tur);

        vm.SayimSatirlari[^1].Tutar = 100m;
        vm.SayimSatirlari[^1].Ad = " ";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(KasaSayimiViewModel.SatirAdiMesaji, vm.Hata);
        Assert.Null(api.SonKasaSayimKaydet);

        vm.SatirSilCommand.Execute(vm.SayimSatirlari[^1]);       // tutarlı satır silinince toplam düşer
        Assert.Equal(0m, vm.SayilanTutar);
        Assert.False(vm.SatirlarKullaniliyor);

        while (vm.SayimSatirlari.Count < KasaSayimiViewModel.EnFazlaSatir) vm.SatirEkleCommand.Execute("Diger");
        vm.SatirEkleCommand.Execute("Diger");
        Assert.Equal(KasaSayimiViewModel.EnFazlaSatir, vm.SayimSatirlari.Count);
        Assert.Equal(KasaSayimiViewModel.SatirSiniriMesaji, vm.Hata);
    }

    [Fact]
    public async Task Satirlar_son_satirli_sayimin_adlariyla_gelir_eski_sayim_satirsiz_gorunur()
    {
        var (_, vm) = Kur(a => a.KasaSayimlariListe = new[]
        {
            Sayim(3, new(2026, 9, 20), 500m, 500m),
            Sayim(2, new(2026, 9, 10), 900m, 1_000m, satirlar: new[]
            {
                new SayimSatiriDto(SayimSatirTuru.Nakit, "Kasa", 400m),
                new SayimSatiriDto(SayimSatirTuru.Banka, "Garanti", 500m),
            }),
        });

        await vm.YukleAsync();

        Assert.Equal(["Kasa", "Garanti"], vm.SayimSatirlari.Select(s => s.Ad));
        Assert.False(vm.Sayimlar[0].SatirlarVar);
        Assert.Equal("", vm.Sayimlar[0].SatirlarMetni);
        Assert.Equal("Kasa 400,00 · Garanti 500,00", vm.Sayimlar[1].SatirlarMetni);
    }

    [Fact]
    public async Task Fark_durumu_metni_aciklama_zorunlu_kabul_ve_geri_ac()
    {
        var (api, vm) = Kur(a => a.KasaSayimlariListe = new[]
        {
            Sayim(5, new(2026, 9, 22), 950m, 1_000m),
            Sayim(4, new(2026, 9, 21), 1_000m, 1_000m),
        });
        await vm.YukleAsync();
        var s = vm.Sayimlar[0];
        Assert.True(s.FarkAcik);
        Assert.Equal("Açık: fark açıklanmadı", s.FarkDurumMetni);
        Assert.Equal("", vm.Sayimlar[1].FarkDurumMetni);         // fark yok

        vm.FarkFormuAcKapatCommand.Execute(s);
        Assert.True(s.FarkFormuAcik);
        await vm.FarkAciklaCommand.ExecuteAsync(s);
        Assert.Equal(KasaSayimiViewModel.AciklamaMesaji, vm.Hata);
        Assert.Null(api.SonSayimFarki);

        s.AciklamaGiris = "  Bozuk para eksik  ";
        await vm.FarkAciklaCommand.ExecuteAsync(s);
        Assert.Equal((5, new SayimFarkYaz(SayimFarkDurumu.Aciklandi, "Bozuk para eksik")), api.SonSayimFarki);
        Assert.Equal("Açıklandı: Bozuk para eksik", vm.Sayimlar[0].FarkDurumMetni);

        await vm.FarkKabulEtCommand.ExecuteAsync(vm.Sayimlar[0]);
        Assert.Equal(SayimFarkDurumu.KabulEdildi, api.SonSayimFarki!.Value.G.Durum);

        await vm.FarkAcikYapCommand.ExecuteAsync(vm.Sayimlar[0]);
        Assert.Equal(SayimFarkDurumu.Acik, api.SonSayimFarki!.Value.G.Durum);
    }

    [Fact]
    public async Task Izleyici_fark_durumunu_degistiremez()
    {
        var (api, vm) = Kur(a => a.KasaSayimlariListe = new[] { Sayim(5, new(2026, 9, 22), 950m, 1_000m) }, editor: false);
        await vm.YukleAsync();

        await vm.FarkKabulEtCommand.ExecuteAsync(vm.Sayimlar[0]);

        Assert.Equal(HataMesaji.Yetkisiz, vm.Hata);
        Assert.Null(api.SonSayimFarki);
    }

    [Fact]
    public async Task Neden_degisti_listeyi_yukler_ve_kapatir()
    {
        var (api, vm) = Kur(a =>
        {
            a.KasaSayimlariListe = new[] { Sayim(5, new(2026, 9, 22), 950m, 1_000m) };
            a.NedenDegistiUret = id => new NedenDegistiDto(id, new(2026, 9, 22), 1_000m, 1_200m, 200m, new[]
            {
                new SayimDegisikligiDto(40, new DateTime(2026, 9, 23, 8, 30, 0, DateTimeKind.Utc), "editor", "İşlem", "Eklendi", "20.09.2026 · Market · 200,00"),
            });
        });
        await vm.YukleAsync();
        var s = vm.Sayimlar[0];

        await vm.NedenDegistiCommand.ExecuteAsync(s);

        Assert.True(s.NedenAcik);
        Assert.Equal([5], api.NedenDegistiCagrilari);
        Assert.Equal("Sayımdan sonra bu günü etkileyen 1 değişiklik. Defter değeri +200,00 ₺ değişti.", s.NedenOzeti);
        var d = s.Nedenler.Single();
        Assert.Equal("20.09.2026 · Market · 200,00", d.Ozet);
        Assert.Equal("23.09.2026 08:30 · Editör · İşlem · Eklendi", d.Ayrinti);

        await vm.NedenDegistiCommand.ExecuteAsync(s);
        Assert.False(s.NedenAcik);
        Assert.Single(api.NedenDegistiCagrilari);                 // kapatmak istek atmaz
    }

    [Fact]
    public void Aylik_acik_fark_ozeti_yalniz_acik_farklari_toplar()
    {
        var l = new[]
        {
            Sayim(1, new(2026, 9, 22), 950m, 1_000m),                                      // açık −50
            Sayim(2, new(2026, 9, 15), 1_030m, 1_000m),                                    // açık +30
            Sayim(3, new(2026, 9, 10), 900m, 1_000m, SayimFarkDurumu.Aciklandi, "banka"),  // açıklandı
            Sayim(4, new(2026, 8, 30), 1_000m, 1_000m),                                    // fark yok
            Sayim(5, new(2026, 7, 5), 800m, 1_000m, SayimFarkDurumu.KabulEdildi),
        };

        var a = AylikSayimFarki.Hesapla(l);

        Assert.Equal([new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 1)], a.Select(x => x.Ay));
        Assert.Equal((3, 2, -20m, 80m, 1.0), (a[0].SayimAdet, a[0].AcikAdet, a[0].AcikFark, a[0].AcikFarkMutlak, a[0].Oran));
        Assert.Equal("3 sayım · 2 açık fark · net -20,00 ₺", a[0].Metin);
        Assert.Equal("Eylül 2026", a[0].AyAdi);
        Assert.Equal("1 sayım · açıklanmamış fark yok", a[1].Metin);
        Assert.Equal(0, a[2].Oran);
    }

    [Fact]
    public async Task Son_sayim_hatirlatmasi_yedi_gunu_gecince()
    {
        var (_, bos) = Kur();
        await bos.YukleAsync();
        Assert.True(bos.SayimGecikti);
        Assert.Equal("Henüz sayım yok. Kasayı haftada bir saymanız önerilir.", bos.SonSayimMetni);

        var (_, eski) = Kur(a => a.KasaSayimlariListe = new[] { Sayim(1, new(2026, 9, 14), 1m, 1m) });
        await eski.YukleAsync();
        Assert.True(eski.SayimGecikti);
        Assert.Equal("Son sayım 10 gün önce (14 Eylül). Haftada bir sayım önerilir.", eski.SonSayimMetni);

        var (_, yeni) = Kur(a => a.KasaSayimlariListe = new[] { Sayim(1, new(2026, 9, 23), 1m, 1m), Sayim(2, new(2026, 9, 1), 1m, 1m) });
        await yeni.YukleAsync();
        Assert.False(yeni.SayimGecikti);
        Assert.Equal("Son sayım dün yapıldı.", yeni.SonSayimMetni);
        Assert.True(yeni.AylikFarkVar);
    }
}
