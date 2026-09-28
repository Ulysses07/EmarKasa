using System.Net;
using System.Text.Json.Nodes;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>Oturum, kanallar, genel giderler, gelirler, ayarlar, raporlar, benzer kayıt ve dışa aktarma.</summary>
public class KasaVeRaporSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(IKasaApi.LoginAsync), nameof(IKasaApi.BenKimAsync), nameof(IKasaApi.CikisAsync), nameof(IKasaApi.IzleyiciSifreAsync))]
    public async Task Oturum_uc_rolle_acilir_rol_okunur_cikis_tokeni_siler()
    {
        var editor = Istemci();
        var giris = await editor.Kasa.LoginAsync("editor", SozlesmeFabrikasi.EditorSifresi);
        Assert.Equal("editor", giris.Rol); Assert.False(string.IsNullOrWhiteSpace(giris.Token)); Assert.False(string.IsNullOrWhiteSpace(giris.Cihaz));
        Assert.Equal(giris.Token, await editor.Depo.OkuAsync());
        Assert.Equal("editor", await editor.Kasa.BenKimAsync());
        await editor.Kasa.IzleyiciSifreAsync("izleyici-sozlesme-1");

        var izleyici = Istemci();
        Assert.Equal("viewer", (await izleyici.Kasa.LoginAsync(null, "izleyici-sozlesme-1")).Rol);
        Assert.Equal("viewer", await izleyici.Kasa.BenKimAsync());

        await izleyici.Kasa.CikisAsync();
        Assert.Null(await izleyici.Depo.OkuAsync());
        var hata = await Assert.ThrowsAsync<KasaApiException>(() => izleyici.Kasa.PanelAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, hata.DurumKodu);
        var yanlis = await Assert.ThrowsAsync<KasaApiException>(() => Istemci().Kasa.LoginAsync("editor", "yanlis-sifre"));
        Assert.Equal(HttpStatusCode.Unauthorized, yanlis.DurumKodu);
    }

    [Fact]
    [SozlesmeKapsami(nameof(IKasaApi.AyarGuncelleAsync), nameof(IKasaApi.AyarlarAsync), nameof(IKasaApi.KanalOlusturAsync), nameof(IKasaApi.KanalGuncelleAsync),
        nameof(IKasaApi.KanalSilAsync), nameof(IKasaApi.KanallarAsync), nameof(IKasaApi.IslemOlusturAsync), nameof(IKasaApi.IslemGuncelleAsync),
        nameof(IKasaApi.IslemSilAsync), nameof(IKasaApi.IslemlerAsync), nameof(IKasaApi.GelenKaydetAsync), nameof(IKasaApi.GelenlerAsync),
        nameof(IKasaApi.PanelAsync), nameof(IKasaApi.HaftalikAsync), nameof(IKasaApi.AylikAsync), nameof(IKasaApi.DonemlerAsync),
        nameof(IBenzerKayitApi.BenzerKayitlarAsync), nameof(IYonetimApi.DisariAktarAsync))]
    public async Task Kanal_gider_gelir_ve_raporlar_istemci_turlerine_birebir_uyar()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var ayar = await o.Kasa.AyarlarAsync();
        Assert.Equal(Baslangic, ayar.TakipBaslangic); Assert.Equal(1000m, ayar.KasaAcilisDevri); Assert.False(ayar.IzleyiciSifreVarMi);

        var kanal = await o.Kasa.KanalOlusturAsync(new KanalYaz("SÖZLEŞME", true, 7, 0m));
        Assert.Equal(HttpStatusCode.Created, o.SonYanit.Durum); Assert.True(kanal.Id > 0); Assert.Equal("SÖZLEŞME", kanal.Ad);
        kanal = await o.Kasa.KanalGuncelleAsync(kanal.Id, new KanalYaz("SÖZLEŞME 2", false, 8, 0m));
        Assert.Equal(("SÖZLEŞME 2", false, 8), (kanal.Ad, kanal.Aktif, kanal.Sira));
        Assert.Contains(await o.Kasa.KanallarAsync(), k => k.Id == kanal.Id);

        // Para ve tarih biçimi: kuruşlu tutar ve tarih gidip aynen döner.
        var gider = await o.Kasa.IslemOlusturAsync(new IslemYaz(Bugun, "Sözleşme carisi", 1234.56m, "MEZAT", GiderTipi.Cari, "not"));
        Assert.Equal(HttpStatusCode.Created, o.SonYanit.Durum);
        Assert.Equal((Bugun, 1234.56m, "MEZAT", GiderTipi.Cari, "not"), (gider.Tarih, gider.TutarTl, gider.Kanal, gider.Tip, gider.Not));
        var ikinci = await o.Kasa.IslemOlusturAsync(new IslemYaz(Baslangic.AddDays(3), "Sabit gider", 250.5m, "PERAKENDE", GiderTipi.SabitGider, null));
        ikinci = await o.Kasa.IslemGuncelleAsync(ikinci.Id, new IslemYaz(Baslangic.AddDays(4), "Sabit gider", 300.25m, "PERAKENDE", GiderTipi.SabitGider, "düzeltme"));
        Assert.Equal((Baslangic.AddDays(4), 300.25m), (ikinci.Tarih, ikinci.TutarTl));
        var liste = await o.Kasa.IslemlerAsync(Baslangic, Bugun, "MEZAT", "Sözleşme");
        Assert.Equal(gider.Id, Assert.Single(liste).Id);
        Assert.Equal(2, (await o.Kasa.IslemlerAsync()).Count);

        var gelen = await o.Kasa.GelenKaydetAsync(new GelenYaz(Baslangic, "MEZAT", 5000.75m));
        Assert.Equal((Baslangic, "MEZAT", 5000.75m), (gelen.DonemStart, gelen.Kanal, gelen.TutarTl)); Assert.NotNull(gelen.KanalId);
        Assert.Single(await o.Kasa.GelenlerAsync(Baslangic));
        Assert.Single(await o.Kasa.GelenlerAsync());

        var panel = await o.Kasa.PanelAsync();
        Assert.Equal(1000m + 5000.75m - 1234.56m - 300.25m, panel.GuncelKasa);
        Assert.All(panel.Kanallar, k => Assert.NotNull(k.KanalId));
        var haftalik = await o.Kasa.HaftalikAsync();
        Assert.NotEmpty(haftalik); Assert.All(haftalik, h => Assert.NotEmpty(h.Kanallar));
        var aylik = await o.Kasa.AylikAsync(Bugun.Year, Bugun.Month);
        Assert.Equal((Bugun.Year, Bugun.Month), (aylik.Yil, aylik.Ay)); Assert.Contains(aylik.Kanallar, k => k.CariGiden == 1234.56m);
        var donemler = await o.Kasa.DonemlerAsync();
        Assert.Equal(Baslangic, donemler[0].Start); Assert.All(donemler, d => Assert.Equal(d.Start.Month, d.Ay));

        var benzer = await o.Benzer.BenzerKayitlarAsync(new BenzerlikYaz("Gider", Bugun, 1234.56m, Kanal: "MEZAT"));
        Assert.Equal(gider.Id, Assert.Single(benzer).Id);

        using var csv = new MemoryStream();
        var dosya = await o.Yonetim.DisariAktarAsync(Baslangic, Bugun, null, "csv", csv);
        Assert.EndsWith(".csv", dosya.DosyaAdi); Assert.StartsWith("text/csv", dosya.IcerikTuru); Assert.Equal(csv.Length, dosya.Boyut); Assert.True(dosya.Boyut > 0);

        await o.Kasa.IslemSilAsync(ikinci.Id);
        Assert.Equal(HttpStatusCode.NoContent, o.SonYanit.Durum);
        await o.Kasa.KanalSilAsync(kanal.Id);
        Assert.Equal(HttpStatusCode.NoContent, o.SonYanit.Durum);
        Assert.DoesNotContain(await o.Kasa.KanallarAsync(), k => k.Id == kanal.Id);
    }

    /// <summary>Koşullu alan: sunucu HaftalikOzet.VeriSagligiUyarisi'ni yalnız ufkun ötesinde kayıt varken yazar. Olağan
    /// veriyle alan yanıtta hiç görünmez; burada dolu gelir ve vekil onu istemci DTO'suna karşı denetler (bilinen sapma
    /// izin listesinde gerekçesiyle durur, istemci alanı tanıyınca satır bayatlar).</summary>
    [Fact]
    [SozlesmeKapsami(nameof(IKasaApi.HaftalikAsync))]
    [KosulluAlanSenaryosu(typeof(Kasa.Core.HaftalikOzet), nameof(Kasa.Core.HaftalikOzet.VeriSagligiUyarisi))]
    public async Task Haftalik_rapor_ufuk_otesi_kayitta_veri_sagligi_uyarisini_yazar_istemci_denetimden_gecer()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        await o.Kasa.IslemOlusturAsync(new IslemYaz(Bugun, "Olağan gider", 100m, "MEZAT", GiderTipi.Cari, null));
        // Girdi doğrulaması ufuk ötesi tarihi reddeder: içe aktarılmış/eski kayıt gibi doğrudan veritabanına yazılır.
        F.Veri(db =>
        {
            var kanal = db.Kanallar.Single(k => k.Ad == "MEZAT");
            db.Islemler.Add(new() { Tarih = new DateOnly(2031, 1, 15), Cari = "Yıl yazım hatası", TutarTl = 50m, KanalId = kanal.Id, Kanal = kanal.Ad, Tip = Kasa.Core.GiderTipi.Cari });
            db.SaveChanges();
        });

        var haftalik = await o.Kasa.HaftalikAsync();
        Assert.Equal(900m, haftalik[^1].KasaDevir);
        var donemler = JsonNode.Parse(o.SonYanit.Json!)!.AsArray();
        Assert.Contains("15.01.2031", donemler[^1]!["veriSagligiUyarisi"]!.GetValue<string>());
        Assert.All(donemler.Take(donemler.Count - 1), d => Assert.Null(d!["veriSagligiUyarisi"]));
    }
}
