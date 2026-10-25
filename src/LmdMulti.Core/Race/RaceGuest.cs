using System;

namespace LmdMulti.Core.Race;

/// Invité : invitation en attente de réponse, puis participation. L'hôte fait foi (message Standings) ;
/// notre propre état local (prêt, arrivé…) est gardé s'il est en avance sur le dernier classement reçu.
/// Si l'hôte disparaît avant le départ, la course est annulée ; après (compte à rebours ou course), on
/// la finit seul (HostGone) : chrono et arrivée locaux, classement figé, résultats partiels.
public sealed class RaceGuest : IRaceSession
{
    private readonly Action<ulong, byte[]> _send;
    private readonly Func<double?> _hostOffset;
    private RacerState _myState = RacerState.Invited;
    private uint _myTime;
    private byte _myProgress;

    public bool IsHost => false;
    public ulong MyId { get; }
    public ulong HostId { get; }
    public Standings View { get; private set; }
    public RaceEntry Me => View.Find(MyId);
    /// Invitation affichée, sans réponse encore.
    public bool IsPending { get; private set; } = true;
    public double InviteExpiresAtMs { get; }
    /// L'hôte a disparu après le départ : course finie seul (il peut revenir et reprendre la main).
    public bool HostGone { get; private set; }

    /// hostOffset : horloge de l'hôte − la nôtre (ms), null si inconnue (pas encore de pong).
    public RaceGuest(RaceMessage invite, ulong hostId, ulong myId, string myName, double now,
                     Action<ulong, byte[]> send, Func<double?> hostOffset)
    {
        _send = send;
        _hostOffset = hostOffset;
        MyId = myId;
        HostId = hostId;
        InviteExpiresAtMs = now + RaceRules.InviteWindowMs;
        View = new Standings { RaceId = invite.RaceId, Track = invite.Track, HostId = hostId };
        View.Entries.Add(new RaceEntry { Id = hostId, Name = invite.HostName, State = RacerState.Loading });
        View.Entries.Add(new RaceEntry { Id = myId, Name = myName, State = RacerState.Invited });
    }

    public void Accept()
    {
        if (!IsPending) return;
        IsPending = false;
        SetMine(RacerState.Loading);
        _send(HostId, RaceMessages.Reply(View.RaceId, true));
    }

    public void Decline()
    {
        if (!IsPending) return;
        IsPending = false;
        _send(HostId, RaceMessages.Reply(View.RaceId, false));
        End(CancelReason.None);
    }

    public void Ready(double now)
    {
        if (_myState != RacerState.Loading) return;
        SetMine(RacerState.Ready);
        _send(HostId, RaceMessages.Status(View.RaceId, RacerState.Ready, 0, 0));
    }

    public void Progress(int sections, double now)
    {
        byte p = (byte)Math.Clamp(sections, 0, 255);
        if (_myState != RacerState.Racing || p <= _myProgress) return;
        _myProgress = p;
        SetMine(RacerState.Racing);
        _send(HostId, RaceMessages.Status(View.RaceId, RacerState.Racing, p, 0));
    }

    public void Finish(uint timeMs, double now)
    {
        if (_myState is not (RacerState.Racing or RacerState.Ready)) return;
        _myTime = timeMs;
        SetMine(RacerState.Finished);
        _send(HostId, RaceMessages.Status(View.RaceId, RacerState.Finished, _myProgress, timeMs));
    }

    public void Abandon(double now)
    {
        if (IsPending) { Decline(); return; }
        if (_myState is not (RacerState.Loading or RacerState.Ready or RacerState.Racing)) return;
        SetMine(RacerState.Dnf);
        _send(HostId, RaceMessages.Status(View.RaceId, RacerState.Dnf, _myProgress, 0));
    }

    public void OnMessage(ulong from, RaceMessage m, double now)
    {
        if (from != HostId || m.RaceId != View.RaceId) return;
        if (View.Phase == RacePhase.Cancelled) { RefuseStale(m); return; }
        if (m.Kind == RaceMsgKind.Cancel) { IsPending = false; End(m.Reason); }
        else if (m.Kind == RaceMsgKind.Standings && m.Standings.Revision > View.Revision) Adopt(m.Standings, m.HostNowMs, now);
    }

    /// Course déjà quittée de notre côté (hôte cru parti, délai dépassé) : si l'hôte nous compte encore,
    /// on le lui dit, pour qu'il n'attende pas un coureur qui ne viendra pas.
    private void RefuseStale(RaceMessage m)
    {
        if (m.Kind != RaceMsgKind.Standings || m.Standings.Revision <= _refusedRevision) return;
        _refusedRevision = m.Standings.Revision;
        if (m.Standings.Find(MyId)?.IsActive == true) _send(HostId, RaceMessages.Status(View.RaceId, RacerState.Dnf, _myProgress, 0));
    }

    private uint _refusedRevision;

    /// Classement de l'hôte : heures converties dans notre horloge (décalage mesuré, sinon heure d'envoi).
    private void Adopt(Standings s, double hostNowMs, double now)
    {
        HostGone = false; // l'hôte est revenu : il reprend la main
        double? offset = _hostOffset();
        double ToLocal(double t) => double.IsNaN(t) ? t : offset.HasValue ? t - offset.Value : now + (t - hostNowMs);
        s.StartAtMs = ToLocal(s.StartAtMs);
        s.EndAtMs = ToLocal(s.EndAtMs);
        View = s;
        var me = Me;
        if (me == null) return;
        if (me.State <= RacerState.Racing && _myState > me.State && _myState <= RacerState.Dnf)
        {
            me.State = _myState; me.TimeMs = _myTime; me.Progress = _myProgress;
            // En avance sur l'hôte : notre dernier message s'est peut-être perdu (coupure), on le redit.
            _send(HostId, RaceMessages.Status(View.RaceId, _myState, _myProgress, _myTime));
        }
        else _myState = me.State; // l'hôte a tranché (départ, fin de course, exclusion)
    }

    public void OnPeerLeft(ulong id, double now)
    {
        if (id != HostId || View.IsOver) return;
        IsPending = false;
        if (View.Phase == RacePhase.Gathering || _myState is not (RacerState.Ready or RacerState.Racing or RacerState.Finished or RacerState.Dnf))
        {
            End(CancelReason.HostLeft);
            return;
        }
        HostGone = true; // départ fixé : chacun peut finir sa descente
        View.Reason = CancelReason.HostLeft;
        var host = View.Find(HostId);
        if (host != null && host.IsActive) host.State = RacerState.Dnf;
        Ranking.Sort(View.Entries);
    }

    public void Tick(double now)
    {
        if (IsPending && now > InviteExpiresAtMs) { IsPending = false; End(CancelReason.None); return; }
        if (View.Phase == RacePhase.Gathering && !IsPending && now > InviteExpiresAtMs + RaceRules.GuestGiveUpMs)
            End(CancelReason.HostLost);
        if (View.Phase == RacePhase.Countdown && now >= View.StartAtMs)
        {
            View.Phase = RacePhase.Racing; // localement, sans attendre l'hôte : départ commun à l'heure fixée
            foreach (var e in View.Entries) if (e.State == RacerState.Ready) e.State = RacerState.Racing;
            if (_myState == RacerState.Ready) _myState = RacerState.Racing;
        }
        // Sans hôte : la course finit pour nous quand nous avons fini (ou au bout du temps maximal).
        if (HostGone && View.Phase == RacePhase.Racing && (_myState is RacerState.Finished or RacerState.Dnf || now > View.StartAtMs + RaceRules.MaxRaceMs))
            View.Phase = RacePhase.Finished;
    }

    private void SetMine(RacerState s)
    {
        _myState = s;
        var me = Me;
        if (me != null) { me.State = s; me.TimeMs = _myTime; me.Progress = _myProgress; }
        if (HostGone) Ranking.Sort(View.Entries); // plus de classement de l'hôte : on se range soi-même
    }

    private void End(CancelReason reason)
    {
        if (View.IsOver) return;
        View.Phase = RacePhase.Cancelled;
        View.Reason = reason;
    }
}
