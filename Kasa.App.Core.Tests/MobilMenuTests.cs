namespace Kasa.App.Core.Tests;

public sealed class MobilMenuTests
{
    [Fact]
    public void Mobil_menu_rotalari_rolleri_ve_cikis_eylemini_korur()
    {
        foreach (var rol in Enum.GetValues<Rol>())
        {
            var masaustu = new MenuModeli();
            var mobil = new MenuModeli(mobilSimgeler: true);
            var bolumler = SekmeModeli.Bolumler(rol);
            masaustu.Goster(bolumler);
            mobil.Goster(bolumler);
            Assert.Equal(masaustu.Ogeler.Select(o => (o.Bolum, o.Baslik, o.Rota)), mobil.Ogeler.Select(o => (o.Bolum, o.Baslik, o.Rota)));
            Assert.All(mobil.Ogeler, o => Assert.DoesNotContain(o.Simge, c => c is >= '\uE000' and <= '\uF8FF'));
            Assert.Equal(mobil.Ogeler.Count(), mobil.Ogeler.Select(o => o.Simge).Distinct().Count());
            mobil.RotaSecildi("//kartlar");
            Assert.Equal(masaustu.Ogeler.Any(o => o.Rota == "kartlar"), mobil.Ogeler.Any(o => o.Secili));
            if (mobil.Ogeler.Any())
            {
                var cikis = 0;
                mobil.CikisIstendi += (_, _) => cikis++;
                mobil.Ogeler.Single(o => o.Bolum is null).SecCommand.Execute(null);
                Assert.Equal(1, cikis);
            }
        }
    }
}
