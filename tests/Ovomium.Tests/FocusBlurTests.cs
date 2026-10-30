using Ovomium.Core.View;

namespace Ovomium.Tests;

/// Flou de focale coupé dans les vues 1re et 3e personne de LMD-Camera.
public class FocusBlurTests
{
    [Theory]
    [InlineData("FirstPerson")]
    [InlineData("ThirdPerson")]
    public void VuesDeLmdCamera_CoupeLeFlou(string view)
    {
        Assert.True(FocusBlur.ShouldSuppress(view, vr: false, keepBlur: false));
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("None")]
    [InlineData("Photo")]
    [InlineData(null)]
    public void VueNormaleOuInconnue_GardeLeFlou(string view)
    {
        Assert.False(FocusBlur.ShouldSuppress(view, vr: false, keepBlur: false));
    }

    [Fact]
    public void EnVr_NeTouchePasAuFlou()
    {
        Assert.False(FocusBlur.ShouldSuppress("FirstPerson", vr: true, keepBlur: false));
        Assert.False(FocusBlur.ShouldSuppress("ThirdPerson", vr: true, keepBlur: false));
    }

    [Fact]
    public void OptionDuJoueur_GardeLeFlou()
    {
        Assert.False(FocusBlur.ShouldSuppress("FirstPerson", vr: false, keepBlur: true));
    }
}
