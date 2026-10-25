using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace Ovomium;

/// Réglages (MelonPreferences), dans leur propre fichier <jeu>/UserData/Ovomium.cfg (TOML, éditable).
internal static class Config
{
    public static string FilePath { get; private set; }
    public static string DataDir { get; private set; }

    public static MelonPreferences_Entry<int> Port;
    public static MelonPreferences_Entry<string> ManualPeers;
    public static MelonPreferences_Entry<string> Nickname;
    public static MelonPreferences_Entry<string> PanelKey, EchoKey, StyleKey, RaceKey;
    public static MelonPreferences_Entry<bool> ShowPanel;
    public static MelonPreferences_Entry<bool> EchoMode;
    public static MelonPreferences_Entry<float> EchoDelaySeconds;
    public static MelonPreferences_Entry<int> SendRateHz;
    public static MelonPreferences_Entry<int> InterpolationDelayMs;
    public static MelonPreferences_Entry<string> GhostStyle;
    public static MelonPreferences_Entry<bool> RecordPoses;
    public static MelonPreferences_Entry<bool> DebugLog;
    public static MelonPreferences_Entry<bool> TestMode;
    public static MelonPreferences_Entry<bool> SkipIntro;
    public static MelonPreferences_Entry<string> KeyboardLayout, CameraKey;

    public static void Init()
    {
        DataDir = Path.Combine(MelonEnvironment.UserDataDirectory, "Ovomium");
        Directory.CreateDirectory(DataDir);
        FilePath = Path.Combine(MelonEnvironment.UserDataDirectory, "Ovomium.cfg");
        var c = MelonPreferences.CreateCategory("Ovomium", "Ovomium (améliorations de Lonely Mountains: Downhill : multijoueur, course, vue, clavier…)");
        c.SetFilePath(FilePath, autoload: true, printmsg: false);
        Port = c.CreateEntry("Port", 47700, description: "Port UDP (le même pour tous les joueurs)");
        ManualPeers = c.CreateEntry("ManualPeers", "", description: "IP des joueurs hors réseau local (Tailscale…), séparées par des virgules, ex. \"100.64.0.2, 100.64.0.3:47701\"");
        Nickname = c.CreateEntry("Nickname", "", description: "Pseudo affiché ; vide = pseudo Steam");
        RaceKey = c.CreateEntry("RaceKey", "F6", description: "Touche course. Appui : lancer une course sur ta piste / accepter une invitation / (hôte) partir sans attendre / fermer les résultats. Maintenue 1,5 s : abandonner / (hôte) annuler. Manette : Select + A, Select + B maintenus");
        PanelKey = c.CreateEntry("PanelKey", "F8", description: "Touche : afficher/masquer le panneau des joueurs");
        EchoKey = c.CreateEntry("EchoKey", "F9", description: "Touche : activer/couper le mode écho");
        StyleKey = c.CreateEntry("StyleKey", "F7", description: "Touche : changer l'aspect des fantômes (GhostStyle)");
        SkipIntro = c.CreateEntry("SkipIntro", true, description: "Démarrage rapide : passe les logos et l'écran « Appuyez sur… » pour arriver directement au menu");
        KeyboardLayout = c.CreateEntry("KeyboardLayout", "auto", description: "Clavier : auto (lettres du jeu, ex. ZQSD pour diriger, adaptées à ta disposition : AZERTY, QWERTZ…), azerty, qwertz, ou qwerty (touches du jeu inchangées)");
        CameraKey = c.CreateEntry("CameraKey", "V", description: "Touche : vue à la première personne ⇄ vue normale (mod LMD-Camera ; manette : clic du stick droit)");
        ShowPanel = c.CreateEntry("ShowPanel", true, description: "Panneau des joueurs visible au démarrage");
        EchoMode = c.CreateEntry("EchoMode", false, description: "Debug : affiche son propre fantôme, revenu par le réseau local avec un retard");
        EchoDelaySeconds = c.CreateEntry("EchoDelaySeconds", 2f, description: "Retard du fantôme écho (secondes)");
        SendRateHz = c.CreateEntry("SendRateHz", 25, description: "Poses envoyées par seconde (10 à 60)");
        InterpolationDelayMs = c.CreateEntry("InterpolationDelayMs", 100, description: "Retard d'affichage des fantômes (ms) : absorbe la gigue du réseau");
        GhostStyle = c.CreateEntry("GhostStyle", "None", description: "Aspect des fantômes (F7 pour essayer) : None (normal, comme ton vélo), Translucent (semi-transparent, nuancé de la couleur du joueur), Tint (opaque, teinté)");
        RecordPoses = c.CreateEntry("RecordPoses", false, description: "Debug : enregistre ses poses dans UserData/Ovomium/recordings (rejouables par LmdBot)");
        DebugLog = c.CreateEntry("DebugLog", false, description: "Debug : statistiques réseau et mouvements des os dans le journal");
        TestMode = c.CreateEntry("TestMode", false, description: "Tests seulement : le jeu n'enregistre aucun résultat (classements en ligne et locaux, succès, stats Steam, progression). À laisser à false pour jouer");
        foreach (var retired in new[] { "CaptureKey", "RaceQuitKey" }) c.DeleteEntry(retired); // réglages retirés du mod
        c.SaveToFile(false); // écrit les valeurs par défaut manquantes (fichier documenté)
    }

    public static KeyCode Key(MelonPreferences_Entry<string> entry) =>
        Enum.TryParse<KeyCode>(entry.Value, true, out var k) ? k : KeyCode.None;

    public static int ClampedSendRate => Math.Clamp(SendRateHz.Value, 10, 60);
}
