using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Kredi bildirimine tıklanınca (//krediler?KrediId=2) Krediler sayfası o krediyi seçer; kredi yoksa sayfa hatası yazılır;
/// alıcı rolü krediye geçemez (Kartlar'daki IdIleSec ile aynı kural).</summary>
public class KrediIdIleSecimTests
{
    [Fact]
    public async Task Bildirimden_gelen_kredi_kimligi_krediyi_secer_bulunamazsa_hata_yazar()
    {
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor };
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };
        var vm = new KrediTakipViewModel(new FinansTakipTests.Sahte(), finans, auth);
        await vm.YukleAsync();
        Assert.True(vm.IdIleSec(2));
        Assert.Equal(2, vm.Secili!.Id);
        Assert.NotEmpty(vm.Taksitler);
        Assert.False(vm.IdIleSec(99));
        Assert.Equal("Kredi bulunamadı. Listeyi yenileyip tekrar deneyin.", vm.Hata);
        auth.AktifRol = Rol.Alici;
        Assert.False(vm.IdIleSec(2));
    }
}
