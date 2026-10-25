using System.Net;
using System.Net.Sockets;
using LmdMulti.Core;
using LmdMulti.Core.Net;
using LmdMulti.Core.Poses;
using LmdMulti.Core.Protocol;

namespace LmdMulti.Tests;

/// Deux nœuds réels sur la boucle locale (sans broadcast, IP manuelles) : découverte, poses, ping, départ.
public class UdpNodeTests
{
    private static int _nextPort = 48100 + Environment.ProcessId % 500 * 4;

    private static UdpNode Node(int port, int peerPort, string name)
    {
        var n = new UdpNode(new NodeOptions
        {
            Port = port, DiscoveryPort = port, Broadcast = false, AnnounceIntervalMs = 100, PingIntervalMs = 100,
            PeerTimeoutMs = 1000, ManualPeers = { new IPEndPoint(IPAddress.Loopback, peerPort) },
        });
        Assert.True(n.Start(), n.Status);
        n.SetLocal(name, 1, "CA_01_A2", true);
        return n;
    }

    /// Lit les événements jusqu'à ce que `match` soit vrai (ou échec après 3 s).
    private static NetEvent WaitFor(UdpNode n, Func<NetEvent, bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            while (n.TryGetEvent(out var e)) if (match(e)) return e;
            Thread.Sleep(10);
        }
        throw new TimeoutException("événement attendu non reçu");
    }

    private static byte[] PosePacket(ulong sender, uint t)
    {
        var w = new PacketWriter();
        Messages.WritePose(w, sender, new PoseFrame { TimeMs = t, RootPos = new Vec3(t, 0, 0) });
        return w.ToArray();
    }

    [Fact]
    public void Discovery_Poses_Ping_Bye()
    {
        int pa = Interlocked.Add(ref _nextPort, 4), pb = pa + 1;
        using var a = Node(pa, pb, "Alice");
        var b = Node(pb, pa, "Bob");
        var joined = WaitFor(a, e => e.Kind == NetEventKind.PeerJoined);
        Assert.Equal("Bob", joined.Peer.Name);
        Assert.Equal("CA_01_A2", joined.Peer.Track);
        WaitFor(b, e => e.Kind == NetEventKind.PeerJoined);

        b.SendPose(PosePacket(b.Id, 777));
        var pose = WaitFor(a, e => e.Kind == NetEventKind.Pose);
        Assert.Equal(b.Id, pose.PeerId);
        Assert.Equal(777f, pose.Pose.RootPos.X);

        b.SetLocal("Bob", 1, "", false); // retour au menu
        var changed = WaitFor(a, e => e.Kind == NetEventKind.PeerChanged);
        Assert.False(changed.Peer.OnTrack);

        SpinWait.SpinUntil(() => a.Peers().Any(p => p.RttMs >= 0), 3000);
        Assert.InRange(a.Peers().Single().RttMs, 0, 200);

        b.Dispose();
        Assert.Equal(b.Id, WaitFor(a, e => e.Kind == NetEventKind.PeerLeft).PeerId);
    }

    [Fact]
    public void Echo_DeliversOwnPose_AsEchoPeer()
    {
        int p = Interlocked.Add(ref _nextPort, 4);
        using var a = Node(p, p + 3, "Edia");
        a.Echo = true;
        a.SendPose(PosePacket(a.Id, 5));
        var joined = WaitFor(a, e => e.Kind == NetEventKind.PeerJoined);
        Assert.True(joined.Peer.IsEcho);
        Assert.Equal("Edia (écho)", joined.Peer.Name);
        Assert.Equal(a.EchoId, WaitFor(a, e => e.Kind == NetEventKind.Pose).PeerId);
        a.Echo = false;
        Assert.Equal(a.EchoId, WaitFor(a, e => e.Kind == NetEventKind.PeerLeft).PeerId);
    }

    [Fact]
    public void OtherProtocolVersion_IsListedAsIncompatible()
    {
        int p = Interlocked.Add(ref _nextPort, 4);
        using var a = Node(p, p + 3, "Edia");
        var w = new PacketWriter();
        Messages.WriteAnnounce(w, 99, new Announce { Name = "Futur" });
        w.Buffer[2] = 200;
        using var raw = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        raw.SendTo(w.Buffer, 0, w.Length, SocketFlags.None, new IPEndPoint(IPAddress.Loopback, p));
        var joined = WaitFor(a, e => e.Kind == NetEventKind.PeerJoined);
        Assert.False(joined.Peer.IsCompatible);
        Assert.Equal(200, joined.Peer.IncompatibleVersion);
        // Ses poses éventuelles ne sont jamais décodées ni livrées.
        Assert.DoesNotContain(a.Peers(), x => x.IsCompatible);
    }

    [Fact]
    public void EmptyDatagram_DoesNotStallReception()
    {
        int pa = Interlocked.Add(ref _nextPort, 4), pb = pa + 1;
        using var a = Node(pa, pb, "Alice");
        using var raw = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        raw.SendTo(Array.Empty<byte>(), new IPEndPoint(IPAddress.Loopback, pa)); // scan de port, outil tiers…
        Thread.Sleep(50);
        using var b = Node(pb, pa, "Bob");
        Assert.Equal("Bob", WaitFor(a, e => e.Kind == NetEventKind.PeerJoined).Peer.Name);
    }

    [Fact]
    public void BusyPort_FallsBackToNextOne()
    {
        int p = Interlocked.Add(ref _nextPort, 4);
        using var a = Node(p, p + 3, "A");
        using var b = new UdpNode(new NodeOptions { Port = p, Broadcast = false });
        Assert.True(b.Start());
        Assert.Equal(p + 1, b.BoundPort);
    }
}
