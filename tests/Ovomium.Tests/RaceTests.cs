using Ovomium.Core.Race;

namespace Ovomium.Tests;

/// Machine à états de course : un hôte et des invités reliés par une file en mémoire (messages encodés
/// puis décodés comme sur le réseau). Horloge simulée ; l'horloge de l'hôte a 5 s d'avance sur les invités.
public class RaceTests
{
    private const ulong Host = 1, G1 = 2, G2 = 3;
    private const double HostAhead = 5000;

    private sealed class Bus
    {
        public readonly Queue<(ulong from, ulong to, byte[] data)> Queue = new();
        public RaceHost H;
        public readonly Dictionary<ulong, RaceGuest> Guests = new();
        public double Now; // horloge des invités ; l'hôte lit Now + HostAhead

        public Action<ulong, byte[]> From(ulong from) => (to, data) => Queue.Enqueue((from, to, data));

        public void Deliver()
        {
            while (Queue.TryDequeue(out var m))
            {
                var msg = RaceMessages.Read(m.data);
                if (m.to == Host) H.OnMessage(m.from, msg, Now + HostAhead);
                else if (Guests.TryGetValue(m.to, out var g)) g.OnMessage(m.from, msg, Now);
                else if (msg.Kind == RaceMsgKind.Invite)
                    Guests[m.to] = new RaceGuest(msg, m.from, m.to, $"G{m.to}", Now, From(m.to), () => HostAhead);
            }
        }

        public void Tick(double dt = 0)
        {
            Now += dt;
            Deliver();
            H.Tick(Now + HostAhead);
            Deliver();
            foreach (var g in Guests.Values) g.Tick(Now);
            Deliver();
        }
    }

    private static Bus Start(params ulong[] guests)
    {
        var bus = new Bus();
        bus.H = new RaceHost(42, "CA_01_A2", Host, "Edia", guests.Select(g => (g, $"G{g}")), HostAhead, bus.From(Host));
        bus.Deliver();
        return bus;
    }

    [Fact]
    public void FullRace_CountdownSynced_RankingAndGaps()
    {
        var bus = Start(G1, G2);
        Assert.True(bus.Guests[G1].IsPending);
        Assert.Equal("Edia", bus.Guests[G1].View.HostName);
        bus.Guests[G1].Accept(); bus.Guests[G2].Accept();
        bus.Tick(100);
        bus.H.Ready(bus.Now + HostAhead); bus.Guests[G1].Ready(bus.Now);
        bus.Tick(100);
        Assert.Equal(RacePhase.Gathering, bus.H.View.Phase); // G2 charge encore
        bus.Guests[G2].Ready(bus.Now);
        bus.Tick(100);
        Assert.Equal(RacePhase.Countdown, bus.H.View.Phase);
        // Départ à la même heure réelle chez tous : heure de l'hôte − son avance.
        double start = bus.Guests[G1].View.StartAtMs;
        Assert.Equal(bus.H.View.StartAtMs - HostAhead, start, 3);
        Assert.Equal(start, bus.Guests[G2].View.StartAtMs, 3);

        bus.Tick(RaceRules.CountdownLeadMs);
        Assert.Equal(RacePhase.Racing, bus.Guests[G1].View.Phase);
        Assert.Equal(RacerState.Racing, bus.Guests[G1].Me.State);
        bus.Guests[G2].Progress(3, bus.Now);
        bus.Tick(100);
        Assert.Equal(G2, bus.H.View.Entries[0].Id); // en tête aux checkpoints
        bus.Guests[G1].Finish(30000, bus.Now);
        bus.H.Finish(31500, bus.Now + HostAhead);
        bus.Tick(100);
        Assert.Equal(new[] { G1, Host, G2 }, bus.Guests[G2].View.Entries.Select(e => e.Id));
        Assert.Equal(RacePhase.Racing, bus.H.View.Phase); // G2 a encore le temps
        bus.Guests[G2].Finish(40000, bus.Now);
        bus.Tick(100);
        Assert.Equal(RacePhase.Finished, bus.Guests[G1].View.Phase);
        Assert.Equal(3, Ranking.PlaceOf(bus.Guests[G1].View, G2));
        Assert.Equal("1:02.35", Ranking.FormatTime(62350));
    }

    [Fact]
    public void Decline_LateLoader_AndTimeout()
    {
        var bus = Start(G1, G2);
        bus.Guests[G1].Decline();
        bus.Guests[G2].Accept();
        bus.H.Ready(bus.Now + HostAhead);
        bus.Tick(100);
        Assert.Equal(RacerState.Declined, bus.H.View.Find(G1).State);
        bus.Tick(RaceRules.LoadTimeoutMs + 1); // G2 n'est jamais prêt : exclu, personne ne reste
        Assert.Equal(RacePhase.Cancelled, bus.H.View.Phase);
        Assert.Equal(CancelReason.NobodyJoined, bus.H.View.Reason);
        Assert.Equal(CancelReason.Excluded, bus.Guests[G2].View.Reason);
    }

    [Fact]
    public void ForceStart_ExcludesLoading_And_FinishWindowGivesDnf()
    {
        var bus = Start(G1, G2);
        bus.Guests[G1].Accept(); bus.Guests[G2].Accept();
        bus.Tick(100);
        Assert.False(bus.H.ForceStart(bus.Now + HostAhead)); // hôte pas prêt
        bus.H.Ready(bus.Now + HostAhead); bus.Guests[G1].Ready(bus.Now);
        bus.Tick(100);
        Assert.True(bus.H.ForceStart(bus.Now + HostAhead));
        bus.Tick(100);
        Assert.Equal(RacePhase.Cancelled, bus.Guests[G2].View.Phase);
        Assert.Equal(CancelReason.Excluded, bus.Guests[G2].View.Reason);
        bus.Tick(RaceRules.CountdownLeadMs);
        bus.H.Finish(20000, bus.Now + HostAhead);
        bus.Tick(100);
        Assert.False(double.IsNaN(bus.Guests[G1].View.EndAtMs));
        bus.Tick(RaceRules.FinishWindowMs);
        Assert.Equal(RacePhase.Finished, bus.Guests[G1].View.Phase);
        Assert.Equal(RacerState.Dnf, bus.Guests[G1].Me.State);
    }

    [Fact]
    public void GuestLeaving_IsDnf_HostLeaving_CancelsForGuest()
    {
        var bus = Start(G1, G2);
        bus.Guests[G1].Accept(); bus.Guests[G2].Accept();
        bus.H.Ready(bus.Now + HostAhead); bus.Guests[G1].Ready(bus.Now); bus.Guests[G2].Ready(bus.Now);
        bus.Tick(100);
        bus.Tick(RaceRules.CountdownLeadMs);
        bus.H.OnPeerLeft(G2, bus.Now + HostAhead);
        bus.Guests[G1].Abandon(bus.Now);
        bus.Tick(100);
        Assert.Equal(RacerState.Dnf, bus.H.View.Find(G2).State);
        Assert.Equal(RacerState.Dnf, bus.H.View.Find(G1).State);
        Assert.Equal(RacePhase.Racing, bus.H.View.Phase); // l'hôte roule encore
        bus.Guests[G1].OnPeerLeft(Host, bus.Now);
        Assert.Equal(CancelReason.HostLeft, bus.Guests[G1].View.Reason);
    }

    [Fact]
    public void Invitation_ExpiresWithoutAnswer()
    {
        var bus = Start(G1);
        bus.Tick(RaceRules.InviteWindowMs + 1);
        Assert.False(bus.Guests[G1].IsPending);
        Assert.Equal(RacerState.Declined, bus.H.View.Find(G1).State);
        Assert.Equal(RacePhase.Cancelled, bus.H.View.Phase); // hôte pas prêt, mais plus personne à attendre
    }
}
