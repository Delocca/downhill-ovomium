using Ovomium.Core.Appearance;
using Ovomium.Core.Protocol;

namespace Ovomium.Tests;

public class RiderLookTests
{
    private static readonly RiderLook Knight = new(5000, 5231, 337, 6, 6, 0, 2, 1, 104, 57, 612);

    [Fact]
    public void Announce_CarriesLook()
    {
        var w = new PacketWriter();
        Messages.WriteAnnounce(w, 7, new Announce { Name = "Edia", Track = "CA_01_A2", OnTrack = true, ModVersion = "0.4.0", Look = Knight });
        var a = Messages.ReadAnnounce(Messages.BodyReader(w.Buffer, w.Length));
        Assert.Equal(Knight, a.Look);
        Assert.Equal("0.4.0", a.ModVersion);
    }

    [Fact]
    public void Look_KeepsNegativeAndLargeValues()
    {
        var odd = new RiderLook(int.MaxValue, -1, 0, 255, 300, -5, 16, 4, 163, 13, 500);
        var w = new PacketWriter();
        odd.Write(w);
        Assert.Equal(44, w.Length);
        Assert.Equal(odd, RiderLook.Read(new PacketReader(w.Buffer, w.Length, 0)));
    }

    [Fact]
    public void Look_DefaultIsUnknown_AndValueEqualityMakesCacheKeys()
    {
        Assert.False(default(RiderLook).IsKnown);
        Assert.True(Knight.IsKnown);
        var cache = new Dictionary<RiderLook, int> { [Knight] = 1 };
        Assert.True(cache.ContainsKey(Knight with { }));
        Assert.False(cache.ContainsKey(Knight with { HairColor = 7 }));
    }
}
