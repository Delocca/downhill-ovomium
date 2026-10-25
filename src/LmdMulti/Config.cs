using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace LmdMulti;

/// Réglages (MelonPreferences), dans leur propre fichier <jeu>/UserData/LmdMulti.cfg (TOML, éditable).
internal static class Config
{
    public static string FilePath { get; private set; }
    public static string DataDir { get; private set; }

    public static MelonPreferences_Entry<int> Port;
    public static MelonPreferences_Entry<string> ManualPeers;
    public static MelonPreferences_Entry<string> Nickname;
    public static MelonPreferences_Entry<string> PanelKey, EchoKey, CaptureKey;
    public static MelonPreferences_Entry<bool> ShowPanel;
    public static MelonPreferences_Entry<bool> EchoMode;
    public static MelonPreferences_Entry<float> EchoDelaySeconds;
    public static MelonPreferences_Entry<int> SendRateHz;
    public static MelonPreferences_Entry<int> InterpolationDelayMs;
    public static MelonPreferences_Entry<bool> GhostTint;
    public static MelonPreferences_Entry<bool> RecordPoses;
    public static MelonPreferences_Entry<bool> DebugLog;

    public static void Init()
    {
        DataDir = Path.Combine(MelonEnvironment.UserDataDirectory, "LmdMulti");
        Directory.CreateDirectory(DataDir);
        FilePath = Path.Combine(MelonEnvironment.UserDataDirectory, "LmdMulti.cfg");
        var c = MelonPreferences.CreateCategory("LmdMulti", "LmdMulti (multijoueur LAN)");
        c.SetFilePath(FilePath, autoload: true, printmsg: false);
        Port = c.CreateEntry("Port", 47700, description: "Port UDP (le même pour tous les joueurs)");
        ManualPeers = c.CreateEntry("ManualPeers", "", description: "IP des joueurs hors réseau local (Tailscale…), séparées par des virgules, ex. \"100.64.0.2, 100.64.0.3:47701\"");
        Nickname = c.CreateEntry("Nickname", "", description: "Pseudo affiché ; vide = pseudo Steam");
        PanelKey = c.CreateEntry("PanelKey", "F8", description: "Touche : afficher/masquer le panneau des joueurs");
        EchoKey = c.CreateEntry("EchoKey", "F9", description: "Touche : activer/couper le mode écho");
        CaptureKey = c.CreateEntry("CaptureKey", "F10", description: "Touche : capture d'écran dans UserData/LmdMulti/captures");
        ShowPanel = c.CreateEntry("ShowPanel", true, description: "Panneau des joueurs visible au démarrage");
        EchoMode = c.CreateEntry("EchoMode", false, description: "Debug : affiche son propre fantôme, revenu par le réseau local avec un retard");
        EchoDelaySeconds = c.CreateEntry("EchoDelaySeconds", 2f, description: "Retard du fantôme écho (secondes)");
        SendRateHz = c.CreateEntry("SendRateHz", 25, description: "Poses envoyées par seconde (10 à 60)");
        InterpolationDelayMs = c.CreateEntry("InterpolationDelayMs", 100, description: "Retard d'affichage des fantômes (ms) : absorbe la gigue du réseau");
        GhostTint = c.CreateEntry("GhostTint", true, description: "Teinte bleutée des fantômes");
        RecordPoses = c.CreateEntry("RecordPoses", false, description: "Debug : enregistre ses poses dans UserData/LmdMulti/recordings (rejouables par LmdBot)");
        DebugLog = c.CreateEntry("DebugLog", false, description: "Debug : statistiques réseau et mouvements des os dans le journal");
        c.SaveToFile(false); // écrit les valeurs par défaut manquantes (fichier documenté)
    }

    public static KeyCode Key(MelonPreferences_Entry<string> entry) =>
        Enum.TryParse<KeyCode>(entry.Value, true, out var k) ? k : KeyCode.None;

    public static int ClampedSendRate => Math.Clamp(SendRateHz.Value, 10, 60);
}
