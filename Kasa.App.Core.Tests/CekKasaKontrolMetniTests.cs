using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core.Tests;

/// <summary>Kasa dökümü ve değişiklik geçmişinde çek satırlarının görünen adları (docs/specs/2026-10-01-cekler.md).</summary>
public class CekKasaKontrolMetniTests
{
    private static DenetimOlayDto Olay(string varlik, string id) =>
        new(1, DateTimeOffset.UnixEpoch, "editor", null, null, "Ekle", varlik, id, null, "{}", null, null, null, null);

    [Fact]
    public void Cek_hareket_turu_ve_denetim_varliklari_turkce_adlanir()
    {
        Assert.Equal("Çek", KasaKontrolMetni.HareketTuru(KasaHareketTurleri.Cek));
        Assert.Equal("Çek #4", KasaKontrolMetni.Kayit(Olay("Cek", "4")));
        Assert.Equal("Çek hareketi #9", KasaKontrolMetni.Kayit(Olay("CekHareket", "9")));
    }
}
