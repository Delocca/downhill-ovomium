using System;
using System.Collections.Generic;
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
    private readonly float _radius;
    public OrbitSource(float radius) => _radius = radius;

    public PoseFrame Next(double nowMs, PoseFrame anchor)
    {
        float angle = (float)(nowMs / 8000.0 * 2 * Math.PI);
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
    private double _loopStart = double.NaN;
    private int _index;

    public ReplaySource(List<PoseFrame> frames)
    {
        if (frames.Count < 2) throw new ArgumentException("enregistrement trop court");
        _frames = frames;
        _lengthMs = frames[^1].TimeMs - frames[0].TimeMs;
    }

    public double LengthS => _lengthMs / 1000;

    public PoseFrame Next(double nowMs, PoseFrame anchor)
    {
        if (double.IsNaN(_loopStart)) _loopStart = nowMs;
        double elapsed = nowMs - _loopStart;
        if (elapsed > _lengthMs + 500) { _loopStart = nowMs; _index = 0; elapsed = 0; } // boucle (pause de 0,5 s à l'arrivée)
        uint target = _frames[0].TimeMs + (uint)Math.Min(elapsed, _lengthMs);
        while (_index + 1 < _frames.Count && _frames[_index + 1].TimeMs <= target) _index++;
        var f = new PoseFrame();
        f.CopyFrom(_frames[_index]);
        return f;
    }
}
