using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>Değişiklik geçmişi (denetim izi): istemci süzgeçleri sorgu dizesine doğru taşır, satır türü sunucununkiyle
/// birebir uyar, kilit açılışının kimliği ay kilidi geçmişindeki olayın kimliğidir; izleyici okuyamaz.</summary>
public class DenetimSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(IDenetimApi.DenetimOlaylariAsync), nameof(IKasaApi.IslemOlusturAsync), nameof(IKasaApi.IslemGuncelleAsync),
        nameof(IAylikGiderApi.AyKilidiDegistirAsync))]
    public async Task Degisiklik_gecmisi_suzgecleri_ve_kilit_penceresi_istemci_turune_birebir_uyar()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var gecenAy = new DateOnly(Bugun.Year, Bugun.Month, 1).AddMonths(-1);
        var gider = await o.Kasa.IslemOlusturAsync(new IslemYaz(gecenAy.AddDays(2), "Tedarikçi", 1250.5m, "MEZAT", GiderTipi.Cari, "Fatura"));
        var kilit = await o.AylikGider.AyKilidiDegistirAsync(true, new AyKilidiYaz(Guid.NewGuid(), (await o.AylikGider.AyKilidiAsync()).Surum, gecenAy.Year, gecenAy.Month, "Ay kapandı"));
        kilit = await o.AylikGider.AyKilidiDegistirAsync(false, new AyKilidiYaz(Guid.NewGuid(), kilit.Surum, gecenAy.Year, gecenAy.Month, "Fatura düzeltmesi"));
        await o.Kasa.IslemGuncelleAsync(gider.Id, new IslemYaz(gecenAy.AddDays(2), "Tedarikçi", 1300m, "MEZAT", GiderTipi.Cari, "Fatura"));

        var id = gider.Id.ToString(CultureInfo.InvariantCulture);
        var olaylar = await o.Denetim.DenetimOlaylariAsync(new DenetimSorgusu(Varlik: "Islem", VarlikId: id));
        Assert.Equal(["Degistir", "Ekle"], olaylar.Select(x => x.Tur));
        Assert.All(olaylar, x => Assert.Equal(("editor", "Islem", id), (x.AktorRol, x.Varlik, x.VarlikId)));
        Assert.Equal((1250.5m, 1300m), (JsonNode.Parse(olaylar[0].OncekiJson!)!["TutarTl"]!.GetValue<decimal>(), JsonNode.Parse(olaylar[0].YeniJson!)!["TutarTl"]!.GetValue<decimal>()));
        var acilis = kilit.Gecmis[0];
        Assert.Equal((acilis.Id, (int?)null), (olaylar[0].KilitAcmaOlayiId, olaylar[1].KilitAcmaOlayiId));

        var pencere = await o.Denetim.DenetimOlaylariAsync(new DenetimSorgusu(KilitAcmaOlayiId: acilis.Id));
        Assert.Equal(["Degistir", "KilitAc"], pencere.Select(x => x.Tur));
        Assert.Equal("Fatura düzeltmesi", pencere[1].Gerekce);
        var ilk = Assert.Single(await o.Denetim.DenetimOlaylariAsync(new DenetimSorgusu(Varlik: "Islem", VarlikId: id, Adet: 1)));
        var sonraki = Assert.Single(await o.Denetim.DenetimOlaylariAsync(new DenetimSorgusu(Varlik: "Islem", VarlikId: id, OncekiId: ilk.Id, Adet: 1)));
        Assert.Equal(olaylar.Select(x => x.Id), [ilk.Id, sonraki.Id]);
        Assert.NotEmpty(await o.Denetim.DenetimOlaylariAsync());

        await o.Kasa.IzleyiciSifreAsync("izleyici-denetim-1");
        var izleyici = Istemci();
        await izleyici.Kasa.LoginAsync(null, "izleyici-denetim-1");
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<KasaApiException>(() => izleyici.Denetim.DenetimOlaylariAsync())).DurumKodu);
        var gecersiz = await Assert.ThrowsAsync<KasaApiException>(() => o.Denetim.DenetimOlaylariAsync(new DenetimSorgusu(Adet: 500)));
        Assert.Equal(HttpStatusCode.BadRequest, gecersiz.DurumKodu);
    }

    /// <summary>İptal edilen aylık gider ödemesi ay listesinin ayrı iptal listesinde, iptal edilen ekstre satırı belgenin kayıt
    /// listesinde gerekçesi ve iptal anıyla (denetim izinden) istemci türlerine taşınır; iptal edilen ödemenin planı yeniden
    /// 'Planlandi' olur.</summary>
    [Fact]
    [SozlesmeKapsami(nameof(IAylikGiderApi.AylikGiderlerAsync), nameof(IAylikGiderApi.AylikGiderIptalAsync), nameof(IEkstreAktarmaApi.EkstreKayitIptalAsync),
        nameof(IEkstreAktarmaApi.EkstreBelgeAsync))]
    public async Task Iptal_gerekcesi_ve_iptal_ani_aylik_gider_ve_ekstre_istemci_turlerine_tasinir()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var sablon = await o.AylikGider.AylikGiderSablonKaydetAsync(null, new AylikGiderSablonYaz(Guid.NewGuid(), 0, "Kira", "Kira", 5000m, 5, "Genel", [], new DateOnly(Bugun.Year, Bugun.Month, 1)));
        var odenen = await o.AylikGider.AylikGiderOdeAsync(sablon.Id, new AylikGiderOdemeYaz(Guid.NewGuid(), sablon.Surum, Bugun.Year, Bugun.Month, Bugun, "Havale"));
        var iptal = await o.AylikGider.AylikGiderIptalAsync(odenen.OdemeId!.Value, new AylikGiderIptalYaz(Guid.NewGuid(), "Kira yanlış aya girildi"));
        Assert.Equal(("Iptal", "Kira yanlış aya girildi"), (iptal.Durum, iptal.IptalAciklamasi));
        Assert.NotNull(iptal.IptalZamani);
        var ay = await o.AylikGider.AylikGiderlerAsync(Bugun.Year, Bugun.Month);
        Assert.Equal(("Planlandi", 0m), (Assert.Single(ay.Kayitlar).Durum, ay.OdenenToplam));
        var iptalEdilen = Assert.Single(ay.Iptaller!);
        Assert.Equal((odenen.OdemeId, "Iptal", "Kira yanlış aya girildi", iptal.IptalZamani), (iptalEdilen.OdemeId, iptalEdilen.Durum, iptalEdilen.IptalAciklamasi, iptalEdilen.IptalZamani));

        var belge = await o.Ekstre.EkstreYukleAsync("%PDF-1.7 sozlesme"u8.ToArray(), "ekstre.pdf", "Banka", "Akbank", "Ana hesap", null);
        var okunan = Assert.Single(belge.Satirlar);
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [new EkstreSatirYaz(okunan.No, okunan.Tarih!.Value, okunan.Aciklama, Math.Abs(okunan.Tutar!.Value), "Gider", "Genel", [])]);
        var onizleme = await o.Ekstre.EkstreOnizlemeAsync(belge.Id, istek);
        belge = await o.Ekstre.EkstreKaydetAsync(belge.Id, istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = true });
        var kayit = Assert.Single(belge.Kayitlar);
        Assert.Equal(((string?)null, (DateTimeOffset?)null), (kayit.IptalAciklamasi, kayit.IptalZamani));
        await o.Ekstre.EkstreKayitIptalAsync(belge.Id, kayit.Id, new EkstreIptalYaz(Guid.NewGuid(), "Yanlış satır"));
        var satir = Assert.Single((await o.Ekstre.EkstreBelgeAsync(belge.Id)).Kayitlar);
        Assert.Equal((true, "Yanlış satır"), (satir.Iptal, satir.IptalAciklamasi));
        Assert.NotNull(satir.IptalZamani);
    }
}
