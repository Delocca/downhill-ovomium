using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(LmdExplorer.ExplorerMod), "LmdExplorer", "0.1.0", "Edia")]
[assembly: MelonGame("Megagon Industries", "Lonely Mountains: Downhill")]

namespace LmdExplorer;

/// Mod explorateur : journalise périodiquement l'état du jeu, fait un dump complet et un test de
/// marionnette à la première apparition du vélo, et exécute les commandes de cmd.txt.
/// Fichier « autostart » dans UserData/LmdExplorer : lance la piste sélectionnée depuis le menu.
public class ExplorerMod : MelonMod
{
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private float _nextBeat, _nextPoll, _nextStatus, _bikeSince = -1f, _menuSince = -1f;
    private bool _autoDumped, _autoStarted;
    private int _lastState = int.MinValue;

    public override void OnInitializeMelon()
    {
        Out.Init(LoggerInstance);
        IntroProbe.Install(HarmonyInstance);
        Out.Info($"prêt. Commandes : une par ligne dans {Out.Dir}\\cmd.txt ou /tmp/lmdexplorer-cmd.txt (« help » pour la liste)");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        Out.Info($"scène chargée : {sceneName} ({buildIndex})");
        _autoDumped = false;
        _bikeSince = -1f;
    }

    public override void OnUpdate()
    {
        float now = (float)_clock.Elapsed.TotalSeconds; // horloge réelle, indépendante de Time.*
        Out.Try("intro", IntroProbe.Poll);
        if (now >= _nextBeat)
        {
            _nextBeat = now + 10f;
            Out.Info($"battement: frame={Time.frameCount} unscaledTime={Time.unscaledTime:F1} scène={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        }
        if (now >= _nextPoll) { _nextPoll = now + 1f; Commands.Poll(); WatchState(); AutoActions(now); }
        OnlineScores.Poll();
        if (now >= _nextStatus)
        {
            _nextStatus = now + 2f;
            if (BikeProbe.Find() != null) Out.Info(Probes.StatusLine());
        }
        // F11/F12 : F6-F10 sont prises par LmdMulti (course, aspect, panneau, écho, capture).
        if (Input.GetKeyDown(KeyCode.F11)) Out.Try("F11", () => Commands.Run("dump"));
        if (Input.GetKeyDown(KeyCode.F12)) Out.Try("F12", () => Commands.Run("puppet"));
    }

    public override void OnLateUpdate() => PuppetTest.Follow();

    private void WatchState()
    {
        int state = int.MinValue;
        Out.Try("state", () => state = TrackProbe.GameState());
        if (state == _lastState) return;
        Out.Info($"GameState {_lastState} -> {state}");
        _lastState = state;
    }

    private void AutoActions(float now)
    {
        bool hasBike = BikeProbe.Find() != null;
        _bikeSince = hasBike ? (_bikeSince < 0 ? now : _bikeSince) : -1f;
        // Fichier drapeau « autodump » : dump + marionnette de test à la première apparition du vélo.
        if (hasBike && !_autoDumped && now - _bikeSince > 5f && Out.Flag("autodump"))
        {
            _autoDumped = true;
            Commands.Run("dump");
            if (!PuppetTest.Active) Out.Try("puppet", () => Commands.Run("puppet"));
        }
        // Démarrage auto (fichier « autostart ») : écran titre -> menu de jeu -> piste sélectionnée.
        if (_autoStarted || _lastState == 100 || !Out.Flag("autostart")) return;
        if (_menuSince < 0) _menuSince = now;
        if (now - _menuSince < 8f) return;
        _menuSince = now;
        Out.Try("autostart", () =>
        {
            Commands.Run("screens");
            if (Object.FindObjectOfType<Il2CppMegagon.Downhill.UI.Screens.GameMenu>() != null) { _autoStarted = true; Commands.Run("start"); }
            else if (Object.FindObjectOfType<Il2CppMegagon.Downhill.UI.Screens.MainMenuScreen>() != null) Commands.Run("mainstart");
        });
    }
}
