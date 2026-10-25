using System.Text;
using Il2CppRewired;

namespace LmdExplorer;

/// Commande « maps » : toutes les cartes Rewired du joueur 0 (clavier, souris, manettes), avec pour chaque
/// élément l'action du jeu, la touche ou le bouton, dans UserData/LmdExplorer/maps.txt.
/// Sert à savoir quelles touches/boutons le jeu utilise (AZERTY, touche caméra libre…).
internal static class InputMaps
{
    public static void Dump()
    {
        var player = ReInput.players.GetPlayer(0);
        var all = new Il2CppSystem.Collections.Generic.List<ControllerMap>();
        player.controllers.maps.GetAllMaps(all);
        var sb = new StringBuilder();
        foreach (var map in all)
        {
            sb.AppendLine($"== {map.controllerType} catégorie {map.categoryId} ({CategoryName(map.categoryId)}) disposition {map.layoutId} activée={map.enabled} manette={map.controller?.name}");
            var elems = map.AllMaps;
            int n = elems.Cast<Il2CppSystem.Collections.Generic.ICollection<ActionElementMap>>().Count;
            for (int i = 0; i < n; i++)
            {
                var e = elems[i];
                var action = ReInput.mapping.GetAction(e.actionId);
                string what = map.controllerType == ControllerType.Keyboard
                    ? $"touche {e.keyboardKeyCode} (KeyCode {e.keyCode}) mod={e.modifierKeyFlags}"
                    : $"élément {e.elementIdentifierId} '{e.elementIdentifierName}' {e.elementType} {e.axisRange} {e.axisContribution}";
                sb.AppendLine($"  {action?.name ?? "?"} ({e.actionId}) <- {what}{(e.enabled ? "" : " [coupé]")}");
            }
        }
        Out.WriteFile("maps.txt", sb.ToString());
    }

    private static string CategoryName(int id)
    {
        var c = ReInput.mapping.GetMapCategory(id);
        return c != null ? c.name : "?";
    }
}
