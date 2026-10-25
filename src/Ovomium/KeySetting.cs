using System;
using MelonLoader;
using Ovomium.Core.Keys;
using UnityEngine;

namespace Ovomium;

/// Touche réglable dans Ovomium.cfg. Laissée à son défaut, elle prend VrDefault quand LMD-VR est installé
/// (F6-F9 deviennent F1-F4 : voir View/LmdVrBridge et Core/Keys/KeyChoice).
internal sealed class KeySetting
{
    public KeySetting(MelonPreferences_Entry<string> entry, string vrDefault, string label)
    {
        Entry = entry;
        VrDefault = vrDefault;
        Label = label;
    }

    public MelonPreferences_Entry<string> Entry { get; }
    public string VrDefault { get; }
    /// Rôle, pour les messages (« course », « panneau »…).
    public string Label { get; }

    /// Nom de la touche effective (affiché dans le panneau).
    public string Name => KeyChoice.Effective(Entry.Value, Entry.DefaultValue, VrDefault, View.LmdVrBridge.Installed);

    public KeyCode Code => Enum.TryParse<KeyCode>(Name, true, out var k) ? k : KeyCode.None;
}
