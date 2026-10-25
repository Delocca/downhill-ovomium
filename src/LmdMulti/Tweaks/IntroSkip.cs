using HarmonyLib;
using Il2CppMegagon.Downhill.UI.Screens;
using UnityEngine;

namespace LmdMulti.Tweaks;

/// Démarrage rapide (réglage SkipIntro, actif par défaut) : logos Megagon / Thunderful / partenaires
/// réduits à rien, puis écran titre « Appuyez sur… » passé tout seul, une fois par lancement.
/// Le chargement du profil et la connexion au serveur de classement restent ceux du jeu (ils suivent les
/// logos). Séquence relevée par la sonde de LmdExplorer (docs/game-api.md § 10).
internal static class IntroSkip
{
    // Laisser l'écran titre finir de s'installer avant de « valider » : au moins TitleDelay, puis tant qu'il est
    // occupé (prop_Boolean_0, vrai pendant ses animations d'après la sonde), au plus TitleMaxWait.
    private const float TitleDelay = 0.4f, TitleMaxWait = 2f;
    private static bool _titleDone, _splashDone;
    private static float _titleSeenAt = -1f, _nextPoll;

    private static bool On => Config.SkipIntro != null && Config.SkipIntro.Value;

    /// Logos : appelé à la mise en place de l'écran de logos, avant que sa coroutine ne lise les durées.
    internal static void ShortenSplash(SplashScreen s)
    {
        if (!On) return;
        s.m_preDelay = 0f;
        s.m_betweenDelay = 0f;
        s.m_waitTimeLogo = 0f;
        s.m_minWaitTimeLogo = 0f;
        // Fondus : un peu plus que zéro (une durée nulle pourrait donner 0/0 dans une interpolation).
        s.m_fadeInDuration = s.m_fadeOutDuration = s.m_scaleDuration = 0.01f;
        var mult = s.m_splashScreenWaitMultiplicator;
        for (int i = 0; mult != null && i < mult.Length; i++) mult[i] = 0f;
        var key = s.m_splashScreenWaitForKeypress;
        for (int i = 0; key != null && i < key.Length; i++) key[i] = false;
        // Logos masqués : sinon chacun apparaît une fraction de seconde (la coroutine les parcourt quand même).
        var logos = s.m_splashScreenList;
        for (int i = 0; logos != null && i < logos.Length; i++)
            if (logos[i] != null) logos[i].gameObject.SetActive(false);
        if (!_splashDone) Log.Info($"intro : logos passés ({Time.realtimeSinceStartup:0.0} s depuis le lancement d'Unity)");
        _splashDone = true; // la méthode est appelée deux fois par le jeu
    }

    /// À chaque image jusqu'au menu : écran titre affiché → même action qu'un appui sur « valider ».
    public static void Update()
    {
        if (_titleDone || !On || Time.realtimeSinceStartup < _nextPoll) return;
        _nextPoll = Time.realtimeSinceStartup + 0.1f;
        if (Time.realtimeSinceStartup > 180f) { _titleDone = true; return; } // jamais vu : on n'insiste pas
        var title = Object.FindObjectOfType<MainMenuScreen>();
        if (title == null || !title.isActiveAndEnabled) { _titleSeenAt = -1f; return; }
        if (_titleSeenAt < 0f) { _titleSeenAt = Time.realtimeSinceStartup; return; }
        // Pendant son animation d'arrivée, l'écran titre ignore l'appui (invite restée, premier appui suivant perdu).
        float shown = Time.realtimeSinceStartup - _titleSeenAt;
        if (shown < TitleDelay || (title.prop_Boolean_0 && shown < TitleMaxWait)) return;
        _titleDone = true;
        // Comme un appui (relevé par la sonde de LmdExplorer) : le drapeau « attend un appui » tombe, puis invite
        // masquée et boutons affichés. Sans le drapeau, l'invite restait et le premier appui suivant était perdu.
        title.field_Private_Boolean_0 = false;
        title.Method_Private_Void_2();
        title.ShowButtonList();
        Log.Info($"intro : écran titre passé, menu à {Time.realtimeSinceStartup:0.0} s du lancement d'Unity");
    }

    /// Le joueur a validé l'écran titre lui-même : plus rien à faire.
    internal static void TitleValidated() => _titleDone = true;
}

[HarmonyPatch(typeof(SplashScreen), nameof(SplashScreen.Method_Protected_Virtual_Void_0))]
internal static class SplashShowPatch
{
    private static void Prefix(SplashScreen __instance) => Log.Try("intro : logos", () => IntroSkip.ShortenSplash(__instance));
}

[HarmonyPatch(typeof(MainMenuScreen), nameof(MainMenuScreen.ShowButtonList))]
internal static class TitleValidatedPatch
{
    private static void Postfix() => IntroSkip.TitleValidated();
}
