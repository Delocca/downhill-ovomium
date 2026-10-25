using System;
using System.Collections.Generic;
using System.Linq;

namespace Ovomium.Core.Keys;

/// Touches du mod face à celles de LMD-VR, sans Unity (testé dans tests/).
public static class KeyChoice
{
    /// Touches clavier de LMD-VR 1.0.0 (Mod.HandleKeyboard / KeyboardLook), noms de KeyCode d'Unity.
    /// Son F10 (recentrer) double la capture d'écran du jeu ; ses manettes : LB+RB+croix, R3, L3+R3, stick droit.
    public static readonly IReadOnlyList<string> VrKeys = new[]
    {
        "F5", "F6", "F7", "F9", "F10", "F11", "PageUp", "PageDown", "Equals", "Minus", "KeypadPlus", "KeypadMinus",
        "Keypad2", "Keypad4", "Keypad5", "Keypad6", "Keypad8",
    };

    /// Touches qui tombent sur une touche de LMD-VR : les deux mods réagiraient au même appui.
    public static IEnumerable<string> VrConflicts(IEnumerable<string> keys) =>
        keys.Where(k => VrKeys.Contains(k?.Trim(), StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase);
}
