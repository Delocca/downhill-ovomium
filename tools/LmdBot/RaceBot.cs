using System;
using System.Collections.Generic;
using System.Linq;
using LmdMulti.Core.Net;
using LmdMulti.Core.Race;

namespace LmdBot;

/// Pair de course simulé, avec la même logique que le mod (RaceHost / RaceGuest du cœur) :
///  - join : accepte les invitations, « charge » quelques secondes, se déclare prêt, puis franchit des
///    sections et arrive au bout de --race-time secondes (ou abandonne avec --race-dnf) ;
///  - host : lance une course sur --race-track dès qu'un vrai joueur est vu, et la joue de même.
internal sealed class RaceBot
{
    private const int Sections = 7;
    private readonly BotOptions _o;
    private readonly UdpNode _node;
    private readonly Random _rng;
    private IRaceSession _session;
    private double _acceptAt = double.NaN, _readyAt = double.NaN, _nextHostAt = double.NaN;
    private bool _hosted;

    public RaceBot(BotOptions o, UdpNode node)
    {
        _o = o;
        _node = node;
        _rng = new Random(o.Index * 7919 + 1);
    }

    /// Piste à annoncer : celle de la course une fois « chargée », null sinon.
    public string Track => _loaded && !_session.View.IsOver ? _session.View.Track : null;
    private bool _loaded;

    public void OnEvent(NetEvent e, double now)
    {
        if (e.Kind == NetEventKind.PeerLeft) _session?.OnPeerLeft(e.PeerId, now);
        if (e.Kind != NetEventKind.Message) return;
        var m = RaceMessages.Read(e.Data);
        if (m.Kind == RaceMsgKind.Invite) { OnInvite(e.PeerId, m, now); return; }
        _session?.OnMessage(e.PeerId, m, now);
    }

    private void OnInvite(ulong host, RaceMessage m, double now)
    {
        if (_session != null && !_session.View.IsOver) { _node.SendMessage(host, RaceMessages.Reply(m.RaceId, false)); return; }
        var g = new RaceGuest(m, host, _node.Id, _o.Name, now, _node.SendMessage, () => HostOffset(host));
        _session = g;
        _loaded = false;
        Program.Say($"{_o.Name} : invité par {m.HostName} sur {m.Track}{(_o.RaceDecline ? " (refus)" : "")}");
        if (_o.RaceDecline) g.Decline();
        else _acceptAt = now + 1000 + 500 * _o.Index;
    }

    private double? HostOffset(ulong host)
    {
        var p = _node.Peer(host);
        return p != null && p.HasClock ? p.ClockOffsetMs : null;
    }

    public void Tick(double now, List<PeerInfo> peers)
    {
        if (_o.RaceMode == "host") TryHost(now, peers);
        if (_session == null) return;
        if (now >= _acceptAt && _session is RaceGuest g) { _acceptAt = double.NaN; g.Accept(); _readyAt = now + _o.RaceLoadS * 1000; }
        if (now >= _readyAt) { _readyAt = double.NaN; _loaded = true; _session.Ready(now); Program.Say($"{_o.Name} : prêt"); }
        var phase = _session.View.Phase;
        _session.Tick(now);
        if (phase != _session.View.Phase) Program.Say($"{_o.Name} : course {Describe(_session.View)}");
        if (_session.View.Phase == RacePhase.Racing) Ride(now);
    }

    /// Course simulée : sections régulières, arrivée (ou abandon à mi-course) au bout du temps prévu.
    private void Ride(double now)
    {
        if (_session.Me?.State != RacerState.Racing) return;
        double elapsed = now - _session.View.StartAtMs, total = (_o.RaceTimeS + 4 * _o.Index) * 1000;
        if (_o.RaceDnf && elapsed > total / 2) { _session.Abandon(now); Program.Say($"{_o.Name} : abandon"); return; }
        if (elapsed >= total) { _session.Finish((uint)elapsed, now); Program.Say($"{_o.Name} : arrivée en {Ranking.FormatTime(elapsed)}"); return; }
        _session.Progress((int)(elapsed / total * Sections), now);
    }

    /// Hôte : invite tous les pairs compatibles (sauf les autres bots hôtes) quand un vrai joueur est là.
    private void TryHost(double now, List<PeerInfo> peers)
    {
        if (_hosted || _o.Index != 0) return;
        var real = peers.Where(p => p.IsCompatible && !p.IsEcho && p.ModVersion != "bot").ToList();
        if (real.Count == 0) { _nextHostAt = double.NaN; return; }
        if (double.IsNaN(_nextHostAt)) { _nextHostAt = now + _o.RaceDelayS * 1000; return; }
        if (now < _nextHostAt) return;
        _hosted = true;
        string track = _o.RaceTrack ?? real.FirstOrDefault(p => p.OnTrack)?.Track ?? "CA_01_A2";
        var invitees = peers.Where(p => p.IsCompatible && !p.IsEcho).Select(p => (p.Id, p.Name));
        _session = new RaceHost((uint)_rng.Next(), track, _node.Id, _o.Name, invitees, now, _node.SendMessage);
        _readyAt = now + _o.RaceLoadS * 1000;
        Program.Say($"{_o.Name} : course lancée sur {track}, {((RaceHost)_session).InvitedCount} invité(s)");
    }

    public static string Describe(Standings s)
    {
        string list = string.Join(", ", s.Entries.Where(e => e.IsListed).Select(e =>
            $"{e.Name}={e.State}{(e.State == RacerState.Finished ? " " + Ranking.FormatTime(e.TimeMs) : "")}"));
        return $"{s.Phase}{(s.Reason != CancelReason.None ? $" ({s.Reason})" : "")} [{list}]";
    }
}
