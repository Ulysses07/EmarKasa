using System.Net;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core.Tests;

/// <summary>Verilen öncelikli Çekler ekranı ve toplu giriş (docs/specs/2026-11-25-cekler-verilen-ve-toplu.md). Gün 25 Eylül 2026.</summary>
public class CekTopluTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 25);

    private static async Task<(CekTakipViewModel Vm, CekTakipViewModelTests.Sahte Api)> Kur(params CekDto[] cekler)
    {
        var api = new CekTakipViewModelTests.Sahte { Liste = [.. cekler] };
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0), new KanalDto(2, "PERAKENDE", true, 1, 0)] };
        var vm = new CekTakipViewModel(api, finans, new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor }, new IslemEditorTests.SabitZaman(Bugun));
        await vm.YukleAsync();
        return (vm, api);
    }

    private static async Task<(CekTakipViewModel Vm, CekTakipViewModelTests.Sahte Api)> KurToplu(params CekDto[] cekler)
    {
        var (vm, api) = await Kur(cekler);
        await vm.TopluAcCommand.ExecuteAsync(null);
        vm.TopluBanka = "Ziraat";
        vm.TopluKasa = "MEZAT";
        vm.TopluKisi = "Mehmet Ticaret";
        return (vm, api);
    }

    private static void Seri(CekTakipViewModel vm, int adet = 3, string no = "0012345", decimal tutar = 10_000m, DateOnly? vade = null, string aralik = "1")
    {
        vm.SeriAdet = adet.ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.SeriIlkNo = no;
        vm.SeriTutar = tutar;
        vm.SeriIlkVade = (vade ?? new DateOnly(2027, 1, 31)).ToDateTime(TimeOnly.MinValue);
        vm.SeriAralik = aralik;
        vm.SeriEkleCommand.Execute(null);
    }

    // ---- Verilen öncelikli ----

    [Fact]
    public async Task Ekran_verilenle_acilir_yeni_cek_ve_toplu_giris_verilen_yonuyle_baslar()
    {
        var (vm, api) = await Kur(CekTakipViewModelTests.Cek(1, CekYonleri.Verilen), CekTakipViewModelTests.Cek(2));
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Portfoyde), (api.Sorgular.Single().Yon, api.Sorgular.Single().Durum));
        Assert.Equal([1], vm.Cekler.Select(s => s.Veri.Id));
        Assert.Equal([CekYonleri.Verilen, CekYonleri.Alinan], vm.YonCipleri.Select(c => c.Kod));
        Assert.True(vm.YonCipleri[0].Secili);
        await vm.YeniCekCommand.ExecuteAsync(null);
        Assert.Equal(CekYonleri.Verilen, vm.FormYon!.Kod);
        await vm.TopluAcCommand.ExecuteAsync(null);
        Assert.Equal((CekYonleri.Verilen, CekTurleri.Cek, true), (vm.TopluYon!.Kod, vm.TopluTur!.Kod, vm.TopluVerilen));
    }

    [Fact]
    public async Task Verilen_cek_odenecek_alinan_portfoyde_yazilir()
    {
        var (vm, _) = await Kur(CekTakipViewModelTests.Cek(1, CekYonleri.Verilen));
        Assert.Equal("Ödenecek", vm.DurumCipleri[0].Ad);
        Assert.Equal("Çek Ziraat · 12345 · 50.000,00 ₺ · ödenecek", vm.Cekler[0].Ozet);
        vm.SecCommand.Execute(vm.Cekler[0]);
        Assert.Contains("· ödenecek", vm.AcikOzet);
        await vm.SecYonCommand.ExecuteAsync(vm.YonCipleri[1]);
        Assert.Equal("Portföyde", vm.DurumCipleri[0].Ad);
        Assert.Equal("Kapananlar", vm.DurumCipleri[2].Ad);
    }

    [Fact]
    public async Task Odenecek_toplam_gosterilir_suzgeci_acar_eski_sunucuda_bos_kalir()
    {
        var (vm, api) = await Kur();
        Assert.Equal("", vm.VerilenPortfoyMetni);
        api.Ozet = api.Ozet with { PortfoydekiVerilen = new(4, 81_000m) };
        await vm.HazirSuzgecAsync(CekHazirSuzgec.VerilenPortfoy);
        Assert.Equal("Ödenecek toplam: 81.000,00 ₺ (4 çek)", vm.VerilenPortfoyMetni);
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Portfoyde, (DateOnly?)null, (DateOnly?)null), (vm.Yon, vm.Durum, vm.VadeBas, vm.VadeSon));
    }

    // ---- Seri ----

    [Fact]
    public async Task Seri_numarayi_artirir_bastaki_sifiri_korur_ay_sonu_tasmaz()
    {
        var (vm, _) = await KurToplu();
        Seri(vm, adet: 6);
        Assert.Equal(["0012345", "0012346", "0012347", "0012348", "0012349", "0012350"], vm.TopluSatirlar.Select(s => s.No));
        Assert.Equal(["31.01", "28.02", "31.03", "30.04", "31.05", "30.06"], vm.TopluSatirlar.Select(s => s.Vade.ToString("dd.MM", System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal([1, 2, 3, 4, 5, 6], vm.TopluSatirlar.Select(s => s.Sira));
        Assert.Equal("6 çek · toplam 60.000,00 ₺", vm.TopluToplam);
        Assert.Equal(("", "", 0m), (vm.SeriAdet, vm.SeriIlkNo, vm.SeriTutar));
        vm.TopluSatirSilCommand.Execute(vm.TopluSatirlar[1]);
        Assert.Equal([1, 2, 3, 4, 5], vm.TopluSatirlar.Select(s => s.Sira));
        Assert.Equal("5 çek · toplam 50.000,00 ₺", vm.TopluToplam);
    }

    [Theory]
    [InlineData("A-99", 2, "A-99|A-100")]
    [InlineData("99999999999999999999", 2, "99999999999999999999|100000000000000000000")]
    [InlineData("SERİ", 1, "SERİ")]
    public void Seri_numaralari(string ilk, int adet, string beklenen) =>
        Assert.Equal(beklenen.Split('|'), CekSerisi.Uret(adet, ilk, "", 1m, Bugun, 1).Select(t => t.No));

    [Fact]
    public async Task Seri_hatalari_alanin_altinda_satir_eklenmez()
    {
        var (vm, _) = await KurToplu();
        var gosterim = 0;
        vm.TopluHatalari.GosterIstendi += (_, _) => gosterim++;
        vm.SeriAdet = "0";
        vm.SeriAralik = "x";
        vm.SeriEkleCommand.Execute(null);
        Assert.Equal("Adet 1 ile 200 arasında olmalı.", vm.TopluHatalari[nameof(vm.SeriAdet)]);
        Assert.Equal("İlk numarayı yazın.", vm.TopluHatalari[nameof(vm.SeriIlkNo)]);
        Assert.Equal("Tutar sıfırdan büyük olmalı.", vm.TopluHatalari[nameof(vm.SeriTutar)]);
        Assert.Equal("Aralık 1 ile 24 ay arasında olmalı.", vm.TopluHatalari[nameof(vm.SeriAralik)]);
        Seri(vm, adet: 2, no: "ABC");
        Assert.Equal("İlk numara rakamla bitmeli: seri onu birer artırır.", vm.TopluHatalari[nameof(vm.SeriIlkNo)]);
        Assert.Empty(vm.TopluSatirlar);
        Assert.Equal(2, gosterim);
        Seri(vm, adet: 200);
        Seri(vm, adet: 1);
        Assert.Equal(200, vm.TopluSatirlar.Count);
        Assert.Equal("Bir seferde en çok 200 çek girilir; 200 satır var, 1 satır eklenemedi.", vm.TopluHatalari.Genel);
    }

    // ---- Yapıştırma ----

    [Fact]
    public void Baslikli_yapistirma_sutunlari_basliga_gore_bulur()
    {
        var s = CekYapistirma.Coz("Vade Tarihi\tTUTAR\tÇek No\tFirma\r\n31.01.2027\t12.500,00 ₺\t0001\tAli Ticaret\r\n\r\n2027-2-5\t1.250.000\t2\t\r\n", "Ortak Kişi");
        Assert.Empty(s.Hatalar);
        Assert.Equal([new CekTaslagi("0001", "Ali Ticaret", 12_500m, new(2027, 1, 31)), new CekTaslagi("2", "", 1_250_000m, new(2027, 2, 5))], s.Satirlar);
    }

    [Theory]
    [InlineData("12500", 12500)]
    [InlineData("12500,5", 12500.5)]
    [InlineData("12500.50", 12500.5)]
    [InlineData("12.500", 12500)]
    [InlineData("12.500,00", 12500)]
    [InlineData("12,500.00", 12500)]
    [InlineData("1.250.000", 1250000)]
    [InlineData("1.250.000,75 TL", 1250000.75)]
    public void Basliksiz_yapistirma_sabit_sirali_tutar_bicimleri(string metin, double beklenen)
    {
        var s = CekYapistirma.Coz($"7;Ali;{metin};5/3/2027", "");
        Assert.Empty(s.Hatalar);
        Assert.Equal(new CekTaslagi("7", "Ali", (decimal)beklenen, new(2027, 3, 5)), Assert.Single(s.Satirlar));
        Assert.Equal((decimal)beklenen, Assert.Single(CekYapistirma.Coz($"7\t{metin}\t05-03-2027", "Ortak").Satirlar).Tutar);
    }

    [Theory]
    [InlineData("7\tAli\t12,500\t31.01.2027", "Satır 1: tutar belirsiz (\"12,500\"): 12500 ise binlik ayırıcı kullanmadan yazın, kuruşluysa iki hane yazın.")]
    [InlineData("7\tAli\tonbin\t31.01.2027", "Satır 1: tutar okunamadı (\"onbin\").")]
    [InlineData("7\tAli\t-5\t31.01.2027", "Satır 1: tutar sıfırdan büyük olmalı.")]
    [InlineData("7\tAli\t100\t31.02.2027", "Satır 1: vade okunamadı (\"31.02.2027\"); 31.01.2027 gibi yazın.")]
    [InlineData("\tAli\t100\t31.01.2027", "Satır 1: numara boş.")]
    [InlineData("7\t\t100\t31.01.2027", "Satır 1: kişi boş (üstteki ortak kişiyi de doldurabilirsiniz).")]
    [InlineData("7\t100", "Satır 1: 4 sütun bekleniyor (Numara, Kişi, Tutar, Vade), 2 sütun var.")]
    [InlineData("No\tTutar\tVade\tBanka\n7\t100\t31.01.2027\tZiraat", "Satır 1: \"Banka\" sütunu tanınmadı. Numara, Kişi, Tutar ve Vade sütunları okunur; banka ve kasa üstteki alanlardan seçilir.")]
    [InlineData("No\tTutar\n7\t100", "Satır 1: Vade sütunu yok.")]
    [InlineData("No\tTutar\tVade\tTarih\n7\t100\t31.01.2027\t31.01.2027", "Satır 1: \"Tarih\" sütunu iki kez var.")]
    [InlineData("No\tTutar\tVade", "Başlığın altında satır yok.")]
    [InlineData("  \n ", "Yapıştırılacak satır yok.")]
    public void Yapistirma_hatalari(string metin, string hata)
    {
        var s = CekYapistirma.Coz(metin, "");
        Assert.Equal([hata], s.Hatalar);
        Assert.Empty(s.Satirlar);
    }

    [Fact]
    public async Task Hatali_yapistirmada_hicbir_satir_eklenmez_metin_kutuda_kalir()
    {
        var (vm, _) = await KurToplu();
        vm.Yapistirilan = "1\tAli\t100\t31.01.2027\n2\tAli\t12,500\t31.01.2027\n3\tAli\t100\t";
        vm.YapistirilaniEkleCommand.Execute(null);
        Assert.Empty(vm.TopluSatirlar);
        var hata = vm.TopluHatalari[nameof(vm.Yapistirilan)]!;
        Assert.StartsWith("Hiçbir satır eklenmedi.\nSatır 2: tutar belirsiz", hata);
        Assert.Contains("\nSatır 3: ", hata);
        Assert.NotEqual("", vm.Yapistirilan);
        vm.Yapistirilan = "1\tAli\t100\t31.01.2027";
        Assert.Null(vm.TopluHatalari[nameof(vm.Yapistirilan)]);
        vm.YapistirilaniEkleCommand.Execute(null);
        Assert.Equal(("1", "Ali", 100m, ""), (vm.TopluSatirlar.Single().No, vm.TopluSatirlar[0].Kisi, vm.TopluSatirlar[0].Tutar, vm.Yapistirilan));
    }

    // ---- Kayıt ----

    [Fact]
    public async Task Toplu_kayit_ortak_bilgileri_satirlara_yazar_listeye_vade_sirasiyla_girer()
    {
        var (vm, api) = await KurToplu(CekTakipViewModelTests.Cek(1, CekYonleri.Verilen, vade: new(2027, 2, 10), no: "9"));
        vm.TopluNot = " Kasım alımı ";
        Seri(vm);
        vm.TopluSatirlar[1].Kisi = " Ali ";
        await vm.TopluKaydetCommand.ExecuteAsync(null);
        var g = Assert.Single(api.Toplular);
        Assert.NotEqual(Guid.Empty, g.IstekId);
        Assert.Equal(new CekTopluSatirYaz(CekTurleri.Cek, CekYonleri.Verilen, "0012345", "Ziraat", "Mehmet Ticaret", 10_000m, new(2027, 1, 31), "MEZAT", false, null, "Kasım alımı"), g.Satirlar[0]);
        Assert.Equal(["Mehmet Ticaret", "Ali", "Mehmet Ticaret"], g.Satirlar.Select(s => s.Kisi));
        Assert.Equal([200, 1, 201, 202], vm.Cekler.Select(s => s.Veri.Id));
        Assert.Equal("3 çek kaydedildi · toplam 30.000,00 ₺.", vm.Mesaj);
        Assert.False(vm.TopluAcik);
        Assert.Empty(vm.TopluSatirlar);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        Assert.Equal(2, api.OzetSayisi);
    }

    [Fact]
    public async Task Suzgecte_gorunmeyen_toplu_kayitlar_iletide_soylenir()
    {
        var (vm, _) = await KurToplu();
        await vm.HazirSuzgecAsync(CekHazirSuzgec.Verilen30);
        Seri(vm, adet: 2, vade: Bugun.AddDays(20));
        await vm.TopluKaydetCommand.ExecuteAsync(null);
        Assert.Single(vm.Cekler);
        Assert.Equal("2 çek kaydedildi · toplam 20.000,00 ₺; 1 tanesi seçili süzgeç (Verilen · Ödenecek · Vade 25.09.2026 – 25.10.2026) dışında kaldığı için listede görünmüyor.", vm.Mesaj);
    }

    [Fact]
    public async Task Hatali_satir_varken_istek_gitmez_hatalar_satirda_ve_ozette()
    {
        var (vm, api) = await KurToplu();
        vm.TopluBanka = "";
        vm.TopluKasa = null;
        vm.TopluKisi = "";
        await vm.TopluKaydetCommand.ExecuteAsync(null);
        Assert.Equal("En az bir satır ekleyin.", vm.TopluHatalari.Genel);
        vm.TopluSatirEkleCommand.Execute(null);
        Seri(vm, adet: 1);
        vm.TopluSatirlar[0].No = "1";
        vm.TopluSatirlar[0].Kisi = "Ali";
        await vm.TopluKaydetCommand.ExecuteAsync(null);
        Assert.Equal("Banka boş olamaz.", vm.TopluHatalari[nameof(vm.TopluBanka)]);
        Assert.Equal("Ödeneceği kasayı (kanal ya da Ortak) seçin.", vm.TopluHatalari[nameof(vm.TopluKasa)]);
        Assert.Equal("Tutar sıfırdan büyük olmalı.", vm.TopluSatirlar[0].Hata);
        Assert.Equal("Kişi boş olamaz.", vm.TopluSatirlar[1].Hata);
        Assert.Equal("Hatalı satırlar: 1, 2. Düzeltilmeden hiçbiri kaydedilmez.", vm.TopluHatalari.Genel);
        vm.TopluSatirlar[0].Tutar = 5m;
        Assert.Null(vm.TopluSatirlar[0].Hata);
        Assert.Null(vm.TopluHatalari.Genel);
        Assert.Empty(api.Toplular);
        Assert.True(vm.TopluAcik);
        // Senette banka boş olabilir, alınan çekte kasa seçilmez.
        vm.TopluTur = vm.TurSecenekleri[1];
        vm.TopluYon = vm.YonSecenekleri[1];
        vm.TopluKisi = "Ortak";
        await vm.TopluKaydetCommand.ExecuteAsync(null);
        Assert.Equal((CekTurleri.Senet, CekYonleri.Alinan, (string?)null, (string?)null), Assert.Single(api.Toplular).Satirlar.Select(s => (s.Tur, s.Yon, s.Banka, s.Kanal)).Distinct().Single());
    }

    [Fact]
    public async Task Ayni_cek_kayitlida_ve_listede_uyarilir_yine_de_kaydedilir_degisiklik_uyariyi_kaldirir()
    {
        var (vm, api) = await KurToplu(CekTakipViewModelTests.Cek(1, CekYonleri.Verilen, no: "0012346"));
        Seri(vm);
        vm.TopluSatirlar[2].No = "0012345";
        await vm.TopluKaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.Toplular);
        Assert.Contains("satır 2 (0012346) — kayıtlı: Mehmet Ticaret · 50.000,00 ₺ · vade 30.09.2026; satır 3 (0012345) — listede birden çok", vm.TopluAyniUyarisi);
        vm.TopluSatirlar[2].No = "0012347";
        Assert.False(vm.TopluAyniVar);
        await vm.TopluKaydetCommand.ExecuteAsync(null);
        Assert.True(vm.TopluAyniVar);
        await vm.TopluYineDeKaydetCommand.ExecuteAsync(null);
        Assert.Single(api.Toplular);
        Assert.False(vm.TopluAcik);
    }

    [Fact]
    public async Task Sunucunun_satir_hatasi_satira_yazilir_yeni_istek_kimligi_alinir_yanit_kaybinda_kimlik_korunur()
    {
        var (vm, api) = await KurToplu();
        Seri(vm);
        api.TopluHatasi = new HttpRequestException();
        await vm.TopluKaydetCommand.ExecuteAsync(null);
        Assert.Equal(Yurutucu.KayitBaglantiIletisi, vm.TopluHatalari.Genel);
        api.TopluHatasi = new KasaApiException(HttpStatusCode.BadRequest, "Satır 2: Kişi: Geçersiz karakter.\nSatır 9: yok");
        await vm.TopluKaydetCommand.ExecuteAsync(null);
        Assert.Equal(api.Toplular[0].IstekId, api.Toplular[1].IstekId);
        Assert.Equal([null, "Kişi: Geçersiz karakter.", null], vm.TopluSatirlar.Select(s => s.Hata));
        Assert.Equal("Hatalı satırlar: 2. Düzeltilmeden hiçbiri kaydedilmez.", vm.TopluHatalari.Genel);
        await vm.TopluKaydetCommand.ExecuteAsync(null);
        Assert.NotEqual(api.Toplular[1].IstekId, api.Toplular[2].IstekId);
        Assert.False(vm.TopluAcik);
    }

    [Fact]
    public async Task Yazilmis_toplu_form_birakilmadan_once_sorulur_liste_yenilenince_kasa_secimi_kalir()
    {
        var (vm, _) = await Kur();
        await vm.TopluAcCommand.ExecuteAsync(null);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        vm.TopluKasa = "PERAKENDE";
        Assert.True(vm.KaydedilmemisDegisiklikVar);
        await vm.YukleAsync();
        Assert.Equal("PERAKENDE", vm.TopluKasa);
        var sorulan = 0;
        vm.BirakmaOnayi = _ => { sorulan++; return Task.FromResult(false); };
        await vm.TopluAcCommand.ExecuteAsync(null);
        Assert.Equal((1, "PERAKENDE"), (sorulan, vm.TopluKasa));
        vm.DegisiklikleriBirak();
        Assert.False(vm.TopluAcik);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
    }
}
