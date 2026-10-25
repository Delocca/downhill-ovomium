using System.Net;
using Ovomium.Core.Net;

namespace Ovomium.Tests;

/// Canal fiable (logique pure) et synchro d'horloge ; puis messages fiables entre deux vrais nœuds avec pertes.
public class ReliableTests
{
    private static byte[] B(byte v) => new[] { v };

    [Fact]
    public void DeliversInOrder_DropsDuplicates_BuffersEarly()
    {
        var tx = new ReliableChannel(1);
        var rx = new ReliableChannel(2);
        var (stream, s1) = tx.Enqueue(9, B(1), 0);
        var (_, s2) = tx.Enqueue(9, B(2), 0);
        var (_, s3) = tx.Enqueue(9, B(3), 0);
        Assert.Empty(rx.OnData(5, stream, s2, B(2), 0));       // en avance : attend le 1
        Assert.Empty(rx.OnData(5, stream, s3, B(3), 0));
        var got = rx.OnData(5, stream, s1, B(1), 0);
        Assert.Equal(new byte[] { 1, 2, 3 }, got.Select(p => p[0]));
        Assert.Empty(rx.OnData(5, stream, s2, B(2), 0));       // doublon (accusé perdu)
    }

    [Fact]
    public void ResendsUntilAcked_ThenGivesUp()
    {
        var tx = new ReliableChannel(1);
        var (stream, seq) = tx.Enqueue(9, B(1), 0);
        Assert.Empty(tx.Due(100));
        Assert.Single(tx.Due(ReliableChannel.ResendMs + 1));
        Assert.Empty(tx.Due(ReliableChannel.ResendMs + 2));    // pas deux fois de suite
        tx.OnAck(9, stream, seq);
        Assert.Empty(tx.Due(10000));
        tx.Enqueue(9, B(2), 0);
        Assert.Empty(tx.Due(ReliableChannel.GiveUpMs + 1));
        Assert.Equal(1, tx.GivenUp);
    }

    [Fact]
    public void NewStream_ResetsReception_AndOldGapIsSkipped()
    {
        var rx = new ReliableChannel(2);
        Assert.Single(rx.OnData(5, 111, 1, B(1), 0));
        Assert.Single(rx.OnData(5, 222, 1, B(7), 0));           // l'émetteur nous a oubliés : nouveau flux
        Assert.Empty(rx.OnData(5, 222, 3, B(9), 0));            // le 2 a été abandonné par l'émetteur
        var skipped = new List<(ulong, byte[])>();
        rx.Due(ReliableChannel.GapSkipMs + 1, skipped);
        Assert.Equal(9, Assert.Single(skipped).Item2[0]);
    }

    [Fact]
    public void ClockSync_KeepsLowestRttSample()
    {
        var c = new ClockSync();
        // Le pair a 1000 ms d'avance. Échantillon lent (aller 80, retour 20) puis rapide et symétrique.
        c.Add(pingSentMs: 0, responderMs: 1080, pongRecvMs: 100);
        Assert.Equal(1030, c.OffsetMs);
        c.Add(pingSentMs: 200, responderMs: 1205, pongRecvMs: 210);
        Assert.Equal(1000, c.OffsetMs);
        Assert.Equal(10, c.BestRttMs);
        c.Add(pingSentMs: 300, responderMs: 1390, pongRecvMs: 400);
        Assert.Equal(1000, c.OffsetMs);
    }

    [Fact]
    public void UdpNodes_ExchangeReliableMessages_DespiteLoss()
    {
        int pa = 48900 + Environment.ProcessId % 50 * 2, pb = pa + 1;
        UdpNode Node(int port, int peer, string name)
        {
            var n = new UdpNode(new NodeOptions
            {
                Port = port, DiscoveryPort = port, Broadcast = false, AnnounceIntervalMs = 50, PingIntervalMs = 100,
                PeerTimeoutMs = 3000, SimulatedLoss = 0.3, ManualPeers = { new IPEndPoint(IPAddress.Loopback, peer) },
            });
            Assert.True(n.Start(), n.Status);
            n.SetLocal(name, 1, "", false);
            return n;
        }
        using var a = Node(pa, pb, "A");
        using var b = Node(pb, pa, "B");
        Assert.True(SpinWait.SpinUntil(() => a.Peers().Count == 1 && b.Peers().Count == 1, 5000), "découverte");
        for (byte i = 1; i <= 30; i++) a.SendMessage(b.Id, B(i));
        var got = new List<byte>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (got.Count < 30 && DateTime.UtcNow < deadline)
        {
            while (b.TryGetEvent(out var e)) if (e.Kind == NetEventKind.Message) got.Add(e.Data[0]);
            Thread.Sleep(10);
        }
        Assert.Equal(Enumerable.Range(1, 30).Select(i => (byte)i), got);
        Assert.True(SpinWait.SpinUntil(() => a.PendingMessages(b.Id) == 0, 3000), "accusés");
        Assert.True(SpinWait.SpinUntil(() => b.Peer(a.Id)?.HasClock == true, 3000), "horloge");
        Assert.InRange(b.Peer(a.Id).ClockOffsetMs, -50, 50); // même processus, même horloge
    }
}
