using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core.Tests;

/// <summary>Çekler ekranının modeli (docs/specs/2026-10-01-cekler.md "Masaüstü ekranı"): süzgeçler sunucuya gider, hazır süzgeçler
/// (üst şerit, panel), satırın altında açılan ayrıntı, hareket formu varsayılanları, geri alma, aynı çek uyarısı ve bildirimden
/// açılış. Gün 25 Eylül 2026.</summary>
public class CekTakipViewModelTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 25);

    internal sealed class Sahte : ICekApi
    {
        public List<CekDto> Liste = [];
        public CekOzetDto Ozet = new(Bugun, new(2, 60_000m), new(1, 50_000m), new(1, 7_000m), new(0, 0m), new(2, 12_500.5m));
        public List<(string? Yon, string? Durum, string? Ara, DateOnly? Bas, DateOnly? Son)> Sorgular = [];
        public List<(int? Id, CekYaz Govde)> Kayitlar = [];
        public List<(int Id, CekHareketYaz Govde)> Hareketler = [];
        public List<(int Id, CekSilYaz Govde)> GeriAlmalar = [];
        public List<(int Id, CekSilYaz Govde)> Silmeler = [];
        /// <summary>Son istek kazanır testleri: ilk CeklerAsync çağrısının yanıtı bu kapı tamamlanana kadar bekler (ikinci ve
        /// sonraki çağrılar hemen sonuçlanır), yavaş/eski bir isteğin geç yanıtının sonraki seçimi ezmediğini doğrulamak için.</summary>
        public TaskCompletionSource? BeklemeKapisi;
        private bool _ilkCeklerCagrisiBeklendi;
        /// <summary>Verilirse sonraki CekKaydetAsync istek kaydedildikten sonra bu hatayla düşer (yanıtı kaybolan istek); bir kez.</summary>
        public Exception? KayitHatasi;
        public int OzetSayisi;

        public async Task<IReadOnlyList<CekDto>> CeklerAsync(string? yon = null, string? durum = null, string? ara = null, DateOnly? vadeBas = null, DateOnly? vadeSon = null)
        {
            Sorgular.Add((yon, durum, ara, vadeBas, vadeSon));
            if (BeklemeKapisi is { } kapi && !_ilkCeklerCagrisiBeklendi)
            { _ilkCeklerCagrisiBeklendi = true; await kapi.Task; }
            return Liste.Where(c => yon is null || c.Yon == yon).ToList();
        }
        public Task<CekDto> CekAsync(int id) => Task.FromResult(Liste.Single(c => c.Id == id));
        public Task<CekOzetDto> CekOzetAsync()
        {
            OzetSayisi++;
            return Task.FromResult(Ozet);
        }
        public Task<CekDto> CekKaydetAsync(int? id, CekYaz g)
        {
            Kayitlar.Add((id, g));
            if (KayitHatasi is { } hata)
            {
                KayitHatasi = null;
                return Task.FromException<CekDto>(hata);
            }
            return Task.FromResult(Cek(id ?? 99, g.Yon, g.Tutar, no: g.No, vade: g.VadeTarihi) with { Surum = g.Surum + 1 });
        }
        public Task CekSilAsync(int id, CekSilYaz g)
        {
            Silmeler.Add((id, g));
            return Task.CompletedTask;
        }
        public Task<CekDto> CekHareketEkleAsync(int id, CekHareketYaz g)
        {
            Hareketler.Add((id, g));
            var eski = Liste.Single(c => c.Id == id);
            return Task.FromResult(eski with
            {
                Surum = eski.Surum + 1,
                Kalan = eski.Kalan - g.Tutar,
                Hareketler = [.. eski.Hareketler, new CekHareketDto(50, eski.Hareketler.Count + 1, g.Tur, g.Tarih, g.Tutar, g.NetTutar, 1, g.Kanal, g.Karsi)],
            });
        }
        public Task<CekDto> CekHareketGeriAlAsync(int id, CekSilYaz g)
        {
            GeriAlmalar.Add((id, g));
            return Task.FromResult(Liste.Single(c => c.Id == id) with { Hareketler = [], Surum = g.Surum + 1 });
        }
    }

    internal static CekDto Cek(int id, string yon = CekYonleri.Alinan, decimal tutar = 50_000m, decimal? kalan = null, DateOnly? vade = null, string no = "12345",
        IReadOnlyList<CekHareketDto>? hareketler = null, IReadOnlyList<string>? izinli = null) =>
        new(id, 1, CekTurleri.Cek, yon, no, "Ziraat", yon == CekYonleri.Alinan ? "Ahmet Yılmaz" : "Mehmet Ticaret", tutar, vade ?? Bugun.AddDays(5),
            null, yon == CekYonleri.Verilen ? KanalEtiketleri.Ortak : null, false,
            yon == CekYonleri.Alinan ? CekKonumlari.Elde : null, null, CekDurumlari.Portfoyde, kalan ?? tutar,
            izinli ?? (yon == CekYonleri.Alinan
                ? [CekHareketTurleri.Tahsilat, CekHareketTurleri.Ciro, CekHareketTurleri.Kirdirma, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade]
                : [CekHareketTurleri.Odeme, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade]),
            hareketler ?? [], null);

    private static AuthViewModel Auth(Rol rol = Rol.Editor) => new(new SahteApi()) { AktifRol = rol };

    private static async Task<(CekTakipViewModel Vm, Sahte Api)> Vm(Rol rol = Rol.Editor, params CekDto[] cekler)
    {
        var api = new Sahte { Liste = [.. cekler] };
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0), new KanalDto(2, "PERAKENDE", true, 1, 0), new KanalDto(3, "ESKI", false, 2, 0)] };
        var vm = new CekTakipViewModel(api, finans, Auth(rol), new IslemEditorTests.SabitZaman(Bugun));
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Varsayilan_alinan_portfoyde_yuklenir_satir_vade_rozeti_ve_kalan_gosterir()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1, kalan: 30_000m), Cek(2, vade: Bugun.AddDays(-3)), Cek(3, CekYonleri.Verilen));
        Assert.Equal((CekYonleri.Alinan, CekSuzgecleri.Portfoyde, (string?)null, (DateOnly?)null, (DateOnly?)null), api.Sorgular.Single());
        Assert.Equal(new[] { 1, 2 }, vm.Cekler.Select(s => s.Veri.Id));
        Assert.Equal("30.09.2026 · 5 gün kaldı · Ahmet Yılmaz", vm.Cekler[0].Baslik);
        Assert.Equal("Çek Ziraat · 12345 · 50.000,00 ₺ · Kalan: 30.000,00 ₺ · portföyde · Elde", vm.Cekler[0].Ozet);
        Assert.Equal("22.09.2026 · 3 gün geçti · Ahmet Yılmaz", vm.Cekler[1].Baslik);
        Assert.Equal(new[] { "MEZAT", "PERAKENDE" }, vm.KasaSecenekleri);
        Assert.Equal(new[] { KanalEtiketleri.Ortak, "MEZAT", "PERAKENDE" }, vm.CekKasaSecenekleri);
        Assert.Equal("Portföydeki alınan: 60.000,00 ₺ (2 çek)", vm.PortfoyMetni);
        Assert.Equal("30 gün içinde ödenecek: 7.000,00 ₺ (1 çek)", vm.Verilen30Metni);
        Assert.Equal("Vadesi geçmiş, ödenmemiş: 12.500,50 ₺ (2 çek)", vm.VerilenGecmisMetni);
        Assert.True(vm.YonCipleri[0].Secili);
        Assert.True(vm.DurumCipleri[0].Secili);
    }

    [Fact]
    public void Karsiliksiz_durumda_icradaki_konum_da_gosterilir()
    {
        var cek = Cek(1, no: "1") with { Durum = CekDurumlari.Karsiliksiz, Konum = CekKonumlari.Icrada };
        var satir = new CekSatiri(cek, Bugun);
        Assert.Equal("Çek Ziraat · 1 · 50.000,00 ₺ · karşılıksız · İcrada", satir.Ozet);
    }

    [Fact]
    public async Task Yon_durum_arama_ve_hazir_suzgecler_sunucuya_gider()
    {
        var (vm, api) = await Vm();
        await vm.SecYonCommand.ExecuteAsync(vm.YonCipleri[1]);
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Portfoyde), (api.Sorgular[^1].Yon, api.Sorgular[^1].Durum));
        Assert.True(vm.YonCipleri[1].Secili);
        Assert.False(vm.YonCipleri[0].Secili);
        await vm.SecDurumCommand.ExecuteAsync(vm.DurumCipleri[2]);
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Kapanan), (api.Sorgular[^1].Yon, api.Sorgular[^1].Durum));
        vm.Ara = " ahmet ";
        await vm.AraCommand.ExecuteAsync(null);
        Assert.Equal("ahmet", api.Sorgular[^1].Ara);

        await vm.HazirSuzgecAsync(CekHazirSuzgec.Alinan30);
        Assert.Equal((CekYonleri.Alinan, CekSuzgecleri.Portfoyde, (string?)null, (DateOnly?)Bugun, (DateOnly?)Bugun.AddDays(30)), api.Sorgular[^1]);
        Assert.Equal("Vade 25.09.2026 – 25.10.2026", vm.VadeSuzgeci);
        await vm.HazirSuzgecAsync(CekHazirSuzgec.Verilen30);
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Portfoyde, (string?)null, (DateOnly?)Bugun, (DateOnly?)Bugun.AddDays(30)), api.Sorgular[^1]);
        await vm.HazirSuzgecAsync(CekHazirSuzgec.VadesiGecmis);
        Assert.Equal((CekYonleri.Alinan, CekSuzgecleri.Portfoyde, (string?)null, (DateOnly?)null, (DateOnly?)Bugun.AddDays(-1)), api.Sorgular[^1]);
        Assert.Equal("Vade 24.09.2026 ve öncesi", vm.VadeSuzgeci);
        vm.Ara = "mehmet";
        await vm.HazirSuzgecAsync(CekHazirSuzgec.VerilenVadesiGecmis);
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Portfoyde, (string?)null, (DateOnly?)null, (DateOnly?)Bugun.AddDays(-1)), api.Sorgular[^1]);
        Assert.Equal("Vade 24.09.2026 ve öncesi", vm.VadeSuzgeci);
        Assert.True(vm.YonCipleri[1].Secili);
        await vm.VadeSuzgeciniKaldirCommand.ExecuteAsync(null);
        Assert.False(vm.VadeSuzgeciVar);
        Assert.Null(api.Sorgular[^1].Son);
    }

    [Fact]
    public async Task Satira_tiklaninca_ayrinti_satirin_altinda_acilir_ikinci_tiklama_kapatir()
    {
        var (vm, _) = await Vm(Rol.Editor, Cek(1), Cek(2), Cek(3));
        vm.SecCommand.Execute(vm.Cekler[1]);
        Assert.Equal(2, vm.Acik!.Id);
        Assert.Equal(new[] { 1, 2 }, vm.OncekiSatirlar.Select(s => s.Veri.Id));
        Assert.Equal(new[] { 3 }, vm.SonrakiSatirlar.Select(s => s.Veri.Id));
        Assert.Equal(new[] { "Tahsilat", "Ciro", "Kırdırma", "Karşılıksız", "İade" }, vm.HareketCipleri.Select(c => c.Ad));
        vm.SecCommand.Execute(vm.Cekler[1]);
        Assert.Null(vm.Acik);
        Assert.Equal(new[] { 1, 2, 3 }, vm.OncekiSatirlar.Select(s => s.Veri.Id));
        Assert.Empty(vm.SonrakiSatirlar);
    }

    [Fact]
    public async Task Hareket_formu_bugun_kalan_ve_son_secilen_kasayla_acilir_kirdirmada_masraf_hesaplanir()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1, kalan: 30_000m), Cek(2));
        vm.SecCommand.Execute(vm.Cekler[0]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri[0]);
        Assert.Equal((Bugun.ToDateTime(TimeOnly.MinValue), 30_000m, "MEZAT", true, false), (vm.HareketTarihi, vm.HareketTutari, vm.HareketKasasi, vm.KasaGerekli, vm.KarsiGerekli));
        vm.HareketKasasi = "PERAKENDE";
        vm.HareketTutari = 10_000m;
        await vm.HareketKaydetCommand.ExecuteAsync(null);
        var (id, g) = Assert.Single(api.Hareketler);
        Assert.Equal((1, CekHareketTurleri.Tahsilat, 10_000m, "PERAKENDE", (string?)null, (decimal?)null, 1), (id, g.Tur, g.Tutar, g.Kanal, g.Karsi, g.NetTutar, g.Surum));
        Assert.NotEqual(Guid.Empty, g.IstekId);
        Assert.Equal("Tahsilat kaydedildi.", vm.Mesaj);
        Assert.Equal(20_000m, vm.Acik!.Kalan);

        vm.SecCommand.Execute(vm.Cekler[1]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri.Single(c => c.Kod == CekHareketTurleri.Kirdirma));
        Assert.Equal("PERAKENDE", vm.HareketKasasi);
        Assert.True(vm.NetGerekli);
        vm.NetTutar = 48_750m;
        Assert.Equal("Masraf: 1.250,00 ₺ (karşı tarafa Cari gider). Kasaya 48.750,00 ₺ girer.", vm.MasrafMetni);
    }

    [Fact]
    public async Task Verilen_cek_odemesinde_kasa_gonderilmez_donus_tutari_cirodan_gelir()
    {
        var ciro = new CekHareketDto(7, 1, CekHareketTurleri.Ciro, Bugun.AddDays(-2), 50_000m, null, 1, "MEZAT", "Veli");
        var (vm, api) = await Vm(Rol.Editor, Cek(1, CekYonleri.Verilen, 30_000m), Cek(2, kalan: 0m, hareketler: [ciro], izinli: [CekHareketTurleri.Donus]));
        await vm.SecYonCommand.ExecuteAsync(vm.YonCipleri[1]);
        vm.SecCommand.Execute(vm.Cekler.Single(s => s.Veri.Id == 1));
        vm.SecHareketCommand.Execute(vm.HareketCipleri[0]);
        Assert.False(vm.KasaGerekli);
        await vm.HareketKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.Hareketler.Single().Govde.Kanal);

        await vm.SecYonCommand.ExecuteAsync(vm.YonCipleri[0]);
        vm.SecCommand.Execute(vm.Cekler.Single(s => s.Veri.Id == 2));
        vm.SecHareketCommand.Execute(vm.HareketCipleri.Single());
        Assert.Equal((CekHareketTurleri.Donus, 50_000m, false), (vm.HareketTuru, vm.HareketTutari, vm.KasaGerekli));
        Assert.True(vm.GeriAlinabilir);
        await vm.GeriAlAsync();
        Assert.Equal((2, 1), (api.GeriAlmalar.Single().Id, api.GeriAlmalar.Single().Govde.Surum));
        Assert.Equal("Son hareket geri alındı.", vm.Mesaj);
    }

    [Fact]
    public async Task Ayni_cek_uyarisi_kaydetmeden_once_gosterilir_yine_de_kaydet_ayni_istek_kimligiyle_kaydeder()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1));
        vm.YeniCekCommand.Execute(null);
        Assert.Equal((CekYonleri.Alinan, CekTurleri.Cek, CekKonumlari.Elde, "Yeni çek / senet"), (vm.FormYon!.Kod, vm.FormTur!.Kod, vm.Konum!.Kod, vm.FormBasligi));
        vm.No = "12345";
        vm.Banka = " ziraat ";
        vm.Kisi = "Ayşe";
        vm.Tutar = 10_000m;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.Kayitlar);
        Assert.Equal((CekYonleri.Alinan, CekSuzgecleri.Hepsi, "12345"), (api.Sorgular[^1].Yon, api.Sorgular[^1].Durum, api.Sorgular[^1].Ara));
        Assert.StartsWith("Aynı yön, banka ve numarayla kayıtlı çek var: Ahmet Yılmaz · 50.000,00 ₺ · vade 30.09.2026.", vm.AyniCekUyarisi);

        // "Yine de kaydet"in yanıtı kaybolur (ağ hatası): form açık kalır, yeniden gönderim aynı istek kimliğini taşır.
        api.KayitHatasi = new HttpRequestException("bağlantı koptu");
        await vm.YineDeKaydetCommand.ExecuteAsync(null);
        var ilk = Assert.Single(api.Kayitlar).Govde;
        Assert.NotEqual(Guid.Empty, ilk.IstekId);
        Assert.True(vm.FormAcik);
        Assert.Equal(Yurutucu.KayitBaglantiIletisi, vm.Hatalar.Genel);
        await vm.YineDeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.Kayitlar.Count);
        var (id, g) = api.Kayitlar[1];
        Assert.Equal(ilk.IstekId, g.IstekId);
        Assert.Equal(((int?)null, "ziraat", "Ayşe", (string?)null, CekKonumlari.Elde), (id, g.Banka, g.Kisi, g.Kanal, g.Konum));
        Assert.False(vm.FormAcik);
        Assert.Null(vm.AyniCekUyarisi);
        Assert.Equal("Çek kaydedildi.", vm.Mesaj);
    }

    [Fact]
    public async Task Numara_bossa_ayni_cek_denetimi_ve_kayit_yapilmaz_alan_hatasi_yazilir()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1));
        var sorguSayisi = api.Sorgular.Count;
        vm.YeniCekCommand.Execute(null);
        vm.No = "   ";
        vm.Banka = "Ziraat";
        vm.Kisi = "Deneme";
        vm.Tutar = 1_000m;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(sorguSayisi, api.Sorgular.Count);
        Assert.Empty(api.Kayitlar);
        Assert.Null(vm.AyniCekUyarisi);
        Assert.Equal("Çek / senet numarası boş olamaz.", vm.Hatalar[nameof(vm.No)]);
    }

    [Fact]
    public async Task Kirdirmada_net_tutar_cek_tutarini_asarsa_uyari_gosterilir()
    {
        var (vm, _) = await Vm(Rol.Editor, Cek(1, kalan: 30_000m));
        vm.SecCommand.Execute(vm.Cekler[0]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri.Single(c => c.Kod == CekHareketTurleri.Kirdirma));
        vm.NetTutar = vm.HareketTutari + 1;
        Assert.Equal("Hesaba geçen tutar çek tutarını aşamaz.", vm.MasrafMetni);
    }

    [Fact]
    public async Task Suzgec_tiklamasi_suren_eski_istegi_ezer_son_secim_kazanir()
    {
        var api = new Sahte { Liste = [Cek(1), Cek(2, CekYonleri.Verilen)] };
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };
        var vm = new CekTakipViewModel(api, finans, Auth(), new IslemEditorTests.SabitZaman(Bugun));
        var kapi = new TaskCompletionSource();
        api.BeklemeKapisi = kapi;
        var ilkYukleme = vm.YukleAsync(); // Alinan/Portfoyde; CeklerAsync yanıtı bekliyor, henüz bitmedi.
        await vm.SecYonCommand.ExecuteAsync(vm.YonCipleri[1]); // Verilen'e geçiş sırada atlanmaz, hemen sonuçlanır.
        Assert.Equal(CekYonleri.Verilen, vm.Yon);
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Portfoyde), (api.Sorgular[^1].Yon, api.Sorgular[^1].Durum));
        Assert.Equal(new[] { 2 }, vm.Cekler.Select(s => s.Veri.Id));
        kapi.SetResult();
        await ilkYukleme;
        // Eski (Alinan) isteğin geç yanıtı yeni seçimi (Verilen) ezmez.
        Assert.Equal(CekYonleri.Verilen, vm.Yon);
        Assert.Equal(new[] { 2 }, vm.Cekler.Select(s => s.Veri.Id));
        Assert.True(vm.YonCipleri[1].Secili);
        Assert.True(vm.VeriHazir);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Bildirimden_acilis_suren_liste_yuklemesini_ezer_atlanmaz()
    {
        var verilen = Cek(5, CekYonleri.Verilen);
        var api = new Sahte { Liste = [Cek(1), verilen] };
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };
        var vm = new CekTakipViewModel(api, finans, Auth(), new IslemEditorTests.SabitZaman(Bugun));
        var kapi = new TaskCompletionSource();
        api.BeklemeKapisi = kapi;
        var ilkYukleme = vm.YukleAsync(); // Sayfa açılışı (Alinan/Portfoyde); CeklerAsync yanıtı bekliyor.
        await vm.CekIcinYukleAsync(5); // Aynı anda bildirimden gelen açılış; sürerken atlanmaz, hemen sonuçlanır.
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Hepsi), (vm.Yon, vm.Durum));
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Hepsi), (api.Sorgular[^1].Yon, api.Sorgular[^1].Durum));
        Assert.True(vm.VeriHazir);
        Assert.Null(vm.Hata);
        Assert.True(vm.IdIleSec(5));
        Assert.Equal(5, vm.Acik!.Id);
        kapi.SetResult();
        await ilkYukleme; // Eskiyen ilk istek tamamlanır ama bildirimin süzgecini ve seçimini geri almaz.
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Hepsi), (vm.Yon, vm.Durum));
        Assert.Equal(5, vm.Acik!.Id);
    }

    [Fact]
    public async Task Duzeltme_formu_surum_degisince_kapanir_kaydetme_yakalanan_surumu_kullanir()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1));
        vm.SecCommand.Execute(vm.Cekler[0]);
        vm.DuzeltCommand.Execute(null);
        Assert.True(vm.FormAcik);

        // Kaydetme listeden değil, Düzelt'te yakalanan sürümle gider (liste ayrı bir yoldan değişse de).
        vm.Cekler[0] = new CekSatiri(vm.Cekler[0].Veri with { Surum = 99 }, Bugun);
        vm.Tutar = 60_000m;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(1, Assert.Single(api.Kayitlar).Govde.Surum);

        // Form yeniden açılır (Acik, kaydın karşılığıdır); sunucu sürümü başka bir işlemle değişir, yenilenince form kapanır.
        vm.DuzeltCommand.Execute(null);
        Assert.True(vm.FormAcik);
        api.Liste[0] = api.Liste[0] with { Surum = 99 };
        await vm.YukleAsync();
        Assert.False(vm.FormAcik);
        Assert.Equal("Çek başka bir işlemle değişti; formu yeniden açın.", vm.Mesaj);
    }

    /// <summary>Hareket formu açıkken açık çek başka bir işlemle değişirse (sürüm farkı) form iletiyle kapanır (görev 14-19
    /// incelemesi; Yansit'teki sürüm denetimi, düzeltme formununkinden ayrı).</summary>
    [Fact]
    public async Task Hareket_formu_acikken_cek_baska_islemle_degisince_iletiyle_kapanir()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1, kalan: 30_000m));
        vm.SecCommand.Execute(vm.Cekler[0]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri[0]);
        Assert.True(vm.HareketFormuAcik);

        api.Liste[0] = api.Liste[0] with { Surum = 99 };
        await vm.YukleAsync();

        Assert.False(vm.HareketFormuAcik);
        Assert.Equal("Çek başka bir işlemle değişti; hareketi yeniden girin.", vm.Mesaj);
    }

    /// <summary>Hareket formu açıkken açık çek yenilenen listeden tamamen düşerse (ör. süzgeç artık onu göstermiyor) form
    /// iletisiz kapanmaz (görev 14-19 incelemesi): "Çek listede artık yok; hareket kaydedilmedi." söylenir.</summary>
    [Fact]
    public async Task Hareket_formu_acikken_cek_listeden_duserse_iletiyle_kapanir()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1, kalan: 30_000m));
        vm.SecCommand.Execute(vm.Cekler[0]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri[0]);
        Assert.True(vm.HareketFormuAcik);

        api.Liste.Clear();
        await vm.YukleAsync();

        Assert.False(vm.HareketFormuAcik);
        Assert.Null(vm.Acik);
        Assert.Equal("Çek listede artık yok; hareket kaydedilmedi.", vm.Mesaj);
    }

    [Fact]
    public async Task Pasif_kasali_verilen_cekte_duzelt_secenekleri_kasayi_korur_yeni_cekte_yalniz_aktifler_kalir()
    {
        var cek = Cek(1, CekYonleri.Verilen) with { Kanal = "ESKI" };
        var (vm, _) = await Vm(Rol.Editor, cek);
        await vm.SecYonCommand.ExecuteAsync(vm.YonCipleri[1]);
        vm.SecCommand.Execute(vm.Cekler[0]);
        vm.DuzeltCommand.Execute(null);
        Assert.Contains("ESKI", vm.CekKasaSecenekleri);
        Assert.Equal("ESKI", vm.CekKasasi);

        vm.YeniCekCommand.Execute(null);
        Assert.DoesNotContain("ESKI", vm.CekKasaSecenekleri);
        Assert.Null(vm.CekKasasi);
    }

    [Fact]
    public async Task Acik_cege_IdIleSec_cagrilinca_detay_acik_kalir()
    {
        var (vm, _) = await Vm(Rol.Editor, Cek(1), Cek(2));
        vm.SecCommand.Execute(vm.Cekler[0]);
        Assert.Equal(1, vm.Acik!.Id);
        Assert.True(vm.IdIleSec(1));
        Assert.Equal(1, vm.Acik!.Id);
    }

    [Fact]
    public async Task Tam_tahsil_sonrasi_cek_suzgecten_duser_ozet_yenilenir()
    {
        var kapanan = Cek(1, kalan: 10_000m) with { Durum = CekDurumlari.TahsilEdildi };
        var (vm, api) = await Vm(Rol.Editor, kapanan);
        var yeniOzet = new CekOzetDto(Bugun, new(5, 500_000m), new(2, 20_000m), new(1, 7_000m), new(0, 0m), new(1, 3_000m));
        api.Ozet = yeniOzet;
        var degisenler = new List<string?>();
        vm.PropertyChanged += (_, e) => degisenler.Add(e.PropertyName);
        vm.SecCommand.Execute(vm.Cekler[0]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri[0]); // Tahsilat
        vm.HareketTutari = 10_000m;
        await vm.HareketKaydetCommand.ExecuteAsync(null);
        Assert.Empty(vm.Cekler);
        Assert.Null(vm.Acik);
        Assert.Equal("Tahsilat kaydedildi; seçili süzgeç (Alınan · Portföyde) dışında kaldığı için listede görünmüyor.", vm.Mesaj);
        Assert.Equal(yeniOzet, vm.Ozet);
        Assert.Equal("Portföydeki alınan: 500.000,00 ₺ (5 çek)", vm.PortfoyMetni);
        Assert.Contains(nameof(vm.VerilenGecmisMetni), degisenler);
        Assert.Equal("Vadesi geçmiş, ödenmemiş: 3.000,00 ₺ (1 çek)", vm.VerilenGecmisMetni);
    }

    [Fact]
    public async Task Kayit_geri_alma_ve_silmeden_sonra_ozet_yenilenir_suzgece_uyan_satir_kalir()
    {
        var hareket = new CekHareketDto(7, 1, CekHareketTurleri.Tahsilat, Bugun.AddDays(-1), 1_000m, null, 1, "MEZAT", null);
        var (vm, api) = await Vm(Rol.Editor, Cek(1, kalan: 49_000m, hareketler: [hareket]), Cek(2));
        var sayi = api.OzetSayisi;
        vm.SecCommand.Execute(vm.Cekler[0]);
        await vm.GeriAlAsync();
        Assert.Equal(sayi + 1, api.OzetSayisi);
        Assert.Equal("Son hareket geri alındı.", vm.Mesaj);
        Assert.Equal(1, vm.Acik!.Id);

        vm.DuzeltCommand.Execute(null);
        vm.Kisi = "Ali";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(sayi + 2, api.OzetSayisi);
        Assert.Equal("Çek güncellendi.", vm.Mesaj);
        Assert.Contains(vm.Cekler, s => s.Veri.Id == 1);

        await vm.SilAsync();
        Assert.Equal(sayi + 3, api.OzetSayisi);
        Assert.Equal(new[] { 2 }, vm.Cekler.Select(s => s.Veri.Id));
    }

    [Fact]
    public async Task Arama_etkinken_uymayan_yeni_cek_listeye_girmez_mesaj_soylenir()
    {
        var (vm, _) = await Vm(Rol.Editor, Cek(1, no: "12345"));
        vm.Ara = "12345";
        await vm.AraCommand.ExecuteAsync(null);
        Assert.Equal(new[] { 1 }, vm.Cekler.Select(s => s.Veri.Id));

        vm.YeniCekCommand.Execute(null);
        vm.No = "99999"; // arama metnine ("12345") uymaz
        vm.Banka = "Ziraat";
        vm.Kisi = "Deneme";
        vm.Tutar = 1_000m;
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(new[] { 1 }, vm.Cekler.Select(s => s.Veri.Id));
        Assert.Equal("Çek kaydedildi; seçili süzgeç (Alınan · Portföyde) dışında kaldığı için listede görünmüyor.", vm.Mesaj);
    }

    [Fact]
    public async Task Arama_etkinken_uyan_cek_duzeltilip_uymaz_olunca_duser()
    {
        var (vm, _) = await Vm(Rol.Editor, Cek(1, no: "12345"));
        vm.Ara = "12345";
        await vm.AraCommand.ExecuteAsync(null);
        Assert.Equal(new[] { 1 }, vm.Cekler.Select(s => s.Veri.Id));

        vm.SecCommand.Execute(vm.Cekler[0]);
        vm.DuzeltCommand.Execute(null);
        vm.No = "00000"; // artık arama metnine uymaz
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Empty(vm.Cekler);
        Assert.Null(vm.Acik);
        Assert.Equal("Çek güncellendi; seçili süzgeç (Alınan · Portföyde) dışında kaldığı için listede görünmüyor.", vm.Mesaj);
    }

    [Fact]
    public async Task Vadesi_daha_gec_olan_yeni_cek_listenin_dogru_yerine_girer()
    {
        var (vm, _) = await Vm(Rol.Editor, Cek(1, vade: Bugun.AddDays(5)), Cek(2, vade: Bugun.AddDays(10)));
        vm.YeniCekCommand.Execute(null);
        vm.No = "999";
        vm.Banka = "Ziraat";
        vm.Kisi = "Deneme";
        vm.Tutar = 1_000m;
        vm.Vade = Bugun.AddDays(20).ToDateTime(TimeOnly.MinValue);
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(new[] { 1, 2, 99 }, vm.Cekler.Select(s => s.Veri.Id));
    }

    [Fact]
    public async Task Vadesi_degistirilen_cek_yeni_yerine_tasinir()
    {
        var (vm, _) = await Vm(Rol.Editor, Cek(1, vade: Bugun.AddDays(5)), Cek(2, vade: Bugun.AddDays(10)), Cek(3, vade: Bugun.AddDays(15)));
        vm.SecCommand.Execute(vm.Cekler[0]); // Id 1
        vm.DuzeltCommand.Execute(null);
        vm.Vade = Bugun.AddDays(20).ToDateTime(TimeOnly.MinValue);
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(new[] { 2, 3, 1 }, vm.Cekler.Select(s => s.Veri.Id));
    }

    [Fact]
    public async Task Verilen_yeni_cek_kasasiyla_ve_konumsuz_gider()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1));
        vm.YeniCekCommand.Execute(null);
        vm.FormYon = vm.YonSecenekleri[1];
        vm.FormTur = vm.TurSecenekleri[1];   // senette banka boş olabilir (sunucu çekte bankayı ister)
        vm.No = "999";
        vm.Kisi = "Mehmet";
        vm.Tutar = 5_000m;
        vm.CekKasasi = KanalEtiketleri.Ortak;
        await vm.KaydetCommand.ExecuteAsync(null);
        var g = Assert.Single(api.Kayitlar).Govde;
        Assert.Equal((CekYonleri.Verilen, KanalEtiketleri.Ortak, (string?)null, (string?)null), (g.Yon, g.Kanal, g.Konum, g.Banka));
        // Liste "Alınan" süzgecinde: yeni verilen çek listeye eklenmez, neden görünmediği söylenir.
        Assert.Equal(new[] { 1 }, vm.Cekler.Select(s => s.Veri.Id));
        Assert.Equal("Çek kaydedildi; seçili süzgeç (Alınan · Portföyde) dışında kaldığı için listede görünmüyor.", vm.Mesaj);
    }

    [Fact]
    public async Task Bildirimden_gelen_cek_yonune_ve_hepsi_suzgecine_gecip_acilir_alici_gecemez()
    {
        var verilen = Cek(5, CekYonleri.Verilen);
        var (vm, api) = await Vm(Rol.Editor, Cek(1), verilen);
        await vm.CekIcinYukleAsync(5);
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Hepsi), (api.Sorgular[^1].Yon, api.Sorgular[^1].Durum));
        Assert.True(vm.DurumCipleri.Single(c => c.Kod == CekSuzgecleri.Hepsi).Secili);
        Assert.True(vm.IdIleSec(5));
        Assert.Equal(5, vm.Acik!.Id);
        Assert.False(vm.IdIleSec(42));
        Assert.Equal("Çek bulunamadı. Listeyi yenileyip tekrar deneyin.", vm.Hata);
        var (alici, _) = await Vm(Rol.Alici, Cek(1));
        Assert.False(alici.IdIleSec(1));
    }

    [Fact]
    public async Task Izleyici_hareket_ve_kayit_gonderemez()
    {
        var (vm, api) = await Vm(Rol.Izleyici, Cek(1));
        vm.SecCommand.Execute(vm.Cekler[0]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri[0]);
        await vm.HareketKaydetCommand.ExecuteAsync(null);
        vm.YeniCekCommand.Execute(null);
        vm.No = "1";
        vm.Kisi = "X";
        vm.Tutar = 1m;
        await vm.KaydetCommand.ExecuteAsync(null);
        await vm.SilAsync();
        Assert.Empty(api.Hareketler);
        Assert.Empty(api.Kayitlar);
        Assert.Empty(api.Silmeler);
    }

    [Fact]
    public async Task Panel_ozeti_dort_satiri_bicimler()
    {
        var api = new Sahte();
        var vm = new CekOzetViewModel(api, Auth());
        var degisenler = new List<string?>();
        vm.PropertyChanged += (_, e) => degisenler.Add(e.PropertyName);
        await vm.YukleAsync();
        Assert.Equal(("30 gün içinde tahsil edilecek: 50.000,00 ₺ (1 çek)", "30 gün içinde ödenecek: 7.000,00 ₺ (1 çek)", "Vadesi geçmiş, tahsil edilmemiş: 0,00 ₺ (0 çek)",
                "Vadesi geçmiş, ödenmemiş: 12.500,50 ₺ (2 çek)"),
            (vm.Alinan30Metni, vm.Verilen30Metni, vm.GecmisMetni, vm.VerilenGecmisMetni));
        Assert.Contains(nameof(vm.VerilenGecmisMetni), degisenler);
        Assert.True(vm.VeriHazir);
    }
}
