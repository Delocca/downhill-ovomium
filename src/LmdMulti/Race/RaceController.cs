using System;
using System.Linq;
using LmdMulti.Core.Net;
using LmdMulti.Core.Race;
using LmdMulti.Game;
using LmdMulti.UI;
using UnityEngine;

namespace LmdMulti.Race;

/// Mode course : relie la session (RaceHost / RaceGuest du cœur), le réseau, les touches, la
/// participation côté jeu (RaceRun) et l'affichage (RaceHud). Une seule course à la fois.
internal sealed class RaceController
{
    private const double ResultsShowMs = 30000, AbandonConfirmMs = 3000;
    private static readonly Color Info = new(1f, 0.85f, 0.4f), Bad = new(1f, 0.55f, 0.45f);

    private readonly UdpNode _node;
    private readonly Toasts _toasts;
    private readonly RaceRun _run = new();
    private readonly System.Random _rng = new();
    private RacePhase _lastPhase;
    private double _abandonArmedUntil, _resultsUntil;

    public IRaceSession Session { get; private set; }
    public RaceRun Run => _run;
    public bool ResultsVisible(double now) => Session?.View.Phase == RacePhase.Finished && now < _resultsUntil;

    public RaceController(UdpNode node, Toasts toasts)
    {
        _node = node;
        _toasts = toasts;
    }

    private bool Busy => Session != null && !Session.View.IsOver && !(Session is RaceGuest g && g.IsPending);

    public void OnMessage(ulong from, byte[] data, double now, string myName)
    {
        RaceMessage m;
        try { m = RaceMessages.Read(data); }
        catch (FormatException) { return; }
        if (m.Kind != RaceMsgKind.Invite) { Session?.OnMessage(from, m, now); return; }
        if (Busy) { _node.SendMessage(from, RaceMessages.Reply(m.RaceId, false)); Log.Info($"course : invitation de {m.HostName} refusée (déjà en course)"); return; }
        if (Session is RaceGuest old && old.IsPending) old.Decline(); // une invitation plus récente remplace
        Session = new RaceGuest(m, from, _node.Id, myName, now, _node.SendMessage, () => HostOffset(from));
        _lastPhase = Session.View.Phase;
        Log.Info($"course : invitation de {m.HostName} sur {m.Track}");
    }

    private double? HostOffset(ulong host)
    {
        var p = _node.Peer(host);
        return p != null && p.HasClock ? p.ClockOffsetMs : null;
    }

    public void OnPeerLeft(ulong id, double now) => Session?.OnPeerLeft(id, now);

    public void Update(double now)
    {
        if (TrackAccess.HasForced && !_run.Loading) TrackAccess.RestoreExcept(GameInfo.CurrentTrackId());
        if (Session == null) return;
        Session.Tick(now);
        string error = _run.Update(Session, now);
        if (error != null) Fail(error, now);
        if (Session.View.Phase != _lastPhase) OnPhaseChanged(now);
        if (Session.View.IsOver) _run.End();
    }

    private void Fail(string error, double now)
    {
        _toasts.Show(error, Bad, "race");
        if (Session is RaceHost h && h.View.Phase == RacePhase.Gathering) h.Cancel(CancelReason.HostFailed);
        else Session.Abandon(now);
        _run.End();
    }

    private void OnPhaseChanged(double now)
    {
        _lastPhase = Session.View.Phase;
        var v = Session.View;
        Log.Info($"course : {v.Phase} {(v.Reason != CancelReason.None ? v.Reason.ToString() : "")}");
        if (v.Phase == RacePhase.Finished) _resultsUntil = now + ResultsShowMs;
        if (v.Phase == RacePhase.Cancelled && CancelText(v.Reason) is string text) _toasts.Show(text, Bad, "race");
    }

    private static string CancelText(CancelReason r) => r switch
    {
        CancelReason.HostCancelled => "Course annulée par l'hôte",
        CancelReason.NobodyJoined => "Personne n'a rejoint la course",
        CancelReason.HostLeft => "L'hôte a quitté : course annulée",
        CancelReason.Excluded => "Chargement trop long : la course est partie sans toi",
        CancelReason.TooLate => "Trop tard : la course est déjà partie",
        CancelReason.HostFailed => "L'hôte n'a pas pu charger la piste : course annulée",
        _ => null,
    };

    /// F6 : accepter / lancer / partir sans attendre / fermer. F5 : refuser / annuler / abandonner.
    public void HandleKeys(double now, string myName)
    {
        if (Input.GetKeyDown(Config.Key(Config.RaceKey))) OnRaceKey(now, myName);
        if (Input.GetKeyDown(Config.Key(Config.RaceQuitKey))) OnQuitKey(now);
    }

    private void OnRaceKey(double now, string myName)
    {
        if (Session is RaceGuest g && g.IsPending)
        {
            g.Accept();
            _run.Begin(g.View.Track, now);
            _toasts.Show($"Course acceptée : chargement de {GameInfo.TrackName(g.View.Track)}", Info, "race");
        }
        else if (Session is RaceHost h && h.View.Phase == RacePhase.Gathering)
        {
            if (!h.ForceStart(now)) _toasts.Show("Attends d'être au départ pour lancer la course", Info, "race");
        }
        else if (ResultsVisible(now)) _resultsUntil = 0;
        else if (!Busy) Host(now, myName);
    }

    private void Host(double now, string myName)
    {
        string track = GameInfo.CurrentTrackId() ?? GameInfo.SelectedTrackId();
        if (track == null) { _toasts.Show("Choisis d'abord une piste", Info, "race"); return; }
        var peers = _node.Peers().Where(p => p.IsCompatible && !p.IsEcho).Select(p => (p.Id, p.Name)).ToList();
        if (peers.Count == 0) { _toasts.Show("Aucun autre joueur détecté : pas de course possible", Info, "race"); return; }
        var host = new RaceHost((uint)_rng.Next(1, int.MaxValue), track, _node.Id, myName, peers, now, _node.SendMessage);
        Session = host;
        _lastPhase = host.View.Phase;
        _run.Begin(track, now);
        _toasts.Show($"Course sur {GameInfo.TrackName(track)} : {host.InvitedCount} joueur(s) invité(s)", Info, "race");
    }

    private void OnQuitKey(double now)
    {
        if (Session == null) return;
        if (Session is RaceGuest g && g.IsPending) { g.Decline(); return; }
        if (ResultsVisible(now)) { _resultsUntil = 0; return; }
        if (Session is RaceHost h && h.View.Phase == RacePhase.Gathering)
        {
            h.Cancel(CancelReason.HostCancelled);
            _toasts.Show("Course annulée", Info, "race");
            return;
        }
        if (Session.View.IsOver || Session.Me == null || !Session.Me.IsActive) return;
        if (now > _abandonArmedUntil)
        {
            _abandonArmedUntil = now + AbandonConfirmMs;
            _toasts.Show($"Appuie encore sur {Config.RaceQuitKey.Value} pour abandonner", Info, "race");
            return;
        }
        Session.Abandon(now);
        _run.End();
        _toasts.Show("Tu as abandonné", Bad, "race");
    }

    /// Départ du mod (fermeture du jeu) : abandon propre.
    public void Shutdown(double now)
    {
        if (Session != null && !Session.View.IsOver) Session.Abandon(now);
        _run.End();
    }
}
