using System.Collections.Specialized;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>İşlemler ekranı listesi: son başlatılan istek kazanır, hafta seçici listeyle tutarlı kalır,
/// yükleme ve hata durumu görünür ("Henüz işlem yok" yalnız gerçekten boşken).</summary>
public class IslemListesiTests
{
    private static readonly DonemDto Hafta1 = new(new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 12), 2026, 7);
    private static readonly DonemDto Hafta2 = new(new DateOnly(2026, 7, 13), new DateOnly(2026, 7, 19), 2026, 7);
    private static readonly DonemDto Hafta3 = new(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 26), 2026, 7);
    private static readonly KanalDto Mezat = new(1, "MEZAT", true, 0, 0m);

    private static IslemDto Islem(int id, decimal tutar, DateOnly? tarih = null) => new(id, tarih ?? new DateOnly(2026, 7, 8), "Gider " + id, tutar, "MEZAT", GiderTipi.Cari, null);
    private static SahteApi Api() => new() { KanallarListe = new[] { Mezat }, DonemlerListe = new[] { Hafta1, Hafta2 } };
    private static IslemlerViewModel Vm(SahteApi api) => new(api, TestOturumu.Ac(), zaman: new IslemEditorTests.SabitZaman(new DateOnly(2026, 7, 15)));

    /// <summary>Her liste isteği ayrı bekleyen yanıt alır; test yanıtların sırasını belirler.</summary>
    private static List<TaskCompletionSource<IReadOnlyList<IslemDto>>> Bekleyenler(SahteApi api)
    {
        var liste = new List<TaskCompletionSource<IReadOnlyList<IslemDto>>>();
        api.IslemlerGetir = (_, _, _) => { var t = new TaskCompletionSource<IReadOnlyList<IslemDto>>(); liste.Add(t); return t.Task; };
        return liste;
    }

    /// <summary>MAUI Picker gibi: ItemsSource sıfırlanınca (Reset) seçim düşer ve TwoWay bağlama VM'ye null yazar.</summary>
    private static void PickerGibi(IslemlerViewModel vm)
        => vm.FiltreDonemler.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) vm.SeciliDonem = null; };

    private static SecimCipi Kanal(IslemlerViewModel vm, string ad) => vm.FiltreKanallari.First(c => c.Ad == ad);
    private static SecimCipi Zaman(IslemlerViewModel vm, string ad) => vm.FiltreZamanlar.First(c => c.Ad == ad);

    // ---- appcore-3: son istek kazanır ----

    [Fact]
    public async Task Filtre_degisince_eski_istegin_gec_yaniti_yeni_listeyi_ve_ozeti_ezmez()
    {
        var api = Api();
        var vm = Vm(api);
        await vm.YukleAsync();
        var istekler = Bekleyenler(api);

        var mezat = vm.SecFiltreKanalCommand.ExecuteAsync(Kanal(vm, "MEZAT"));   // tüm zamanlar: yavaş yanıt
        var buAy = vm.SecFiltreZamanCommand.ExecuteAsync(Zaman(vm, "Bu ay"));
        Assert.True(vm.ListeYukleniyor);

        istekler[1].SetResult(new[] { Islem(2, 50m, new DateOnly(2026, 7, 14)) });
        await buAy;
        Assert.Equal(2, Assert.Single(vm.Islemler).Id);
        Assert.False(vm.ListeYukleniyor);
        Assert.True(vm.VeriVar);
        Assert.Equal("MEZAT · Bu ay · 1 işlem · toplam 50,00 ₺", vm.FiltreOzet);

        istekler[0].SetResult(new[] { Islem(1, 900m), Islem(3, 100m) });
        await mezat;
        Assert.Equal(2, Assert.Single(vm.Islemler).Id);
        Assert.Equal(50m, vm.FiltreToplam);
        Assert.Equal(1, vm.FiltreSayi);
        Assert.Equal("MEZAT · Bu ay · 1 işlem · toplam 50,00 ₺", vm.FiltreOzet);
        Assert.False(vm.ListeYukleniyor);
    }

    [Fact]
    public async Task Once_biten_eski_istek_yuklemeyi_bitirmez_hatasi_gosterilmez()
    {
        var api = Api();
        var vm = Vm(api);
        await vm.YukleAsync();
        var istekler = Bekleyenler(api);

        var eski = vm.SecFiltreKanalCommand.ExecuteAsync(Kanal(vm, "MEZAT"));
        var yeni = vm.SecFiltreZamanCommand.ExecuteAsync(Zaman(vm, "Geçen ay"));

        istekler[0].SetException(new HttpRequestException());
        await eski;
        Assert.Null(vm.YuklemeHatasi);
        Assert.True(vm.ListeYukleniyor);
        Assert.False(vm.VeriVar);

        istekler[1].SetResult(new[] { Islem(4, 10m, new DateOnly(2026, 6, 3)) });
        await yeni;
        Assert.Null(vm.YuklemeHatasi);
        Assert.False(vm.ListeYukleniyor);
        Assert.True(vm.VeriVar);
        Assert.Equal(4, Assert.Single(vm.Islemler).Id);
        Assert.Equal((new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)), (api.SonFiltreBaslangic!.Value, api.SonFiltreBitis!.Value));
    }

    [Fact]
    public async Task Hafta_secimiyle_baslayan_istegin_gec_yaniti_sonraki_filtreyi_ezmez()
    {
        var api = Api();
        var vm = Vm(api);
        await vm.YukleAsync();
        var istekler = Bekleyenler(api);

        vm.SeciliDonem = vm.FiltreDonemler.Single(d => d.Start == Hafta1.Start);
        var hafta = vm.ListeYuklemesi;
        var tumu = vm.SecFiltreZamanCommand.ExecuteAsync(Zaman(vm, "Tümü"));

        istekler[1].SetResult(new[] { Islem(5, 70m, new DateOnly(2026, 5, 2)) });
        await tumu;
        istekler[0].SetResult(new[] { Islem(6, 999m) });
        await hafta;

        Assert.Equal(5, Assert.Single(vm.Islemler).Id);
        Assert.Null(vm.SeciliDonem);
        Assert.Null(vm.FiltreBaslangic);
        Assert.Equal("Tüm tarihler · 1 işlem · toplam 70,00 ₺", vm.FiltreOzet);
        Assert.False(vm.ListeYukleniyor);
    }

    // ---- maui-2: hafta seçici ile liste tutarlı ----

    [Fact]
    public async Task Kayit_sonrasi_donem_listesi_yenilense_de_hafta_secimi_ve_suzgec_tutarli_kalir()
    {
        var api = Api();
        var vm = Vm(api);
        await vm.YukleAsync();
        PickerGibi(vm);
        vm.SeciliDonem = vm.FiltreDonemler.Single(d => d.Start == Hafta1.Start);
        await vm.ListeYuklemesi;

        // Dönem listesi aynı kaldı: seçici sıfırlanmaz.
        vm.DuzenTarih = new DateTime(2026, 7, 8);
        vm.DuzenCari = "Kira";
        vm.DuzenTutar = 100m;
        vm.DuzenKanal = "MEZAT";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(Hafta1, vm.SeciliDonem);
        Assert.Equal((Hafta1.Start, Hafta1.End), (api.SonFiltreBaslangic!.Value, api.SonFiltreBitis!.Value));
        Assert.StartsWith("06 Tem – 12 Tem · ", vm.FiltreOzet);
        Assert.Equal("Gider kaydedildi.", vm.Mesaj);

        // Kayıt yeni bir hafta açtı: liste yenilenir, seçici aynı haftayı geri seçer, süzgeç izi kalır.
        api.DonemlerListe = new[] { Hafta1, Hafta2, Hafta3 };
        vm.DuzenTarih = new DateTime(2026, 7, 21);
        vm.DuzenCari = "Elektrik";
        vm.DuzenTutar = 40m;
        vm.DuzenKanal = "MEZAT";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(new[] { Hafta3, Hafta2, Hafta1 }, vm.FiltreDonemler);
        Assert.Equal(Hafta1, vm.SeciliDonem);
        Assert.Equal((Hafta1.Start, Hafta1.End), (api.SonFiltreBaslangic!.Value, api.SonFiltreBitis!.Value));
        Assert.StartsWith("06 Tem – 12 Tem · ", vm.FiltreOzet);
        Assert.Equal("Gider kaydedildi; seçili süzgeç (06 Tem – 12 Tem) dışında kaldığı için listede görünmüyor.", vm.Mesaj);

        // Ekrana dönüş (tam yükleme) de seçimi korur.
        await vm.YukleAsync();
        Assert.Equal(Hafta1, vm.SeciliDonem);
        Assert.Equal(Hafta1.Start, api.SonFiltreBaslangic);
    }

    [Fact]
    public async Task Secili_hafta_listeden_kalkarsa_suzgec_tum_tarihlere_doner_ve_bunu_gosterir()
    {
        var api = Api();
        var vm = Vm(api);
        await vm.YukleAsync();
        PickerGibi(vm);
        vm.SeciliDonem = vm.FiltreDonemler.Single(d => d.Start == Hafta1.Start);
        await vm.ListeYuklemesi;

        api.DonemlerListe = new[] { Hafta2, Hafta3 };
        await vm.YukleAsync();

        Assert.Null(vm.SeciliDonem);
        Assert.Null(vm.FiltreBaslangic);
        Assert.Null(vm.FiltreBitis);
        Assert.Null(api.SonFiltreBaslangic);
        Assert.Null(api.SonFiltreBitis);
        Assert.True(Zaman(vm, "Tümü").Secili);
        Assert.StartsWith("Tüm tarihler · ", vm.FiltreOzet);
    }

    [Fact]
    public async Task Hafta_listesi_yenilenirken_secicinin_yazdigi_bos_secim_listelemeyi_tetiklemez()
    {
        var api = Api();
        var vm = Vm(api);
        await vm.YukleAsync();
        PickerGibi(vm);
        vm.SeciliDonem = vm.FiltreDonemler.Single(d => d.Start == Hafta2.Start);
        await vm.ListeYuklemesi;
        var sayac = 0;
        api.IslemlerGetir = (_, _, _) => { sayac++; return Task.FromResult<IReadOnlyList<IslemDto>>(Array.Empty<IslemDto>()); };

        api.DonemlerListe = new[] { Hafta1, Hafta2, Hafta3 };
        await vm.YukleAsync();

        Assert.Equal(1, sayac);                                   // yalnız tam yüklemenin kendi isteği
        Assert.Equal(Hafta2, vm.SeciliDonem);
        Assert.False(Zaman(vm, "Tümü").Secili);
    }

    // ---- maui-6: meşgul ve hata durumu ----

    [Fact]
    public async Task Yukleme_hatasi_listede_gosterilir_bos_liste_basligi_gosterilmez_yenile_ile_duzelir()
    {
        var api = Api();
        api.YuklemeHatasi = new HttpRequestException();
        var vm = Vm(api);

        await vm.YukleAsync();

        Assert.Contains("Sunucuya ulaşılamadı", vm.YuklemeHatasi);
        Assert.False(vm.VeriVar);
        Assert.Empty(vm.Islemler);
        Assert.Equal("", vm.FiltreOzet);
        Assert.False(vm.ListeYukleniyor);
        Assert.False(vm.Mesgul);
        Assert.Null(vm.SonGuncelleme);
        Assert.Null(vm.Hata);                                     // form hatası alanı liste hatasıyla karışmaz

        api.YuklemeHatasi = null;
        api.IslemlerListe = new[] { Islem(1, 10m) };
        await vm.YenileCommand.ExecuteAsync(null);

        Assert.Null(vm.YuklemeHatasi);
        Assert.True(vm.VeriVar);
        Assert.NotNull(vm.SonGuncelleme);
        Assert.Single(vm.Islemler);
        Assert.Contains("güncelleme: 15.07.2026 12:00", vm.SonGuncellemeMetni);
    }

    [Fact]
    public async Task Filtre_yukleme_hatasi_eski_listeyi_ve_toplamini_gostermez()
    {
        var api = Api();
        api.IslemlerListe = new[] { Islem(1, 10m), Islem(2, 20m) };
        var vm = Vm(api);
        await vm.YukleAsync();
        Assert.Equal(2, vm.Islemler.Count);

        api.IslemlerGetir = (_, _, _) => Task.FromException<IReadOnlyList<IslemDto>>(new TimeoutException(KasaZamanAsimlari.Ileti));
        await vm.SecFiltreZamanCommand.ExecuteAsync(Zaman(vm, "Bu ay"));

        Assert.Contains("zamanında yanıt vermedi", vm.YuklemeHatasi);
        Assert.Empty(vm.Islemler);
        Assert.Equal(0m, vm.FiltreToplam);
        Assert.Equal("", vm.FiltreOzet);
        Assert.False(vm.VeriVar);
    }

    [Fact]
    public async Task Yukleme_surerken_yukleniyor_gorunur_bos_baslik_yalniz_basarili_bos_yuklemede_ve_suzgece_gore()
    {
        var api = Api();
        var istekler = Bekleyenler(api);
        var vm = Vm(api);

        var yukleme = vm.YukleAsync();
        Assert.True(vm.ListeYukleniyor);
        Assert.False(vm.VeriVar);
        istekler[0].SetResult(Array.Empty<IslemDto>());
        await yukleme;

        Assert.False(vm.ListeYukleniyor);
        Assert.True(vm.VeriVar);
        Assert.Equal("Henüz işlem yok", vm.BosListeBasligi);

        var filtre = vm.SecFiltreKanalCommand.ExecuteAsync(Kanal(vm, "MEZAT"));
        istekler[1].SetResult(Array.Empty<IslemDto>());
        await filtre;
        Assert.Equal("Bu süzgeçte işlem yok", vm.BosListeBasligi);
        Assert.Contains("Tümü", vm.BosListeAciklamasi);
    }

    [Fact]
    public async Task Kayit_basarisi_liste_yenilenemese_de_bildirilir_form_hatasi_olarak_gosterilmez()
    {
        var api = Api();
        var vm = Vm(api);
        await vm.YukleAsync();
        api.YuklemeHatasi = new HttpRequestException();
        vm.DuzenTarih = new DateTime(2026, 7, 8);
        vm.DuzenCari = "Kira";
        vm.DuzenTutar = 100m;
        vm.DuzenKanal = "MEZAT";

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(1, api.IslemOlusturCagri);
        Assert.Equal("Gider kaydedildi.", vm.Mesaj);
        Assert.Null(vm.Hata);
        Assert.Contains("Sunucuya ulaşılamadı", vm.YuklemeHatasi);
        Assert.Equal("", vm.DuzenCari);                          // form temizlendi: aynı gider yeniden girilmez
    }

    [Fact]
    public async Task Silme_basarisi_bildirilir_ve_liste_yenilenir()
    {
        var api = Api();
        api.IslemlerListe = new[] { Islem(1, 10m) };
        var vm = Vm(api);
        await vm.YukleAsync();
        api.IslemlerListe = Array.Empty<IslemDto>();

        await vm.SilCommand.ExecuteAsync(vm.Islemler[0]);

        Assert.Equal(1, api.SonIslemSil);
        Assert.Equal("Kayıt silindi.", vm.Mesaj);
        Assert.Empty(vm.Islemler);
        Assert.True(vm.VeriVar);
    }

    // ---- eskimiş tam yükleme kaynakları ve salt okuma zaman aşımı ----

    private static readonly KanalDto Toptan = new(2, "TOPTAN", true, 1, 0m);
    private static readonly KanalDto Eski = new(3, "ESKİ KANAL", true, 2, 0m);
    private static string[] GiderKanalAdlari(IslemlerViewModel vm) => vm.GiderKanallari.Select(c => c.Ad).ToArray();

    [Fact]
    public async Task Eski_tam_yuklemenin_gec_gelen_kaynaklari_yeni_tam_yuklemenin_kanal_ciplerini_ezmez()
    {
        var api = Api();
        var eski = new TaskCompletionSource<IReadOnlyList<KanalDto>>();
        var cagri = 0;
        api.KanallarGetir = () => ++cagri == 1 ? eski.Task : Task.FromResult<IReadOnlyList<KanalDto>>(new[] { Mezat, Toptan });
        var vm = Vm(api);

        var ilk = vm.YukleAsync();
        await vm.YukleAsync();
        Assert.Equal(new[] { "MEZAT", "TOPTAN", "Ortak" }, GiderKanalAdlari(vm));

        eski.SetResult(new[] { Eski });
        await ilk;

        Assert.Equal(new[] { "MEZAT", "TOPTAN", "Ortak" }, GiderKanalAdlari(vm));
        Assert.DoesNotContain(vm.FiltreKanallari, c => c.Ad == "ESKİ KANAL");
        Assert.True(vm.VeriVar);
        Assert.False(vm.ListeYukleniyor);
    }

    [Fact]
    public async Task Tam_yukleme_surerken_suzgec_degisse_de_kaynaklar_uygulanir()
    {
        var api = Api();
        var kanallar = new TaskCompletionSource<IReadOnlyList<KanalDto>>();
        api.KanallarGetir = () => kanallar.Task;
        var vm = Vm(api);

        var tam = vm.YukleAsync();
        await vm.SecFiltreKanalCommand.ExecuteAsync(new SecimCipi("MEZAT"));   // yalnız liste isteği: kaynakları eskitmez
        kanallar.SetResult(new[] { Mezat, Toptan });
        await tam;

        Assert.Equal(new[] { "MEZAT", "TOPTAN", "Ortak" }, GiderKanalAdlari(vm));
        Assert.Equal(2, vm.FiltreDonemler.Count);
    }

    [Fact]
    public async Task Oturum_degisince_onceki_oturumun_gec_kaynaklari_uygulanmaz()
    {
        var api = Api();
        var kanallar = new TaskCompletionSource<IReadOnlyList<KanalDto>>();
        api.KanallarGetir = () => kanallar.Task;
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor };
        var vm = new IslemlerViewModel(api, auth: auth, zaman: new IslemEditorTests.SabitZaman(new DateOnly(2026, 7, 15)));

        var tam = vm.YukleAsync();
        auth.OturumSurumu++;
        kanallar.SetResult(new[] { Eski });
        await tam;

        Assert.Empty(vm.GiderKanallari);
        Assert.Empty(vm.FiltreDonemler);
        Assert.False(vm.ListeYukleniyor);
    }

    // ---- İŞ-01: en yeni kayıt ilk sırada ----

    [Fact]
    public async Task Son_islemler_en_yeniden_eskiye_siralanir()
    {
        var api = Api();
        api.IslemlerListe = new[]
        {
            Islem(1, 10m, new DateOnly(2026, 7, 1)),
            Islem(2, 20m, new DateOnly(2026, 7, 10)),
            Islem(3, 30m, new DateOnly(2026, 7, 5)),
        };
        var vm = Vm(api);

        await vm.YukleAsync();

        Assert.Equal(new[] { 2, 3, 1 }, vm.Islemler.Select(i => i.Id));
    }

    [Fact]
    public async Task Ayni_tarihli_islemler_kimlige_gore_azalan_siralanir()
    {
        var api = Api();
        api.IslemlerListe = new[]
        {
            Islem(1, 10m, new DateOnly(2026, 7, 5)),
            Islem(5, 20m, new DateOnly(2026, 7, 5)),
            Islem(3, 30m, new DateOnly(2026, 7, 5)),
        };
        var vm = Vm(api);

        await vm.YukleAsync();

        Assert.Equal(new[] { 5, 3, 1 }, vm.Islemler.Select(i => i.Id));
    }

    [Fact]
    public async Task Liste_okumasinin_zaman_asimi_islem_tamamlanmis_olabilir_demez()
    {
        var api = Api();
        api.YuklemeHatasi = new TimeoutException(KasaZamanAsimlari.Ileti);
        var vm = Vm(api);

        await vm.YukleAsync();

        Assert.Contains("zamanında yanıt vermedi", vm.YuklemeHatasi);
        Assert.DoesNotContain("tamamlanmış olabilir", vm.YuklemeHatasi);
    }

    [Fact]
    public async Task Tum_tarihler_ilk_sayfayi_gosterir_ve_eski_kayitlari_istekle_ekler()
    {
        var api = Api();
        api.IslemSayfasiGetir = imlec => Task.FromResult(imlec is null
            ? new IslemSayfasiDto([Islem(3, 30m, new DateOnly(2026, 7, 15)), Islem(2, 20m, new DateOnly(2026, 7, 14))], "20260714-2", true)
            : new IslemSayfasiDto([Islem(1, 10m, new DateOnly(2026, 7, 13))], null, false));
        var vm = Vm(api);

        await vm.YukleAsync();

        Assert.Equal([3, 2], vm.Islemler.Select(i => i.Id));
        Assert.True(vm.DevamVar);
        Assert.Equal(50m, vm.FiltreToplam);
        Assert.Contains("gösterilen toplam", vm.FiltreOzet);

        await vm.DahaFazlaYukleCommand.ExecuteAsync(null);

        Assert.Equal([3, 2, 1], vm.Islemler.Select(i => i.Id));
        Assert.Equal([null, "20260714-2"], api.IslemSayfaImlecleri);
        Assert.False(vm.DevamVar);
        Assert.Equal(60m, vm.FiltreToplam);
        Assert.Equal("Tüm tarihler · 3 işlem · toplam 60,00 ₺", vm.FiltreOzet);
    }

    [Fact]
    public async Task Eski_sayfa_gec_gelirse_yeni_suzgecin_listesine_karismaz()
    {
        var api = Api();
        var gecSayfa = new TaskCompletionSource<IslemSayfasiDto>();
        var ilk = 0;
        api.IslemSayfasiGetir = imlec => imlec is not null ? gecSayfa.Task : Task.FromResult(++ilk == 1
            ? new IslemSayfasiDto([Islem(3, 30m)], "20260708-3", true)
            : new IslemSayfasiDto([Islem(9, 90m)], null, false));
        var vm = Vm(api);
        await vm.YukleAsync();

        var eski = vm.DahaFazlaYukleCommand.ExecuteAsync(null);
        await vm.SecFiltreZamanCommand.ExecuteAsync(Zaman(vm, "Bu ay"));
        gecSayfa.SetResult(new([Islem(1, 10m)], null, false));
        await eski;

        Assert.Equal(9, Assert.Single(vm.Islemler).Id);
        Assert.Equal("Bu ay · 1 işlem · toplam 90,00 ₺", vm.FiltreOzet);
        Assert.False(vm.DahaFazlaYukleniyor);
    }
}
