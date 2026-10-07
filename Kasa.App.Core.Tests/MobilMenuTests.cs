namespace Kasa.App.Core.Tests;

public sealed class MobilMenuTests
{
    [Fact]
    public void Menu_rotalari_rolleri_ve_cikis_eylemini_korur()
    {
        foreach (var rol in Enum.GetValues<Rol>())
        {
            var mobil = new MenuModeli();
            var bolumler = SekmeModeli.Bolumler(rol);
            mobil.Goster(bolumler);
            Assert.Equal(bolumler.Order(), mobil.Ogeler.Where(o => o.Bolum is not null).Select(o => o.Bolum!.Value).Order());
            Assert.All(mobil.Ogeler, o => Assert.DoesNotContain(o.Simge, c => c is >= '\uE000' and <= '\uF8FF'));
            Assert.Equal(mobil.Ogeler.Count(), mobil.Ogeler.Select(o => o.Simge).Distinct().Count());
            mobil.RotaSecildi("//kartlar");
            Assert.Equal(bolumler.Contains(Bolum.Kartlar), mobil.Ogeler.Any(o => o.Secili));
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
