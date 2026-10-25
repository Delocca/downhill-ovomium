using UnityEngine;

namespace LmdMulti.UI;

/// Styles et primitives OnGUI partagés (créés au premier OnGUI, taille selon la hauteur d'écran).
internal static class GuiKit
{
    private static Texture2D _dark;
    private static int _builtForHeight;

    public static GUIStyle Text, Small, Label, Centered;
    public static float Scale => Screen.height / 1080f;

    public static void Ensure()
    {
        if (_dark != null && _builtForHeight == Screen.height) return;
        _builtForHeight = Screen.height;
        if (_dark == null)
        {
            _dark = new Texture2D(1, 1);
            _dark.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
            _dark.Apply();
        }
        Text = Make(17, FontStyle.Normal);
        Small = Make(14, FontStyle.Normal);
        Label = Make(18, FontStyle.Bold);
        Label.alignment = TextAnchor.MiddleCenter;
        Centered = Make(19, FontStyle.Bold);
        Centered.alignment = TextAnchor.MiddleCenter;
    }

    private static GUIStyle Make(int size, FontStyle style)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(10, Mathf.RoundToInt(size * Scale)), fontStyle = style, richText = true };
        s.normal.textColor = Color.white;
        return s;
    }

    public static void Panel(Rect r) => GUI.DrawTexture(r, _dark);

    /// Texte avec ombre portée : lisible sur la neige comme sur la roche.
    public static void Shadowed(Rect r, string text, GUIStyle style, Color color)
    {
        var old = style.normal.textColor;
        style.normal.textColor = new Color(0, 0, 0, color.a * 0.8f);
        GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
        style.normal.textColor = color;
        GUI.Label(r, text, style);
        style.normal.textColor = old;
    }
}
