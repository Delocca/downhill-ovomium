using System.Collections.Generic;
using Il2CppRewired;

namespace LmdMulti.Race;

/// Manette pour les commandes de course, via Rewired (la bibliothèque d'entrées du jeu, docs/game-api.md
/// § 9) : Select/View/Back sert de modificateur, A et B y sont combinés. Pour que le jeu ne voie rien :
/// - Back est retiré des cartes manette du jeu (il n'y faisait que « pause » en piste, comme Start) ;
/// - tant que Back est tenu, les cartes manette du joueur sont coupées (A = sprint / valider, B = crash
///   volontaire / retour), puis rétablies une fois tous les boutons relâchés.
/// Tout est en mémoire : rien n'est sauvegardé (le jeu ne garde que ses préréglages).
internal sealed class PadReader
{
    // Identifiants d'éléments Rewired des manettes Xbox 360 / One (XInput, ce que Proton présente).
    public const int ButtonA = 6, ButtonB = 7, ButtonBack = 12, ButtonR3 = 15;

    private readonly List<ControllerMap> _muted = new();
    private readonly HashSet<string> _unmapped = new();
    private double _nextScan;
    private string _lastPads = "";

    public bool Back { get; private set; }
    public bool A { get; private set; }
    public bool B { get; private set; }
    /// Dernier périphérique utilisé dans le jeu = manette (sinon clavier/souris) : textes d'aide adaptés.
    public bool LastWasPad { get; private set; }

    /// Une image : lit les boutons, entretient le retrait de Back et la coupure des cartes.
    public void Update(double now)
    {
        Back = A = B = false;
        if (!ReInput.isReady) return;
        var player = ReInput.players.GetPlayer(0);
        if (player == null) return;
        var joys = player.controllers.Joysticks;
        int count = player.controllers.joystickCount;
        for (int i = 0; i < count; i++)
        {
            var j = joys[i];
            Back |= Pressed(j, ButtonBack);
            A |= Pressed(j, ButtonA);
            B |= Pressed(j, ButtonB);
        }
        var last = player.controllers.GetLastActiveController();
        if (last != null) LastWasPad = last.type == ControllerType.Joystick;
        if (now >= _nextScan) { _nextScan = now + 1000; Scan(player, joys, count); }
        UpdateMute(player);
    }

    /// Bouton tenu sur l'une des manettes du joueur (hors commandes de course : ex. R3 pour la vue).
    public static bool AnyButton(int elementId)
    {
        if (!ReInput.isReady) return false;
        var player = ReInput.players.GetPlayer(0);
        if (player == null) return false;
        var joys = player.controllers.Joysticks;
        for (int i = 0; i < player.controllers.joystickCount; i++)
            if (Pressed(joys[i], elementId)) return true;
        return false;
    }

    private static bool Pressed(Joystick j, int elementId)
    {
        int index = j.GetButtonIndexById(elementId);
        return index >= 0 && j.GetButton(index);
    }

    /// Back retiré des cartes manette (elles sont rechargées si le joueur change de préréglage, ou
    /// à la connexion d'une manette : on repasse chaque seconde) ; manettes connectées journalisées.
    private void Scan(Player player, Il2CppSystem.Collections.Generic.IList<Joystick> joys, int count)
    {
        string pads = "";
        for (int i = 0; i < count; i++) pads += $"{joys[i].name} [{joys[i].hardwareTypeGuid.ToString()}] ; ";
        if (pads != _lastPads) { _lastPads = pads; Log.Info($"manettes : {(pads == "" ? "aucune" : pads)}"); }
        foreach (var map in JoystickMaps(player))
        {
            var buttons = map.ButtonMaps;
            int n = buttons.Cast<Il2CppSystem.Collections.Generic.ICollection<ActionElementMap>>().Count;
            for (int k = n - 1; k >= 0; k--)
            {
                var aem = buttons[k];
                if (aem.elementIdentifierId != ButtonBack) continue;
                int action = aem.actionId;
                string key = $"{map.categoryId}/{map.layoutId} (action {action})";
                // Le jeu recharge ses cartes à plusieurs étapes (profil, menus) : journalisé une fois par carte.
                if (map.DeleteElementMap(aem.id) && _unmapped.Add(key))
                    Log.Info($"manette : Back retiré de la carte {key} : il sert aux commandes de course");
            }
        }
    }

    private static List<ControllerMap> JoystickMaps(Player player)
    {
        var all = new Il2CppSystem.Collections.Generic.List<ControllerMap>();
        player.controllers.maps.GetAllMaps(all);
        var maps = new List<ControllerMap>();
        foreach (var m in all)
            if (m.controllerType == ControllerType.Joystick) maps.Add(m);
        return maps;
    }

    /// Back tenu : cartes manette coupées. Rétablies quand Back, A et B sont tous relâchés (sinon un A
    /// encore tenu serait vu par le jeu comme un nouvel appui).
    private void UpdateMute(Player player)
    {
        if (Back && _muted.Count == 0)
        {
            foreach (var m in JoystickMaps(player))
                if (m.enabled) { m.enabled = false; _muted.Add(m); }
        }
        else if (!Back && !A && !B && _muted.Count > 0) Unmute();
    }

    public void Unmute()
    {
        foreach (var m in _muted) Log.Try("manette : carte rétablie", () => m.enabled = true);
        _muted.Clear();
    }
}
