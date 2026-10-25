using HarmonyLib;
using Il2Cpp;
using Il2CppMegagon.Downhill;
using Il2CppMegagon.Downhill.TrackElements;
using Il2CppMegagon.Downhill.Tracks;
using Il2CppMegagon.Downhill.Tracks.SceneManagement;
using Il2CppMegagon.Downhill.UI.Screens;
using Il2CppMegagon.Downhill.Vehicle.Controller;
using Il2CppMegagon.Downhill.Vehicle;
using UnityEngine;

namespace LmdExplorer;

// Patches Harmony « espions » : ils ne modifient rien, ils journalisent les appels pour confirmer
// le rôle des méthodes obfusquées (rôle déduit des signatures / des noms de lambdas).
// Bilan des essais (batchmode, 2026-10-25) : vus en jeu = PlayManager PDM_3 (lancement piste),
// SetupRoute, SetToTransform, LockMovement, OnRestartRun. Jamais déclenchés = LoadTrack (PlaySceneManager),
// PlayManager Int32, Crash (pas appelé lors d'un crash FallTime). Section/Finish/Checkpoint : non testés
// (le vélo ne roule pas en batchmode). Détails : docs/game-api.md.

[HarmonyPatch(typeof(PlaySceneManager), nameof(PlaySceneManager.Method_Public_Void_String_String_EnumPublicSealedva5v2_Action_1_TrackRoute_Action_PDM_0))]
internal static class HookLoadTrack
{
    private static void Prefix(string param_1, string param_2, EnumPublicSealedva5v2 param_3) =>
        Out.Info($"[hook] PlaySceneManager.LoadTrack?('{param_1}', '{param_2}', tod={(int)param_3})");
}

[HarmonyPatch(typeof(PlayManager), nameof(PlayManager.Method_Private_Void_TrackRoute_PDM_0))]
internal static class HookSetupRoute
{
    private static void Prefix(TrackRoute param_1) =>
        Out.Info($"[hook] PlayManager.SetupRoute?({(param_1 != null ? param_1.m_trackRouteID : "null")})");
}

[HarmonyPatch(typeof(PlayManager), nameof(PlayManager.Method_Private_Void_PDM_3))]
internal static class HookPlayPdm3
{
    private static void Prefix() => Out.Info("[hook] PlayManager.Method_Private_Void_PDM_3 (chargement piste ?)");
}

[HarmonyPatch(typeof(PlayManager), nameof(PlayManager.Method_Public_Static_Void_Int32_0))]
internal static class HookPlayInt
{
    private static void Prefix(int param_1) => Out.Info($"[hook] PlayManager.Method_Public_Static_Void_Int32_0({param_1})");
}

[HarmonyPatch(typeof(ObjectPublicBoGaObInSiStObDi2LiUnique), nameof(ObjectPublicBoGaObInSiStObDi2LiUnique.Method_Public_Void_Int32_Single_PDM_0))]
internal static class HookSection
{
    private static void Prefix(int param_1, float param_2) => Out.Info($"[hook] tracker.Section({param_1}, {param_2:F3}s)");
}

[HarmonyPatch(typeof(ObjectPublicBoGaObInSiStObDi2LiUnique), nameof(ObjectPublicBoGaObInSiStObDi2LiUnique.Method_Public_Void_PDM_0))]
internal static class HookFinish
{
    private static void Postfix() => Out.Info($"[hook] tracker.Finish() -> {TrackProbe.TimerLine()}");
}

[HarmonyPatch(typeof(TrackCheckpoint), nameof(TrackCheckpoint.Method_Public_Virtual_Void_Int32_Boolean_0))]
internal static class HookCheckpointTrigger
{
    private static void Prefix(TrackCheckpoint __instance, int param_1, bool param_2) =>
        Out.Info($"[hook] TrackCheckpoint.Trigger?({__instance.name}, {param_1}, {param_2})");
}

[HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.Method_Public_Static_Void_Transform_0))]
internal static class HookSetToTransform
{
    private static void Prefix(Transform param_1) =>
        Out.Info($"[hook] PlayerManager.SetToTransform?({(param_1 != null ? $"{param_1.name} {param_1.position}" : "null")})");
}

[HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.Method_Public_Static_Void_Boolean_0))]
internal static class HookLockMovement
{
    private static void Prefix(bool param_1) => Out.Info($"[hook] PlayerManager.LockMovement?({param_1})");
}

[HarmonyPatch(typeof(BikeLocomotion), nameof(BikeLocomotion.Method_Private_Void_CrashReason_Boolean_0))]
internal static class HookCrash
{
    private static void Prefix(CrashReason param_1, bool param_2) => Out.Info($"[hook] BikeLocomotion.Crash?({param_1}, {param_2})");
}

[HarmonyPatch(typeof(PauseScreen), nameof(PauseScreen.OnRestartRun))]
internal static class HookRestartRun
{
    private static void Prefix() => Out.Info("[hook] PauseScreen.OnRestartRun");
}
