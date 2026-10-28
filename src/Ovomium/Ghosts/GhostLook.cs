using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Ovomium.Ghosts;

/// Aspect des fantômes (réglage GhostStyle) :
///  - None (défaut, « normal ») : identique au vélo local ; le pseudo coloré distingue les joueurs ;
///  - Translucent : semi-transparent, sans éclairage ni ombre, nuancé de la couleur du joueur
///    sur toutes les parties du modèle, attaches comprises (matériaux propres au fantôme, un par
///    texture d'origine, détruits avec lui) ;
///  - Tint : opaque, couleurs multipliées par celle du joueur (MaterialPropertyBlock : les matériaux
///    partagés avec le vélo local ne sont jamais modifiés). Effet discret : les couleurs viennent
///    surtout des sommets du modèle, que _Color ne fait que moduler.
internal static class GhostLook
{
    public enum Style { None, Tint, Translucent }

    private const string TranslucentShader = "Legacy Shaders/Particles/Alpha Blended";
    private const string ModelShader = "Megagon/Standard VertexColor"; // vélo + rider (variantes Simple, NoCulling…)
    private static bool _logged;

    public static Style Parse(string s) => System.Enum.TryParse<Style>(s, true, out var v) ? v : Style.None;

    private static readonly Dictionary<ulong, int> Slots = new();

    /// Couleur d'un joueur (fantôme, pseudo, pastille du panneau), ambre pour l'écho. Attribuée dans
    /// l'ordre d'arrivée, teintes espacées du nombre d'or : les premiers joueurs ont des couleurs bien
    /// distinctes (un hachage du pseudo en donnait souvent de très proches). Locale à chaque PC.
    public static Color ColorFor(ulong peerId, bool echo)
    {
        if (echo) return new Color(1f, 0.78f, 0.3f);
        if (!Slots.TryGetValue(peerId, out int slot)) Slots[peerId] = slot = Slots.Count;
        return Color.HSVToRGB((0.55f + slot * 0.618034f) % 1f, 0.6f, 1f);
    }

    /// Applique le style ; renvoie les matériaux créés pour ce fantôme (à détruire avec lui).
    public static List<Material> Apply(GameObject go, Color color, Style style)
    {
        var created = new List<Material>();
        if (style == Style.None) return created;
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        LogMaterials(renderers);
        var translucent = new Dictionary<int, Material>(); // par texture d'origine (0 = aucune)
        foreach (var r in renderers)
        {
            var mats = r.sharedMaterials;
            if (style == Style.Translucent && IsModelPart(mats))
            {
                var arr = new Material[mats.Length];
                for (int i = 0; i < arr.Length; i++) arr[i] = TranslucentFor(mats[i], color, translucent, created) ?? mats[i];
                r.sharedMaterials = arr;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                continue;
            }
            for (int i = 0; i < mats.Length; i++) Tint(r, i, mats[i], color);
        }
        return created;
    }

    /// Partie du vélo ou du rider (corps fusionné, cheveux, casque, dos…), et non phares ou particules.
    private static bool IsModelPart(Material[] mats) =>
        mats.Length > 0 && mats.All(m => m != null && m.shader != null && m.shader.name.StartsWith(ModelShader));

    /// Matériau translucide qui reprend la texture du matériau d'origine (palette des attaches, lue par UV).
    private static Material TranslucentFor(Material source, Color color, Dictionary<int, Material> cache, List<Material> created)
    {
        var tex = Declares(source, "_MainTex") ? source.GetTexture("_MainTex") : null;
        int key = tex != null ? tex.GetInstanceID() : 0;
        if (cache.TryGetValue(key, out var m)) return m;
        m = MakeTranslucent(color);
        if (m != null)
        {
            m.SetTexture("_MainTex", tex); // sans elle, cheveux et peau perdent leur teinte (blanchâtres)
            created.Add(m);
        }
        return cache[key] = m;
    }

    private static void Tint(Renderer r, int index, Material m, Color color)
    {
        // HasProperty a répondu faux en batchmode pour ce shader : on teste aussi sa déclaration.
        if (m == null || m.shader == null || !Declares(m, "_Color")) return;
        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block, index);
        // Multiplier assombrit : on compense un peu pour garder un fantôme lumineux.
        // (Essayé : _EMISSIVE/_EmissiveColor par bloc de propriétés, sans effet visible sur ce shader.)
        block.SetColor("_Color", m.GetColor("_Color") * Color.Lerp(Color.white, color, 0.75f) * 1.3f);
        r.SetPropertyBlock(block, index);
    }

    /// Matériau translucide non éclairé. Le shader calcule 2 × couleur de sommet × _MainTex (palette où
    /// la fabrique place peau et cheveux par les UV) × _TintColor : teinte à moitié pour garder les
    /// couleurs, nuancée par celle du joueur.
    private static Material MakeTranslucent(Color color)
    {
        var shader = Shader.Find(TranslucentShader);
        if (shader == null) { Log.Warn($"shader {TranslucentShader} absent : fantômes opaques"); return null; }
        var m = new Material(shader) { name = "LmdGhostTranslucent" };
        var tint = Color.Lerp(Color.white, color, TranslucentHue) * 0.5f;
        tint.a = TranslucentAlpha * 0.5f;
        m.SetColor("_TintColor", tint);
        return m;
    }

    // Réglages du style translucide (opacité finale, part de la couleur du joueur).
    private const float TranslucentAlpha = 0.5f, TranslucentHue = 0.45f;

    private static bool Declares(Material m, string prop) =>
        m.HasProperty(prop) || m.shader.FindPropertyIndex(prop) >= 0;

    /// DebugLog, une fois : matériaux du modèle et shaders disponibles (pour choisir un style).
    private static void LogMaterials(Renderer[] renderers)
    {
        if (_logged || !Config.DebugLog.Value) return;
        _logged = true;
        var log = new StringBuilder($"matériaux des fantômes ({renderers.Length} renderers) :");
        foreach (var r in renderers)
            foreach (var m in r.sharedMaterials)
                if (m != null)
                    log.Append($"\n  {r.GetIl2CppType().Name} {r.name} {m.name} shader={m.shader?.name} " +
                               $"texture={(Declares(m, "_MainTex") ? m.GetTexture("_MainTex")?.name : "-")}");
        Log.Info(log.ToString());
        var shaders = Resources.FindObjectsOfTypeAll<Shader>().Select(s => s.name).Where(n => !n.StartsWith("Hidden/")).Distinct().OrderBy(n => n);
        Log.Info("shaders chargés : " + string.Join(", ", shaders));
    }
}
