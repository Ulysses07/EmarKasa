using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Seçilen dosyaları sırayla döndüren dosya seçici sahtesi.</summary>
public sealed class SahteDosyaSecici : IDosyaSecici
{
    public bool KameraVar { get; set; }
    public Queue<IReadOnlyList<SecilenDosya>> Belgeler = new();
    public Queue<SecilenDosya?> Fotograflar = new();
    public Queue<SecilenDosya?> Csvler = new();
    public int CsvCagri;

    public Task<IReadOnlyList<SecilenDosya>> BelgeSecAsync()
        => Task.FromResult(Belgeler.Count > 0 ? Belgeler.Dequeue() : (IReadOnlyList<SecilenDosya>)[]);

    public Task<SecilenDosya?> FotografCekAsync() => Task.FromResult(Fotograflar.Count > 0 ? Fotograflar.Dequeue() : null);

    public Task<SecilenDosya?> CsvSecAsync()
    {
        CsvCagri++;
        return Task.FromResult(Csvler.Count > 0 ? Csvler.Dequeue() : null);
    }
}

public sealed class SahteEkAcici : IEkAcici
{
    public List<(string Ad, byte[] Icerik)> Acilanlar = new();
    public Task AcAsync(string dosyaAdi, byte[] icerik) { Acilanlar.Add((dosyaAdi, icerik)); return Task.CompletedTask; }
}

/// <summary>İşlem formunun belge bölümü: belge türü/no, fatura bekleniyor ve ekler (Paket F, madde 17 ve 41).</summary>
public class IslemBelgeTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    private static SecilenDosya Dosya(string ad, int boyut = 100) => new(ad, Enumerable.Repeat((byte)1, boyut).ToArray());

    private static EkDto Ek(int id, int islemId, string ad) => new(id, islemId, ad, ad.EndsWith(".pdf") ? "application/pdf" : "image/jpeg", 1234,
        new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc));

    private static (SahteApi api, IslemlerViewModel vm, SahteDosyaSecici secici, SahteEkAcici acici) Kur()
    {
        var api = new SahteApi { CarilerListe = [new CariDto(1, "Yılmaz Gıda", true)] };
        var secici = new SahteDosyaSecici();
        var acici = new SahteEkAcici();
        var vm = new IslemlerViewModel(api, new SabitSaat(Bugun.AddHours(10)))
        {
            DosyaSecici = secici,
            EkAcici = acici,
            DuzenTarih = Bugun,
            DuzenCari = "Yılmaz Gıda",
            DuzenTutar = 1500m,
            DuzenKanal = "MEZAT",
            DuzenTip = GiderTipi.Cari,
        };
        return (api, vm, secici, acici);
    }

    private static IslemDto Islem(int id, BelgeTuru? tur = null, string? no = null, bool bekleniyor = false)
        => new(id, new DateOnly(2026, 9, 20), "Yılmaz Gıda", 1500m, "MEZAT", GiderTipi.Cari, null)
        { BelgeTuru = tur, BelgeNo = no, FaturaBekleniyor = bekleniyor };

    [Fact]
    public async Task Belge_girilmeden_kaydetmek_bos_belge_gonderir_ve_ek_yuklemez()
    {
        var (api, vm, _, _) = Kur();
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal(BelgeBilgisi.Bos, api.SonIslemOlustur!.Belge);
        Assert.Empty(api.EkYuklemeleri);
    }

    [Fact]
    public async Task Belge_turu_no_ve_fatura_bekleniyor_kaydetmede_gider_bosluklar_kirpilir()
    {
        var (api, vm, _, _) = Kur();
        vm.SecBelgeTuruCommand.Execute(vm.BelgeTuruCipleri.Single(c => c.Ad == "e-Arşiv"));
        vm.DuzenBelgeNo = "  GIB2026000123  ";
        vm.DuzenFaturaBekleniyor = true;

        Assert.Equal(BelgeTuru.EArsiv, vm.DuzenBelgeTuru);
        Assert.True(vm.BelgeTuruCipleri.Single(c => c.Ad == "e-Arşiv").Secili);
        Assert.False(vm.BelgeTuruCipleri.Single(c => c.Ad == BelgeMetin.Belirtilmedi).Secili);

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal(new BelgeBilgisi(BelgeTuru.EArsiv, "GIB2026000123", true), api.SonIslemOlustur!.Belge);
        // Kaydetten sonra form boşalır.
        Assert.Null(vm.DuzenBelgeTuru);
        Assert.Null(vm.DuzenBelgeNo);
        Assert.False(vm.DuzenFaturaBekleniyor);
        Assert.True(vm.BelgeTuruCipleri[0].Secili);
    }

    [Fact]
    public void Belge_turu_cipleri_belirtilmedi_ile_baslar()
    {
        var (_, vm, _, _) = Kur();
        Assert.Equal([BelgeMetin.Belirtilmedi, "e-Fatura", "e-Arşiv", "Fiş", "Makbuz", "Belgesiz"], vm.BelgeTuruCipleri.Select(c => c.Ad));
        vm.SecBelgeTuruCommand.Execute(vm.BelgeTuruCipleri[5]);
        Assert.Equal(BelgeTuru.Belgesiz, vm.DuzenBelgeTuru);
        vm.SecBelgeTuruCommand.Execute(vm.BelgeTuruCipleri[0]);
        Assert.Null(vm.DuzenBelgeTuru);
    }

    [Fact]
    public async Task Duzenle_belgeyi_forma_alir_ve_ekleri_yukler()
    {
        var (api, vm, _, _) = Kur();
        api.EklerSozluk[42] = [Ek(7, 42, "fis.jpg"), Ek(8, 42, "fatura.pdf")];

        vm.DuzenleCommand.Execute(Islem(42, BelgeTuru.EFatura, "F-12", true));
        await Task.Yield();

        Assert.Equal(BelgeTuru.EFatura, vm.DuzenBelgeTuru);
        Assert.Equal("F-12", vm.DuzenBelgeNo);
        Assert.True(vm.DuzenFaturaBekleniyor);
        Assert.True(vm.BelgeTuruCipleri.Single(c => c.Ad == "e-Fatura").Secili);
        Assert.Equal([7, 8], vm.MevcutEkler.Select(e => e.Id));
        Assert.True(vm.MevcutEkVar);
        Assert.Equal("2 ek", vm.EkOzeti);

        // Güncellemede de belge gider (id korunur).
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(42, api.SonIslemGuncelle!.Value.Id);
        Assert.Equal(new BelgeBilgisi(BelgeTuru.EFatura, "F-12", true), api.SonIslemGuncelle!.Value.G.Belge);
    }

    [Fact]
    public async Task Yeni_belge_bolumunu_ve_bekleyen_ekleri_bosaltir()
    {
        var (api, vm, secici, _) = Kur();
        api.EklerSozluk[42] = [Ek(7, 42, "fis.jpg")];
        vm.DuzenleCommand.Execute(Islem(42, BelgeTuru.Fis));
        await Task.Yield();
        secici.Belgeler.Enqueue([Dosya("yeni.png")]);
        await vm.EkEkleCommand.ExecuteAsync(null);
        Assert.Single(vm.BekleyenEkler);

        vm.YeniCommand.Execute(null);

        Assert.Null(vm.DuzenBelgeTuru);
        Assert.Empty(vm.MevcutEkler);
        Assert.Empty(vm.BekleyenEkler);
        Assert.Equal("Ek yok", vm.EkOzeti);
    }

    [Fact]
    public async Task Secilen_dosyalar_kaydetten_sonra_yeni_islemin_idsine_yuklenir()
    {
        var (api, vm, secici, _) = Kur();
        secici.Belgeler.Enqueue([Dosya("fis.jpg"), Dosya("fatura.PDF")]);

        await vm.EkEkleCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal(["fis.jpg", "fatura.PDF"], vm.BekleyenEkler.Select(e => e.Ad));
        Assert.Equal("2 dosya kaydetmede yüklenecek", vm.EkOzeti);
        Assert.Empty(api.EkYuklemeleri);   // kaydetmeden yükleme yok

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        // Sahte API yeni işleme Id 0 verir: ekler dönen işlemin Id'sine yüklenir.
        Assert.Equal([(0, "fis.jpg"), (0, "fatura.PDF")], api.EkYuklemeleri.Select(y => (y.IslemId, y.Ad)));
        Assert.Empty(vm.BekleyenEkler);
    }

    [Fact]
    public async Task Uygun_olmayan_dosyalar_eklenmez_uygunlar_eklenir()
    {
        var (_, vm, secici, _) = Kur();
        secici.Belgeler.Enqueue([
            Dosya("fis.jpg"),
            Dosya("virus.exe"),
            new SecilenDosya("dev.pdf", [], 11L * 1024 * 1024),
            new SecilenDosya("bos.png", []),
        ]);

        await vm.EkEkleCommand.ExecuteAsync(null);

        Assert.Equal(["fis.jpg"], vm.BekleyenEkler.Select(e => e.Ad));
        Assert.Contains(EkKurallari.TurMesaji, vm.Hata);
        Assert.Contains("'dev.pdf' 10 MB'tan büyük", vm.Hata);
        Assert.Contains("'bos.png' boş", vm.Hata);
    }

    [Fact]
    public async Task Islem_basina_en_fazla_on_ek()
    {
        var (api, vm, secici, _) = Kur();
        api.EklerSozluk[42] = Enumerable.Range(1, 8).Select(i => Ek(i, 42, $"e{i}.jpg")).ToList();
        vm.DuzenleCommand.Execute(Islem(42));
        await Task.Yield();
        secici.Belgeler.Enqueue([Dosya("a.jpg"), Dosya("b.jpg"), Dosya("c.jpg")]);

        await vm.EkEkleCommand.ExecuteAsync(null);

        Assert.Equal(["a.jpg", "b.jpg"], vm.BekleyenEkler.Select(e => e.Ad));
        Assert.Equal(EkKurallari.SayiMesaji, vm.Hata);
    }

    [Fact]
    public async Task Bekleyen_ek_kaldirilabilir()
    {
        var (api, vm, secici, _) = Kur();
        secici.Belgeler.Enqueue([Dosya("a.jpg"), Dosya("b.jpg")]);
        await vm.EkEkleCommand.ExecuteAsync(null);

        vm.BekleyenEkiKaldirCommand.Execute(vm.BekleyenEkler[0]);
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(["b.jpg"], api.EkYuklemeleri.Select(y => y.Ad));
    }

    [Fact]
    public async Task Secici_yoksa_ya_da_kamera_yoksa_anlasilir_hata()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api);
        Assert.False(vm.KameraVar);

        await vm.EkEkleCommand.ExecuteAsync(null);
        Assert.Equal(IslemlerViewModel.SeciciYokMesaji, vm.Hata);

        vm.DosyaSecici = new SahteDosyaSecici { KameraVar = false };
        await vm.FotografCekCommand.ExecuteAsync(null);
        Assert.Equal(IslemlerViewModel.KameraYokMesaji, vm.Hata);
    }

    [Fact]
    public async Task Fotograf_cek_kamera_varsa_bekleyenlere_ekler_vazgecilirse_bir_sey_olmaz()
    {
        var (_, vm, secici, _) = Kur();
        secici.KameraVar = true;
        vm.DosyaSecici = secici;   // KameraVar bildirimi
        Assert.True(vm.KameraVar);

        secici.Fotograflar.Enqueue(Dosya("kamera.jpg"));
        await vm.FotografCekCommand.ExecuteAsync(null);
        await vm.FotografCekCommand.ExecuteAsync(null);   // vazgeçildi (null)

        Assert.Null(vm.Hata);
        Assert.Equal(["kamera.jpg"], vm.BekleyenEkler.Select(e => e.Ad));
    }

    [Fact]
    public async Task Yuklenemeyen_ek_varsa_islem_kayitli_kalir_form_o_islemde_kalir_ve_tekrar_denenir()
    {
        var (api, vm, secici, _) = Kur();
        vm.DuzenleCommand.Execute(Islem(42, BelgeTuru.Fis));
        await Task.Yield();
        secici.Belgeler.Enqueue([Dosya("iyi.jpg"), Dosya("bozuk.jpg")]);
        await vm.EkEkleCommand.ExecuteAsync(null);
        api.YuklenemeyenEkler.Add("bozuk.jpg");

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(42, api.SonIslemGuncelle!.Value.Id);
        Assert.Contains("İşlem kaydedildi ama 1 dosya yüklenemedi", vm.Hata);
        Assert.Contains("bozuk.jpg: Dosyanın içeriği uzantısıyla uyuşmuyor.", vm.Hata);
        Assert.Equal(42, vm.DuzenId);                       // form kaydedilen işlemde
        Assert.Equal(["bozuk.jpg"], vm.BekleyenEkler.Select(e => e.Ad));
        Assert.Equal(["iyi.jpg"], api.EkYuklemeleri.Select(y => y.Ad));

        // Sorun giderildi: Kaydet yeniden basılınca yalnız bekleyen dosya yüklenir.
        api.YuklenemeyenEkler.Clear();
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal(["iyi.jpg", "bozuk.jpg"], api.EkYuklemeleri.Select(y => y.Ad));
        Assert.Empty(vm.BekleyenEkler);
        Assert.Equal(0, vm.DuzenId);
    }

    [Fact]
    public async Task Ek_silme_iki_adimlidir_vazgecilirse_silinmez()
    {
        var (api, vm, _, _) = Kur();
        api.EklerSozluk[42] = [Ek(7, 42, "fis.jpg"), Ek(8, 42, "fatura.pdf")];
        vm.DuzenleCommand.Execute(Islem(42));
        await Task.Yield();

        vm.EkSilIsteCommand.Execute(vm.MevcutEkler[0]);
        Assert.True(vm.EkSilmeOnayiBekliyor);
        Assert.Contains("'fis.jpg' silinsin mi?", vm.SilinecekEkMetni);
        vm.EkSilVazgecCommand.Execute(null);
        Assert.False(vm.EkSilmeOnayiBekliyor);
        Assert.Empty(api.SilinenEkler);

        vm.EkSilIsteCommand.Execute(vm.MevcutEkler[0]);
        await vm.EkSilOnaylaCommand.ExecuteAsync(null);

        Assert.Equal([7], api.SilinenEkler);
        Assert.Equal([8], vm.MevcutEkler.Select(e => e.Id));
        Assert.False(vm.EkSilmeOnayiBekliyor);
    }

    [Fact]
    public async Task Ek_ac_indirir_ve_cihazda_acar()
    {
        var (api, vm, _, acici) = Kur();
        api.EklerSozluk[42] = [Ek(7, 42, "fis.jpg")];
        vm.DuzenleCommand.Execute(Islem(42));
        await Task.Yield();

        await vm.EkAcCommand.ExecuteAsync(vm.MevcutEkler[0]);

        Assert.Null(vm.Hata);
        Assert.Equal([7], api.IndirilenEkler);
        Assert.Equal("fis.jpg", Assert.Single(acici.Acilanlar).Ad);
    }

    [Fact]
    public async Task Baska_isleme_gecince_eski_ek_listesi_yenisini_ezmez()
    {
        var (api, vm, _, _) = Kur();
        api.EklerSozluk[1] = [Ek(11, 1, "bir.jpg")];
        api.EklerSozluk[2] = [Ek(22, 2, "iki.jpg")];

        vm.DuzenleCommand.Execute(Islem(1));
        vm.DuzenleCommand.Execute(Islem(2));
        await Task.Yield();

        Assert.Equal([22], vm.MevcutEkler.Select(e => e.Id));
    }

    [Fact]
    public void Belgesiz_ile_fatura_bekleniyor_birlikte_secilemez_biri_secilince_oteki_kalkar()
    {
        var (_, vm, _, _) = Kur();
        vm.DuzenFaturaBekleniyor = true;
        vm.SecBelgeTuruCommand.Execute(vm.BelgeTuruCipleri.Single(c => c.Ad == "Belgesiz"));
        Assert.Equal(BelgeTuru.Belgesiz, vm.DuzenBelgeTuru);
        Assert.False(vm.DuzenFaturaBekleniyor);

        vm.DuzenFaturaBekleniyor = true;
        Assert.True(vm.DuzenFaturaBekleniyor);
        Assert.Null(vm.DuzenBelgeTuru);   // türü fatura gelince seçilir
        Assert.True(vm.BelgeTuruCipleri[0].Secili);

        // Başka türlerle fatura bekleniyor bir arada durabilir (ör. e-Arşiv bekleniyor).
        vm.SecBelgeTuruCommand.Execute(vm.BelgeTuruCipleri.Single(c => c.Ad == "e-Arşiv"));
        Assert.Equal((BelgeTuru.EArsiv, true), (vm.DuzenBelgeTuru!.Value, vm.DuzenFaturaBekleniyor));
    }

    /// <summary>
    /// Bulgu: işlem listesinde belge/ek görünmüyordu; ortaklar (izleyici) fişi/faturayı hiçbir yerden açamıyordu.
    /// Satırdaki "Ekler (n)" düğmesi her iki rolde salt okunur bir panel açar; düzenleme formuna dokunmaz.
    /// </summary>
    [Fact]
    public async Task Izleyici_listede_ekleri_salt_okunur_panelde_acar_form_etkilenmez()
    {
        var (api, vm, _, acici) = Kur();
        vm.EditorMu = false;
        api.EklerSozluk[42] = [Ek(7, 42, "fis.jpg"), Ek(8, 42, "fatura.pdf")];
        var satir = Islem(42, BelgeTuru.Fis, "F-9") with { EkSayisi = 2 };
        Assert.Equal((true, "Ekler (2)", true), (satir.EkVar, satir.EkEtiketi, satir.BelgeVar));
        Assert.False(vm.ListeEkPaneliGorunur);

        await vm.ListeEkleriGosterCommand.ExecuteAsync(satir);

        Assert.Null(vm.Hata);
        Assert.True(vm.ListeEkPaneliGorunur);
        Assert.Equal("Ekler · Yılmaz Gıda · 1.500,00 ₺ · 20.09.2026", vm.ListeEkBasligi);
        Assert.Equal([7, 8], vm.ListeEkleri.Select(e => e.Id));
        Assert.Equal("Fiş · F-9", BelgeMetin.Ozet(vm.ListeEkIslemi!.Belge));
        Assert.Equal(0, vm.DuzenId);                 // düzenleme formu açılmadı
        Assert.Empty(vm.MevcutEkler);

        await vm.EkAcCommand.ExecuteAsync(vm.ListeEkleri[1]);
        Assert.Null(vm.Hata);
        Assert.Equal([8], api.IndirilenEkler);
        Assert.Equal("fatura.pdf", Assert.Single(acici.Acilanlar).Ad);

        // Başka satıra geçilince panel onun eklerini gösterir; Kapat boşaltır.
        api.EklerSozluk[43] = [Ek(9, 43, "makbuz.jpg")];
        await vm.ListeEkleriGosterCommand.ExecuteAsync(Islem(43) with { EkSayisi = 1 });
        Assert.Equal([9], vm.ListeEkleri.Select(e => e.Id));
        vm.ListeEkleriKapatCommand.Execute(null);
        Assert.False(vm.ListeEkPaneliGorunur);
        Assert.Equal("", vm.ListeEkBasligi);
        Assert.Empty(vm.ListeEkleri);
    }

    [Fact]
    public async Task Liste_ek_paneli_yuklenemezse_hata_gosterir()
    {
        var (api, vm, _, _) = Kur();
        api.YuklemeHatasi = new HttpRequestException("bağlantı yok");
        await vm.ListeEkleriGosterCommand.ExecuteAsync(Islem(42) with { EkSayisi = 1 });
        Assert.NotNull(vm.Hata);
        Assert.Empty(vm.ListeEkleri);
    }

    [Fact]
    public async Task Formdan_ek_silinince_listedeki_sayi_ve_acik_panel_guncellenir()
    {
        var (api, vm, _, _) = Kur();
        vm.EditorMu = true;
        api.EklerSozluk[42] = [Ek(7, 42, "fis.jpg"), Ek(8, 42, "fatura.pdf")];
        api.IslemlerListe = [Islem(42, BelgeTuru.Fis) with { EkSayisi = 2 }, Islem(43)];
        await vm.YukleAsync();
        var satir = vm.Islemler.Single(i => i.Id == 42);
        await vm.ListeEkleriGosterCommand.ExecuteAsync(satir);

        vm.DuzenleCommand.Execute(satir);
        await Task.Yield();
        vm.EkSilIsteCommand.Execute(vm.MevcutEkler.Single(e => e.Id == 7));
        await vm.EkSilOnaylaCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal("Ekler (1)", vm.Islemler.Single(i => i.Id == 42).EkEtiketi);
        Assert.Equal(0, vm.Islemler.Single(i => i.Id == 43).EkSayisi);
        Assert.Equal([8], vm.ListeEkleri.Select(e => e.Id));

        vm.EkSilIsteCommand.Execute(vm.MevcutEkler.Single());
        await vm.EkSilOnaylaCommand.ExecuteAsync(null);
        Assert.False(vm.Islemler.Single(i => i.Id == 42).EkVar);   // "Ekler" düğmesi kaybolur
    }

    [Fact]
    public async Task Islem_silinince_onun_ek_paneli_kapanir_baska_islemin_paneli_kalir()
    {
        var (api, vm, _, _) = Kur();
        vm.EditorMu = true;
        api.EklerSozluk[42] = [Ek(7, 42, "fis.jpg")];
        await vm.ListeEkleriGosterCommand.ExecuteAsync(Islem(42) with { EkSayisi = 1 });

        await vm.SilCommand.ExecuteAsync(Islem(43));
        Assert.True(vm.ListeEkPaneliGorunur);

        await vm.SilCommand.ExecuteAsync(Islem(42));
        Assert.False(vm.ListeEkPaneliGorunur);
        Assert.Empty(vm.ListeEkleri);
    }
}

/// <summary>Ek kuralları ve belge metinleri.</summary>
public class EkKurallariTests
{
    [Theory]
    [InlineData("fis.jpg", null)]
    [InlineData("fis.JPEG", null)]
    [InlineData("foto.heic", null)]
    [InlineData("fatura.pdf", null)]
    [InlineData("tarama.webp", null)]
    [InlineData("uzantisiz", null)]   // sunucu içerikten anlar
    [InlineData("belge.docx", EkKurallari.TurMesaji)]
    [InlineData("x.exe", EkKurallari.TurMesaji)]
    public void Uzanti_kurali(string ad, string? beklenen)
        => Assert.Equal(beklenen, EkKurallari.Hata(new SecilenDosya(ad, [1, 2, 3])));

    [Fact]
    public void Boyut_siniri_tam_on_megabayt()
    {
        Assert.Null(EkKurallari.Hata(new SecilenDosya("a.jpg", [1], EkKurallari.EnFazlaBoyut)));
        Assert.Equal(EkKurallari.BoyutMesaji("a.jpg"), EkKurallari.Hata(new SecilenDosya("a.jpg", [1], EkKurallari.EnFazlaBoyut + 1)));
        Assert.Equal(EkKurallari.BosMesaji("a.jpg"), EkKurallari.Hata(new SecilenDosya("a.jpg", [])));
    }

    [Theory]
    [InlineData(1L, "1 KB")]
    [InlineData(1024L, "1 KB")]
    [InlineData(350_000L, "342 KB")]
    [InlineData(1_258_291L, "1,2 MB")]
    [InlineData(10_485_760L, "10 MB")]
    public void Boyut_metni(long bayt, string beklenen) => Assert.Equal(beklenen, EkKurallari.BoyutMetni(bayt));

    [Fact]
    public void Belge_ozeti()
    {
        Assert.Equal("", BelgeMetin.Ozet(BelgeBilgisi.Bos));
        Assert.Equal("e-Fatura · F-12 · fatura bekleniyor", BelgeMetin.Ozet(new BelgeBilgisi(BelgeTuru.EFatura, "F-12", true)));
        Assert.Equal("fatura bekleniyor", BelgeMetin.Ozet(new BelgeBilgisi(null, " ", true)));
        foreach (var t in BelgeMetin.Turler) Assert.Equal(t, BelgeMetin.TurDegeri(BelgeMetin.TurAdi(t)));
    }
}
