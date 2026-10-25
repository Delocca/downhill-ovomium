using System.Collections.Generic;
using UnityEngine;

namespace LmdMulti.UI;

/// Pseudo au-dessus de chaque fantôme visible, estompé avec la distance.
internal static class NameLabels
{
    private const float FadeStart = 40f, FadeEnd = 120f;

    public static void Draw(IEnumerable<(string name, Vector3 pos, bool echo)> labels)
    {
        var cam = FindCamera();
        if (cam == null) return;
        GuiKit.Ensure();
        foreach (var (name, pos, echo) in labels)
        {
            var sp = cam.WorldToScreenPoint(pos);
            if (sp.z <= 0.5f) continue; // derrière la caméra
            float alpha = Mathf.Clamp01(1f - (sp.z - FadeStart) / (FadeEnd - FadeStart));
            if (alpha <= 0.02f) continue;
            var color = echo ? new Color(1f, 0.85f, 0.4f, alpha) : new Color(0.75f, 0.92f, 1f, alpha);
            float w = 320 * GuiKit.Scale, h = 30 * GuiKit.Scale;
            // Repère écran Unity : origine en bas ; GUI : origine en haut.
            var r = new Rect(sp.x - w / 2, Screen.height - sp.y - h, w, h);
            GuiKit.Shadowed(r, name, GuiKit.Label, color);
        }
    }

    /// Caméra de jeu : Camera.main si étiquetée, sinon la caméra active de plus grande profondeur.
    private static Camera FindCamera()
    {
        var main = Camera.main;
        if (main != null && main.isActiveAndEnabled) return main;
        Camera best = null;
        foreach (var c in Camera.allCameras)
            if (c.isActiveAndEnabled && (best == null || c.depth > best.depth)) best = c;
        return best;
    }
}
