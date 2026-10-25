namespace LmdMulti.Race;

/// Noms des commandes de course à l'écran, pour le périphérique utilisé en dernier (clavier ou manette).
internal static class RaceKeys
{
    /// Manette utilisée en dernier (mis à jour à chaque image par RaceController).
    public static bool Pad { get; private set; }
    private static bool _lastRewiredPad;

    /// Appui sur la touche → clavier ; Select → manette ; sinon on suit le dernier périphérique vu par le jeu.
    public static void Track(bool keyDown, bool padModifier, bool rewiredLastWasPad)
    {
        if (rewiredLastWasPad != _lastRewiredPad) { _lastRewiredPad = rewiredLastWasPad; Pad = rewiredLastWasPad; }
        if (keyDown) Pad = false;
        else if (padModifier) Pad = true;
    }

    public static string Go => Pad ? "Select + A" : Config.RaceKey.Value;
    public static string Quit => Pad ? "Select + B maintenus" : $"{Config.RaceKey.Value} maintenu";
}
