namespace Ovomium.Core.Race;

/// Ce que le joueur demande au mode course pendant cette image.
public enum RaceCommand
{
    None,
    /// Lancer une course / accepter l'invitation / (hôte) partir sans attendre / fermer les résultats.
    Go,
    /// Abandonner / (hôte) annuler : seulement après un appui maintenu (HoldMs), anti-erreur.
    Quit,
    /// Appui bref alors que seul « maintenir » a un effet : rappeler qu'il faut maintenir.
    HoldHint,
}

/// État brut des boutons de course pour une image (lu par le mod, ou injecté par les tests).
public readonly record struct RaceButtonState(bool Key, bool PadModifier, bool PadGo, bool PadQuit);

/// Interprète les boutons de course, sans Unity (testé dans tests/) :
/// - clavier, une seule touche : appui bref = Go ; maintenue HoldMs = Quit ; relâchée entre les deux = rien.
///   Si Quit n'a pas de sens à ce moment, Go part dès l'appui (pas d'attente du relâchement) ;
/// - manette : modificateur maintenu + bouton Go = Go ; modificateur + bouton Quit maintenus HoldMs = Quit.
/// HoldProgress (0 à 1) alimente la jauge à l'écran ; relâcher avant la fin annule.
public sealed class RaceButtons
{
    public const double HoldMs = 1500, TapMaxMs = 300;

    private RaceButtonState _prev;
    private double _keyDownAt = double.NaN, _padHoldAt = double.NaN;
    private bool _keyConsumed; // touche déjà utilisée (Go immédiat, Quit fait) : attendre son relâchement

    /// Avancement du « maintenir pour abandonner » en cours (0 = rien).
    public double HoldProgress { get; private set; }
    /// Le maintien vient de la manette (sinon du clavier) : pour le texte de la jauge.
    public bool HoldFromPad { get; private set; }

    public RaceCommand Update(RaceButtonState s, double now, bool canGo, bool canQuit)
    {
        var cmd = UpdateKey(s.Key, now, canGo, canQuit);
        var pad = UpdatePad(s, now, canGo, canQuit);
        if (cmd == RaceCommand.None) cmd = pad;
        _prev = s;
        HoldProgress = System.Math.Max(Progress(_keyDownAt, now, canQuit && !_keyConsumed), Progress(_padHoldAt, now, canQuit));
        HoldFromPad = !double.IsNaN(_padHoldAt);
        return cmd;
    }

    private static double Progress(double since, double now, bool active) =>
        active && !double.IsNaN(since) ? System.Math.Clamp((now - since) / HoldMs, 0, 1) : 0;

    private RaceCommand UpdateKey(bool key, double now, bool canGo, bool canQuit)
    {
        if (key && !_prev.Key)
        {
            _keyConsumed = false;
            _keyDownAt = now;
            if (!canQuit) { _keyConsumed = true; return canGo ? RaceCommand.Go : RaceCommand.None; }
            return RaceCommand.None;
        }
        if (key)
        {
            if (_keyConsumed || !canQuit || now - _keyDownAt < HoldMs) return RaceCommand.None;
            _keyConsumed = true;
            return RaceCommand.Quit;
        }
        if (!_prev.Key) return RaceCommand.None;
        // Relâchée.
        bool tap = !_keyConsumed && now - _keyDownAt < TapMaxMs;
        _keyDownAt = double.NaN;
        if (!tap) return RaceCommand.None;
        return canGo ? RaceCommand.Go : canQuit ? RaceCommand.HoldHint : RaceCommand.None;
    }

    private RaceCommand UpdatePad(RaceButtonState s, double now, bool canGo, bool canQuit)
    {
        if (!s.PadModifier) { _padHoldAt = double.NaN; return RaceCommand.None; }
        if (s.PadGo && !_prev.PadGo && canGo) return RaceCommand.Go;
        if (!s.PadQuit || !canQuit) { _padHoldAt = double.NaN; return RaceCommand.None; }
        if (!_prev.PadQuit || !_prev.PadModifier) { _padHoldAt = now; return RaceCommand.None; }
        if (double.IsNaN(_padHoldAt) || now - _padHoldAt < HoldMs) return RaceCommand.None;
        _padHoldAt = double.NaN; // fait : relâcher puis rappuyer pour recommencer
        return RaceCommand.Quit;
    }
}
