using HarmonyLib;
using Il2Cpp;
using Il2CppMegagon.Downhill;
using Il2CppMegagon.Downhill.UI.Screens;
using Ovomium.Core.Net;
using Ovomium.Game;
using UnityEngine;

namespace Ovomium.Race;

/// Accès au jeu pour la course (voir docs/game-api.md § 5) : blocage du vélo au départ, retour au départ,
/// écran de chargement, sections franchies et arrivée (signalées par le tracker de manche du jeu).
/// Sections et arrivée viennent de patchs sur ses méthodes, pas de ses champs : ceux-ci gardent les
/// valeurs de la manche précédente tant que le joueur n'a pas démarré (vérifié : fausse arrivée sinon).
internal static class RaceGame
{
    /// Tant que vrai, le jeu ne peut pas libérer le vélo (premier appui du joueur, restart) : on attend le « GO ».
    public static bool HoldAtStart;
    /// Dernier appel à la méthode « arrivée » du tracker (NetClock).
    public static double FinishCalledAtMs = double.NaN;
    /// Dernière section franchie signalée par le jeu (1, 2… ; la ligne d'arrivée compte pour la dernière).
    public static int LastSection;

    public static void LockBike(bool locked) => PlayerManager.Method_Public_Static_Void_Boolean_0(locked);

    /// « Recommencer la tentative » du menu pause : retour au départ, vélo bloqué.
    public static void RestartRun()
    {
        foreach (var p in Resources.FindObjectsOfTypeAll<PauseScreen>())
            if (p.gameObject.scene.IsValid()) { p.OnRestartRun(); return; } // pas le prefab
        Log.Warn("course : écran pause introuvable, pas de retour au départ");
    }

    /// Chrono affiché par le jeu (contrôle du chrono du mod).
    public static string GameTimeText()
    {
        var ps = Object.FindObjectOfType<PlayScreen>();
        return ps != null && ps.m_timePanelText != null ? ps.m_timePanelText.text : "?";
    }

    /// Nombre de checkpoints de la piste en cours, départ et arrivée compris (0 si inconnu).
    public static int CheckpointCount()
    {
        var route = PlayManager.prop_TrackRoute_0;
        return route?.m_checkpoints != null ? route.m_checkpoints.Length : 0;
    }
}

/// « LockMovement(bool) » du jeu : on refuse la libération du vélo tant que la course n'est pas partie.
[HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.Method_Public_Static_Void_Boolean_0))]
internal static class HoldBikePatch
{
    private static bool Prefix(bool param_1) => param_1 || !RaceGame.HoldAtStart;
}

/// Section franchie (tracker de manche, vérifié en jeu le 2026-10-25 : 0 au premier checkpoint après le
/// départ, la ligne d'arrivée = dernière) : classement en direct.
[HarmonyPatch(typeof(ObjectPublicBoGaObInSiStObDi2LiUnique), nameof(ObjectPublicBoGaObInSiStObDi2LiUnique.Method_Public_Void_Int32_Single_PDM_0))]
internal static class SectionPatch
{
    private static void Prefix(int param_1) => RaceGame.LastSection = param_1 + 1; // le jeu compte depuis 0
}

/// Arrivée (tracker de manche, vérifié en jeu le 2026-10-25 : appelée au franchissement de la ligne).
[HarmonyPatch(typeof(ObjectPublicBoGaObInSiStObDi2LiUnique), nameof(ObjectPublicBoGaObInSiStObDi2LiUnique.Method_Public_Void_PDM_0))]
internal static class FinishPatch
{
    private static void Postfix() => RaceGame.FinishCalledAtMs = NetClock.NowMs;
}
