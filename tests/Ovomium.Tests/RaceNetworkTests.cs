using System.Diagnostics;
using System.Net;
using Ovomium.Core.Net;
using Ovomium.Core.Race;

namespace Ovomium.Tests;

/// Course complète entre vrais nœuds UDP sur la boucle locale, avec pertes simulées : 15 joueurs, dont
/// 8 en course (l'hôte + 7 qui acceptent) et 7 qui refusent, 20 % des paquets jetés à l'émission
/// (annonces, pings, messages fiables, accusés). Vérifie départ commun, classement complet et refus.
public class RaceNetworkTests
{
    private const int Players = 15, Racers = 8;

    private sealed class Player
    {
        public UdpNode Node;
        public RaceLobby Lobby;
        public int Index;
        public double FinishAt = double.NaN;
    }

    [Fact]
    public void FifteenPeers_EightRacing_TwentyPercentLoss()
    {
        int basePort = 51000 + Environment.ProcessId % 400 * 16;
        var players = Enumerable.Range(0, Players).Select(i => Create(i, basePort)).ToList();
        try
        {
            Run(players, p => p.Node.Peers().Count(x => x.IsCompatible) == Players - 1, 8000, "découverte");
            var host = players[0].Lobby.Host("CA_01_A2", players[0].Node.Peers().Select(x => (x.Id, x.Name)), "P0", NetClock.NowMs);
            Assert.Equal(Players - 1, host.InvitedCount);
            host.Ready(NetClock.NowMs);
            Run(players, _ => host.View.IsOver, 20000, "fin de course");

            Assert.Equal(RacePhase.Finished, host.View.Phase);
            Assert.Equal(Racers, host.View.Entries.Count(e => e.State == RacerState.Finished));
            Assert.Equal(Players - Racers, host.View.Entries.Count(e => e.State == RacerState.Declined));
            foreach (var p in players.Skip(1).Take(Racers - 1))
            {
                var s = p.Lobby.Session;
                Assert.InRange(s.View.StartAtMs - host.View.StartAtMs, -30, 30); // même horloge ici : décalage ≈ 0
                Run(players, _ => s.View.Phase == RacePhase.Finished, 5000, $"résultats chez P{p.Index}");
                Assert.Equal(Racers, s.View.Entries.Count(e => e.State == RacerState.Finished));
            }
        }
        finally { foreach (var p in players) p.Node.Dispose(); }
    }

    private static Player Create(int i, int basePort)
    {
        var o = new NodeOptions
        {
            Port = basePort + i, PortAttempts = 1, DiscoveryPort = basePort, Broadcast = false, SimulatedLoss = 0.2,
            AnnounceIntervalMs = 200, PingIntervalMs = 200, PeerTimeoutMs = 4000,
        };
        for (int j = 0; j < Players; j++)
            if (j != i) o.ManualPeers.Add(new IPEndPoint(IPAddress.Loopback, basePort + j));
        var node = new UdpNode(o);
        Assert.True(node.Start(), node.Status);
        node.SetLocal($"P{i}", 0, "CA_01_A2", true);
        var p = new Player { Node = node, Index = i };
        p.Lobby = new RaceLobby(node.Id, node.SendMessage, id => node.Peer(id) is { HasClock: true } x ? x.ClockOffsetMs : null, i);
        return p;
    }

    /// Fait vivre tout le monde (événements, réponses, course simulée) jusqu'à `done` ou échec.
    private static void Run(List<Player> players, Func<Player, bool> done, int timeoutMs, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!players.All(done))
        {
            if (watch.ElapsedMilliseconds > timeoutMs) throw new TimeoutException($"{what} : délai dépassé");
            foreach (var p in players) Step(p, NetClock.NowMs);
            Thread.Sleep(5);
        }
    }

    private static void Step(Player p, double now)
    {
        while (p.Node.TryGetEvent(out var e))
        {
            if (e.Kind == NetEventKind.PeerLeft) p.Lobby.OnPeerLeft(e.PeerId, now);
            else if (e.Kind == NetEventKind.PeerJoined) p.Lobby.OnPeerBack(e.PeerId, now);
            else if (e.Kind == NetEventKind.Message && p.Lobby.OnMessage(e.PeerId, e.Data, now, $"P{p.Index}") is { Choice: InviteChoice.Show })
            {
                var g = p.Lobby.PendingInvite;
                if (p.Index < Racers) { g.Accept(); g.Ready(now); } // chargement instantané
                else g.Decline();
            }
        }
        p.Lobby.Tick(now);
        var s = p.Lobby.Session;
        if (s?.View.Phase != RacePhase.Racing || s.Me?.State != RacerState.Racing) return;
        if (double.IsNaN(p.FinishAt)) p.FinishAt = s.View.StartAtMs + 300 + 60 * p.Index;
        s.Progress(1, now);
        if (now >= p.FinishAt) s.Finish((uint)(now - s.View.StartAtMs), now);
    }
}
