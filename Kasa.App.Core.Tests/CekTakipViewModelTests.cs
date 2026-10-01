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
        public CekOzetDto Ozet = new(Bugun, new(2, 60_000m), new(1, 50_000m), new(1, 7_000m), new(0, 0m));
        public List<(string? Yon, string? Durum, string? Ara, DateOnly? Bas, DateOnly? Son)> Sorgular = [];
        public List<(int? Id, CekYaz Govde)> Kayitlar = [];
        public List<(int Id, CekHareketYaz Govde)> Hareketler = [];
        public List<(int Id, CekSilYaz Govde)> GeriAlmalar = [];
        public List<(int Id, CekSilYaz Govde)> Silmeler = [];

        public Task<IReadOnlyList<CekDto>> CeklerAsync(string? yon = null, string? durum = null, string? ara = null, DateOnly? vadeBas = null, DateOnly? vadeSon = null)
        {
            Sorgular.Add((yon, durum, ara, vadeBas, vadeSon));
            return Task.FromResult<IReadOnlyList<CekDto>>(Liste.Where(c => yon is null || c.Yon == yon).ToList());
        }
        public Task<CekDto> CekAsync(int id) => Task.FromResult(Liste.Single(c => c.Id == id));
        public Task<CekOzetDto> CekOzetAsync() => Task.FromResult(Ozet);
        public Task<CekDto> CekKaydetAsync(int? id, CekYaz g)
        {
            Kayitlar.Add((id, g));
            return Task.FromResult(Cek(id ?? 99, g.Yon, g.Tutar, no: g.No) with { Surum = g.Surum + 1 });
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
        Assert.True(vm.YonCipleri[0].Secili);
        Assert.True(vm.DurumCipleri[0].Secili);
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
        await vm.YineDeKaydetCommand.ExecuteAsync(null);
        var (id, g) = Assert.Single(api.Kayitlar);
        Assert.Equal(((int?)null, "ziraat", "Ayşe", (string?)null, CekKonumlari.Elde), (id, g.Banka, g.Kisi, g.Kanal, g.Konum));
        Assert.False(vm.FormAcik);
        Assert.Null(vm.AyniCekUyarisi);
        Assert.Equal("Çek kaydedildi.", vm.Mesaj);
    }

    [Fact]
    public async Task Verilen_yeni_cek_kasasiyla_ve_konumsuz_gider()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1));
        vm.YeniCekCommand.Execute(null);
        vm.FormYon = vm.YonSecenekleri[1];
        vm.No = "999";
        vm.Kisi = "Mehmet";
        vm.Tutar = 5_000m;
        vm.CekKasasi = KanalEtiketleri.Ortak;
        await vm.KaydetCommand.ExecuteAsync(null);
        var g = Assert.Single(api.Kayitlar).Govde;
        Assert.Equal((CekYonleri.Verilen, KanalEtiketleri.Ortak, (string?)null, (string?)null), (g.Yon, g.Kanal, g.Konum, g.Banka));
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
    public async Task Panel_ozeti_uc_satiri_bicimler()
    {
        var api = new Sahte();
        var vm = new CekOzetViewModel(api, Auth());
        await vm.YukleAsync();
        Assert.Equal(("30 gün içinde tahsil edilecek: 50.000,00 ₺ (1 çek)", "30 gün içinde ödenecek: 7.000,00 ₺ (1 çek)", "Vadesi geçmiş, tahsil edilmemiş: 0,00 ₺ (0 çek)"),
            (vm.Alinan30Metni, vm.Verilen30Metni, vm.GecmisMetni));
        Assert.True(vm.VeriHazir);
    }
}
