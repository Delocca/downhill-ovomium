using Ovomium.Core.View;

namespace Ovomium.Tests;

/// Cycle des vues de LMD-Camera (touche V / R3).
public class ViewCycleTests
{
    [Fact]
    public void Cycle_TroisVues_DansLOrdre()
    {
        Assert.Equal("FirstPerson", ViewCycle.Next("Default"));
        Assert.Equal("ThirdPerson", ViewCycle.Next("FirstPerson"));
        Assert.Equal("Default", ViewCycle.Next("ThirdPerson"));
    }

    [Fact]
    public void Cycle_RevientAuDepart_ApresTroisAppuis()
    {
        string view = "Default";
        for (int i = 0; i < 3; i++) view = ViewCycle.Next(view);
        Assert.Equal("Default", view);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Photo")]
    [InlineData("")]
    [InlineData(null)]
    public void VueInconnue_RepartDeLaVueNormale(string view)
    {
        Assert.Equal("Default", ViewCycle.Next(view));
    }

    [Fact]
    public void Libelles()
    {
        Assert.Equal("normale", ViewCycle.Label("Default"));
        Assert.Equal("1re personne", ViewCycle.Label("FirstPerson"));
        Assert.Equal("3e personne", ViewCycle.Label("ThirdPerson"));
    }
}
