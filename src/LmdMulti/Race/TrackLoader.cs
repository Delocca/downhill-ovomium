using Il2CppMegagon.Downhill;
using Il2CppMegagon.Downhill.UI.Screens;
using LmdMulti.Game;
using UnityEngine;

namespace LmdMulti.Race;

/// Amène le joueur au départ d'une piste, d'où qu'il soit (écran titre, menus, autre piste, même piste),
/// une étape par image (voir docs/game-api.md § 4) :
///   même piste → « recommencer la tentative » ; autre piste → retour au menu ; écran titre → « commencer » ;
///   puis sélection (TrackAccess, verrou contourné) et lancement depuis le menu de jeu.
internal sealed class TrackLoader
{
    public enum Step { Idle, Leaving, WaitMenu, Launching, WaitTrack, Done, Failed }

    private const double TimeoutMs = 60000, MenuSettleMs = 800;
    public Step Current { get; private set; } = Step.Idle;
    public string Target { get; private set; }
    public string Error { get; private set; }
    /// Instant (NetClock) où le joueur est arrivé sur la piste visée.
    public double ArrivedAtMs { get; private set; }

    private double _deadline, _menuSince = double.NaN, _nextTitlePress;

    public bool Busy => Current is Step.Leaving or Step.WaitMenu or Step.Launching or Step.WaitTrack;

    public void Start(string trackId, double now)
    {
        Target = trackId;
        Error = null;
        _deadline = now + TimeoutMs;
        _menuSince = double.NaN;
        if (GameInfo.CurrentTrackId() == trackId)
        {
            Log.Info($"course : déjà sur {trackId}, retour au départ");
            RaceGame.RestartRun();
            Arrive(now);
        }
        else if (GameInfo.State() == GameInfo.StateOnTrack)
        {
            Log.Info($"course : retour au menu pour charger {trackId}");
            GameStateManager.Method_Public_Static_Void_1();
            Current = Step.Leaving;
        }
        else Current = Step.WaitMenu;
    }

    public void Cancel() { if (Busy) Current = Step.Idle; }

    public void Update(double now)
    {
        if (!Busy) return;
        if (now > _deadline) { Fail($"chargement trop long ({Current})"); return; }
        switch (Current)
        {
            case Step.Leaving: if (GameInfo.State() != GameInfo.StateOnTrack) Current = Step.WaitMenu; break;
            case Step.WaitMenu: WaitMenu(now); break;
            case Step.Launching: Launch(now); break;
            case Step.WaitTrack: if (GameInfo.CurrentTrackId() == Target) Arrive(now); break;
        }
    }

    /// Menu de jeu prêt depuis un instant (les écrans s'animent) ; sur l'écran titre, on appuie sur « commencer ».
    private void WaitMenu(double now)
    {
        if (Object.FindObjectOfType<GameMenu>() == null)
        {
            _menuSince = double.NaN;
            var title = Object.FindObjectOfType<MainMenuScreen>();
            if (title != null && now >= _nextTitlePress) { _nextTitlePress = now + 3000; title.OnStartButton(); }
            return;
        }
        if (double.IsNaN(_menuSince)) _menuSince = now;
        if (now - _menuSince >= MenuSettleMs) Current = Step.Launching;
    }

    private void Launch(double now)
    {
        if (!TrackAccess.Select(Target, out string error)) { Fail(error); return; }
        var menu = Object.FindObjectOfType<GameMenu>();
        if (menu == null) { Current = Step.WaitMenu; return; }
        Log.Info($"course : lancement de {Target}");
        menu.Method_Public_Void_1();
        Current = Step.WaitTrack;
    }

    private void Arrive(double now)
    {
        ArrivedAtMs = now;
        Current = Step.Done;
        Log.Info($"course : sur la piste {Target}");
    }

    private void Fail(string error)
    {
        Error = error;
        Current = Step.Failed;
        Log.Warn($"course : chargement de {Target} impossible : {error}");
    }
}
