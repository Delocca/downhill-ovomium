using HarmonyLib;
using Il2Cpp;
using Il2CppMegagon.Downhill;
using Il2CppMegagon.Downhill.Analytics;
using Il2CppMegagon.Downhill.Statistics;

namespace LmdMulti.Diagnostics;

/// Mode test (réglage TestMode, faux par défaut) : le jeu n'enregistre AUCUN résultat de manche — ni
/// classements (serveur de Megagon et records locaux, temps total et temps de section, envoyés à chaque
/// checkpoint), ni succès Steam, ni stats Steam, ni analytics, ni progression. Indispensable pour toute
/// manche scriptée ou téléportée (temps irréalistes). Une vraie course entre amis, mode test coupé,
/// compte normalement. Chemins d'appel (désassemblage) : docs/game-api.md § 5 « Arrivées de test ».
internal static class TestMode
{
    public static bool On => Config.TestMode != null && Config.TestMode.Value;

    /// Prefix commun : laisse passer hors mode test, sinon bloque et le dit dans le journal.
    public static bool Allow(string what)
    {
        if (!On) return true;
        Log.Info($"[mode test] bloqué : {what}");
        return false;
    }
}

/// Point unique d'envoi d'un temps (total ou section) vers chaque fournisseur de classement, en ligne et local.
[HarmonyPatch(typeof(ObjectPublicAbstractStInObBoObStInObInStUnique),
    nameof(ObjectPublicAbstractStInObBoObStInObInStUnique.Method_Public_Virtual_Final_New_Void_UInt32_Int32_Int32_Boolean_String_Action_1_ValueTypePublicSealedBoBoBoUnique_0))]
internal static class BlockScoreSubmit
{
    private static bool Prefix(uint param_1) => TestMode.Allow($"temps de classement {param_1} ms");
}

/// Filets (ne doivent jamais se déclencher en mode test si le blocage ci-dessus tient) : envoi en ligne, écriture locale.
[HarmonyPatch(typeof(Object1PublicVoStObIn4289VoAc1VoUnique),
    nameof(Object1PublicVoStObIn4289VoAc1VoUnique.Method_Protected_Virtual_Void_InterfacePublicAbstractUIInUIVoInUIInInUIUIUnique_String_Action_1_ValueTypePublicSealedBoBoBoUnique_0))]
internal static class BlockOnlineScore
{
    private static bool Prefix() => TestMode.Allow("FILET : envoi au serveur de classement");
}

[HarmonyPatch(typeof(Object1PublicFu2StUIAc2StUIUnique),
    nameof(Object1PublicFu2StUIAc2StUIUnique.Method_Protected_Virtual_Void_InterfacePublicAbstractUIInUIVoInUIInInUIUIUnique_String_Action_1_ValueTypePublicSealedBoBoBoUnique_0))]
internal static class BlockLocalScore
{
    private static bool Prefix() => TestMode.Allow("FILET : record local (fournisseur)");
}

[HarmonyPatch(typeof(LeaderboardManager), nameof(LeaderboardManager.Method_Private_Void_String_UInt32_PDM_0))]
internal static class BlockLocalRecord
{
    private static bool Prefix(string param_1, uint param_2) => TestMode.Allow($"FILET : record local {param_1} = {param_2} ms");
}

[HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.Method_Public_Static_Void_PDM_0))]
internal static class BlockAchievements
{
    private static bool Prefix() => TestMode.Allow("succès de fin de manche");
}

[HarmonyPatch(typeof(StatisticManager), nameof(StatisticManager.Method_Private_Void_StatisticsMetric_Single_2))]
internal static class BlockStatFloat
{
    private static bool Prefix(StatisticsMetric param_1) => TestMode.Allow($"stat Steam {param_1}");
}

[HarmonyPatch(typeof(StatisticManager), nameof(StatisticManager.Method_Private_Void_StatisticsMetric_Int32_PDM_3))]
internal static class BlockStatInt
{
    private static bool Prefix(StatisticsMetric param_1) => TestMode.Allow($"stat Steam {param_1}");
}

[HarmonyPatch(typeof(AnalyticsReporter), nameof(AnalyticsReporter.Method_Public_Static_Void_PDM_0))]
internal static class BlockAnalytics
{
    private static bool Prefix() => TestMode.Allow("analytics de checkpoint");
}
