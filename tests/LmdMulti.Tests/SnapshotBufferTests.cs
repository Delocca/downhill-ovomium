using LmdMulti.Core;
using LmdMulti.Core.Poses;

namespace LmdMulti.Tests;

public class SnapshotBufferTests
{
    /// Pose d'un émetteur qui avance à 10 m/s en X ; horloge émetteur = t.
    private static PoseFrame At(uint t) => new() { TimeMs = t, RootPos = new Vec3(t / 100f, 0, 0) };

    [Fact]
    public void Interpolates_LinearMotion_DespiteJitter()
    {
        var buf = new SnapshotBuffer(2000);
        var rng = new Random(4);
        const double clockOffset = 50_000; // horloges locale et distante sans rapport
        var output = new PoseFrame();
        int interpolated = 0;
        for (uint t = 0; t < 3000; t += 40) // 25 Hz
        {
            double latency = 5 + rng.NextDouble() * 30; // 5 à 35 ms de gigue
            buf.Add(At(t), t + clockOffset + latency);
            double now = t + clockOffset + 40;
            if (t < 200) continue;
            var status = buf.Sample(now, 100, output);
            if (status != SampleStatus.Interpolated) continue;
            interpolated++;
            // Instant visé ≈ now - (offset min ≈ offset + 5 ms) - 100 ms ; tolérance = gigue.
            double expectedX = (now - clockOffset - 5 - 100) / 100.0;
            Assert.InRange(output.RootPos.X, expectedX - 0.35, expectedX + 0.05);
        }
        Assert.True(interpolated > 60, $"{interpolated} échantillons interpolés");
    }

    [Fact]
    public void Holds_WhenStarved_AndWaits_BeforeFirst()
    {
        var buf = new SnapshotBuffer(2000);
        var o = new PoseFrame();
        Assert.Equal(SampleStatus.Empty, buf.Sample(0, 100, o));
        buf.Add(At(1000), 1000);
        buf.Add(At(1040), 1040);
        Assert.Equal(SampleStatus.Waiting, buf.Sample(1050, 100, o));
        Assert.Equal(10f, o.RootPos.X);
        Assert.Equal(SampleStatus.Interpolated, buf.Sample(1120, 100, o));
        Assert.Equal(10.2f, o.RootPos.X, 0.001f);
        Assert.Equal(SampleStatus.Holding, buf.Sample(2000, 100, o));
        Assert.Equal(10.4f, o.RootPos.X);
        Assert.Equal(1, buf.HoldCount);
    }

    [Fact]
    public void OutOfOrder_AndDuplicates_AreSorted()
    {
        var buf = new SnapshotBuffer(2000);
        buf.Add(At(0), 0);
        buf.Add(At(80), 80);
        buf.Add(At(40), 85); // en retard
        buf.Add(At(40), 86); // doublon
        Assert.Equal(3, buf.Count);
        var o = new PoseFrame();
        buf.Sample(160, 100, o); // visé : 60
        Assert.Equal(0.6f, o.RootPos.X, 0.001f);
    }

    [Fact]
    public void Teleport_IsNotInterpolated()
    {
        var buf = new SnapshotBuffer(2000);
        buf.Add(new PoseFrame { TimeMs = 0, RootPos = new Vec3(0, 0, 0) }, 0);
        buf.Add(new PoseFrame { TimeMs = 40, RootPos = new Vec3(500, 0, 0) }, 40); // respawn
        var o = new PoseFrame();
        buf.Sample(120, 100, o); // visé : 20, à mi-chemin
        Assert.Equal(0f, o.RootPos.X);
    }

    [Fact]
    public void SenderClockReset_ClearsBuffer()
    {
        var buf = new SnapshotBuffer(2000);
        for (uint t = 0; t <= 10000; t += 1000) buf.Add(At(t), t);
        buf.Add(At(0), 10100); // l'émetteur a redémarré
        Assert.Equal(1, buf.Count);
    }

    [Fact]
    public void OldEntries_AreTrimmed()
    {
        var buf = new SnapshotBuffer(500);
        for (uint t = 0; t <= 2000; t += 40) buf.Add(At(t), t);
        Assert.InRange(buf.Count, 12, 14);
    }

    [Fact]
    public void BonesInterpolate_AndRagdollFlagFollowsNearest()
    {
        var a = new PoseFrame { TimeMs = 0, Flags = PoseFlags.HasBones };
        var b = new PoseFrame { TimeMs = 100, Flags = PoseFlags.HasBones | PoseFlags.Ragdoll };
        for (int i = 0; i < a.BoneRot.Length; i++) { a.BoneRot[i] = Quat.Identity; b.BoneRot[i] = Quat.FromYaw(MathF.PI / 2); }
        var o = new PoseFrame();
        o.SetLerp(a, b, 0.5f);
        Assert.Equal(45f, Quat.AngleDeg(Quat.Identity, o.BoneRot[3]), 0.1f);
        Assert.True(o.IsRagdoll);
        o.SetLerp(a, b, 0.2f);
        Assert.False(o.IsRagdoll);
    }
}
