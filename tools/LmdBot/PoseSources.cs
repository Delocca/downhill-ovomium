using System;
using System.Collections.Generic;
using System.Linq;
using LmdMulti.Core;
using LmdMulti.Core.Poses;

namespace LmdBot;

/// Produit la prochaine pose du bot. `anchor` : dernière pose reçue d'un vrai joueur (ou null).
internal interface IPoseSource
{
    PoseFrame Next(double nowMs, PoseFrame anchor);
}

/// Mode orbite : le bot tourne autour du joueur (rayon fixe, un tour en 8 s) en copiant sa pose,
/// tournée de l'angle parcouru. Sans joueur : cercle autour de l'origine, racine seule.
internal sealed class OrbitSource : IPoseSource
{
    private readonly float _radius, _phase;
    public OrbitSource(float radius, float phase = 0) { _radius = radius; _phase = phase; }

    public PoseFrame Next(double nowMs, PoseFrame anchor)
    {
        float angle = (float)(nowMs / 8000.0 * 2 * Math.PI) + _phase;
        var offset = new Vec3(_radius * MathF.Cos(angle), 0, _radius * MathF.Sin(angle));
        var f = new PoseFrame();
        if (anchor != null)
        {
            f.CopyFrom(anchor);
            f.RootRot = Quat.FromYaw(-angle) * anchor.RootRot;
        }
        f.RootPos = (anchor?.RootPos ?? default) + offset;
        return f;
    }
}

/// Mode rejeu : un enregistrement .lmdrec rejoué en boucle à sa vitesse d'origine.
internal sealed class ReplaySource : IPoseSource
{
    private readonly List<PoseFrame> _frames;
    private readonly double _lengthMs;
    private readonly double _startOffsetMs;
    private double _loopStart = double.NaN;
    private int _index;

    /// startOffsetMs : le premier tour commence plus loin dans l'enregistrement (bots étalés sur la piste).
    public ReplaySource(List<PoseFrame> frames, double startOffsetMs = 0)
    {
        if (frames.Count < 2) throw new ArgumentException("enregistrement trop court");
        _frames = frames;
        _lengthMs = frames[^1].TimeMs - frames[0].TimeMs;
        _startOffsetMs = startOffsetMs % Math.Max(1, _lengthMs);
    }

    public double LengthS => _lengthMs / 1000;

    /// Retire l'attente au départ (menus, intro, essais à l'arrêt) : le rejeu commence 1 s avant que
    /// le vélo ne s'éloigne de plus de 50 cm de sa première position.
    public static List<PoseFrame> TrimIdleStart(List<PoseFrame> frames)
    {
        int moving = frames.FindIndex(f => Vec3.Distance(f.RootPos, frames[0].RootPos) > 0.5f);
        if (moving <= 0) return frames;
        uint from = frames[moving].TimeMs - Math.Min(1000u, frames[moving].TimeMs - frames[0].TimeMs);
        return frames.SkipWhile(f => f.TimeMs < from).ToList();
    }

    public PoseFrame Next(double nowMs, PoseFrame anchor)
    {
        if (double.IsNaN(_loopStart)) _loopStart = nowMs - _startOffsetMs;
        double elapsed = nowMs - _loopStart;
        if (elapsed > _lengthMs + 500) { _loopStart = nowMs; _index = 0; elapsed = 0; } // boucle (pause de 0,5 s à l'arrivée)
        uint target = _frames[0].TimeMs + (uint)Math.Min(elapsed, _lengthMs);
        while (_index + 1 < _frames.Count && _frames[_index + 1].TimeMs <= target) _index++;
        var f = new PoseFrame();
        f.CopyFrom(_frames[_index]);
        return f;
    }
}
