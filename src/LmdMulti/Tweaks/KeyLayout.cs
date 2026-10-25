using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LmdMulti.Tweaks;

/// Disposition du clavier : pour chaque lettre du jeu (défini en QWERTY), la lettre que porte la touche
/// située au même endroit sur le clavier du joueur (AZERTY : W → Z, A → Q, Q → A, Z → W).
/// Le jeu (Rewired) lit les touches par leur lettre (code de touche virtuel Windows), pas par leur position :
/// sans cela, un joueur AZERTY doit diriger avec Z… en bas à gauche (vérifié sous Proton, docs/game-api.md § 9).
internal static class KeyLayout
{
    // Codes de position (scancodes) des lettres d'un clavier QWERTY US.
    private static readonly Dictionary<KeyCode, uint> UsScancodes = new()
    {
        [KeyCode.Q] = 0x10, [KeyCode.W] = 0x11, [KeyCode.E] = 0x12, [KeyCode.R] = 0x13, [KeyCode.T] = 0x14,
        [KeyCode.Y] = 0x15, [KeyCode.U] = 0x16, [KeyCode.I] = 0x17, [KeyCode.O] = 0x18, [KeyCode.P] = 0x19,
        [KeyCode.A] = 0x1E, [KeyCode.S] = 0x1F, [KeyCode.D] = 0x20, [KeyCode.F] = 0x21, [KeyCode.G] = 0x22,
        [KeyCode.H] = 0x23, [KeyCode.J] = 0x24, [KeyCode.K] = 0x25, [KeyCode.L] = 0x26, [KeyCode.Z] = 0x2C,
        [KeyCode.X] = 0x2D, [KeyCode.C] = 0x2E, [KeyCode.V] = 0x2F, [KeyCode.B] = 0x30, [KeyCode.N] = 0x31,
        [KeyCode.M] = 0x32,
    };

    private static readonly Dictionary<KeyCode, KeyCode> Azerty = new()
        { [KeyCode.W] = KeyCode.Z, [KeyCode.Z] = KeyCode.W, [KeyCode.A] = KeyCode.Q, [KeyCode.Q] = KeyCode.A };
    private static readonly Dictionary<KeyCode, KeyCode> Qwertz = new()
        { [KeyCode.Y] = KeyCode.Z, [KeyCode.Z] = KeyCode.Y };

    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyExW(uint code, uint mapType, IntPtr hkl);
    private const uint ScancodeToVirtualKey = 1; // MAPVK_VSC_TO_VK

    /// Lettres déplacées (QWERTY → lettre du joueur) selon le réglage, et description pour le journal.
    public static Dictionary<KeyCode, KeyCode> Resolve(string setting, out string description)
    {
        switch ((setting ?? "auto").Trim().ToLowerInvariant())
        {
            case "qwerty": description = "QWERTY (réglage)"; return new();
            case "azerty": description = "AZERTY (réglage)"; return new(Azerty);
            case "qwertz": description = "QWERTZ (réglage)"; return new(Qwertz);
        }
        var moved = Detect(out IntPtr hkl);
        description = $"{Name(moved)} (détectée, disposition Windows 0x{hkl.ToInt64():X8})";
        return moved;
    }

    /// Lettre portée par chaque position de lettre QWERTY, d'après la disposition active du fil du jeu.
    private static Dictionary<KeyCode, KeyCode> Detect(out IntPtr hkl)
    {
        hkl = GetKeyboardLayout(0);
        var moved = new Dictionary<KeyCode, KeyCode>();
        foreach (var (us, scancode) in UsScancodes)
        {
            uint vk = MapVirtualKeyExW(scancode, ScancodeToVirtualKey, hkl);
            if (vk < 'A' || vk > 'Z') continue; // pas une lettre ici (ex. touche accentuée en BÉPO) : inchangée
            var mine = (KeyCode)(vk - 'A' + (int)KeyCode.A);
            if (mine != us) moved[us] = mine;
        }
        return moved;
    }

    private static string Name(Dictionary<KeyCode, KeyCode> moved)
    {
        if (moved.Count == 0) return "QWERTY";
        if (Azerty.All(p => moved.TryGetValue(p.Key, out var k) && k == p.Value)) return "AZERTY";
        if (Qwertz.All(p => moved.TryGetValue(p.Key, out var k) && k == p.Value)) return "QWERTZ";
        return $"autre ({moved.Count} lettres déplacées)";
    }

    public static string Describe(Dictionary<KeyCode, KeyCode> moved) =>
        string.Join(", ", moved.OrderBy(p => p.Key).Select(p => $"{p.Key}→{p.Value}"));
}
