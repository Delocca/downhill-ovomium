using Ovomium.Game;
using Ovomium.Race;
using Ovomium.UI;
using UnityEngine;

namespace Ovomium.View;

/// Bascule vue à la première personne ⇄ vue précédente (normale ou 3e personne de LMD-Camera), en piste :
/// touche CameraKey (V par défaut) ou clic du stick droit (R3, libre en piste dans les cartes manette du jeu,
/// docs/game-api.md § 9). La vue choisie est mémorisée par LMD-Camera (LastViewMode).
internal sealed class CameraToggle
{
    private static readonly Color ToastColor = new(0.8f, 0.9f, 1f);
    private LmdCameraBridge _bridge;
    private bool _searched, _padWasDown, _absentShown;
    private string _previous = "Default"; // vue à retrouver en quittant la 1re personne

    /// Aide affichée dans le panneau (« V vue » ou « R3 vue »), vide sans LMD-Camera.
    public string Hint => _bridge == null ? "" : $" · {(RaceKeys.Pad ? "R3" : Config.CameraKey.Value)} vue";

    public void Update(bool onTrack, Toasts toasts)
    {
        if (!_searched) { _searched = true; _bridge = LmdCameraBridge.Find(); }
        bool padDown = PadReader.AnyButton(PadReader.ButtonR3);
        bool pressed = Input.GetKeyDown(Config.Key(Config.CameraKey)) || (padDown && !_padWasDown);
        _padWasDown = padDown;
        string view = _bridge?.View;
        if (view == "Default" || view == "ThirdPerson") _previous = view; // aussi après les touches 2 et 3 du mod
        if (!pressed || !onTrack || GameInfo.IsPaused()) return;
        if (_bridge == null)
        {
            if (!_absentShown) toasts.Show("Vue 1re personne indisponible : mod LMD-Camera absent ou incompatible", ToastColor, "vue");
            _absentShown = true;
            return;
        }
        Switch(view, toasts);
    }

    private void Switch(string view, Toasts toasts)
    {
        if (!_bridge.CanSwitch(out bool ragdoll))
        {
            if (ragdoll) toasts.Show("Vue : changement impossible pendant une chute", ToastColor, "vue");
            return;
        }
        string target = view == "FirstPerson" ? _previous : "FirstPerson";
        _bridge.SetView(target);
        Log.Info($"vue : {view} → {target}");
        string label = target switch { "FirstPerson" => "1re personne", "ThirdPerson" => "3e personne", _ => "normale" };
        toasts.Show($"Vue : {label}", ToastColor, "vue");
    }
}
