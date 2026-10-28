using HarmonyLib;
using Il2Cpp;
using Il2CppMegagon.Downhill.Factories.Tools;
using Il2CppMegagon.Downhill.Vehicle.Configuration;
using Il2CppSystem.Collections.Generic;
using UnityEngine;
using ColorizedMesh = Il2CppMegagon.Framework.Graphics.ColorizedMesh;

namespace LmdExplorer;

// Espions de la fabrique d'apparence : arguments réels passés par le jeu quand il construit le joueur
// (à comparer au profil pour connaître le rôle des 4 entiers) et flottants transmis à la fusion / aux attaches.

[HarmonyPatch(typeof(ObjectPublicLi1CoHaFaHeBaSiReSiUnique),
    nameof(ObjectPublicLi1CoHaFaHeBaSiReSiUnique.Method_Private_GameObject_VehicleConfiguration_List_1_ColorizedMesh_List_1_ColorizedMesh_Int32_Int32_Int32_Int32_0))]
internal static class HookBuildPlayer
{
    private static void Prefix(ObjectPublicLi1CoHaFaHeBaSiReSiUnique __instance, VehicleConfiguration param_1,
        List<ColorizedMesh> param_2, List<ColorizedMesh> param_3, int param_4, int param_5, int param_6, int param_7) =>
        Out.Info($"[hook] fabrique.Build(vélo={param_1?.ID}, {param_2?.Count} + {param_3?.Count} meshes, " +
                 $"ints={param_4} {param_5} {param_6} {param_7}) champs={__instance.field_Private_HairType_0} " +
                 $"{__instance.field_Private_FacialHairType_0} {__instance.field_Private_HelmetType_0} {__instance.field_Private_BackAccessoryType_0} " +
                 $"floats={__instance.field_Private_Single_0} {__instance.field_Private_Single_1} {__instance.field_Private_Single_2}");
}

[HarmonyPatch(typeof(MeshMerger), nameof(MeshMerger.Method_Public_Void_List_1_ColorizedMesh_Single_Single_0))]
internal static class HookMerge
{
    private static void Prefix(List<ColorizedMesh> param_1, float param_2, float param_3) =>
        Out.Info($"[hook] MeshMerger.Merge({param_1?.Count} meshes, {param_2:G5}, {param_3:G5})");
}

[HarmonyPatch(typeof(RiderAttachment), nameof(RiderAttachment.InitializeAttachment))]
internal static class HookAttach
{
    // Noms de paramètres non obfusqués ici (Harmony les apparie par nom).
    private static void Prefix(RiderAttachment __instance, Material material, float skinHue, float hairHue) =>
        Out.Info($"[hook] RiderAttachment.Init({__instance.name}, {material?.name}, peau={skinHue:G5}, cheveux={hairHue:G5})");
}
