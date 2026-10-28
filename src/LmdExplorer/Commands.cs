using System;
using System.Collections.Generic;
using System.IO;
using Il2CppMegagon.Downhill;
using Il2CppMegagon.Downhill.Tracks;
using Il2CppMegagon.Downhill.UI.Screens;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LmdExplorer;

/// Commandes de test, déclenchées par le fichier UserData/LmdExplorer/cmd.txt (une commande par ligne)
/// pour piloter le jeu sans clavier (Xvfb). Le fichier est supprimé après lecture.
internal static class Commands
{
    private const string FileName = "cmd.txt";

    // Sous Proton, Z: = racine Linux : /tmp/lmdexplorer-cmd.txt est aussi lu (pratique pour les tests).
    private static readonly string[] Paths = { Path.Combine(Out.Dir, FileName), @"Z:\tmp\lmdexplorer-cmd.txt" };

    public static void Poll()
    {
        foreach (var p in Paths) PollFile(p);
    }

    private static void PollFile(string path)
    {
        if (!File.Exists(path)) return;
        string[] lines = File.ReadAllLines(path);
        File.Delete(path);
        foreach (var line in lines)
            if (!string.IsNullOrWhiteSpace(line)) Out.Try($"commande '{line}'", () => Run(line.Trim()));
    }

    /// Table des commandes : nom -> action(argument éventuel).
    private static readonly Dictionary<string, Action<string>> Table = new()
    {
        ["dump"] = _ => Probes.FullDump(),
        ["status"] = _ => Out.Info(Probes.StatusLine()),
        ["puppet"] = _ => TogglePuppet(),
        ["skin"] = SkinProbe.Run,                                                          // skin | skin build [a b c d] [clé=val…] | skin off
        ["screens"] = _ => ListScreens(),
        // Navigation / chargement de piste
        ["mainstart"] = _ => Object.FindObjectOfType<MainMenuScreen>().OnStartButton(),  // écran titre -> menu de jeu
        ["start"] = _ => Object.FindObjectOfType<GameMenu>().Method_Public_Void_1(),      // lance la piste sélectionnée
        ["menu"] = _ => GameStateManager.Method_Public_Static_Void_1(),                    // piste -> menu de jeu
        ["catalogue"] = _ => Out.WriteFile("catalogue.txt", TrackProbe.Catalogue()),
        ["sel"] = _ => Out.Info(TrackProbe.SelectionLine()),
        ["select"] = id => TrackProbe.Select(id),                                           // select AL_01_A1
        // Course
        ["restart"] = _ => FirstPause().OnRestartRun(),
        ["restartcp"] = _ => FirstPause().OnRestartCheckpoint(),
        ["lock"] = _ => PlayerManager.Method_Public_Static_Void_Boolean_0(true),
        ["unlock"] = _ => PlayerManager.Method_Public_Static_Void_Boolean_0(false),
        ["tostart"] = _ => PlayerManager.Method_Public_Static_Void_Transform_0(StartTransform()),
        ["tocp"] = n => PlayerManager.Method_Public_Static_Void_Transform_0(CheckpointTransform(n)),         // tocp [N] (défaut : avant-dernier ; f = arrivée)
        ["timer"] = _ => Out.Info(TrackProbe.TimerLine()),
        ["record"] = id => LocalRecords(id),                                                                // record RO_01_A4
        ["online"] = id => OnlineScores.Read(id),                                                           // online RO_01_A4 (lecture seule)

        ["call"] = spec => Invoker.Call(spec),
        ["maps"] = _ => InputMaps.Dump(),                                                                   // cartes Rewired -> maps.txt
        ["refs"] = type => RefProbe.Run(type),                                                              // refs [Type] : références périmées ?
        ["res"] = SetResolution,                                                                            // res 1920 1080 : fenêtre (session seulement)
        ["nodof"] = _ => NoDepthOfField(),                                                                  // coupe le flou de focale (session seulement, captures nettes)
    };

    /// Flou de focale coupé en mémoire : réglages du post-traitement et pont du jeu qui les pilote.
    private static void NoDepthOfField()
    {
        int n = 0;
        foreach (var b in Resources.FindObjectsOfTypeAll<Il2CppMegagon.Framework.Graphics.DepthOfFieldBridge>()) { b.enabled = false; n++; }
        foreach (var d in Resources.FindObjectsOfTypeAll<UnityEngine.Rendering.PostProcessing.DepthOfField>()) { d.enabled.value = false; n++; }
        Out.Info($"flou de focale coupé ({n} objets)");
    }

    public static void Run(string line)
    {
        Out.Info($"> {line}");
        string[] a = line.Split(' ', 2);
        if (Table.TryGetValue(a[0], out var action)) action(a.Length > 1 ? a[1] : null);
        else Out.Info($"commande inconnue. Connues : {string.Join(", ", Table.Keys)}");
    }

    private static void ListScreens()
    {
        foreach (var s in Object.FindObjectsOfType<ScreenBase>())
            Out.Info($"écran actif : {s.GetIl2CppType().Name} ({s.name})");
    }

    private static void TogglePuppet()
    {
        var bike = BikeProbe.Find();
        PuppetTest.Toggle(bike != null ? bike.gameObject : null);
    }

    /// Résolution de la fenêtre pour cette session (tests d'affichage) : les réglages du jeu ne sont pas
    /// modifiés, mais Unity mémorise la taille en quittant (registre du préfixe : tools/lmd-virtual.sh la remet).
    private static void SetResolution(string arg)
    {
        string[] wh = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Screen.SetResolution(int.Parse(wh[0]), int.Parse(wh[1]), FullScreenMode.Windowed);
        Out.Info($"résolution demandée : {wh[0]}x{wh[1]} fenêtré");
    }

    private static PauseScreen FirstPause()
    {
        var all = Resources.FindObjectsOfTypeAll<PauseScreen>();
        return all.Length > 0 ? all[0] : null;
    }

    /// Records locaux (profil) d'une piste : total (section 0) puis chaque section, en ms
    /// (≈ 999998976 = aucun). Identifiant de classement construit comme le fait le tracker du jeu.
    private static void LocalRecords(string trackId)
    {
        var lm = Il2CppMegagon.Downhill.Managers.Manager<LeaderboardManager>.prop_T_0;
        var mode = MountainManager.prop_GameMode_0;
        var tod = MountainManager.prop_EnumPublicSealedva5v2_0;
        for (int s = 0; s <= 9; s++)
        {
            string id = HighscoreManager.Method_Public_Static_String_String_Int32_GameMode_EnumPublicSealedva5v2_0(trackId, s, mode, tod);
            Out.Info($"record local {id} = {lm.Method_Private_UInt32_String_PDM_0(id)}");
        }
    }

    /// Point de respawn du checkpoint N (défaut : le dernier qui en a un avant l'arrivée) : pour rouler
    /// seulement la dernière section et tester la détection d'arrivée.
    private static Transform CheckpointTransform(string arg)
    {
        var cps = PlayManager.prop_TrackRoute_0.m_checkpoints;
        if (arg == "f") return cps[cps.Length - 1].transform; // dans la ligne d'arrivée
        if (arg != null && arg.StartsWith("g")) // gN : dans la porte du checkpoint N (franchissement)
        {
            var gate = cps[int.Parse(arg.Substring(1))];
            foreach (var col in gate.GetComponentsInChildren<Collider>(true))
                Out.Info($"porte {gate.name} : {col.name} trigger={col.isTrigger} centre={col.bounds.center} taille={col.bounds.size}");
            return gate.transform;
        }
        int n = string.IsNullOrEmpty(arg) ? cps.Length - 2 : int.Parse(arg);
        while (n > 0 && !cps[n].m_hasSpawnPoint) n--;
        var cp = cps[n];
        Out.Info($"vers cp[{n}] = {cp.name} spawn={cp.m_spawnTransform?.position} (arrivée {cps[cps.Length - 1].transform.position})");
        return cp.m_spawnTransform != null ? cp.m_spawnTransform : cp.transform;
    }

    /// Point de spawn du premier checkpoint de la route courante (= départ, supposé).
    private static Transform StartTransform()
    {
        var route = PlayManager.prop_TrackRoute_0;
        var cp = route.m_checkpoints[0];
        Out.Info($"départ = {cp.name} spawn={cp.m_spawnTransform?.position}");
        return cp.m_spawnTransform != null ? cp.m_spawnTransform : cp.transform;
    }
}
