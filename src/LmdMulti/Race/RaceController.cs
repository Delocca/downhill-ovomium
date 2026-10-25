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
    private const double ResultsShowMs = 30000;
    private static readonly Color Info = new(1f, 0.85f, 0.4f), Bad = new(1f, 0.55f, 0.45f);

    private readonly UdpNode _node;
    private readonly Toasts _toasts;
    private readonly RaceLobby _lobby;
    private readonly RaceRun _run = new();
    private RacePhase _lastPhase;
    private double _resultsUntil, _nextRestoreCheck;

    public IRaceSession Session => _lobby.Session;
    public RaceRun Run => _run;
    public bool ResultsVisible(double now) => Session?.View.Phase == RacePhase.Finished && now < _resultsUntil;

    public RaceController(UdpNode node, Toasts toasts)
    {
        _node = node;
        _toasts = toasts;
        _lobby = new RaceLobby(node.Id, node.SendMessage, HostOffset);
    }

    private bool Busy => _lobby.Busy;

    public void OnMessage(ulong from, byte[] data, double now, string myName)
    {
        var r = _lobby.OnMessage(from, data, now, myName);
        if (r == null) return;
        var m = r.Invite;
        if (r.Choice == InviteChoice.Decline) { Log.Info($"course : invitation de {m.HostName} refusée (déjà en course)"); return; }
        _lastPhase = Session.View.Phase;
        Log.Info($"course : invitation de {m.HostName} sur {m.Track} ({r.Choice}{(r.AutoAccepted ? ", acceptée d'office" : "")})");
        if (r.Choice != InviteChoice.Yield) return;
        // Lancements simultanés : la course de l'autre est retenue (RaceInvites), la nôtre disparaît.
        _run.End();
        if (r.AutoAccepted) _run.Begin(m.Track, now);
        _toasts.Show(r.AutoAccepted ? $"{m.HostName} a lancé la même course en même temps : tu la rejoins"
                                    : $"{m.HostName} a lancé une course en même temps : la sienne est retenue", Info, "race");
    }

    private double? HostOffset(ulong host)
    {
        var p = _node.Peer(host);
        return p != null && p.HasClock ? p.ClockOffsetMs : null;
    }

    public void OnPeerLeft(ulong id, double now)
    {
        bool wasGone = Session is RaceGuest { HostGone: true };
        _lobby.OnPeerLeft(id, now);
        if (!wasGone && Session is RaceGuest { HostGone: true } g && !g.View.IsOver)
            _toasts.Show("L'hôte a quitté : finis ta descente, classement figé", Bad, "race");
    }

    /// Pair (re)venu : s'il avait disparu en pleine course que nous accueillons, il retrouve sa place.
    public void OnPeerJoined(ulong id, double now) => _lobby.OnPeerBack(id, now);

    public void Update(double now)
    {
        RestoreLocksWhenSettled(now);
        if (Session == null) return;
        _lobby.Tick(now);
        string error = _run.Update(Session, now);
        if (error != null) Fail(error, now);
        if (Session.View.Phase != _lastPhase) OnPhaseChanged(now);
        if (Session.View.IsOver) _run.End();
    }

    /// Verrous de pistes forcées remis seulement au menu installé depuis 2 s : remis pendant la sortie de
    /// piste, ils empêchent le jeu de ranger le vélo, et le chargement suivant se fige (docs/game-api.md § 4).
    private void RestoreLocksWhenSettled(double now)
    {
        if (!TrackAccess.HasForced || _run.Loading || now < _nextRestoreCheck) return;
        _nextRestoreCheck = now + 250;
        if (!GameInfo.MenuSettled()) { _menuSince = double.NaN; return; }
        if (double.IsNaN(_menuSince)) _menuSince = now;
        if (now - _menuSince < 2000) return;
        TrackAccess.RestoreSelection(); // course quittée avant d'être prête : au moins le profil retrouve sa piste
        TrackAccess.RestoreAll();
    }

    private double _menuSince = double.NaN;

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
        CancelReason.Superseded => "Course annulée : l'hôte a rejoint une course lancée en même temps",
        CancelReason.HostLost => "L'hôte ne répond plus : course annulée",
        _ => null,
    };

    /// Commandes (RaceButtons) : appui = accepter / lancer / partir sans attendre / fermer ;
    /// maintien 1,5 s = abandonner / (hôte) annuler. Refuser une invitation = la laisser expirer.
    public void HandleKeys(double now, string myName)
    {
        Log.Try("manette", () => _pad.Update(now)); // une erreur de Rewired ne doit pas priver du clavier
        var state = new RaceButtonState(Input.GetKey(Config.Key(Config.RaceKey)), _pad.Back, _pad.A, _pad.B);
        RaceKeys.Track(state.Key, state.PadModifier, _pad.LastWasPad);
        switch (Buttons.Update(state, now, CanGo(now), CanQuit))
        {
            case RaceCommand.Go: OnGo(now, myName); break;
            case RaceCommand.Quit: OnQuit(now); break;
            case RaceCommand.HoldHint: _toasts.Show($"Pour abandonner : {RaceKeys.Quit} 1,5 s", Info, "race"); break;
        }
    }

    public RaceButtons Buttons { get; } = new();
    private readonly PadReader _pad = new();
    /// Le maintien en cours annulerait la course (hôte, avant le départ) plutôt qu'abandonner.
    public bool HoldCancels => Session is RaceHost { View.Phase: RacePhase.Gathering };

    private bool CanGo(double now) => Session == null || !Busy || ResultsVisible(now)
        || Session is RaceGuest { IsPending: true } || Session is RaceHost { View.Phase: RacePhase.Gathering };

    private bool CanQuit => Session != null && !Session.View.IsOver && Session.Me != null && Session.Me.IsActive;

    private void OnGo(double now, string myName)
    {
        if (Session is RaceGuest g && g.IsPending)
        {
            g.Accept();
            _run.Begin(g.View.Track, now);
            Log.Info($"course acceptée : chargement de {g.View.Track}"); // le panneau de course le dit déjà à l'écran
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
        var host = _lobby.Host(track, peers, myName, now);
        _lastPhase = host.View.Phase;
        _run.Begin(track, now);
        _toasts.Show($"Course sur {GameInfo.TrackName(track)} : {host.InvitedCount} joueur(s) invité(s)", Info, "race");
    }

    private void OnQuit(double now)
    {
        if (Session is RaceHost h && h.View.Phase == RacePhase.Gathering)
        {
            h.Cancel(CancelReason.HostCancelled);
            _toasts.Show("Course annulée", Info, "race");
            return;
        }
        Session.Abandon(now);
        _run.End();
        _toasts.Show("Tu as abandonné", Bad, "race");
    }

    /// Départ du mod (fermeture du jeu) : abandon propre, cartes manette rétablies.
    public void Shutdown(double now)
    {
        if (Session != null && !Session.View.IsOver) Session.Abandon(now);
        _run.End();
        _pad.Unmute();
    }
}
