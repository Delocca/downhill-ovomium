using Ovomium.Core.View;
using Ovomium.Game;
using Ovomium.Race;
using Ovomium.UI;
using UnityEngine;

namespace Ovomium.View;

/// Fait tourner les vues de LMD-Camera en piste (normale → 1re personne → 3e personne, Core/View/ViewCycle) :
/// touche CameraKey (V par défaut) ou clic du stick droit (R3, libre en piste dans les cartes manette du jeu,
/// docs/game-api.md § 9). Part de la vue courante, même changée par les touches 1/2/3 du mod.
/// La vue choisie est mémorisée par LMD-Camera (LastViewMode).
internal sealed class CameraToggle
{
    private static readonly Color ToastColor = new(0.8f, 0.9f, 1f);
    private LmdCameraBridge _bridge;
    private bool _searched, _padWasDown, _absentShown, _vrShown, _vr;

    /// Aide affichée dans le panneau (« V vue » ou « R3 vue »), vide sans LMD-Camera ou pendant la VR.
    public string Hint => _bridge == null || _vr ? "" : $" · {(RaceKeys.Pad ? "R3" : Config.CameraKey.Name)} vue";

    public void Update(bool onTrack, Toasts toasts)
    {
        if (!_searched) { _searched = true; _bridge = LmdCameraBridge.Find(); }
        _vr = LmdVrBridge.Active;
        bool padDown = PadReader.AnyButton(PadReader.ButtonR3);
        bool pressed = Input.GetKeyDown(Config.CameraKey.Code) || (padDown && !_padWasDown);
        _padWasDown = padDown;
        if (!pressed || !onTrack || GameInfo.IsPaused()) return;
        if (_vr) { ExplainVr(toasts); return; } // R3 et L3+R3 sont à LMD-VR, qui pilote aussi la caméra
        if (_bridge == null)
        {
            if (!_absentShown) toasts.Show("Changement de vue indisponible : mod LMD-Camera absent ou incompatible", ToastColor, "vue");
            _absentShown = true;
            return;
        }
        Switch(toasts);
    }

    private void ExplainVr(Toasts toasts)
    {
        if (_vrShown) return;
        _vrShown = true;
        Log.Info("vue : bascule ignorée, VR de LMD-VR démarrée");
        toasts.Show("VR démarrée : la vue se change avec LMD-VR (F5, ou LB + RB + croix) ; V et R3 d'Ovomium sont coupés", ToastColor, "vue");
    }

    private void Switch(Toasts toasts)
    {
        if (!_bridge.CanSwitch(out bool ragdoll))
        {
            if (ragdoll) toasts.Show("Vue : changement impossible pendant une chute", ToastColor, "vue");
            return;
        }
        string view = _bridge.View;
        string target = ViewCycle.Next(view);
        _bridge.SetView(target);
        Log.Info($"vue : {view} → {target}");
        toasts.Show($"Vue : {ViewCycle.Label(target)}", ToastColor, "vue");
    }
}
