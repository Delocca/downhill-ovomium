namespace Ovomium.Core.View;

/// Flou de focale (profondeur de champ) du jeu dans les vues de LMD-Camera, sans Unity (testé dans tests/).
/// Sa mise au point est réglée pour la vue normale, caméra loin du vélo : en 1re et 3e personne, elle floute la piste.
public static class FocusBlur
{
    /// Couper le flou ? Seulement en 1re ou 3e personne (noms de l'enum ViewMode de LMD-Camera), jamais en VR
    /// (LMD-VR pilote la caméra), et pas si le joueur le garde (DepthOfFieldInCustomViews).
    public static bool ShouldSuppress(string view, bool vr, bool keepBlur) =>
        !vr && !keepBlur && view is "FirstPerson" or "ThirdPerson";
}
