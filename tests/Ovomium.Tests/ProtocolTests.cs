using Ovomium.Core;
using Ovomium.Core.Poses;
using Ovomium.Core.Protocol;

namespace Ovomium.Tests;

public class ProtocolTests
{
    internal static Quat RandomQuat(Random r) =>
        new Quat((float)r.NextDouble() * 2 - 1, (float)r.NextDouble() * 2 - 1, (float)r.NextDouble() * 2 - 1, (float)r.NextDouble() * 2 - 1).Normalized();

    internal static PoseFrame RandomPose(Random r, uint timeMs)
    {
        var f = new PoseFrame
        {
            TimeMs = timeMs, Flags = PoseFlags.HasBones | PoseFlags.Ragdoll,
            RootPos = new Vec3(1234.5f, -56.25f, 789f), RootRot = RandomQuat(r), PosMask = Skeleton.DefaultPositionMask,
        };
        for (int i = 0; i < Skeleton.Count; i++)
        {
            f.BoneRot[i] = RandomQuat(r);
            f.BonePos[i] = new Vec3((float)r.NextDouble() - 0.5f, (float)r.NextDouble(), 0.1f * i);
        }
        return f;
    }

    [Fact]
    public void QuatCodec_RoundTrip_StaysUnderQuarterDegree()
    {
        var r = new Random(1);
        float worst = 0;
        for (int i = 0; i < 20000; i++)
        {
            var q = RandomQuat(r);
            worst = MathF.Max(worst, Quat.AngleDeg(q, QuatCodec.Unpack(QuatCodec.Pack(q))));
        }
        Assert.True(worst < 0.25f, $"erreur max {worst}°");
    }

    [Fact]
    public void Pose_RoundTrip_KeepsEverything()
    {
        var f = RandomPose(new Random(2), 123456);
        var w = new PacketWriter();
        Messages.WritePose(w, 42, f);
        Assert.Equal(Messages.PoseSize(f), w.Length);
        Assert.True(w.Length < 300, $"pose de {w.Length} octets");

        Assert.Equal(HeaderStatus.Ok, Messages.ReadHeader(w.Buffer, w.Length, out var h));
        Assert.Equal(MessageType.Pose, h.Type);
        Assert.Equal(42UL, h.SenderId);
        var g = Messages.ReadPose(Messages.BodyReader(w.Buffer, w.Length));
        Assert.Equal(f.TimeMs, g.TimeMs);
        Assert.Equal(f.Flags, g.Flags);
        Assert.Equal(f.PosMask, g.PosMask);
        Assert.Equal(f.RootPos.X, g.RootPos.X);
        Assert.True(Quat.AngleDeg(f.RootRot, g.RootRot) < 0.25f);
        for (int i = 0; i < Skeleton.Count; i++)
        {
            Assert.True(Quat.AngleDeg(f.BoneRot[i], g.BoneRot[i]) < 0.25f);
            if ((f.PosMask >> i & 1) != 0) Assert.True(Vec3.Distance(f.BonePos[i], g.BonePos[i]) < 0.005f);
        }
    }

    [Fact]
    public void Pose_WithoutBones_IsSmall()
    {
        var f = new PoseFrame { TimeMs = 5, RootPos = new Vec3(1, 2, 3) };
        var w = new PacketWriter();
        Messages.WritePose(w, 1, f);
        Assert.Equal(33, w.Length);
        Assert.False(Messages.ReadPose(Messages.BodyReader(w.Buffer, w.Length)).HasBones);
    }

    [Fact]
    public void Announce_RoundTrip_AndUtf8Truncation()
    {
        var w = new PacketWriter();
        string longName = new string('é', 40); // 80 octets UTF-8 > 48
        Messages.WriteAnnounce(w, 7, new Announce { SteamId = 765, Name = longName, Track = "CA_01_A2", OnTrack = true, ModVersion = "0.1.0" });
        var a = Messages.ReadAnnounce(Messages.BodyReader(w.Buffer, w.Length));
        Assert.Equal(765UL, a.SteamId);
        Assert.Equal(new string('é', 24), a.Name); // coupé sur une frontière de caractère
        Assert.Equal("CA_01_A2", a.Track);
        Assert.True(a.OnTrack);
        Assert.Equal("0.1.0", a.ModVersion);
    }

    [Fact]
    public void Header_DetectsForeignAndOtherVersions()
    {
        var w = new PacketWriter();
        Messages.WriteAnnounce(w, 9, new Announce());
        w.Buffer[2] = ProtocolInfo.Version + 1;
        Assert.Equal(HeaderStatus.WrongVersion, Messages.ReadHeader(w.Buffer, w.Length, out var h));
        Assert.Equal(9UL, h.SenderId);
        Assert.Equal(HeaderStatus.NotOurs, Messages.ReadHeader(new byte[] { 1, 2, 3 }, 3, out _));
        w.Buffer[0] = (byte)'X';
        Assert.Equal(HeaderStatus.NotOurs, Messages.ReadHeader(w.Buffer, w.Length, out _));
    }

    [Fact]
    public void TruncatedPacket_ThrowsFormatException()
    {
        var w = new PacketWriter();
        Messages.WritePose(w, 1, RandomPose(new Random(3), 1));
        Assert.Throws<FormatException>(() => Messages.ReadPose(Messages.BodyReader(w.Buffer, w.Length - 5)));
    }
}
