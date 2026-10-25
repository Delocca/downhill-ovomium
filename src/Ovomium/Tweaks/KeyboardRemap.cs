using System;
using System.Collections.Generic;
using Il2CppRewired;
using UnityEngine;

namespace Ovomium.Tweaks;

/// Touches lettres du jeu adaptées à la disposition du clavier (réglage KeyboardLayout, KeyLayout) : les
/// cartes clavier Rewired du joueur sont modifiées en mémoire (rien n'est sauvegardé ; le jeu ne garde que
/// ses préréglages). Le jeu recharge ses cartes plusieurs fois (démarrage, profil, préréglage changé) :
/// chaque seconde, toute carte clavier pas encore vue est adaptée, une seule fois.
internal sealed class KeyboardRemap
{
    private Dictionary<KeyCode, KeyCode> _moved;
    // Cartes déjà adaptées. Les garder référencées empêche que l'adresse d'une carte libérée soit réutilisée
    // par une nouvelle (qui passerait pour déjà adaptée). Quelques cartes par chargement : négligeable.
    private readonly List<ControllerMap> _done = new();
    private readonly HashSet<IntPtr> _donePtr = new();
    private double _nextScan;

    public void Update(double now)
    {
        if (now < _nextScan || !ReInput.isReady) return;
        _nextScan = now + 1000;
        if (_moved == null)
        {
            _moved = KeyLayout.Resolve(Config.KeyboardLayout.Value, out string description);
            Log.Info($"clavier : {description}{(_moved.Count > 0 ? $" ; touches du jeu adaptées : {KeyLayout.Describe(_moved)}" : "")}");
        }
        if (_moved.Count == 0) return;
        var player = ReInput.players.GetPlayer(0);
        if (player == null) return;
        var all = new Il2CppSystem.Collections.Generic.List<ControllerMap>();
        player.controllers.maps.GetAllMaps(all);
        foreach (var map in all)
            if (map.controllerType == ControllerType.Keyboard && _donePtr.Add(map.Pointer))
            {
                _done.Add(map);
                int n = Remap(map);
                if (n > 0) Log.Info($"clavier : carte {map.categoryId}/{map.layoutId} adaptée ({n} touches)");
            }
    }

    private readonly record struct Binding(int ActionId, Pole Pole, KeyCode Key, ModifierKeyFlags Modifiers);

    /// Remplace chaque touche déplacée : tout est relevé, puis supprimé, puis recréé (A et Q s'échangent).
    private int Remap(ControllerMap map)
    {
        var elems = map.AllMaps;
        int count = elems.Cast<Il2CppSystem.Collections.Generic.ICollection<ActionElementMap>>().Count;
        var todo = new List<(int id, Binding b)>();
        for (int i = 0; i < count; i++)
        {
            var e = elems[i];
            // Élément désactivé (aucun dans cette build) : laissé tel quel, l'état « désactivé » n'est pas recopiable.
            if (e.enabled && _moved.TryGetValue(e.keyCode, out var mine))
                todo.Add((e.id, new Binding(e.actionId, e.axisContribution, mine, e.modifierKeyFlags)));
        }
        foreach (var (id, _) in todo) map.DeleteElementMap(id);
        foreach (var (_, b) in todo)
            if (!map.CreateElementMap(b.ActionId, b.Pole, b.Key, b.Modifiers, out ActionElementMap _))
                Log.Warn($"clavier : touche {b.Key} non recréée (action {b.ActionId})");
        return todo.Count;
    }
}
