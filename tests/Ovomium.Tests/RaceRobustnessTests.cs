using Ovomium.Core.Race;

namespace Ovomium.Tests;

/// Scénarios de robustesse de la course, sur plusieurs joueurs reliés par une file en mémoire
/// (RaceLobby, comme le mod et le bot) : hôte qui part, invité qui se coupe puis revient, lancements
/// simultanés, filet de l'invité. Une coupure jette les messages dans les deux sens (comme le canal
/// fiable qui oublie un pair disparu) et chacun voit l'autre partir (PeerLeft), puis revenir (PeerJoined).
public class RaceRobustnessTests
{
    private const string Track = "CA_01_A2";

    private sealed class Net
    {
        public readonly Dictionary<ulong, RaceLobby> Nodes = new();
        private readonly HashSet<ulong> _offline = new();
        private readonly Queue<(ulong from, ulong to, byte[] data)> _queue = new();
        public double Now = 1000;

        public RaceLobby Add(ulong id) =>
            Nodes[id] = new RaceLobby(id, (to, data) => _queue.Enqueue((id, to, data)), _ => 0.0, (int)id);

        public IRaceSession S(ulong id) => Nodes[id].Session;
        public RaceGuest G(ulong id) => (RaceGuest)Nodes[id].Session;
        public RaceHost H(ulong id) => (RaceHost)Nodes[id].Session;

        public void Deliver()
        {
            while (_queue.TryDequeue(out var m))
                if (!_offline.Contains(m.from) && !_offline.Contains(m.to) && Nodes.TryGetValue(m.to, out var l))
                    l.OnMessage(m.from, m.data, Now, $"P{m.to}");
        }

        public void Tick(double dt = 0)
        {
            Now += dt;
            Deliver();
            foreach (var l in Nodes.Values) l.Tick(Now);
            Deliver();
        }

        /// Coupure réseau (ou jeu fermé) : chacun voit l'autre partir.
        public void Cut(ulong id)
        {
            _offline.Add(id);
            foreach (var (other, l) in Nodes.Where(n => n.Key != id))
            {
                l.OnPeerLeft(id, Now);
                Nodes[id].OnPeerLeft(other, Now);
            }
        }

        /// Retour de la même instance (même identifiant) : chacun revoit l'autre.
        public void Restore(ulong id)
        {
            _offline.Remove(id);
            foreach (var (other, l) in Nodes.Where(n => n.Key != id))
            {
                l.OnPeerBack(id, Now);
                Nodes[id].OnPeerBack(other, Now);
            }
        }

        /// Messages perdus sans que personne ne voie de départ (coupure plus courte que l'expiration).
        public void Mute(ulong id) => _offline.Add(id);
        public void Unmute(ulong id) => _offline.Remove(id);
    }

    /// Hôte 1, invités donnés ; tout le monde accepte et se déclare prêt ; renvoie le réseau au compte à rebours.
    private static Net ToCountdown(params ulong[] guests)
    {
        var net = new Net();
        net.Add(1);
        foreach (var g in guests) net.Add(g);
        net.Nodes[1].Host(Track, guests.Select(g => (g, $"P{g}")), "Hôte", net.Now);
        net.Deliver();
        foreach (var g in guests) net.G(g).Accept();
        net.Tick(100);
        net.S(1).Ready(net.Now);
        foreach (var g in guests) net.S(g).Ready(net.Now);
        net.Tick(100);
        Assert.Equal(RacePhase.Countdown, net.S(1).View.Phase);
        return net;
    }

    [Fact]
    public void HostQuitsDuringRace_GuestsFinishAlone_WithPartialResults()
    {
        var net = ToCountdown(2, 3);
        net.Tick(RaceRules.CountdownLeadMs);
        net.S(2).Progress(2, net.Now);
        net.Tick(100);
        net.Cut(1); // jeu de l'hôte fermé brutalement (vu après 5 s de silence) ou départ propre (Bye)
        var g2 = net.G(2);
        Assert.True(g2.HostGone);
        Assert.Equal(RacePhase.Racing, g2.View.Phase); // la descente continue
        Assert.Equal(RacerState.Dnf, g2.View.Find(1).State);
        g2.Finish(42000, net.Now);
        net.Tick(100);
        Assert.Equal(RacePhase.Finished, g2.View.Phase); // résultats partiels, chez soi
        Assert.Equal(CancelReason.HostLeft, g2.View.Reason);
        Assert.Equal(1, Ranking.PlaceOf(g2.View, 2));
        Assert.Equal(2UL, g2.View.Entries[0].Id); // rangé en tête (l'hôte parti passe derrière)
        Assert.Equal(RacePhase.Racing, net.S(3).View.Phase); // l'autre roule encore
        net.S(3).Abandon(net.Now);
        net.Tick(100);
        Assert.Equal(RacePhase.Finished, net.S(3).View.Phase);
    }

    [Fact]
    public void HostQuitsDuringCountdown_GuestsStillStartTogether()
    {
        var net = ToCountdown(2, 3);
        double start = net.S(2).View.StartAtMs;
        net.Cut(1);
        net.Tick(RaceRules.CountdownLeadMs);
        Assert.Equal(RacePhase.Racing, net.S(2).View.Phase);
        Assert.Equal(RacerState.Racing, net.S(3).Me.State);
        Assert.Equal(start, net.S(3).View.StartAtMs, 3);
    }

    [Fact]
    public void HostQuitsBeforeStart_GuestsCancelled()
    {
        var net = new Net();
        net.Add(1); net.Add(2);
        net.Nodes[1].Host(Track, new[] { (2UL, "P2") }, "Hôte", net.Now);
        net.Deliver();
        net.G(2).Accept();
        net.Tick(100);
        net.Cut(1);
        Assert.Equal(RacePhase.Cancelled, net.S(2).View.Phase);
        Assert.Equal(CancelReason.HostLeft, net.S(2).View.Reason);
    }

    [Fact]
    public void GuestCutDuringRace_ComesBack_AndItsFinishCounts()
    {
        var net = ToCountdown(2, 3);
        net.Tick(RaceRules.CountdownLeadMs);
        net.Cut(2);
        Assert.Equal(RacerState.Dnf, net.H(1).View.Find(2).State);
        Assert.True(net.G(2).HostGone);
        net.S(2).Finish(30000, net.Now); // arrivé pendant la coupure : message perdu
        net.Tick(100);
        Assert.Equal(RacePhase.Finished, net.S(2).View.Phase); // fini seul en attendant
        net.Restore(2);
        net.Tick(100);
        net.Tick(100);
        Assert.False(net.G(2).HostGone);
        Assert.Equal(RacerState.Finished, net.H(1).View.Find(2).State);
        Assert.Equal(30000u, net.H(1).View.Find(2).TimeMs);
        Assert.Equal(RacePhase.Racing, net.S(2).View.Phase); // classement de l'hôte de nouveau suivi
        Assert.Equal(RacerState.Finished, net.S(3).View.Find(2).State);
    }

    [Fact]
    public void GuestCutDuringLoading_IsOutCleanly_RaceGoesOn()
    {
        var net = new Net();
        net.Add(1); net.Add(2); net.Add(3);
        net.Nodes[1].Host(Track, new[] { (2UL, "P2"), (3UL, "P3") }, "Hôte", net.Now);
        net.Deliver();
        net.G(2).Accept(); net.G(3).Accept();
        net.Tick(100);
        net.Cut(2);
        Assert.Equal(RacerState.Excluded, net.H(1).View.Find(2).State);
        Assert.Equal(CancelReason.HostLeft, net.S(2).View.Reason); // de son côté : hôte disparu, annulé
        net.Restore(2);
        net.Tick(100);
        net.Tick(100);
        // Revenu pendant le chargement : l'hôte lui a rendu sa place, mais il avait abandonné ; il le dit.
        Assert.False(net.H(1).View.Find(2).IsActive);
        net.S(1).Ready(net.Now); net.S(3).Ready(net.Now);
        net.Tick(100);
        Assert.Equal(RacePhase.Countdown, net.S(1).View.Phase); // la course n'attend pas l'absent
    }

    [Fact]
    public void ShortSilence_HostRevivesLoadingGuest()
    {
        var net = new Net();
        net.Add(1); net.Add(2); net.Add(3);
        net.Nodes[1].Host(Track, new[] { (2UL, "P2"), (3UL, "P3") }, "Hôte", net.Now);
        net.Deliver();
        net.G(2).Accept(); net.G(3).Accept();
        net.Tick(100);
        net.H(1).OnPeerLeft(2, net.Now); // seul l'hôte l'a perdu de vue un moment
        net.H(1).OnPeerBack(2, net.Now);
        net.Tick(100);
        Assert.Equal(RacerState.Loading, net.H(1).View.Find(2).State);
        net.S(1).Ready(net.Now); net.S(2).Ready(net.Now); net.S(3).Ready(net.Now);
        net.Tick(100);
        Assert.Equal(RacePhase.Countdown, net.S(2).View.Phase);
    }

    [Fact]
    public void ExclusionLost_GuestIsToldWhenItSpeaks()
    {
        var net = new Net();
        net.Add(1); net.Add(2); net.Add(3);
        net.Nodes[1].Host(Track, new[] { (2UL, "P2"), (3UL, "P3") }, "Hôte", net.Now);
        net.Deliver();
        net.G(2).Accept(); net.G(3).Accept();
        net.Tick(100);
        net.S(1).Ready(net.Now); net.S(3).Ready(net.Now);
        net.Mute(2);
        net.Tick(RaceRules.LoadTimeoutMs + 1); // exclu, mais le message d'exclusion se perd
        net.Unmute(2);
        Assert.Equal(RacePhase.Gathering, net.S(2).View.Phase);
        net.S(2).Ready(net.Now);
        net.Tick(100);
        Assert.Equal(RacePhase.Cancelled, net.S(2).View.Phase);
        Assert.Equal(CancelReason.Excluded, net.S(2).View.Reason);
    }

    [Fact]
    public void GuestGivesUp_WhenHostNeverStarts()
    {
        var net = new Net();
        net.Add(1); net.Add(2);
        net.Nodes[1].Host(Track, new[] { (2UL, "P2") }, "Hôte", net.Now);
        net.Deliver();
        net.G(2).Accept();
        net.Tick(100);
        net.Mute(1); // hôte figé (jeu bloqué) mais toujours « présent »
        net.Nodes.Remove(1);
        net.Tick(RaceRules.InviteWindowMs + RaceRules.GuestGiveUpMs - 1000);
        Assert.Equal(RacePhase.Gathering, net.S(2).View.Phase);
        net.Tick(2000);
        Assert.Equal(RacePhase.Cancelled, net.S(2).View.Phase);
        Assert.Equal(CancelReason.HostLost, net.S(2).View.Reason);
    }

    [Theory]
    [InlineData(false, Track)]
    [InlineData(true, Track)]
    [InlineData(false, "AL_01_A1")]
    public void SimultaneousLaunch_LowestIdWins_Everywhere(bool lowFirst, string otherTrack)
    {
        const ulong Low = 5, High = 9, Third = 7;
        var net = new Net();
        net.Add(Low); net.Add(High); net.Add(Third);
        void HostLow() => net.Nodes[Low].Host(Track, new[] { (High, "P9"), (Third, "P7") }, "P5", net.Now);
        void HostHigh() => net.Nodes[High].Host(otherTrack, new[] { (Low, "P5"), (Third, "P7") }, "P9", net.Now);
        if (lowFirst) { HostLow(); HostHigh(); } else { HostHigh(); HostLow(); } // invitations croisées
        net.Tick(100);
        Assert.IsType<RaceHost>(net.S(Low));
        Assert.Equal(Low, net.G(High).HostId);
        Assert.Equal(Low, net.G(Third).HostId); // le tiers garde l'invitation prioritaire
        Assert.True(net.G(Third).IsPending);
        bool same = otherTrack == Track;
        Assert.Equal(!same, net.G(High).IsPending); // même piste : acceptée d'office
        Assert.Equal(same ? RacerState.Loading : RacerState.Invited, net.H(Low).View.Find(High).State);
    }

    [Fact]
    public void LaunchAfterSomeoneAccepted_IsNotAbandoned()
    {
        var net = new Net();
        net.Add(9); net.Add(5); net.Add(7);
        net.Nodes[9].Host(Track, new[] { (7UL, "P7") }, "P9", net.Now);
        net.Deliver();
        net.G(7).Accept();
        net.Tick(100);
        net.Nodes[5].Host(Track, new[] { (9UL, "P9") }, "P5", net.Now); // 5 était occupé ailleurs, puis lance la sienne
        net.Tick(100);
        Assert.IsType<RaceHost>(net.S(9)); // 9 garde sa course déjà acceptée, refuse celle de 5
        Assert.Equal(RacerState.Declined, net.H(5).View.Find(9).State);
    }

    [Fact]
    public void InviteWhileRacing_IsDeclined_PendingReplacedOnlyByPriority()
    {
        var net = ToCountdown(2);
        net.Add(8);
        net.Nodes[8].Host(Track, new[] { (2UL, "P2") }, "P8", net.Now);
        net.Tick(100);
        Assert.Equal(RacerState.Declined, net.H(8).View.Find(2).State);
        Assert.Equal(1UL, net.G(2).HostId);

        net.S(2).Abandon(net.Now); // sorti de la course : de nouveau libre
        net.Tick(100);
        Assert.False(net.Nodes[2].Busy);
        net.Add(9);
        net.Nodes[9].Host("AL_01_A1", new[] { (2UL, "P2") }, "P9", net.Now);
        net.Tick(100);
        Assert.Equal(9UL, net.G(2).HostId);
        Assert.True(net.G(2).IsPending);

        var lobby = new RaceLobby(50, (_, _) => { }, _ => null);
        lobby.OnMessage(30, RaceMessages.Invite(1, Track, "P30"), 0, "moi");
        Assert.Equal(InviteChoice.Decline, lobby.OnMessage(40, RaceMessages.Invite(2, Track, "P40"), 0, "moi").Choice);
        Assert.Equal(InviteChoice.Show, lobby.OnMessage(20, RaceMessages.Invite(3, Track, "P20"), 0, "moi").Choice);
        Assert.Equal(20UL, lobby.PendingInvite.HostId);
    }
}
