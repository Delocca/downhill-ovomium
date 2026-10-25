using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppMegagon.Downhill;
using Il2CppMegagon.Downhill.UI.Screens;

namespace LmdExplorer;

/// Sonde du démarrage (fichier drapeau « introprobe ») : journalise les appels des méthodes des écrans
/// de démarrage (logos, profil, écran titre) et les réglages de durée lus sur les objets, pour savoir
/// quoi raccourcir (src/LmdMulti/Tweaks/IntroSkip.cs). Ne modifie rien.
internal static class IntroProbe
{
    private static readonly Dictionary<string, int> Calls = new();
    private static readonly Type[] Watched =
    {
        typeof(SplashScreen), typeof(ProfileSelectionScreen), typeof(MainMenuScreen),
    };

    public static void Install(HarmonyLib.Harmony harmony)
    {
        if (!Out.Flag("introprobe")) return;
        var postfix = new HarmonyMethod(typeof(IntroProbe).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic));
        int n = 0;
        foreach (var type in Watched)
            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                // Un premier essai sur toutes les méthodes (statiques, Task, IEnumerator) a planté le jeu au démarrage.
                if (m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || m.IsGenericMethod || m.ReturnType != typeof(void)) continue;
                try { harmony.Patch(m, postfix: postfix); n++; }
                catch (Exception e) { Out.Info($"[intro] non patché {type.Name}.{m.Name} : {e.Message}"); }
            }
        Out.Info($"[intro] {n} méthodes espionnées");
    }

    private static void Postfix(MethodBase __originalMethod, object __instance)
    {
        string key = $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}";
        Calls.TryGetValue(key, out int c);
        Calls[key] = c + 1;
        if (c >= 3) return;
        Out.Info($"[intro] {DateTime.Now:HH:mm:ss.fff} {key}{(c > 0 ? $" (#{c + 1})" : "")}");
        if (c == 0) Out.Try("intro détail", () => Detail(__instance));
    }

    private static string _lastScreens = "", _lastText = "", _lastFlags = "";

    /// À chaque image tant que la sonde est active : écrans actifs et texte d'état du profil, à chaque changement.
    public static void Poll()
    {
        if (Calls.Count == 0 || Calls.ContainsKey("done")) return;
        string screens = string.Join(", ", UnityEngine.Object.FindObjectsOfType<ScreenBase>().Select(s => s.GetIl2CppType().Name));
        if (!Calls.ContainsKey("game") && UnityEngine.Object.FindObjectOfType<Game>() is Game g) { Calls["game"] = 1; Detail(g); }
        if (screens != _lastScreens) { _lastScreens = screens; Out.Info($"[intro] {DateTime.Now:HH:mm:ss.fff} écrans : {screens}"); }
        var p = UnityEngine.Object.FindObjectOfType<ProfileSelectionScreen>();
        string text = p != null && p.m_statusText != null ? $"{p.m_statusText.text} int={p.field_Private_Int32_0} bool={p.field_Private_Boolean_0}" : "";
        if (text != _lastText) { _lastText = text; Out.Info($"[intro] {DateTime.Now:HH:mm:ss.fff} profil : {text}"); }
        var m = UnityEngine.Object.FindObjectOfType<MainMenuScreen>();
        if (m != null)
        {
            ScreenBase b = m;
            string flags = $"mm.bool0={m.field_Private_Boolean_0} mm.prop={m.prop_Boolean_0} base.bool0={b.field_Private_Boolean_0} base.bool1={b.field_Private_Boolean_1} base.int={b.field_Private_Int32_0} base.prop={b.prop_Boolean_0} invite={(m.m_pressToStartLabel != null && m.m_pressToStartLabel.gameObject.activeInHierarchy)} boutons={(m.m_buttonList != null && m.m_buttonList.gameObject.activeInHierarchy)}";
            if (flags != _lastFlags) { _lastFlags = flags; Out.Info($"[intro] {DateTime.Now:HH:mm:ss.fff} titre : {flags}"); }
        }
        if (UnityEngine.Time.realtimeSinceStartup > 120) Calls["done"] = 1;
    }

    private static void Detail(object o)
    {
        switch (o)
        {
            case SplashScreen s:
                Out.Info($"[intro]   splash : pre={s.m_preDelay} between={s.m_betweenDelay} wait={s.m_waitTimeLogo} min={s.m_minWaitTimeLogo} fadeIn={s.m_fadeInDuration} fadeOut={s.m_fadeOutDuration} scale={s.m_scaleDuration} bgScale={s.m_backgroundScaleDuration} " +
                         $"mult=[{string.Join(",", s.m_splashScreenWaitMultiplicator.ToArray())}] key=[{string.Join(",", s.m_splashScreenWaitForKeypress.ToArray())}] écrans={s.m_splashScreenList?.Length}");
                break;
            case Game g:
                Out.Info($"[intro]   game : preSplash={(g.m_preSplashScreen != null ? g.m_preSplashScreen.name : "null")} delay={g.m_preSplashScreenDelay} noWarmup={Game.GlobalFlag_NoShaderWarmup}");
                break;
            case ProfileSelectionScreen p:
                Out.Info($"[intro]   profil : int={p.field_Private_Int32_0} bool={p.field_Private_Boolean_0} texte='{(p.m_statusText != null ? p.m_statusText.text : "")}'");
                break;
        }
    }
}
