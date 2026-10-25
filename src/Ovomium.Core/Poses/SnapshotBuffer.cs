using System.Collections.Generic;

namespace Ovomium.Core.Poses;

public enum SampleStatus
{
    Empty,        // rien reçu
    Interpolated, // cas normal : entre deux snapshots
    Waiting,      // instant visé antérieur au plus vieux snapshot (début de flux)
    Holding,      // instant visé postérieur au plus récent (paquets en retard ou perdus) : on fige
}

/// Tampon de snapshots d'un pair + interpolation retardée (« snapshot interpolation », Gaffer On Games).
/// On affiche le pair tel qu'il était il y a `delayMs` : on a alors presque toujours deux snapshots
/// qui encadrent l'instant visé, malgré la gigue et les pertes.
/// L'horloge de l'émetteur est recalée sur la nôtre par le décalage minimal (réception − émission)
/// observé sur la fenêtre conservée : le paquet le plus rapide donne le décalage le moins bruité.
public sealed class SnapshotBuffer
{
    private struct Entry { public PoseFrame Frame; public double RecvMs; }

    private readonly List<Entry> _entries = new();
    private readonly double _keepMs;

    /// Au-delà, un saut de position entre deux snapshots est une téléportation (respawn) : pas d'interpolation.
    public float TeleportDistance = 15f;

    public int Count => _entries.Count;
    public double LastRecvMs { get; private set; } = double.NegativeInfinity;
    public int HoldCount { get; private set; }

    /// keepMs : durée d'historique conservée ; doit dépasser le délai d'affichage (≥ délai + 1 s).
    public SnapshotBuffer(double keepMs) => _keepMs = keepMs;

    public void Clear() => _entries.Clear();

    public void Add(PoseFrame frame, double recvMs)
    {
        LastRecvMs = recvMs;
        if (_entries.Count > 0)
        {
            uint newest = _entries[^1].Frame.TimeMs;
            // Horloge de l'émetteur repartie de zéro (redémarrage, boucle de rejeu) : on repart à vide.
            if (frame.TimeMs + 5000 < newest || frame.TimeMs > newest + 60000) _entries.Clear();
        }
        int i = _entries.Count;
        while (i > 0 && _entries[i - 1].Frame.TimeMs > frame.TimeMs) i--; // paquet arrivé dans le désordre
        if (i > 0 && _entries[i - 1].Frame.TimeMs == frame.TimeMs) return;  // doublon
        _entries.Insert(i, new Entry { Frame = frame, RecvMs = recvMs });
        while (_entries.Count > 2 && _entries[0].RecvMs < recvMs - _keepMs) _entries.RemoveAt(0);
    }

    /// Écrit dans `output` la pose à afficher à l'instant local `nowMs`, avec un retard `delayMs`.
    public SampleStatus Sample(double nowMs, double delayMs, PoseFrame output)
    {
        if (_entries.Count == 0) return SampleStatus.Empty;
        double target = nowMs - ClockOffset() - delayMs; // instant visé, en horloge de l'émetteur
        var first = _entries[0].Frame;
        var last = _entries[^1].Frame;
        if (target <= first.TimeMs) { output.CopyFrom(first); return SampleStatus.Waiting; }
        if (target >= last.TimeMs) { output.CopyFrom(last); HoldCount++; return SampleStatus.Holding; }
        int i = 1;
        while (_entries[i].Frame.TimeMs < target) i++;
        var a = _entries[i - 1].Frame;
        var b = _entries[i].Frame;
        if (Vec3.Distance(a.RootPos, b.RootPos) > TeleportDistance) { output.CopyFrom(a); return SampleStatus.Interpolated; }
        float t = (float)((target - a.TimeMs) / (b.TimeMs - a.TimeMs));
        output.SetLerp(a, b, t);
        return SampleStatus.Interpolated;
    }

    private double ClockOffset()
    {
        double min = double.MaxValue;
        foreach (var e in _entries)
            if (e.RecvMs - e.Frame.TimeMs < min) min = e.RecvMs - e.Frame.TimeMs;
        return min;
    }
}
