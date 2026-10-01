namespace Kasa.App.Core.Tests;

/// <summary>Pencereli süreç adlı mutex tutar; pencere açmadan çalışan görev onu görünce çıkar. Kilit bırakılınca görünmez. Test,
/// gerçek uygulamanın adıyla çakışmamak için her çalışmada benzersiz ad kullanır.</summary>
public class PencereKilidiTests
{
    [Fact]
    public void Kilit_tutulurken_acik_birakilinca_kapali_gorunur()
    {
        var ad = @"Local\kasa-test-" + Guid.NewGuid().ToString("N");
        Assert.False(PencereKilidi.AcikMi(ad));
        using (PencereKilidi.Al(ad))
            Assert.True(PencereKilidi.AcikMi(ad));
        Assert.False(PencereKilidi.AcikMi(ad));
        Assert.Equal(@"Local\EmarKasa.Pencere", PencereKilidi.VarsayilanAd);
    }
}
