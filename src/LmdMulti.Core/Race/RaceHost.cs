using System;
using System.Collections.Generic;
using System.Linq;

namespace LmdMulti.Core.Race;

/// Ce que le joueur local fait dans une course, qu'il en soit l'hôte ou un invité.
public interface IRaceSession
{
    bool IsHost { get; }
    ulong MyId { get; }
    /// Vue courante (classement, phase, heures locales de départ et de fin).
    Standings View { get; }
    RaceEntry Me { get; }
    void Ready(double now);
    void Progress(int sections, double now);
    void Finish(uint timeMs, double now);
    void Abandon(double now);
    void OnMessage(ulong from, RaceMessage m, double now);
    void OnPeerLeft(ulong id, double now);
    void Tick(double now);
}

/// Hôte : invite, attend les réponses et les chargements, fixe le départ, collecte les arrivées et
/// publie le classement (message Standings) à chaque changement. Sans réseau ni Unity : `send` émet.
public sealed class RaceHost : IRaceSession
{
    private readonly Action<ulong, byte[]> _send;
    private bool _dirty = true;

    public bool IsHost => true;
    public ulong MyId { get; }
    public Standings View { get; } = new();
    public RaceEntry Me => View.Find(MyId);

    public RaceHost(uint raceId, string track, ulong myId, string myName, IEnumerable<(ulong id, string name)> invitees,
                    double now, Action<ulong, byte[]> send)
    {
        _send = send;
        MyId = myId;
        View.RaceId = raceId; View.Track = track; View.HostId = myId;
        View.Entries.Add(new RaceEntry { Id = myId, Name = myName, State = RacerState.Loading });
        foreach (var (id, name) in invitees.Take(RaceRules.MaxRacers - 1))
        {
            View.Entries.Add(new RaceEntry { Id = id, Name = name, State = RacerState.Invited, DeadlineMs = now + RaceRules.InviteWindowMs });
            _send(id, RaceMessages.Invite(raceId, track, myName));
        }
    }

    public int InvitedCount => View.Entries.Count - 1;

    public void OnMessage(ulong from, RaceMessage m, double now)
    {
        var e = View.Find(from);
        if (m.RaceId != View.RaceId || e == null || e.Id == MyId) return;
        if (m.Kind == RaceMsgKind.Reply) OnReply(e, m.Accept, now);
        else if (m.Kind == RaceMsgKind.Status) Apply(e, m.State, m.Progress, m.TimeMs, now);
    }

    private void OnReply(RaceEntry e, bool accept, double now)
    {
        if (e.State != RacerState.Invited) return;
        if (!accept) { Set(e, RacerState.Declined); return; }
        if (View.Phase != RacePhase.Gathering) { Set(e, RacerState.Declined); _send(e.Id, RaceMessages.Cancel(View.RaceId, CancelReason.TooLate)); return; }
        e.DeadlineMs = now + RaceRules.LoadTimeoutMs;
        Set(e, RacerState.Loading);
    }

    /// Changement d'état d'un coureur (lui-même ou nous) : seulement vers l'avant.
    private void Apply(RaceEntry e, RacerState state, byte progress, uint timeMs, double now)
    {
        if (!e.IsActive) return; // arrivé, abandonné, exclu : définitif
        switch (state)
        {
            case RacerState.Ready when e.State == RacerState.Loading: Set(e, RacerState.Ready); break;
            case RacerState.Racing when View.Phase >= RacePhase.Countdown && progress > e.Progress:
                e.Progress = progress; e.ProgressAtMs = now; Set(e, RacerState.Racing); break;
            case RacerState.Finished when View.Phase is RacePhase.Countdown or RacePhase.Racing:
                e.TimeMs = timeMs;
                if (double.IsNaN(View.EndAtMs)) View.EndAtMs = now + RaceRules.FinishWindowMs;
                Set(e, RacerState.Finished); break;
            case RacerState.Dnf: Set(e, RacerState.Dnf); break;
        }
    }

    private void Set(RaceEntry e, RacerState s) { e.State = s; _dirty = true; }

    public void Ready(double now) => Apply(Me, RacerState.Ready, 0, 0, now);
    public void Progress(int sections, double now) => Apply(Me, RacerState.Racing, (byte)Math.Clamp(sections, 0, 255), 0, now);
    public void Finish(uint timeMs, double now) => Apply(Me, RacerState.Finished, 0, timeMs, now);
    public void Abandon(double now) => Apply(Me, RacerState.Dnf, 0, 0, now);

    public void OnPeerLeft(ulong id, double now)
    {
        var e = View.Find(id);
        if (e == null || id == MyId) return;
        if (e.State == RacerState.Invited) Set(e, RacerState.Declined);
        else if (e.IsActive) Set(e, View.Phase == RacePhase.Gathering ? RacerState.Excluded : RacerState.Dnf);
    }

    /// Annulation par l'hôte (avant le départ, ou chargement impossible).
    public void Cancel(CancelReason reason)
    {
        if (View.IsOver) return;
        View.Phase = RacePhase.Cancelled;
        View.Reason = reason;
        foreach (var e in View.Entries.Where(e => e.Id != MyId && e.State == RacerState.Invited))
            _send(e.Id, RaceMessages.Cancel(View.RaceId, reason)); // l'invitation disparaît chez eux
        _dirty = true;
    }

    /// Départ sans attendre les retardataires. Faux si l'hôte lui-même n'est pas prêt.
    public bool ForceStart(double now)
    {
        if (View.Phase != RacePhase.Gathering || Me.State != RacerState.Ready) return false;
        foreach (var e in View.Entries.Where(e => e.Id != MyId))
            if (e.State == RacerState.Invited) Drop(e, RacerState.Declined);
            else if (e.State == RacerState.Loading) Drop(e, RacerState.Excluded);
        TryStart(now);
        return true;
    }

    private void Drop(RaceEntry e, RacerState s)
    {
        Set(e, s);
        _send(e.Id, RaceMessages.Cancel(View.RaceId, s == RacerState.Excluded ? CancelReason.Excluded : CancelReason.TooLate));
    }

    public void Tick(double now)
    {
        switch (View.Phase)
        {
            case RacePhase.Gathering: TickGathering(now); break;
            case RacePhase.Countdown when now >= View.StartAtMs:
                View.Phase = RacePhase.Racing;
                foreach (var e in View.Entries.Where(e => e.State == RacerState.Ready)) e.State = RacerState.Racing;
                _dirty = true;
                break;
            case RacePhase.Racing: TickRacing(now); break;
        }
        if (_dirty) Publish(now);
    }

    private void TickGathering(double now)
    {
        foreach (var e in View.Entries.Where(e => e.Id != MyId && now > e.DeadlineMs))
            if (e.State == RacerState.Invited) Drop(e, RacerState.Declined);
            else if (e.State == RacerState.Loading) Drop(e, RacerState.Excluded);
        if (View.Entries.Any(e => e.Id != MyId && e.State is RacerState.Invited or RacerState.Loading)) return;
        if (!View.Entries.Any(e => e.Id != MyId && e.State == RacerState.Ready)) Cancel(CancelReason.NobodyJoined);
        else if (Me.State == RacerState.Ready) TryStart(now);
    }

    private void TryStart(double now)
    {
        if (!View.Entries.Any(e => e.Id != MyId && e.State == RacerState.Ready)) { Cancel(CancelReason.NobodyJoined); return; }
        View.Phase = RacePhase.Countdown;
        View.StartAtMs = now + RaceRules.CountdownLeadMs;
        _dirty = true;
    }

    private void TickRacing(double now)
    {
        bool timeUp = now >= View.EndAtMs || now >= View.StartAtMs + RaceRules.MaxRaceMs;
        if (!timeUp && View.Entries.Any(e => e.State == RacerState.Racing)) return;
        foreach (var e in View.Entries.Where(e => e.IsActive)) e.State = RacerState.Dnf;
        View.Phase = RacePhase.Finished;
        _dirty = true;
    }

    /// Classement trié, envoyé à chaque coureur engagé (pas aux invitations en attente, refus, exclus).
    private void Publish(double now)
    {
        _dirty = false;
        View.Revision++;
        Ranking.Sort(View.Entries);
        byte[] msg = RaceMessages.Standings(View, now);
        foreach (var e in View.Entries)
            if (e.Id != MyId && e.IsListed) _send(e.Id, msg);
    }
}
