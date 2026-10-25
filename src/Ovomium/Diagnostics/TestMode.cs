using HarmonyLib;
using Il2Cpp;
using Il2CppMegagon.Downhill;
using Il2CppMegagon.Downhill.Analytics;
using Il2CppMegagon.Downhill.Progression;
using Il2CppMegagon.Downhill.Statistics;
using Ovomium.Game;
using Ovomium.Race;

namespace Ovomium.Diagnostics;

/// Résultats de manche que le jeu n'enregistre pas, par prefix sur ses méthodes d'enregistrement
/// (chemins d'appel : docs/game-api.md § 5 « Arrivées de test ») :
/// - mode test (réglage TestMode, faux par défaut) : RIEN — ni classements (serveur de Megagon et records
///   locaux, temps total et de section, envoyés à chaque checkpoint), ni succès, stats Steam, analytics,
///   progression. Obligatoire pour toute manche scriptée ou téléportée ;
/// - piste forcée (verrouillée chez ce joueur, ouverte pour une course : TrackAccess) : ni temps (en ligne
///   et local) ni progression ; succès et stats restent comptés.
/// Une course sur une piste normalement débloquée, mode test coupé, compte normalement.
internal static class TestMode
{
    public static bool On => Config.TestMode != null && Config.TestMode.Value;

    /// Prefix commun aux enregistrements hors temps et progression : bloqué en mode test seulement.
    public static bool Allow(string what)
    {
        if (!On) return true;
        Log.Info($"[mode test] bloqué : {what}");
        return false;
    }

    /// Prefix des temps et de la progression : bloqués sur une piste forcée, et en mode test. La piste
    /// forcée est vérifiée d'abord : en mode test, le journal dit quelle règle aurait joué hors test.
    public static bool AllowResult(string what, string id = null)
    {
        if (TrackAccess.IsForcedFor(GameInfo.CurrentTrackId(), id))
        {
            Log.Info($"résultat non enregistré : piste forcée ({what})");
            return false;
        }
        if (!On) return true;
        Log.Info($"[mode test] bloqué : {what} (piste débloquée : hors mode test, enregistré normalement)");
        return false;
    }
}

/// Point unique d'envoi d'un temps (total ou section) vers chaque fournisseur de classement, en ligne et local.
[HarmonyPatch(typeof(ObjectPublicAbstractStInObBoObStInObInStUnique),
    nameof(ObjectPublicAbstractStInObBoObStInObInStUnique.Method_Public_Virtual_Final_New_Void_UInt32_Int32_Int32_Boolean_String_Action_1_ValueTypePublicSealedBoBoBoUnique_0))]
internal static class BlockScoreSubmit
{
    // param_5 : chaîne vide lors des essais (2026-10-25), pas l'ID de classement ; la piste en cours fait foi.
    private static bool Prefix(uint param_1, string param_5) => TestMode.AllowResult($"temps de classement {param_1} ms", param_5);
}

/// Filets (ne doivent jamais se déclencher si le blocage ci-dessus tient) : envoi en ligne, écriture locale.
[HarmonyPatch(typeof(Object1PublicVoStObIn4289VoAc1VoUnique),
    nameof(Object1PublicVoStObIn4289VoAc1VoUnique.Method_Protected_Virtual_Void_InterfacePublicAbstractUIInUIVoInUIInInUIUIUnique_String_Action_1_ValueTypePublicSealedBoBoBoUnique_0))]
internal static class BlockOnlineScore
{
    private static bool Prefix(string param_2) => TestMode.AllowResult($"FILET : envoi au serveur de classement {param_2}", param_2);
}

[HarmonyPatch(typeof(Object1PublicFu2StUIAc2StUIUnique),
    nameof(Object1PublicFu2StUIAc2StUIUnique.Method_Protected_Virtual_Void_InterfacePublicAbstractUIInUIVoInUIInInUIUIUnique_String_Action_1_ValueTypePublicSealedBoBoBoUnique_0))]
internal static class BlockLocalScore
{
    private static bool Prefix(string param_2) => TestMode.AllowResult($"FILET : record local (fournisseur) {param_2}", param_2);
}

[HarmonyPatch(typeof(LeaderboardManager), nameof(LeaderboardManager.Method_Private_Void_String_UInt32_PDM_0))]
internal static class BlockLocalRecord
{
    private static bool Prefix(string param_1, uint param_2) => TestMode.AllowResult($"FILET : record local {param_1} = {param_2} ms", param_1);
}

/// Arrivée enregistrée par la progression du jeu (objectifs, déblocages) : sur une piste forcée, des
/// objectifs validés débloqueraient d'autres pistes pour de bon.
[HarmonyPatch(typeof(ProgressionDB), nameof(ProgressionDB.Method_Public_Void_String_GameMode_Int32_EnumPublicSealedva5v2_Single_Int32_PDM_0))]
internal static class BlockProgression
{
    private static bool Prefix(string param_1) => TestMode.AllowResult($"progression ({param_1})", param_1);
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
