namespace Ovomium.Core.View;

/// Cycle des vues de LMD-Camera pour la touche V / R3, sans Unity (testé dans tests/).
/// Noms de son enum ViewMode : "None" (pas en piste), "Default", "FirstPerson", "ThirdPerson".
public static class ViewCycle
{
    /// Vue suivante : normale → 1re personne → 3e personne → normale.
    /// Vue inconnue (autre version de LMD-Camera, "None", null) : retour à la vue normale.
    public static string Next(string current) => current switch
    {
        "Default" => "FirstPerson",
        "FirstPerson" => "ThirdPerson",
        _ => "Default",
    };

    /// Libellé du message « Vue : … ».
    public static string Label(string view) => view switch
    {
        "FirstPerson" => "1re personne",
        "ThirdPerson" => "3e personne",
        _ => "normale",
    };
}
