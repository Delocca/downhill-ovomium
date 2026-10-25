using System.Collections.Generic;
using Ovomium.Core.Net;
using UnityEngine;

namespace Ovomium.UI;

/// Pseudo au-dessus de chaque fantôme visible, dans la couleur du joueur. De loin, il pâlit sans
/// disparaître et indique la distance : on sait toujours où sont les autres.
internal static class NameLabels
{
    private const float NearM = 30f, FarM = 150f, FarAlpha = 0.55f;
    private static Camera _cam;
    private static double _nextCamSearch;

    /// me : position du joueur local (distance affichée), ou null.
    public static void Draw(IEnumerable<(string name, Vector3 pos, Color color)> labels, Vector3? me)
    {
        Camera cam = null;
        foreach (var (name, pos, color) in labels)
        {
            cam ??= FindCamera();
            if (cam == null) return;
            GuiKit.Ensure();
            var sp = cam.WorldToScreenPoint(pos);
            if (sp.z <= 0.5f) continue; // derrière la caméra
            float dist = me.HasValue ? Vector3.Distance(me.Value, pos) : sp.z;
            float far = Mathf.InverseLerp(NearM, FarM, dist);
            string text = far > 0 ? $"{name}  <size={GuiKit.Small.fontSize}>{dist:0} m</size>" : name;
            var style = GuiKit.Label;
            var size = style.CalcSize(new GUIContent(text));
            // Repère écran Unity : origine en bas ; GUI : origine en haut.
            var r = new Rect(sp.x - size.x / 2, Screen.height - sp.y - size.y, size.x, size.y);
            GuiKit.Shadowed(r, text, style, new Color(color.r, color.g, color.b, Mathf.Lerp(1f, FarAlpha, far)));
        }
    }

    /// Caméra de jeu : Camera.main si étiquetée, sinon la caméra active de plus grande profondeur
    /// (recherchée au plus 1×/s). Ne pas utiliser Camera.allCameras : plantage natif dans cette build.
    private static Camera FindCamera()
    {
        var main = Camera.main;
        if (main != null && main.isActiveAndEnabled) return main;
        if (_cam != null && _cam.isActiveAndEnabled) return _cam;
        if (NetClock.NowMs < _nextCamSearch) return null;
        _nextCamSearch = NetClock.NowMs + 1000;
        _cam = null;
        foreach (var c in Object.FindObjectsOfType<Camera>())
            if (c.isActiveAndEnabled && (_cam == null || c.depth > _cam.depth)) _cam = c;
        return _cam;
    }
}
