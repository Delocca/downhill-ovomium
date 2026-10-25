using System;
using System.Collections.Generic;
using System.Linq;
using Ovomium.Core.Net;
using Ovomium.Core.Race;

namespace LmdBot;

/// Pair de course simulé, avec la même logique que le mod (RaceLobby, RaceHost / RaceGuest du cœur) :
///  - join : accepte les invitations, « charge » quelques secondes, se déclare prêt, puis franchit des
///    sections et arrive au bout de --race-time secondes (ou abandonne avec --race-dnf) ;
///  - host : lance une course sur --race-track dès qu'un vrai joueur est vu, et la joue de même.
internal sealed class RaceBot
{
    private const int Sections = 7;
    private readonly BotOptions _o;
    private readonly UdpNode _node;
    private readonly RaceLobby _lobby;
    private double _acceptAt = double.NaN, _readyAt = double.NaN, _nextHostAt = double.NaN;
    private bool _hosted, _loaded;
    private RacePhase _lastPhase;

    public RaceBot(BotOptions o, UdpNode node)
    {
        _o = o;
        _node = node;
        _lobby = new RaceLobby(node.Id, node.SendMessage, HostOffset, o.Index * 7919 + 1);
    }

    private IRaceSession Session => _lobby.Session;

    /// Piste à annoncer : celle de la course une fois « chargée », null sinon.
    public string Track => _loaded && !Session.View.IsOver ? Session.View.Track : null;

    public void OnEvent(NetEvent e, double now)
    {
        if (e.Kind == NetEventKind.PeerLeft) _lobby.OnPeerLeft(e.PeerId, now);
        if (e.Kind == NetEventKind.PeerJoined) _lobby.OnPeerBack(e.PeerId, now);
        if (e.Kind != NetEventKind.Message) return;
        var r = _lobby.OnMessage(e.PeerId, e.Data, now, _o.Name);
        if (r == null || r.Choice == InviteChoice.Decline) return;
        _loaded = false;
        _readyAt = double.NaN;
        Program.Say($"{_o.Name} : invité par {r.Invite.HostName} sur {r.Invite.Track} ({r.Choice}){(_o.RaceDecline ? " (refus)" : "")}");
        if (r.AutoAccepted) _readyAt = now + _o.RaceLoadS * 1000;
        else if (_o.RaceDecline) _lobby.PendingInvite?.Decline();
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
        if (Session == null) return;
        if (now >= _acceptAt)
        {
            _acceptAt = double.NaN;
            if (_lobby.PendingInvite is RaceGuest g) { g.Accept(); _readyAt = now + _o.RaceLoadS * 1000; }
        }
        if (now >= _readyAt) { _readyAt = double.NaN; _loaded = true; Session.Ready(now); Program.Say($"{_o.Name} : prêt"); }
        _lobby.Tick(now);
        if (_lastPhase != Session.View.Phase) // changé ici ou par un message de l'hôte
        {
            _lastPhase = Session.View.Phase;
            Program.Say($"{_o.Name} : course {Describe(Session.View)}");
        }
        if (Session.View.Phase == RacePhase.Racing) Ride(now);
    }

    /// Course simulée : sections régulières, arrivée (ou abandon à mi-course) au bout du temps prévu.
    private void Ride(double now)
    {
        if (Session.Me?.State != RacerState.Racing) return;
        double elapsed = now - Session.View.StartAtMs, total = (_o.RaceTimeS + 4 * _o.Index) * 1000;
        if (_o.RaceDnf && elapsed > total / 2) { Session.Abandon(now); Program.Say($"{_o.Name} : abandon"); return; }
        if (elapsed >= total) { Session.Finish((uint)elapsed, now); Program.Say($"{_o.Name} : arrivée en {Ranking.FormatTime(elapsed)}"); return; }
        Session.Progress((int)(elapsed / total * Sections), now);
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
        var host = _lobby.Host(track, invitees, _o.Name, now);
        _loaded = false;
        _readyAt = now + _o.RaceLoadS * 1000;
        Program.Say($"{_o.Name} : course lancée sur {track}, {host.InvitedCount} invité(s)");
    }

    public static string Describe(Standings s)
    {
        string list = string.Join(", ", s.Entries.Where(e => e.IsListed).Select(e =>
            $"{e.Name}={e.State}{(e.State == RacerState.Finished ? " " + Ranking.FormatTime(e.TimeMs) : "")}"));
        return $"{s.Phase}{(s.Reason != CancelReason.None ? $" ({s.Reason})" : "")} [{list}]";
    }
}
