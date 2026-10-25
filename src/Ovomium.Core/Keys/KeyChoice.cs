using System;
using System.Collections.Generic;
using System.Linq;

namespace Ovomium.Core.Keys;

/// Touches effectives du mod, sans Unity (testé dans tests/) : le réglage du joueur s'il l'a changé, sinon le
/// défaut normal, ou le défaut « VR » quand le mod LMD-VR est installé (ses touches sont codées en dur).
public static class KeyChoice
{
    /// Touches clavier de LMD-VR 1.0.0 (Mod.HandleKeyboard / KeyboardLook), noms de KeyCode d'Unity.
    /// Son F10 (recentrer) double la capture d'écran du jeu ; ses manettes : LB+RB+croix, R3, L3+R3, stick droit.
    public static readonly IReadOnlyList<string> VrKeys = new[]
    {
        "F5", "F6", "F7", "F9", "F10", "F11", "PageUp", "PageDown", "Equals", "Minus", "KeypadPlus", "KeypadMinus",
        "Keypad2", "Keypad4", "Keypad5", "Keypad6", "Keypad8",
    };

    /// Touche à utiliser. Un réglage égal au défaut normal compte comme « pas personnalisé ».
    public static string Effective(string value, string normalDefault, string vrDefault, bool vrInstalled) =>
        vrInstalled && IsDefault(value, normalDefault) ? vrDefault : value?.Trim();

    public static bool IsDefault(string value, string normalDefault) =>
        string.Equals(value?.Trim(), normalDefault, StringComparison.OrdinalIgnoreCase);

    /// Touches (effectives) qui tombent sur une touche de LMD-VR : les deux mods réagiraient au même appui.
    public static IEnumerable<string> VrConflicts(IEnumerable<string> keys) =>
        keys.Where(k => VrKeys.Contains(k?.Trim(), StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase);
}
