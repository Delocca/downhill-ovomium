using UnityEngine;

namespace Ovomium.UI;

/// Repères de l'écran titre du jeu, mesurés en 1024×768, en fractions de la hauteur (l'interface du jeu suit la
/// hauteur) : bannière « Nouvelles » jusqu'à 0,35 H, son bord droit à 0,12 H du bord de l'écran ; colonne du menu
/// jusqu'à 0,55 H du bord gauche ; « jouer avec une manette » dès 0,92 H.
internal static class MenuLayout
{
    /// Zone libre du ciel, à droite de la colonne du menu et sous la bannière, alignée sur le bord droit de celle-ci.
    /// below : y à ne pas dépasser vers le haut (ce qui est déjà empilé là), 0 si rien.
    public static Rect Sky(float maxWidth, float below = 0)
    {
        float h = Screen.height, s = GuiKit.Scale;
        float right = Screen.width - 0.118f * h, top = Mathf.Max(0.38f * h, below), bottom = 0.9f * h;
        float w = Mathf.Min(maxWidth, right - 0.58f * h);
        w = Mathf.Max(w, Mathf.Min(300 * s, right - 8)); // fenêtre très étroite : plutôt chevaucher le menu qu'écrire en colonne
        return new Rect(right - w, top, w, bottom - top);
    }
}
