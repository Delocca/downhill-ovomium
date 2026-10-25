using System;
using MelonLoader;
using UnityEngine;

namespace Ovomium;

/// Touche réglable dans Ovomium.cfg.
internal sealed class KeySetting
{
    public KeySetting(MelonPreferences_Entry<string> entry, string label)
    {
        Entry = entry;
        Label = label;
    }

    public MelonPreferences_Entry<string> Entry { get; }
    /// Rôle, pour les messages (« course », « panneau »…).
    public string Label { get; }

    /// Nom de la touche (affiché dans le panneau).
    public string Name => Entry.Value?.Trim();

    public KeyCode Code => Enum.TryParse<KeyCode>(Name, true, out var k) ? k : KeyCode.None;
}
