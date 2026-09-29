using System.Net;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>Alıcı hesapları, alış yaşam döngüsü (alıcı taslağı, belge, gönderme; editör iade, onay, ödeme, düzeltme, iptal)
/// ve alış belgeleri.</summary>
public class AlisSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(IAlisApi.AliciOlusturAsync), nameof(IAlisApi.AliciGuncelleAsync), nameof(IAlisApi.AlicilarAsync), nameof(IAlisApi.AlisKanallariAsync),
        nameof(IAlisApi.AlisOlusturAsync), nameof(IAlisApi.AlisGuncelleAsync), nameof(IAlisApi.AlisGonderAsync), nameof(IAlisApi.AlislarAsync),
        nameof(IAlisApi.AlisIadeAsync), nameof(IAlisApi.AlisOnaylaAsync), nameof(IAlisApi.AlisOdemeKaydetAsync),
        nameof(IAlisOdemeApi.AlisOdemeDuzeltAsync), nameof(IAlisOdemeApi.AlisOdemeIptalAsync),
        nameof(IYonetimApi.BelgeYukleAsync), nameof(IYonetimApi.BelgelerAsync), nameof(IYonetimApi.BelgeIndirAsync), nameof(IYonetimApi.BelgeSilAsync))]
    public async Task Alis_yasam_dongusu_iki_rolle_istemci_turlerine_birebir_uyar()
    {
        var editor = await Editor();
        await editor.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var hesap = await editor.Alis.AliciOlusturAsync(new AliciYaz("sozlesme-alici", "Sözleşme Alıcısı", "alici-sifre-1"));
        Assert.Equal(HttpStatusCode.Created, editor.SonYanit.Durum);
        Assert.Equal(("sozlesme-alici", true), (hesap.Kullanici, hesap.Aktif));
        hesap = await editor.Alis.AliciGuncelleAsync(hesap.Id, new AliciYaz("sozlesme-alici", "Sözleşme Alıcısı 2", null));
        Assert.Equal("Sözleşme Alıcısı 2", hesap.Ad);
        Assert.Equal(hesap.Id, Assert.Single(await editor.Alis.AlicilarAsync()).Id);

        var alici = Istemci();
        Assert.Equal("alici", (await alici.Kasa.LoginAsync("sozlesme-alici", "alici-sifre-1")).Rol);
        Assert.Equal(3, (await alici.Alis.AlisKanallariAsync()).Count);
        var alis = await alici.Alis.AlisOlusturAsync(new AlisYaz(0, Bugun, "Tedarikçi", "İlk not", [new AlisKalemYaz("Mal", 1000m, [new(1, 600m), new(2, 400m)])]));
        Assert.Equal(HttpStatusCode.Created, alici.SonYanit.Durum);
        Assert.Equal((hesap.Id, "Taslak", 1000m), (alis.AliciId, alis.Durum, alis.Toplam));
        alis = await alici.Alis.AlisGuncelleAsync(alis.Id, new AlisYaz(alis.Surum, Bugun, "Tedarikçi A.Ş.", "İlk not", [new AlisKalemYaz("Mal", 1200.5m, [new(1, 700.5m), new(2, 500m)])]));
        Assert.Equal(1200.5m, alis.Toplam);
        Assert.Equal(2, Assert.Single(alis.Kalemler).Dagilimlar.Count);

        var belge = await alici.Yonetim.BelgeYukleAsync(alis.Id, "fatura.pdf", "application/pdf", "%PDF-1.7 fatura"u8.ToArray());
        Assert.Equal(HttpStatusCode.Created, alici.SonYanit.Durum);
        Assert.Equal((alis.Id, "fatura.pdf", "application/pdf", 15L), (belge.AlisId, belge.DosyaAdi, belge.IcerikTuru, belge.Boyut));
        Assert.Equal(("alici", "Sözleşme Alıcısı 2", false), (belge.YukleyenRol, belge.Yukleyen, belge.Silindi));
        Assert.Equal(belge.Id, Assert.Single(await alici.Yonetim.BelgelerAsync(alis.Id)).Id);
        alis = await alici.Alis.AlisGonderAsync(alis.Id, new AlisDurumYaz(alis.Surum));
        Assert.Equal("Incelemede", alis.Durum);
        Assert.Equal(alis.Id, Assert.Single(await alici.Alis.AlislarAsync()).Id);

        alis = await editor.Alis.AlisIadeAsync(alis.Id, new AlisDurumYaz(alis.Surum, "Birim fiyatı kontrol edin"));
        Assert.Equal(("Taslak", "Birim fiyatı kontrol edin"), (alis.Durum, alis.EditorNotu));
        alis = await alici.Alis.AlisGonderAsync(alis.Id, new AlisDurumYaz(alis.Surum));
        alis = await editor.Alis.AlisOnaylaAsync(alis.Id, new AlisDurumYaz(alis.Surum, "Uygun"));
        Assert.Equal("Onaylandi", alis.Durum);
        alis = await editor.Alis.AlisOdemeKaydetAsync(alis.Id, new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Bugun, 500m, Not: "Peşinat"));
        var odeme = Assert.Single(alis.Odemeler);
        Assert.Equal((500m, 500m, 700.5m), (odeme.Tutar, alis.Odenen, alis.Kalan));
        Assert.Equal(1200.5m, alis.Kalemler.Sum(k => k.Tutar));
        alis = await editor.AlisOdeme.AlisOdemeDuzeltAsync(alis.Id, odeme.Id, new AlisOdemeDuzeltYaz(alis.Surum, Guid.NewGuid(), Bugun, 450.25m, null, null, "Tutar düzeltmesi"));
        Assert.Equal(450.25m, Assert.Single(alis.Odemeler).Tutar);
        alis = await editor.AlisOdeme.AlisOdemeIptalAsync(alis.Id, odeme.Id, new AlisOdemeIptalYaz(alis.Surum, Guid.NewGuid(), "Yanlış ödeme"));
        Assert.Empty(alis.Odemeler);
        Assert.Equal(alis.Surum, Assert.Single(await editor.Alis.AlislarAsync()).Surum);

        using var hedef = new MemoryStream();
        var indirilen = await editor.Yonetim.BelgeIndirAsync(belge.Id, hedef);
        Assert.Equal(("fatura.pdf", "application/pdf", 15L), (indirilen.DosyaAdi, indirilen.IcerikTuru, indirilen.Boyut));
        Assert.Equal("%PDF-1.7 fatura"u8.ToArray(), hedef.ToArray());
        // Silme yumuşaktır: editör gerekçeyle kaldırır; varsayılan liste boşalır, silinenler listesinde iziyle görünür.
        await editor.Yonetim.BelgeSilAsync(belge.Id, "Yanlış fatura");
        Assert.Equal(HttpStatusCode.NoContent, editor.SonYanit.Durum);
        Assert.Empty(await editor.Yonetim.BelgelerAsync(alis.Id));
        var silinen = Assert.Single(await editor.Yonetim.BelgelerAsync(alis.Id, silinenler: true));
        Assert.Equal((belge.Id, true, "editor", "Editör", "Yanlış fatura"), (silinen.Id, silinen.Silindi, silinen.SilenRol, silinen.Silen, silinen.SilmeGerekcesi));
        Assert.NotNull(silinen.SilinmeZamani);
    }
}
