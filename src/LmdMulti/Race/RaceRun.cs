using LmdMulti.Core.Race;
using LmdMulti.Game;

namespace LmdMulti.Race;

/// Ma participation côté jeu : charger la piste, attendre bloqué au départ, partir au « GO »,
/// remonter sections franchies et arrivée (chrono du mod : depuis le départ commun).
internal sealed class RaceRun
{
    /// Repli : prêt ce délai après l'arrivée sur la piste même si l'écran de chargement semble encore là.
    /// (Le jeu ne libère le vélo qu'au premier appui du joueur : pas de signal « intro finie » à attendre.)
    private const double ReadyFallbackMs = 25000;
    private readonly TrackLoader _loader = new();
    private string _track;
    private bool _released;
    private double _releasedAt;
    private int _sections;

    /// Heure locale de mon arrivée (NaN sinon), pour l'annonce à l'écran.
    public double FinishedAtMs { get; private set; } = double.NaN;
    public bool Active => _track != null;
    public bool Loading => _loader.Busy;

    public void Begin(string track, double now)
    {
        _track = track;
        _released = false;
        _sections = 0;
        FinishedAtMs = double.NaN;
        RaceGame.HoldAtStart = true;
        _visibleSince = double.NaN;
        _loader.Start(track, now);
    }

    /// Fin de participation (course finie, annulée, quittée) : le vélo ne doit jamais rester bloqué.
    public void End()
    {
        if (_track == null) return;
        _track = null;
        _loader.Cancel();
        if (!RaceGame.HoldAtStart) return;
        RaceGame.HoldAtStart = false;
        if (GameInfo.CurrentTrackId() != null) Log.Try("course : déblocage", () => RaceGame.LockBike(false));
    }

    /// Une image : renvoie un message d'erreur à afficher si la participation échoue, sinon null.
    public string Update(IRaceSession s, double now)
    {
        if (_track == null) return null;
        _loader.Update(now);
        var me = s.Me;
        if (me == null) return null;
        switch (me.State)
        {
            case RacerState.Loading:
                if (_loader.Current == TrackLoader.Step.Failed) return $"Impossible de charger la piste : {_loader.Error}";
                if (_loader.Current == TrackLoader.Step.Done && IsSettled(now))
                {
                    s.Ready(now);
                    Log.Info("course : prêt au départ");
                    Log.Try("course : sélection", TrackAccess.RestoreSelection); // piste chargée : le menu peut retrouver la sienne
                }
                break;
            case RacerState.Ready:
                if (GameInfo.CurrentTrackId() != _track) { s.Abandon(now); return "Tu as quitté la piste : abandon"; }
                break;
            case RacerState.Racing:
                return Ride(s, now);
        }
        return null;
    }

    /// Sur la piste, écran de chargement disparu depuis un instant (la piste est « en cours » pour le jeu
    /// alors que le chargement s'affiche encore : vérifié). Contrôle 4×/s ; repli après ReadyFallbackMs.
    private bool IsSettled(double now)
    {
        if (now < _nextSettleCheck) return false;
        _nextSettleCheck = now + 250;
        if (GameInfo.CurrentTrackId() != _track || GameInfo.LoadingVisible()) { _visibleSince = double.NaN; }
        else if (double.IsNaN(_visibleSince)) _visibleSince = now;
        return now - _visibleSince > SettleMs || now - _loader.ArrivedAtMs > ReadyFallbackMs;
    }

    private const double SettleMs = 1500;
    private double _nextSettleCheck, _visibleSince = double.NaN;

    private string Ride(IRaceSession s, double now)
    {
        if (!_released) Release(s.View.StartAtMs, now);
        if (GameInfo.CurrentTrackId() != _track) { s.Abandon(now); return "Tu as quitté la piste : abandon"; }
        if (RaceGame.LastSection > _sections) { _sections = RaceGame.LastSection; s.Progress(_sections, now); }
        if (!(RaceGame.FinishCalledAtMs > _releasedAt)) return null;
        uint time = (uint)System.Math.Max(0, now - s.View.StartAtMs);
        FinishedAtMs = now;
        s.Finish(time, now);
        Log.Info($"course : arrivée en {Ranking.FormatTime(time)} (sections {_sections}, chrono du jeu {RaceGame.GameTimeText()})");
        return null;
    }

    private void Release(double startAtMs, double now)
    {
        _released = true;
        _releasedAt = now;
        RaceGame.HoldAtStart = false;
        RaceGame.FinishCalledAtMs = double.NaN;
        RaceGame.LastSection = 0;
        _sections = 0;
        Log.Try("course : départ", () => RaceGame.LockBike(false));
        Log.Info($"course : GO, {now - startAtMs:F0} ms après l'heure prévue");
    }
}
