using Il2CppMegagon.Downhill;
using Il2CppMegagon.Downhill.UI.Screens;
using LmdMulti.Game;
using UnityEngine;

namespace LmdMulti.Race;

/// Amène le joueur au départ d'une piste, d'où qu'il soit (écran titre, menus, autre piste, même piste),
/// une étape par image (voir docs/game-api.md § 4) :
///   chargement du jeu en cours → on attend qu'il finisse (jamais deux changements de scène à la fois) ;
///   même piste → « recommencer la tentative » ; autre piste → retour au menu ; écran titre → « commencer » ;
///   puis sélection (TrackAccess, verrou contourné), un instant pour le menu, et lancement.
internal sealed class TrackLoader
{
    public enum Step { Idle, Settling, Leaving, WaitMenu, Selected, WaitTrack, Done, Failed }

    private const double TimeoutMs = 60000, MenuSettleMs = 800, SelectSettleMs = 600, StableMs = 500;
    public Step Current { get; private set; } = Step.Idle;
    public string Target { get; private set; }
    public string Error { get; private set; }
    /// Instant (NetClock) où le joueur est arrivé sur la piste visée.
    public double ArrivedAtMs { get; private set; }

    private double _deadline, _since = double.NaN, _nextTitlePress, _nextCheck;

    public bool Busy => Current is Step.Settling or Step.Leaving or Step.WaitMenu or Step.Selected or Step.WaitTrack;

    public void Start(string trackId, double now)
    {
        Target = trackId;
        Error = null;
        _deadline = now + TimeoutMs;
        _since = double.NaN;
        Current = Step.Settling;
    }

    public void Cancel() { if (Busy) Current = Step.Idle; }

    public void Update(double now)
    {
        if (!Busy) return;
        if (now > _deadline) { Fail($"chargement trop long ({Current})"); return; }
        switch (Current)
        {
            case Step.Settling: Settle(now); break;
            case Step.Leaving: if (GameInfo.State() != GameInfo.StateOnTrack) Current = Step.WaitMenu; break;
            case Step.WaitMenu: WaitMenu(now); break;
            case Step.Selected: if (now - _since >= SelectSettleMs) Launch(); break;
            case Step.WaitTrack: if (GameInfo.CurrentTrackId() == Target) Arrive(now); break;
        }
    }

    /// Écran de chargement du jeu absent depuis un instant (contrôle 4×/s), puis choix du chemin.
    /// Pendant un chargement, l'état et la piste lus peuvent être ceux d'avant (docs/game-api.md § 4).
    private void Settle(double now)
    {
        if (now < _nextCheck) return;
        _nextCheck = now + 250;
        if (GameInfo.LoadingVisible()) { _since = double.NaN; return; }
        if (double.IsNaN(_since)) _since = now;
        if (now - _since < StableMs) return;
        _since = double.NaN;
        if (GameInfo.CurrentTrackId() == Target)
        {
            Log.Info($"course : déjà sur {Target}, retour au départ");
            RaceGame.RestartRun();
            Arrive(now);
        }
        else if (GameInfo.State() == GameInfo.StateOnTrack)
        {
            Log.Info($"course : retour au menu pour charger {Target}");
            GameStateManager.Method_Public_Static_Void_1();
            Current = Step.Leaving;
        }
        else Current = Step.WaitMenu;
    }

    /// Menu de jeu prêt depuis un instant (les écrans s'animent) ; sur l'écran titre, on appuie sur « commencer ».
    private void WaitMenu(double now)
    {
        if (now < _nextCheck) return; // recherches dans la scène : 4×/s suffisent
        _nextCheck = now + 250;
        if (Object.FindObjectOfType<GameMenu>() == null || GameInfo.LoadingVisible())
        {
            _since = double.NaN;
            var title = Object.FindObjectOfType<MainMenuScreen>();
            if (title != null && now >= _nextTitlePress) { _nextTitlePress = now + 3000; title.OnStartButton(); }
            return;
        }
        if (double.IsNaN(_since)) _since = now;
        if (now - _since < MenuSettleMs) return;
        if (!TrackAccess.Select(Target, out string error)) { Fail(error); return; }
        _since = now;
        Current = Step.Selected; // le menu suit la sélection avant le lancement, comme après un choix à la main
    }

    private void Launch()
    {
        var menu = Object.FindObjectOfType<GameMenu>();
        if (menu == null) { _since = double.NaN; Current = Step.WaitMenu; return; }
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
